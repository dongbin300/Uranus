using MachineFree.Core;

using Uranus.Analysis;

using Uranus;
using Uranus.Quantities;

namespace Uranus.Tools
{
	/// <summary>
	/// 밸런스 확인용 자동 플레이. 사람보다 서툴지만 매번 같은 결과가 나와서 수치를 바꿨을 때 비교하기 좋다.
	/// 매 스텝마다: 업그레이드 구매 → 새 생성기 → 다음 규모 업그레이드 재료 → 다음 목표 재료 → (라인이 남으면) 다른 업그레이드 재료
	/// → 생성기 증설 → 시간 진행. 규모는 모든 것을 빠르게 하므로 사람도 먼저 챙긴다.
	/// </summary>
	public sealed class BalanceBot(UranusDatabase db)
	{
		private const int MaxDepth = 20;

		/// <summary>
		/// 1회로 다 채우지 못하는 작은 규모를 쓸 때 허용하는 최대 제작 횟수
		/// </summary>
		private const int SmallScaleCrafts = 100;

		private readonly Simulation sim = new(db, GameState.CreateNew(db));

		public sealed record Milestone(double Time, string Kind, string Name);

		public List<Milestone> Milestones { get; } = [];

		public Simulation Simulation => sim;

		public void Run(double hours, double stepSeconds)
		{
			var end = hours * 3600;
			while (sim.State.PlayTime < end)
			{
				BuyUpgrades();
				if (sim.State.Queue.Count < sim.LineCount * 2)
				{
					BuildNewGeneratorTypes();
					WorkOnNextUpgrade(UpgradeKind.Scale);
					WorkOnNextGoal();
					if (sim.State.Queue.Count < sim.LineCount)
					{
						WorkOnNextUpgrade(null);
					}

					ExpandGenerators();
				}

				sim.Tick(stepSeconds);
				foreach (var goal in sim.NewGoals)
				{
					Milestones.Add(new Milestone(sim.State.PlayTime, "목표", goal.Name.Ko));
				}

				sim.NewGoals.Clear();
				sim.ChangedItems.Clear();
			}
		}

		private void BuyUpgrades()
		{
			foreach (var upgrade in db.Config.Upgrades)
			{
				if (sim.TryBuyUpgrade(upgrade.Id))
				{
					Milestones.Add(new Milestone(sim.State.PlayTime, "업그레이드", upgrade.Name.Ko));
				}
			}
		}

		private void BuildNewGeneratorTypes()
		{
			foreach (var generator in db.GeneratorList)
			{
				if (sim.GetOwnedCount(generator.Item) == 0 && sim.GetQueuedAmount(generator.Item) == 0 &&
					db.FindRecipe(generator.BuildRecipe) is { } recipe && sim.TryEnqueue(recipe.Id, 1))
				{
					Milestones.Add(new Milestone(sim.State.PlayTime, "생성기", db.GetName(generator.Item)));
				}
			}
		}

		/// <summary>
		/// 생성기를 더 짓되, 다음 1대 비용의 3배 이상을 가졌을 때만 짓는다.
		/// </summary>
		private void ExpandGenerators()
		{
			foreach (var generator in db.GeneratorList)
			{
				if (sim.GetOwnedCount(generator.Item) == 0 || db.FindRecipe(generator.BuildRecipe) is not { } recipe)
				{
					continue;
				}

				var cost = sim.GetCost(recipe, 1);
				if (cost.All(x => sim.GetAmount(x.Item) >= x.Amount * 3))
				{
					sim.TryEnqueue(recipe.Id, 1);
				}
			}
		}

		private void WorkOnNextGoal()
		{
			// 한 번의 계획에서 같은 아이템은 한 번만 본다. 그래야 재료가 많은 트리(CCD)에서도 계산량이 아이템 수에 비례한다
			// 만들 방법이 있는 첫 목표 하나에만 집중한다 (사람처럼 차례대로)
			var visited = new HashSet<ItemId>();
			var goal = db.Config.Goals.FirstOrDefault(x => !sim.State.CompletedGoals.ContainsKey(x.Id) && x.Requirements.All(r => CanEverMake(r.Item)));
			foreach (var need in goal?.Requirements ?? [])
			{
				Produce(need.Item, need.Amount, 0, visited);
			}
		}

