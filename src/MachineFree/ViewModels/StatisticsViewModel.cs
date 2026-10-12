using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using MachineFree.Core;

using Uranus;
using Uranus.Quantities;

namespace MachineFree.ViewModels
{
	/// <summary>
	/// 통계 탭: 아이템별 보유량·순변화·생성기 생산량과, 고른 아이템의 보유량 그래프(로그 눈금)
	/// </summary>
	public partial class StatisticsViewModel : ObservableObject
	{
		public const double ChartWidth = 520;
		public const double ChartHeight = 180;

		/// <summary>
		/// 순변화를 계산하는 최근 시간(초)
		/// </summary>
		public const double RateWindow = 60;

		private readonly Simulation simulation;
		private readonly Dictionary<ItemId, StatRowViewModel> rows = [];
		private readonly Dictionary<ItemId, int> order;
		private NumberStyle style = NumberStyle.Short;

		public StatisticsViewModel(Simulation simulation)
		{
			this.simulation = simulation;
			order = simulation.Database.ItemList.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
			ChartPoints = [];
			ChartTitle = "";
			ChartMaxText = "";
			ChartMinText = "";
			ChartSpanText = "";
			Record();
		}

		public InventoryHistory History { get; } = new();

		public ObservableCollection<StatRowViewModel> Rows { get; } = [];

		[ObservableProperty]
		public partial StatRowViewModel? SelectedRow { get; set; }

		[ObservableProperty]
		public partial PointCollection ChartPoints { get; set; }

		[ObservableProperty]
		public partial string ChartTitle { get; set; }

		[ObservableProperty]
		public partial string ChartMaxText { get; set; }

		[ObservableProperty]
		public partial string ChartMinText { get; set; }

		[ObservableProperty]
		public partial string ChartSpanText { get; set; }

		/// <summary>
		/// 기록할 때가 되었으면 지금 보유량을 기록한다
		/// </summary>
		public bool Record() => History.Record(simulation.State.PlayTime, simulation.State.Inventory);

		public void Refresh(NumberStyle style)
		{
			this.style = style;
			foreach (var item in History.Items.Concat(simulation.State.Inventory.Keys).Distinct())
			{
				if (!rows.TryGetValue(item, out var row))
				{
					row = new StatRowViewModel(item, simulation.Database.GetName(item));
					rows[item] = row;
					var index = 0;
					var key = order.GetValueOrDefault(item, int.MaxValue);
					while (index < Rows.Count && order.GetValueOrDefault(Rows[index].Id, int.MaxValue) <= key)
					{
						index++;
					}

					Rows.Insert(index, row);
				}

				row.AmountText = QuantityFormatter.FormatCount(simulation.GetAmount(item), style);
				var rate = History.GetRate(item, RateWindow);
				row.RateText = rate is { } r && Math.Abs(r) > Quantity.Tolerance(simulation.GetAmount(item))
					? (r > 0 ? "+" : "−") + QuantityFormatter.FormatNumber(Math.Abs(r) * 60, style) + "/분"
					: "";
				var produced = simulation.GetProductionRate(item);
				row.GeneratorText = produced > 0 ? QuantityFormatter.FormatNumber(produced, style) + "/초" : "";
			}

			SelectedRow ??= Rows.FirstOrDefault();
			UpdateChart();
		}

		partial void OnSelectedRowChanged(StatRowViewModel? value) => UpdateChart();

		private void UpdateChart()
		{
			var times = History.Times;
			if (SelectedRow is null || times.Count == 0)
			{
				ChartPoints = [];
				ChartTitle = SelectedRow?.Name ?? "";
				ChartMaxText = ChartMinText = ChartSpanText = "";
				return;
			}

			var series = History.GetAmounts(SelectedRow.Id);
			var positive = series.Where(x => x > 0).ToList();
			double low, high;
			if (positive.Count == 0)
			{
				low = 0;
				high = 1;
			}
			else
			{
				low = Math.Floor(Math.Log10(positive.Min()));
				high = Math.Ceiling(Math.Log10(positive.Max()));
				if (high - low < 1)
				{
					high = low + 1;
				}
			}

			// 0 이하는 맨 아래, 기록이 하나뿐이면 가로선으로 그린다
			var span = times[^1] - times[0];
			var points = new PointCollection(series.Count);
			for (var i = 0; i < series.Count; i++)
			{
				var x = span > 0 ? (times[i] - times[0]) / span * ChartWidth : i * ChartWidth;
				var level = series[i] > 0 ? Math.Clamp((Math.Log10(series[i]) - low) / (high - low), 0, 1) : 0;
				points.Add(new Point(x, ChartHeight - level * ChartHeight));
			}

			if (series.Count == 1)
			{
				points.Add(new Point(ChartWidth, points[0].Y));
			}

			points.Freeze();
			ChartPoints = points;
			ChartTitle = $"{SelectedRow.Name} 보유량 (로그 눈금)";
			ChartMaxText = QuantityFormatter.FormatCount(Math.Pow(10, high), style);
			ChartMinText = positive.Count > 0 ? QuantityFormatter.FormatCount(Math.Pow(10, low), style) : "0";
			ChartSpanText = span > 0 ? $"최근 {MainViewModel.FormatDuration(span)} · {QuantityFormatter.FormatNumber(History.Interval)}초마다 기록" : "기록 중…";
		}
	}

	/// <summary>
	/// 통계 표의 한 줄
	/// </summary>
	public partial class StatRowViewModel : ObservableObject
	{
		public StatRowViewModel(ItemId id, string name)
		{
			Id = id;
			Name = name;
			AmountText = "";
			RateText = "";
			GeneratorText = "";
		}

		public ItemId Id { get; }

		public string Name { get; }

		[ObservableProperty]
		public partial string AmountText { get; set; }

		/// <summary>
		/// 최근 1분 순변화(생산 − 소비)를 분당으로
		/// </summary>
		[ObservableProperty]
		public partial string RateText { get; set; }

		/// <summary>
		/// 생성기의 초당 생산량
		/// </summary>
		[ObservableProperty]
		public partial string GeneratorText { get; set; }
	}
}
