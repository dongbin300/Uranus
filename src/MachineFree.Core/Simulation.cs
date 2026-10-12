using Uranus;
using Uranus.Quantities;

namespace MachineFree.Core
{
	/// <summary>
	/// 자리를 비운 동안 진행된 결과
	/// </summary>
	public sealed record OfflineReport(TimeSpan Elapsed, TimeSpan Simulated, IReadOnlyDictionary<ItemId, double> Gained);

	/// <summary>
	/// MachineFree 게임 규칙. UI 없이 돌아가며, 난수를 쓰지 않아 같은 입력이면 항상 같은 결과가 나온다.
	/// - 제작: 재료를 먼저 내고 대기열에 넣으면 시간이 지나 결과물이 나온다(팩토리오식).
	///   생산 라인 수만큼 대기열 앞쪽을 동시에 만든다.
	/// - 생성기: 보유한 대수 × 초당 생산량. n대째 비용은 기본 비용 × 성장률^n.
	/// - 규모: 결과물이 모두 입자인 레시피와 입자를 내는 생성기는 현재 규모 배율만큼 크게 돈다.
	/// - 1보다 작은 결과물은 소수로 쌓인다.
	/// </summary>
	public sealed class Simulation(UranusDatabase database, GameState state)
	{
		/// <summary>
		/// 한 번에 넣을 수 있는 최대 제작 횟수
		/// </summary>
		public const long MaxBatch = 1_000_000_000;

		public UranusDatabase Database { get; } = database;

		public GameState State { get; } = state;

		/// <summary>
		/// 마지막으로 비운 뒤 수량이 바뀐 아이템. UI가 읽고 비운다.
		/// </summary>
		public HashSet<ItemId> ChangedItems { get; } = [];

		/// <summary>
		/// 마지막으로 내린 뒤 대기열 구성이 바뀌었는가. UI가 읽고 내린다.
		/// </summary>
		public bool QueueChanged { get; set; }

		/// <summary>
		/// 마지막으로 내린 뒤 업그레이드를 샀는가. UI가 읽고 내린다.
		/// </summary>
		public bool UpgradesChanged { get; set; }

		/// <summary>
		/// 새로 달성한 목표. UI가 읽고 비운다.
		/// </summary>
		public List<GoalDef> NewGoals { get; } = [];

		public double GetAmount(ItemId item) => State.GetAmount(item);

		/// <summary>
		/// 보유한 생성기(또는 아이템)의 정수 개수
		/// </summary>
		public long GetOwnedCount(ItemId item) => (long)Math.Min(Quantity.Floor(State.GetAmount(item)), long.MaxValue);

		/// <summary>
		/// 생산 라인 수 = 동시에 만드는 대기열 칸 수
		/// </summary>
		public int LineCount => 1 + Database.Config.Upgrades.Count(x => x.Kind == UpgradeKind.Line && State.Upgrades.Contains(x.Id));

		/// <summary>
		/// 보유한 규모 업그레이드 중 가장 큰 것 (없으면 null = 분자 단위)
		/// </summary>
		public UpgradeDef? CurrentScale => Database.Config.Upgrades
			.Where(x => x.Kind == UpgradeKind.Scale && State.Upgrades.Contains(x.Id))
			.MaxBy(x => x.Factor);

		public double ScaleFactor => CurrentScale?.Factor ?? 1;

		/// <summary>
		/// 이 레시피 1회의 규모 배율. 결과물이 모두 입자이고 생성기를 만드는 레시피가 아니면 현재 규모, 아니면 1.
		/// </summary>
		public double GetRecipeScale(RecipeDef recipe) =>
			Database.FindGeneratorBuiltBy(recipe.Id) is null && recipe.Outputs.Count > 0 && recipe.Outputs.All(x => IsParticle(x.Item))
				? ScaleFactor
				: 1;

		/// <summary>
		/// 대기열에서 만들고 있는 이 아이템의 개수
		/// </summary>
		public double GetQueuedAmount(ItemId item)
		{
			var total = 0.0;
			foreach (var entry in State.Queue)
			{
				if (Database.FindRecipe(entry.Recipe) is { } recipe)
				{
					total += entry.Remaining * entry.Scale * recipe.Outputs.Where(x => x.Item == item).Sum(x => x.Amount);
				}
			}

			return total;
		}

		/// <summary>
		/// 이 아이템을 생산하는 생성기 전체의 초당 생산량
		/// </summary>
		public double GetProductionRate(ItemId item)
		{
			var rate = 0.0;
			foreach (var generator in Database.GeneratorList)
			{
				var owned = GetOwnedCount(generator.Item);
				if (owned > 0)
				{
					rate += owned * generator.OutputPerSecond.Where(x => x.Item == item).Sum(x => x.Amount);
				}
			}

			return rate * GetOutputScale(item);
		}

