using MachineFree.Core;

using Uranus.Analysis;

using Uranus;
using Uranus.Quantities;

namespace Uranus.Tools
{
	/// <summary>
	/// 밸런스 확인용 자동 플레이. 사람보다 서툴지만 매번 같은 결과가 나와서 수치를 바꿨을 때 비교하기 좋다.
	/// 매 스텝마다: 업그레이드 구매 → 새 생성기 → 다음 목표에 필요한 재료 제작 → 생성기 증설 → 시간 진행.
	/// </summary>
	public sealed class BalanceBot(UranusDatabase db)
	{
		private const int MaxDepth = 10;

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
					WorkOnNextGoal();
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

				// 기본 레시피를 먼저, 그다음 다른 생산 레시피. 생성기 제작 레시피는 생성기 자체를 만들 때만 쓴다
				var defaultRecipe = RecipeGraph.GetDefaultRecipe(db, item);
				var candidates = db.GetProducers(item)
					.Where(x => db.FindGeneratorBuiltBy(x.Id) is null || db.Generators.ContainsKey(item))
					.OrderBy(x => x == defaultRecipe ? 0 : 1)
					.Select(x => (Recipe: x, PerCraft: sim.GetOutputs(x).Where(o => o.Item == item).Sum(o => o.Amount)))
					.Where(x => x.PerCraft > 0)
					.ToList();
				if (candidates.Count == 0)
				{
					return source is not null && sim.GetQueuedAmount(source.Item) == 0 && Produce(source.Item, 1, depth + 1, visited);
				}

				// 지금 바로 만들 수 있는 레시피가 있으면 그것부터 (일부만 되면 되는 만큼만).
				// 모자란 재료까지 한꺼번에 쫓으면 서로를 만들고 되돌리는 헛바퀴 순환에 빠지기 쉬워서 하지 않는다
				foreach (var (recipe, perCraft) in candidates)
				{
					var crafts = (long)Math.Min(Math.Ceiling(missing / perCraft), 1000);
					var possible = sim.GetMaxCraftable(recipe, crafts);
					if (possible > 0)
					{
						return sim.TryEnqueue(recipe.Id, possible);
					}
				}

				// 없으면 재료부터 거슬러 올라간다
				foreach (var (recipe, perCraft) in candidates)
				{
					var crafts = (long)Math.Min(Math.Ceiling(missing / perCraft), 1000);
					var progressed = false;
					foreach (var input in sim.GetCost(recipe, Math.Max(1, crafts)))
					{
						progressed |= Produce(input.Item, input.Amount, depth + 1, visited);
					}

					if (progressed)
					{
						return true;
					}
				}

				return false;
			}
		}
	}
}
