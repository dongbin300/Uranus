using Uranus;
using Uranus.Quantities;

namespace MachineFree.Core
{
	/// <summary>
	/// 저장되는 게임 상태. 게임 규칙은 Simulation에 있다.
	/// </summary>
	public sealed class GameState
	{
		/// <summary>
		/// 보유 수량. 소수(다음 1개까지 쌓인 양)도 그대로 들고 있다.
		/// </summary>
		public Dictionary<ItemId, double> Inventory { get; } = [];

		public List<QueueEntry> Queue { get; } = [];

		/// <summary>
		/// 한 번이라도 1개 이상 가져 본 아이템. 레시피 공개에 쓴다.
		/// </summary>
		public HashSet<ItemId> Discovered { get; } = [];

		/// <summary>
		/// 산 업그레이드 ID
		/// </summary>
		public HashSet<string> Upgrades { get; } = [];

		/// <summary>
		/// 자동 반복을 켠 레시피. 자동화 업그레이드가 있으면 대기열에서 빠질 때마다 다시 주문한다.
		/// </summary>
		public HashSet<RecipeId> AutoRepeat { get; } = [];

		/// <summary>
		/// 레시피를 돌릴 규모. null이면 가진 것 중 가장 큰 규모, Simulation.UnitScaleId면 분자 단위, 아니면 규모 업그레이드 ID
		/// </summary>
		public string? CraftScale { get; set; }

		/// <summary>
		/// 달성한 목표 ID → 달성한 게임 시간(초)
		/// </summary>
		public Dictionary<string, double> CompletedGoals { get; } = [];

		/// <summary>
		/// 진행된 게임 시간(초)
		/// </summary>
		public double PlayTime { get; set; }

		/// <summary>
		/// 완성한 제작 횟수
		/// </summary>
		public long TotalCrafts { get; set; }

		public DateTimeOffset SavedAt { get; set; }

		public double GetAmount(ItemId item) => Inventory.GetValueOrDefault(item);

		public void Add(ItemId item, double amount)
		{
			if (!(amount > 0) || !double.IsFinite(amount))
			{
				return;
			}

			var total = Math.Min(GetAmount(item) + amount, double.MaxValue);
			Inventory[item] = total;
			if (Quantity.IsAtLeast(total, 1))
			{
				Discovered.Add(item);
			}
		}

		public void Remove(ItemId item, double amount)
		{
			var rest = Quantity.Subtract(GetAmount(item), amount);
			if (rest > 0)
			{
				Inventory[item] = rest;
			}
			else
			{
				Inventory.Remove(item);
			}
		}

		public static GameState CreateNew(UranusDatabase db)
		{
			var state = new GameState { SavedAt = DateTimeOffset.UtcNow };
			foreach (var x in db.Config.StartInventory)
			{
				state.Add(x.Item, x.Amount);
			}

			return state;
		}
	}

	/// <summary>
	/// 제작 대기열 한 칸. 같은 레시피를 Remaining번 만든다.
	/// </summary>
	public sealed class QueueEntry
	{
		public required RecipeId Recipe { get; init; }

		public long Remaining { get; set; }

		/// <summary>
		/// 주문할 때의 규모 배율. 1회에 결과물이 이만큼 곱해져 나온다.
		/// </summary>
		public double Scale { get; set; } = 1;

		/// <summary>
		/// 지금 만들고 있는 1회의 경과 시간(초)
		/// </summary>
		public double Progress { get; set; }

		/// <summary>
		/// 남은 횟수만큼 미리 낸 재료. 취소하면 비율대로 돌려준다.
		/// </summary>
		public Dictionary<ItemId, double> Paid { get; } = [];
	}
}