		/// <summary>
		/// 레시피 1회의 결과물 (규모 반영)
		/// </summary>
		public IReadOnlyList<ItemAmount> GetOutputs(RecipeDef recipe)
		{
			var scale = GetRecipeScale(recipe);
			return [.. recipe.Outputs.Select(x => x with { Amount = x.Amount * scale })];
		}

		/// <summary>
		/// count회 제작에 드는 재료. 생성기 레시피는 보유·대기 중인 대수만큼 비싸지고, 입자 레시피는 규모만큼 커진다.
		/// </summary>
		public IReadOnlyList<ItemAmount> GetCost(RecipeDef recipe, long count)
		{
			var multiplier = GetCostMultiplier(recipe, count) * GetRecipeScale(recipe);
			return [.. recipe.Inputs.Select(x => x with { Amount = x.Amount * multiplier })];
		}

		/// <summary>
		/// 지금 재료로 최대 몇 번 만들 수 있는가
		/// </summary>
		public long GetMaxCraftable(RecipeDef recipe, long limit = MaxBatch)
		{
			if (recipe.Inputs.Count == 0 || recipe.Outputs.Count == 0 || limit <= 0)
			{
				return 0;
			}

			var growth = GetGrowth(recipe);
			var built = growth > 1 ? GetBuiltCount(recipe) : 0;
			var scale = GetRecipeScale(recipe);
			var best = limit;
			foreach (var input in recipe.Inputs)
			{
				var budget = State.GetAmount(input.Item) / (input.Amount * scale);
				double count;
				if (growth > 1)
				{
					// base × g^n × (g^c − 1)/(g − 1) ≤ have
					count = Math.Floor(Math.Log(1 + budget * (growth - 1) / Math.Pow(growth, built)) / Math.Log(growth) + 1e-9);
				}
				else
				{
					count = Quantity.Floor(budget);
				}

				best = Math.Min(best, (long)Math.Clamp(double.IsNaN(count) ? 0 : count, 0, limit));
			}

			// 경계에서 부동소수점 오차로 하나 더 세는 일이 없도록 실제 비용으로 확인한다
			if (best > 0 && !CanAfford(GetCost(recipe, best)))
			{
				long low = 0, high = best - 1;
				while (low < high)
				{
					var middle = low + (high - low + 1) / 2;
					if (CanAfford(GetCost(recipe, middle)))
					{
						low = middle;
					}
					else
					{
						high = middle - 1;
					}
				}

				best = low;
			}

			return best;
		}

		public bool TryEnqueue(RecipeId recipeId, long count)
		{
			if (count <= 0 || count > MaxBatch || Database.FindRecipe(recipeId) is not { } recipe || recipe.Outputs.Count == 0)
			{
				return false;
			}

			var cost = GetCost(recipe, count);
			if (recipe.Inputs.Count == 0 || !CanAfford(cost))
			{
				return false;
			}

			foreach (var x in cost)
			{
				State.Remove(x.Item, x.Amount);
				ChangedItems.Add(x.Item);
			}

			// 주문할 때의 규모를 기억한다. 규모를 올린 뒤 예전 값으로 낸 칸이 큰 결과물을 내면 안 된다
			var scale = GetRecipeScale(recipe);
			var last = State.Queue.Count > 0 ? State.Queue[^1] : null;
			var entry = last is not null && last.Recipe == recipeId && last.Scale == scale ? last : null;
			if (entry is null)
			{
				entry = new QueueEntry { Recipe = recipeId, Scale = scale };
				State.Queue.Add(entry);
			}

			entry.Remaining += count;
			foreach (var x in cost)
			{
				entry.Paid[x.Item] = entry.Paid.GetValueOrDefault(x.Item) + x.Amount;
			}

			QueueChanged = true;
			return true;
		}

		/// <summary>
		/// 대기열 index번째 칸에서 count회를 취소하고 낸 재료를 비율대로 돌려받는다.
		/// </summary>
		public void Cancel(int index, long count)
		{
			if (index < 0 || index >= State.Queue.Count || count <= 0)
			{
				return;
			}

			var entry = State.Queue[index];
			var cancel = Math.Min(count, entry.Remaining);
			var growth = Database.FindRecipe(entry.Recipe) is { } recipe ? GetGrowth(recipe) : 1;
			Refund(entry, GetShare(growth, cancel, entry.Remaining, fromEnd: true));
			entry.Remaining -= cancel;
			if (entry.Remaining <= 0)
			{
				State.Queue.RemoveAt(index);
			}

			QueueChanged = true;
			UpdateGoals();
		}

		public bool IsUpgradeOwned(UpgradeDef upgrade) => State.Upgrades.Contains(upgrade.Id);

