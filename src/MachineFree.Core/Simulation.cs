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
	/// - 생성기: 보유한 대수 × 초당 생산량. n대째 비용은 기본 비용 × 성장률^n.
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

		public double GetAmount(ItemId item) => State.GetAmount(item);

		/// <summary>
		/// 보유한 생성기(또는 아이템)의 정수 개수
		/// </summary>
		public long GetOwnedCount(ItemId item) => (long)Math.Min(Quantity.Floor(State.GetAmount(item)), long.MaxValue);

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
					total += entry.Remaining * recipe.Outputs.Where(x => x.Item == item).Sum(x => x.Amount);
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

			return rate;
		}

		/// <summary>
		/// count회 제작에 드는 재료. 생성기 레시피는 보유·대기 중인 대수만큼 비싸진다.
		/// </summary>
		public IReadOnlyList<ItemAmount> GetCost(RecipeDef recipe, long count)
		{
			var multiplier = GetCostMultiplier(recipe, count);
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
			var best = limit;
			foreach (var input in recipe.Inputs)
			{
				var budget = State.GetAmount(input.Item) / input.Amount;
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

				best = Math.Min(best, (long)Math.Clamp(count, 0, limit));
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

			var entry = State.Queue.Count > 0 && State.Queue[^1].Recipe == recipeId ? State.Queue[^1] : null;
			if (entry is null)
			{
				entry = new QueueEntry { Recipe = recipeId };
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
			Refund(entry, (double)cancel / entry.Remaining);
			entry.Remaining -= cancel;
			if (entry.Remaining <= 0)
			{
				State.Queue.RemoveAt(index);
			}

			QueueChanged = true;
		}

		/// <summary>
		/// 게임 시간을 seconds초 진행한다. 큰 값(오프라인 진행)도 정확하게 처리한다.
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
				if (State.Queue.Count == 0)
				{
					Produce(remaining);
					return;
				}

				var entry = State.Queue[0];
				if (entry.Remaining <= 0)
				{
					State.Queue.RemoveAt(0);
					QueueChanged = true;
					continue;
				}

				if (Database.FindRecipe(entry.Recipe) is not { } recipe)
				{
					// 데이터에서 사라진 레시피는 재료를 돌려주고 뺀다
					Refund(entry, 1);
					State.Queue.RemoveAt(0);
					QueueChanged = true;
					continue;
				}

				if (recipe.Time <= 0)
				{
					Complete(entry, recipe, entry.Remaining);
					continue;
				}

				var untilNext = recipe.Time - entry.Progress;
				if (remaining < untilNext)
				{
					Produce(remaining);
					entry.Progress += remaining;
					return;
				}

				// 생성기는 완성될 때마다 생산량이 바뀌므로 한 대씩, 나머지는 한꺼번에 완성한다
				var crafts = Database.FindGeneratorBuiltBy(recipe.Id) is not null
					? 1
					: Math.Min(entry.Remaining, 1 + (long)Math.Floor((remaining - untilNext) / recipe.Time));
				var span = untilNext + (crafts - 1) * recipe.Time;

				Produce(span);
				remaining -= span;
				entry.Progress = 0;
				Complete(entry, recipe, crafts);
			}
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

			var simulated = TimeSpan.FromHours(Math.Min(elapsed.TotalHours, Database.Config.OfflineCapHours));
			var before = new Dictionary<ItemId, double>(State.Inventory);
			Tick(simulated.TotalSeconds);
			State.SavedAt = now;

			var gained = State.Inventory
				.Select(x => (x.Key, Delta: x.Value - before.GetValueOrDefault(x.Key)))
				.Where(x => x.Delta > Quantity.Tolerance(x.Delta))
				.ToDictionary(x => x.Key, x => x.Delta);
			return new OfflineReport(elapsed, simulated, gained);
		}

		private bool CanAfford(IReadOnlyList<ItemAmount> cost) => cost.All(x => Quantity.IsAtLeast(State.GetAmount(x.Item), x.Amount));

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
			foreach (var generator in Database.GeneratorList)
			{
				var owned = GetOwnedCount(generator.Item);
				if (owned <= 0)
				{
					continue;
				}

				foreach (var output in generator.OutputPerSecond)
				{
					State.Add(output.Item, output.Amount * owned * seconds);
					ChangedItems.Add(output.Item);
				}
			}
		}

		private void Complete(QueueEntry entry, RecipeDef recipe, long crafts)
		{
			foreach (var output in recipe.Outputs)
			{
				State.Add(output.Item, output.Amount * crafts);
				ChangedItems.Add(output.Item);
			}

			var used = (double)crafts / entry.Remaining;
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
