using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MachineFree.Core;

using Uranus;
using Uranus.Analysis;
using Uranus.Quantities;

namespace MachineFree.ViewModels
{
	/// <summary>
	/// 인벤토리 한 칸
	/// </summary>
	public partial class SlotViewModel : ObservableObject
	{
		public SlotViewModel(ItemId id, string name)
		{
			Id = id;
			Name = name;
			CountText = "0";
			Details = name;
		}

		public ItemId Id { get; }

		public string Name { get; }

		[ObservableProperty]
		public partial string CountText { get; set; }

		/// <summary>
		/// 다음 1개까지 쌓인 비율 (0~1)
		/// </summary>
		[ObservableProperty]
		public partial double Fraction { get; set; }

		[ObservableProperty]
		public partial bool HasFraction { get; set; }

		/// <summary>
		/// 툴팁 내용
		/// </summary>
		[ObservableProperty]
		public partial string Details { get; set; }
	}

	/// <summary>
	/// 레시피 툴팁의 재료 한 줄: 보유 / 필요 × 이름
	/// </summary>
	public partial class RequirementLine : ObservableObject
	{
		public RequirementLine(ItemId item, string name)
		{
			Item = item;
			Name = name;
			Text = name;
		}

		public ItemId Item { get; }

		public string Name { get; }

		[ObservableProperty]
		public partial string Text { get; set; }

		[ObservableProperty]
		public partial bool IsMet { get; set; }

		public void Update(double have, double need, NumberStyle style)
		{
			Text = $"{QuantityFormatter.FormatCount(have, style)} / {QuantityFormatter.FormatNumber(need, style)} × {Name}";
			IsMet = Quantity.IsAtLeast(have, need);
		}
	}

	/// <summary>
	/// 레시피 칸. 클릭 1개, 우클릭 5개, Shift+클릭 가능한 만큼 만든다.
	/// </summary>
	public partial class RecipeCardViewModel : ObservableObject
	{
		private readonly Func<RecipeDef, long, bool> enqueue;
		private readonly Func<RecipeDef, long> maxCraftable;
		private readonly string searchText;

		public RecipeCardViewModel(RecipeDef recipe, UranusDatabase db, Func<RecipeDef, long, bool> enqueue, Func<RecipeDef, long> maxCraftable)
		{
			this.enqueue = enqueue;
			this.maxCraftable = maxCraftable;

			Recipe = recipe;
			Title = db.GetName(recipe);
			TileName = recipe.Name?.Ko ?? (recipe.Outputs.Count > 0 ? db.GetName(recipe.Outputs[0].Item) : recipe.Id.Value);
			TimeText = recipe.Time > 0 ? $"제작 시간 {QuantityFormatter.FormatNumber(recipe.Time)}초" : "제작 시간 미정(즉시)";
			OutputLines = [.. recipe.Outputs.Select(x => $"{QuantityFormatter.FormatNumber(x.Amount)} × {db.GetName(x.Item)}")];
			Inputs = [.. recipe.Inputs.Select(x => new RequirementLine(x.Item, db.GetName(x.Item)))];
			CraftableText = "";
			RawMaterialLines = BuildRawMaterialLines(recipe, db);

			searchText = string.Join(" ", new[] { Title, TileName, recipe.Id.Value }
				.Concat(recipe.Inputs.Concat(recipe.Outputs).SelectMany(x => new[] { x.Item.Value, db.GetName(x.Item) })));
		}

		public RecipeDef Recipe { get; }

		public string Title { get; }

		/// <summary>
		/// 칸에 쓰는 짧은 이름
		/// </summary>
		public string TileName { get; }

		public string TimeText { get; }

		public IReadOnlyList<string> OutputLines { get; }

		/// <summary>
		/// 이 레시피 1회를 원료(생성기 산출물 등)까지 펼친 양. 재료가 곧 원료면 비어 있다.
		/// </summary>
		public IReadOnlyList<string> RawMaterialLines { get; }

		public bool HasRawMaterials => RawMaterialLines.Count > 0;

		public ObservableCollection<RequirementLine> Inputs { get; }

		[ObservableProperty]
		public partial long Craftable { get; set; }

		[ObservableProperty]
		public partial string CraftableText { get; set; }

		[ObservableProperty]
		public partial bool IsCraftable { get; set; }

		/// <summary>
		/// 재료를 모두 한 번 이상 가져 봐서 목록에 보이는가
		/// </summary>
		[ObservableProperty]
		public partial bool IsDiscovered { get; set; }

		public bool Matches(string text) => searchText.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);