		/// <summary>
		/// 살 수 있게 된(선행을 갖춘) 업그레이드의 비용을 모은다. 앞에서부터 보고, 무언가를 걸 수 있는 첫 업그레이드에 집중한다
		/// </summary>
		private void WorkOnNextUpgrade(UpgradeKind? kind)
		{
			foreach (var upgrade in db.Config.Upgrades.Where(x => (kind is null || x.Kind == kind) && sim.IsUpgradeAvailable(x) && x.Cost.All(c => CanEverMake(c.Item))))
			{
				var visited = new HashSet<ItemId>();
				var progressed = false;
				foreach (var cost in upgrade.Cost)
				{
					progressed |= Produce(cost.Item, cost.Amount, 0, visited);
				}

				if (progressed)
				{
					return;
				}
			}
		}

		/// <summary>
		/// 다음 목표가 왜 막혔는지: 모자란 재료를 기본 레시피로 거슬러 올라가며 한 줄씩 보여 준다 (가장 큰 규모 기준)
		/// </summary>
		public List<string> ExplainNextGoal()
		{
			var lines = new List<string>();
			var goal = db.Config.Goals.FirstOrDefault(x => !sim.State.CompletedGoals.ContainsKey(x.Id));
			if (goal is null)
			{
				return lines;
			}

			lines.Add($"'{goal.Name.Ko}'에 모자란 것:");
			var visited = new HashSet<ItemId>();
			foreach (var need in goal.Requirements)
			{
				Explain(need.Item, need.Amount, 1, visited, lines);
			}

			return lines;
		}

		private void Explain(ItemId item, double amount, int depth, HashSet<ItemId> visited, List<string> lines)
		{
			var have = sim.GetAmount(item) + sim.GetQueuedAmount(item);
			if (Quantity.IsAtLeast(have, amount))
			{
				return;
			}

			var label = $"{new string(' ', depth * 2)}{db.GetName(item)} {QuantityFormatter.FormatCount(have, NumberStyle.Scientific)}/{QuantityFormatter.FormatCount(amount, NumberStyle.Scientific)}";
			if (!visited.Add(item))
			{
				lines.Add(label + " (위에서 봄)");
				return;
			}

			if (depth > MaxDepth)
			{
				lines.Add(label + " — 봇의 탐색 깊이를 넘음");
				return;
			}

			if (FindSource(item) is { } source && sim.GetOwnedCount(source.Item) > 0)
			{
				lines.Add(label + $" — {db.GetName(source.Item)}가 생산 중");
				return;
			}

			if (RecipeGraph.GetDefaultRecipe(db, item) is not { } recipe)
			{
				lines.Add(label + " — 만들 레시피가 없음");
				return;
			}

			var perCraft = PerCraft(recipe, item);
			var crafts = perCraft > 0 ? (long)Math.Clamp(Math.Ceiling((amount - have) / perCraft), 1, 1000) : 1;
			lines.Add(label + $" ← {recipe.Id} ×{crafts}");
			foreach (var input in sim.GetCost(recipe, crafts))
			{
				Explain(input.Item, input.Amount, depth + 1, visited, lines);
			}
		}

		private double PerCraft(RecipeDef recipe, ItemId item) => sim.GetOutputs(recipe).Where(o => o.Item == item).Sum(o => o.Amount);

		private T WithScale<T>(string? scale, Func<T> action)
		{
			sim.SetCraftScale(scale);
			try
			{
				return action();
			}
			finally
			{
				sim.SetCraftScale(null);
			}
		}

		private bool CanEverMake(ItemId item) =>
			db.GetProducers(item).Count > 0 || db.GeneratorList.Any(g => g.OutputPerSecond.Any(x => x.Item == item));

		private GeneratorDef? FindSource(ItemId item) =>
			db.GeneratorList.FirstOrDefault(g => g.OutputPerSecond.Any(x => x.Item == item));