		/// <summary>
		/// 아직 없고 선행 업그레이드를 모두 가졌는가
		/// </summary>
		public bool IsUpgradeAvailable(UpgradeDef upgrade) => !IsUpgradeOwned(upgrade) && upgrade.Requires.All(State.Upgrades.Contains);

		public bool CanBuyUpgrade(UpgradeDef upgrade) => IsUpgradeAvailable(upgrade) && CanAfford(upgrade.Cost);

		public bool TryBuyUpgrade(string id)
		{
			if (Database.Config.Upgrades.FirstOrDefault(x => x.Id == id) is not { } upgrade || !CanBuyUpgrade(upgrade))
			{
				return false;
			}

			foreach (var x in upgrade.Cost)
			{
				State.Remove(x.Item, x.Amount);
				ChangedItems.Add(x.Item);
			}

			State.Upgrades.Add(upgrade.Id);
			UpgradesChanged = true;
			return true;
		}

		public bool IsGoalMet(GoalDef goal) => goal.Requirements.All(x => Quantity.IsAtLeast(State.GetAmount(x.Item), x.Amount));

		/// <summary>
		/// 지금 보유량으로 달성한 목표를 기록한다. notify가 true면 NewGoals에도 넣는다.
		/// </summary>
		public void UpdateGoals(bool notify = true)
		{
			foreach (var goal in Database.Config.Goals)
			{
				if (!State.CompletedGoals.ContainsKey(goal.Id) && IsGoalMet(goal))
				{
					State.CompletedGoals[goal.Id] = State.PlayTime;
					if (notify)
					{
						NewGoals.Add(goal);
					}
				}
			}
		}

		/// <summary>
		/// 게임 시간을 seconds초 진행한다. 큰 값(오프라인 진행)도 정확하게 처리한다.
		/// 라인마다 대기열 앞쪽 칸을 동시에 만들고, 어느 칸이 끝나거나 생성기가 완성되는 순간마다 끊어서 계산한다.
		/// </summary>
		public void Tick(double seconds)
		{
			if (!(seconds > 0) || !double.IsFinite(seconds))
			{
				return;
			}

			State.PlayTime += seconds;
			var remaining = seconds;
			while (remaining > 0)
			{
				var active = PrepareActiveEntries();
				if (active.Count == 0)
				{
					Produce(remaining);
					break;
				}

				// 다음 사건: 생성기 1대 완성, 또는 다른 칸이 통째로 끝남
				var step = remaining;
				foreach (var (entry, recipe) in active)
				{
					var untilNext = Math.Max(0, recipe.Time - entry.Progress);
					var eventTime = Database.FindGeneratorBuiltBy(recipe.Id) is not null
						? untilNext
						: untilNext + (entry.Remaining - 1) * recipe.Time;
					step = Math.Min(step, eventTime);
				}

				Produce(step);
				remaining -= step;

				foreach (var (entry, recipe) in active)
				{
					var total = entry.Progress + step;
					var crafts = (long)Math.Min(entry.Remaining, Math.Floor(total / recipe.Time + 1e-9));
					if (crafts > 0)
					{
						entry.Progress = Math.Max(0, total - crafts * recipe.Time);
						Complete(entry, recipe, crafts);
					}
					else
					{
						entry.Progress = total;
					}
				}
			}

			UpdateGoals();
		}

		/// <summary>
		/// 저장 시각부터 지금까지(상한까지) 게임을 진행하고 얻은 것을 돌려준다.
		/// </summary>
		public OfflineReport CatchUp(DateTimeOffset now)
		{
			var elapsed = now - State.SavedAt;
			if (elapsed <= TimeSpan.Zero)
			{
				return new OfflineReport(TimeSpan.Zero, TimeSpan.Zero, new Dictionary<ItemId, double>());
			}

			var cap = Database.Config.OfflineCapHours;
			var simulated = TimeSpan.FromHours(Math.Min(elapsed.TotalHours, double.IsFinite(cap) && cap > 0 ? cap : 0));
			var before = new Dictionary<ItemId, double>(State.Inventory);
			Tick(simulated.TotalSeconds);
			State.SavedAt = now;

			var gained = State.Inventory
				.Select(x => (x.Key, Delta: x.Value - before.GetValueOrDefault(x.Key)))
				.Where(x => x.Delta > Quantity.Tolerance(x.Delta))
				.ToDictionary(x => x.Key, x => x.Delta);
			return new OfflineReport(elapsed, simulated, gained);
		}

