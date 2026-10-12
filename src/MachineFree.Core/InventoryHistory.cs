using Uranus;

namespace MachineFree.Core
{
	/// <summary>
	/// 보유량을 일정 게임 시간마다 기록한다. 통계 화면용이라 저장하지 않고 이번 실행 동안만 들고 있다.
	/// </summary>
	public sealed class InventoryHistory(double interval = 5, int capacity = 720)
	{
		private readonly List<double> times = [];

		/// <summary>
		/// 아이템별 기록. times와 같은 순서이고, 처음 보이기 전 기록은 0이다.
		/// </summary>
		private readonly Dictionary<ItemId, List<double>> amounts = [];

		/// <summary>
		/// 기록 간격(게임 시간, 초)
		/// </summary>
		public double Interval { get; } = interval;

		/// <summary>
		/// 들고 있는 최대 기록 수. 넘치면 오래된 것부터 버린다.
		/// </summary>
		public int Capacity { get; } = capacity;

		public IReadOnlyList<double> Times => times;

		public IEnumerable<ItemId> Items => amounts.Keys;

		/// <summary>
		/// 마지막 기록에서 Interval 이상 지났으면 지금 보유량을 기록하고 true를 돌려준다.
		/// 시간이 거꾸로 가면(새 게임 등) 기록을 비우고 새로 시작한다.
		/// </summary>
		public bool Record(double time, IReadOnlyDictionary<ItemId, double> inventory)
		{
			if (times.Count > 0 && time < times[^1])
			{
				Clear();
			}

			if (times.Count > 0 && time - times[^1] < Interval - 1e-9)
			{
				return false;
			}

			foreach (var item in inventory.Keys)
			{
				if (!amounts.ContainsKey(item))
				{
					amounts[item] = [.. Enumerable.Repeat(0.0, times.Count)];
				}
			}

			times.Add(time);
			foreach (var (item, list) in amounts)
			{
				list.Add(inventory.GetValueOrDefault(item));
			}

			if (times.Count > Capacity)
			{
				times.RemoveAt(0);
				foreach (var list in amounts.Values)
				{
					list.RemoveAt(0);
				}
			}

			return true;
		}

		public void Clear()
		{
			times.Clear();
			amounts.Clear();
		}

		/// <summary>
		/// 이 아이템의 기록 (Times와 같은 길이). 한 번도 없었으면 빈 목록
		/// </summary>
		public IReadOnlyList<double> GetAmounts(ItemId item) => amounts.TryGetValue(item, out var list) ? list : [];

		/// <summary>
		/// 최근 window초 동안 보유량의 초당 순변화(생산 − 소비). 기록이 2개 미만이면 null
		/// </summary>
		public double? GetRate(ItemId item, double window)
		{
			if (times.Count < 2)
			{
				return null;
			}

			var last = times.Count - 1;
			var first = last - 1;
			while (first > 0 && times[last] - times[first - 1] <= window + 1e-9)
			{
				first--;
			}

			var series = GetAmounts(item);
			if (series.Count == 0)
			{
				return 0;
			}

			return (series[last] - series[first]) / (times[last] - times[first]);
		}
	}
}