		/// <summary>
		/// item을 amount개 갖도록 제작을 건다. 재료가 없으면 재료부터 거슬러 올라간다. 무언가를 걸었으면 true.
		/// </summary>
		private bool Produce(ItemId item, double amount, int depth, HashSet<ItemId> visited)
		{
			var missing = amount - sim.GetAmount(item) - sim.GetQueuedAmount(item);
			if (missing <= Quantity.Tolerance(amount) || depth > MaxDepth || !visited.Add(item))
			{
				return false;
			}

			{
				// 생성기가 내는 원료: 생성기가 있으면 기다리고, 없으면 레시피로 만들고, 레시피도 없으면 생성기를 짓는다
				var source = FindSource(item);
				if (source is not null && sim.GetOwnedCount(source.Item) > 0)
				{
					return false;
				}

				// 원료를 내는 생성기가 아직 없으면 그것부터 짓는다 (짓는 중이면 레시피로 버틴다)
				if (source is not null && sim.GetQueuedAmount(source.Item) == 0 && Produce(source.Item, 1, depth + 1, visited))
				{
					return true;
				}

				// 기본 레시피를 먼저, 그다음 다른 생산 레시피. 생성기 제작 레시피는 생성기 자체를 만들 때만 쓴다
				var defaultRecipe = RecipeGraph.GetDefaultRecipe(db, item);
				var producers = db.GetProducers(item)
					.Where(x => db.FindGeneratorBuiltBy(x.Id) is null || db.Generators.ContainsKey(item))
					.OrderBy(x => x == defaultRecipe ? 0 : 1)
					.ToList();
				if (producers.Count == 0)
				{
					return source is not null && sim.GetQueuedAmount(source.Item) == 0 && Produce(source.Item, 1, depth + 1, visited);
				}

				// 작업 규모는 큰 것부터 본다. 재료는 생성기가 큰 규모로 대 주므로 남게 만들어도 괜찮지만, 라인 시간은 귀하다.
				// 1회로 필요량을 채우는 가장 작은 규모(fit)보다 작은 규모는 SmallScaleCrafts회 안에 채울 수 있을 때만 쓴다.
				// 그보다 많이 돌리면 라인만 오래 차지한다
				var scales = sim.OwnedScales.OrderByDescending(x => x.Factor).Select(x => x.Id).Append(Simulation.UnitScaleId).ToList();
				var fit = scales.LastOrDefault(s => WithScale(s, () => PerCraft(producers[0], item)) >= missing) ?? scales[0];
				try
				{
					// 지금 바로 만들 수 있는 레시피가 있으면 그것부터 (일부만 되면 되는 만큼만).
					// 모자란 재료까지 한꺼번에 쫓으면 서로를 만들고 되돌리는 헛바퀴 순환에 빠지기 쉬워서 하지 않는다
					for (var i = 0; i < scales.Count; i++)
					{
						sim.SetCraftScale(scales[i]);
						foreach (var recipe in producers)
						{
							var perCraft = PerCraft(recipe, item);
							if (perCraft <= 0 || (i > scales.IndexOf(fit) && missing / perCraft > SmallScaleCrafts))
							{
								continue;
							}

							var possible = sim.GetMaxCraftable(recipe, (long)Math.Min(Math.Ceiling(missing / perCraft), 1000));
							if (possible > 0)
							{
								return sim.TryEnqueue(recipe.Id, possible);
							}
						}
					}

					// 없으면 fit 규모로 재료부터 거슬러 올라간다
					foreach (var recipe in producers)
					{
						sim.SetCraftScale(fit);
						var perCraft = PerCraft(recipe, item);
						if (perCraft <= 0)
						{
							continue;
						}

						var cost = sim.GetCost(recipe, (long)Math.Clamp(Math.Ceiling(missing / perCraft), 1, 1000));
						sim.SetCraftScale(null);
						var progressed = false;
						foreach (var input in cost)
						{
							progressed |= Produce(input.Item, input.Amount, depth + 1, visited);
						}

						if (progressed)
						{
							return true;
						}
					}
				}
				finally
				{
					sim.SetCraftScale(null);
				}

				return false;
			}
		}
	}
}