		/// <summary>
		/// 라인 수만큼 대기열 앞쪽 칸을 고른다. 빈 칸, 데이터에서 사라진 레시피(환불), 시간 0 레시피(즉시 완성)는 먼저 정리한다.
		/// </summary>
		private List<(QueueEntry Entry, RecipeDef Recipe)> PrepareActiveEntries()
		{
			var lines = LineCount;
			while (true)
			{
				var active = new List<(QueueEntry, RecipeDef)>();
				var changed = false;
				for (var i = 0; i < State.Queue.Count && active.Count < lines; i++)
				{
					var entry = State.Queue[i];
					if (entry.Remaining <= 0)
					{
						State.Queue.RemoveAt(i);
						QueueChanged = changed = true;
						break;
					}

					if (Database.FindRecipe(entry.Recipe) is not { } recipe)
					{
						Refund(entry, 1);
						State.Queue.RemoveAt(i);
						QueueChanged = changed = true;
						break;
					}

					if (recipe.Time <= 0)
					{
						Complete(entry, recipe, entry.Remaining);
						changed = true;
						break;
					}

					active.Add((entry, recipe));
				}

				if (!changed)
				{
					return active;
				}
			}
		}

		private bool IsParticle(ItemId item) => Database.FindItem(item)?.Unit == ItemUnit.Particle;

		private double GetOutputScale(ItemId item) => IsParticle(item) ? ScaleFactor : 1;

		private bool CanAfford(IReadOnlyList<ItemAmount> cost) => cost.All(x => Quantity.IsAtLeast(State.GetAmount(x.Item), x.Amount));

		/// <summary>
		/// 한 칸에 남은 remaining개 가운데 count개가 차지하는 비용 비율.
		/// 생성기는 뒤로 갈수록 비싸게 샀으므로(등비수열) 완성은 앞쪽(싼 것)부터, 취소는 뒤쪽(비싼 것)부터 계산한다.
		/// </summary>
		private static double GetShare(double growth, long count, long remaining, bool fromEnd)
		{
			if (count >= remaining)
			{
				return 1;
			}

			if (growth <= 1)
			{
				return (double)count / remaining;
			}

			// 앞쪽 n개: (g^n − 1)/(g^R − 1), 뒤쪽 n개: (g^R − g^(R−n))/(g^R − 1). 넘침을 막으려고 g^−R로 나눠 계산한다
			var all = 1 - Math.Pow(growth, -remaining);
			return fromEnd
				? (1 - Math.Pow(growth, -count)) / all
				: (Math.Pow(growth, count - remaining) - Math.Pow(growth, -remaining)) / all;
		}

		private double GetGrowth(RecipeDef recipe) =>
			Database.FindGeneratorBuiltBy(recipe.Id) is not null ? Math.Max(1, Database.Config.GeneratorCostGrowth) : 1;

		/// <summary>
		/// 이 레시피가 만드는 생성기의 보유 + 대기 대수
		/// </summary>
		private double GetBuiltCount(RecipeDef recipe) =>
			Database.FindGeneratorBuiltBy(recipe.Id) is { } generator
				? GetOwnedCount(generator.Item) + GetQueuedAmount(generator.Item)
				: 0;

		private double GetCostMultiplier(RecipeDef recipe, long count)
		{
			var growth = GetGrowth(recipe);
			if (growth <= 1)
			{
				return count;
			}

			return Math.Pow(growth, GetBuiltCount(recipe)) * (Math.Pow(growth, count) - 1) / (growth - 1);
		}

		private void Produce(double seconds)
		{
			if (!(seconds > 0))
			{
				return;
			}

			foreach (var generator in Database.GeneratorList)
			{
				var owned = GetOwnedCount(generator.Item);
				if (owned <= 0)
				{
					continue;
				}

				foreach (var output in generator.OutputPerSecond)
				{
					State.Add(output.Item, output.Amount * owned * seconds * GetOutputScale(output.Item));
					ChangedItems.Add(output.Item);
				}
			}
		}

		private void Complete(QueueEntry entry, RecipeDef recipe, long crafts)
		{
			foreach (var output in recipe.Outputs)
			{
				State.Add(output.Item, output.Amount * crafts * entry.Scale);
				ChangedItems.Add(output.Item);
			}

			var used = GetShare(GetGrowth(recipe), crafts, entry.Remaining, fromEnd: false);
			foreach (var item in entry.Paid.Keys.ToList())
			{
				entry.Paid[item] -= entry.Paid[item] * used;
			}

			entry.Remaining -= crafts;
			State.TotalCrafts += crafts;
			if (entry.Remaining <= 0)
			{
				State.Queue.Remove(entry);
				QueueChanged = true;
			}
		}

		private void Refund(QueueEntry entry, double fraction)
		{
			foreach (var item in entry.Paid.Keys.ToList())
			{
				var amount = entry.Paid[item] * fraction;
				State.Add(item, amount);
				entry.Paid[item] -= amount;
				ChangedItems.Add(item);
			}
		}
	}
}