		public virtual void Refresh(Simulation simulation, NumberStyle style)
		{
			var cost = simulation.GetCost(Recipe, 1);
			for (var i = 0; i < cost.Count && i < Inputs.Count; i++)
			{
				Inputs[i].Update(simulation.GetAmount(cost[i].Item), cost[i].Amount, style);
			}

			Craftable = simulation.GetMaxCraftable(Recipe);
			CraftableText = Craftable > 0 ? QuantityFormatter.FormatCount(Craftable, style) : "";
			IsCraftable = Craftable > 0;
			IsDiscovered = Recipe.Inputs.All(x => simulation.State.Discovered.Contains(x.Item));
		}

		private static List<string> BuildRawMaterialLines(RecipeDef recipe, UranusDatabase db)
		{
			var total = new Dictionary<ItemId, double>();
			foreach (var input in recipe.Inputs)
			{
				foreach (var (item, amount) in RecipeGraph.ComputeBillOfMaterials(db, input.Item, input.Amount))
				{
					total[item] = total.GetValueOrDefault(item) + amount;
				}
			}

			// 재료를 더 펼칠 것이 없으면 따로 보여 주지 않는다
			if (total.Count == recipe.Inputs.Count && recipe.Inputs.All(x => total.TryGetValue(x.Item, out var a) && a == x.Amount))
			{
				return [];
			}

			var lines = total
				.OrderByDescending(x => x.Value)
				.Take(6)
				.Select(x => $"{QuantityFormatter.FormatNumber(x.Value, NumberStyle.Scientific)} × {db.GetName(x.Key)}")
				.ToList();
			if (total.Count > 6)
			{
				lines.Add($"… 외 {total.Count - 6}종");
			}

			return lines;
		}

		[RelayCommand]
		private void CraftOne() => enqueue(Recipe, 1);

		[RelayCommand]
		private void CraftFive() => enqueue(Recipe, Math.Min(5, maxCraftable(Recipe)));

		[RelayCommand]
		private void CraftMax() => enqueue(Recipe, maxCraftable(Recipe));
	}

	/// <summary>
	/// 생성기 칸: 보유 대수와 초당 생산량을 함께 보여 준다.
	/// </summary>
	public partial class GeneratorCardViewModel : RecipeCardViewModel
	{
		private readonly UranusDatabase db;

		public GeneratorCardViewModel(GeneratorDef generator, RecipeDef recipe, UranusDatabase db, Func<RecipeDef, long, bool> enqueue, Func<RecipeDef, long> maxCraftable)
			: base(recipe, db, enqueue, maxCraftable)
		{
			this.db = db;
			Generator = generator;
			PerUnitText = "1대당 " + string.Join(", ", generator.OutputPerSecond.Select(x => $"{db.GetName(x.Item)} {QuantityFormatter.FormatNumber(x.Amount)}/초"));
			OwnedText = "";
			RateText = "";
		}

		public GeneratorDef Generator { get; }

		public string PerUnitText { get; }

		[ObservableProperty]
		public partial string OwnedText { get; set; }

		[ObservableProperty]
		public partial string RateText { get; set; }

		public override void Refresh(Simulation simulation, NumberStyle style)
		{
			base.Refresh(simulation, style);

			var owned = simulation.GetOwnedCount(Generator.Item);
			OwnedText = owned > 0 ? $"×{QuantityFormatter.FormatCount(owned, style)}" : "";
			RateText = owned > 0
				? "현재 " + string.Join(", ", Generator.OutputPerSecond.Select(x => $"{db.GetName(x.Item)} {QuantityFormatter.FormatNumber(x.Amount * owned, style)}/초"))
				: "아직 없음";
		}
	}

	/// <summary>
	/// 대기열 한 칸. 클릭하면 전부, 우클릭하면 1개 취소한다.
	/// </summary>
	public partial class QueueEntryViewModel : ObservableObject
	{
		private readonly Action<int, long> cancel;

		public QueueEntryViewModel(int index, string title, Action<int, long> cancel)
		{
			this.cancel = cancel;
			Index = index;
			Title = title;
			RemainingText = "";
			ProgressText = "";
		}

		public int Index { get; }

		public string Title { get; }

		[ObservableProperty]
		public partial string RemainingText { get; set; }

		/// <summary>
		/// 지금 만드는 1개의 진행률 (0~1)
		/// </summary>
		[ObservableProperty]
		public partial double Progress { get; set; }

		[ObservableProperty]
		public partial string ProgressText { get; set; }

		[RelayCommand]
		private void CancelAll() => cancel(Index, long.MaxValue);

		[RelayCommand]
		private void CancelOne() => cancel(Index, 1);
	}

	public sealed record CategoryOption(string Label, string? Key)
	{
		public override string ToString() => Label;
	}
}
