using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MachineFree.Core;

using Uranus;
using Uranus.Analysis;
using Uranus.Chemistry;
using Uranus.Quantities;

namespace MachineFree.ViewModels
{
	/// <summary>
	/// 메인 화면. GameSession이 매 틱 Refresh를 부른다.
	/// </summary>
	public partial class MainViewModel : ObservableObject
	{
		private readonly Simulation simulation;
		private readonly UranusDatabase db;
		private readonly MassTable masses;
		private readonly Dictionary<ItemId, SlotViewModel> slots = [];
		private int discoveredCount = -1;
		private string craftableSignature = "";

		public MainViewModel(Simulation simulation)
		{
			this.simulation = simulation;
			db = simulation.Database;
			masses = RecipeGraph.ComputeMasses(db);

			Categories =
			[
				new("전체", null),
				new("화학", "chemistry"),
				new("원료 가공", "material"),
				new("생물", "biology"),
				new("전자", "electronics"),
			];
			SelectedCategory = Categories[0];
			SearchText = "";
			HiddenRecipesText = "";
			StatusText = "";
			OfflineMessage = "";
			NumberStyleLabel = GetNumberStyleLabel(NumberStyle.Short);

			foreach (var recipe in db.RecipeList)
			{
				if (db.FindGeneratorBuiltBy(recipe.Id) is { } generator)
				{
					Generators.Add(new GeneratorCardViewModel(generator, recipe, db, Enqueue, x => simulation.GetMaxCraftable(x)));
				}
				else
				{
					Recipes.Add(new RecipeCardViewModel(recipe, db, Enqueue, x => simulation.GetMaxCraftable(x)));
				}
			}

			RecipesView = CollectionViewSource.GetDefaultView(Recipes);
			RecipesView.Filter = x => FilterRecipe((RecipeCardViewModel)x);
			GeneratorsView = CollectionViewSource.GetDefaultView(Generators);
			GeneratorsView.Filter = x => ((RecipeCardViewModel)x).IsDiscovered;

			Refresh(force: true);

			// 아직 보이는 레시피가 없으면 생성기 탭에서 시작한다
			SelectedTab = Recipes.Any(x => x.IsDiscovered) ? 0 : 1;
		}

		public ObservableCollection<SlotViewModel> Inventory { get; } = [];

		/// <summary>
		/// 0: 레시피, 1: 생성기
		/// </summary>
		[ObservableProperty]
		public partial int SelectedTab { get; set; }

		public ObservableCollection<RecipeCardViewModel> Recipes { get; } = [];

		public ObservableCollection<GeneratorCardViewModel> Generators { get; } = [];

		public ICollectionView RecipesView { get; }

		public ICollectionView GeneratorsView { get; }

		/// <summary>
		/// 대기열에서 지금 만들고 있는 칸 (없으면 null)
		/// </summary>
		[ObservableProperty]
		public partial QueueEntryViewModel? CurrentQueue { get; set; }

		public ObservableCollection<QueueEntryViewModel> WaitingQueue { get; } = [];

		public IReadOnlyList<CategoryOption> Categories { get; }

		[ObservableProperty]
		public partial CategoryOption SelectedCategory { get; set; }

		[ObservableProperty]
		public partial string SearchText { get; set; }

		[ObservableProperty]
		public partial bool ShowCraftableOnly { get; set; }

		[ObservableProperty]
		public partial string HiddenRecipesText { get; set; }

		[ObservableProperty]
		public partial string StatusText { get; set; }

		[ObservableProperty]
		public partial string OfflineMessage { get; set; }

		[ObservableProperty]
		public partial bool HasOfflineMessage { get; set; }

		[ObservableProperty]
		public partial NumberStyle NumberStyle { get; set; }

		[ObservableProperty]
		public partial string NumberStyleLabel { get; set; }

		/// <summary>
		/// 시뮬레이션 상태를 화면에 반영한다.
		/// </summary>
		public void Refresh() => Refresh(force: false);

		public void ShowOfflineReport(OfflineReport report, IEnumerable<string> warnings)
		{
			var lines = new List<string>();
			if (report.Gained.Count > 0)
			{
				var capped = report.Simulated < report.Elapsed ? $", 최대 {FormatDuration(report.Simulated.TotalSeconds)}만 반영" : "";
				var gains = report.Gained
					.OrderByDescending(x => x.Value)
					.Take(6)
					.Select(x => $"{db.GetName(x.Key)} +{QuantityFormatter.FormatCount(x.Value, NumberStyle)}");
				lines.Add($"자리를 비운 동안({FormatDuration(report.Elapsed.TotalSeconds)}{capped}) 얻은 것: {string.Join(", ", gains)}");
			}

			lines.AddRange(warnings);
			OfflineMessage = string.Join(Environment.NewLine, lines);
			HasOfflineMessage = lines.Count > 0;
		}

		[RelayCommand]
		private void DismissOffline() => HasOfflineMessage = false;

		[RelayCommand]
		private void CycleNumberStyle()
		{
			NumberStyle = NumberStyle switch
			{
				NumberStyle.Short => NumberStyle.Korean,
				NumberStyle.Korean => NumberStyle.Scientific,
				_ => NumberStyle.Short,
			};
		}

		private static string GetNumberStyleLabel(NumberStyle style) => style switch
		{
			NumberStyle.Korean => "숫자: 만·억·조",
			NumberStyle.Scientific => "숫자: 1e24",
			_ => "숫자: k·m·b",
		};

		partial void OnNumberStyleChanged(NumberStyle value)
		{
			NumberStyleLabel = GetNumberStyleLabel(value);
			if (Inventory.Count > 0 || Recipes.Count > 0)
			{
				Refresh(force: true);
			}
		}

		partial void OnSelectedCategoryChanged(CategoryOption value) => RecipesView?.Refresh();

		partial void OnSearchTextChanged(string value) => RecipesView?.Refresh();

		partial void OnShowCraftableOnlyChanged(bool value) => RecipesView?.Refresh();

		private void Refresh(bool force)
		{
			List<ItemId> changed = force ? [.. simulation.State.Inventory.Keys, .. slots.Keys] : [.. simulation.ChangedItems];
			simulation.ChangedItems.Clear();

			foreach (var item in changed.Distinct())
			{
				UpdateSlot(item);
			}

			if (force || changed.Count > 0)
			{
				foreach (var card in Recipes)
				{
					card.Refresh(simulation, NumberStyle);
				}

				foreach (var card in Generators)
				{
					card.Refresh(simulation, NumberStyle);
				}

				RefreshViews(force);
			}

			if (force || simulation.QueueChanged)
			{
				simulation.QueueChanged = false;
				RebuildQueue();
			}

			UpdateQueueProgress();
			StatusText = $"플레이 {FormatDuration(simulation.State.PlayTime)} · 제작 {simulation.State.TotalCrafts:N0}회";
		}

		private void UpdateSlot(ItemId item)
		{
			var amount = simulation.GetAmount(item);
			if (!(amount > 0))
			{
				if (slots.Remove(item, out var removed))
				{
					Inventory.Remove(removed);
				}

				return;
			}

			if (!slots.TryGetValue(item, out var slot))
			{
				slot = new SlotViewModel(item, db.GetName(item));
				slots[item] = slot;
				Inventory.Add(slot);
			}

			slot.CountText = QuantityFormatter.FormatCount(amount, NumberStyle);
			slot.Fraction = Quantity.Fraction(amount);
			slot.HasFraction = slot.Fraction > 0.001 && amount < 1e9;
			slot.Details = BuildDetails(item, amount);
		}

		private string BuildDetails(ItemId id, double amount)
		{
			var item = db.FindItem(id);
			var lines = new List<string> { $"{db.GetName(id)}  ({id})" };

			lines.Add(amount < 1e6
				? $"보유 {amount.ToString(amount < 1000 ? "#,0.##" : "#,0")}"
				: $"보유 {QuantityFormatter.FormatCount(amount, NumberStyle.Scientific)}");

			if (item?.Unit == ItemUnit.Particle && amount >= 1e12)
			{
				lines.Add($"≈ {QuantityFormatter.FormatNumber(amount / Formula.Avogadro, NumberStyle)} mol");
			}

			var rate = simulation.GetProductionRate(id);
			if (rate > 0)
			{
				lines.Add($"생산 +{QuantityFormatter.FormatNumber(rate, NumberStyle)}/초");
			}

			if (db.Generators.TryGetValue(id, out var generator))
			{
				lines.Add("1대당 " + string.Join(", ", generator.OutputPerSecond.Select(x => $"{db.GetName(x.Item)} {QuantityFormatter.FormatNumber(x.Amount)}/초")));
			}

			if (item?.Formula is { } formula)
			{
				lines.Add($"화학식 {formula}");
			}

			if (masses.GetMass(id) is { } grams)
			{
				lines.Add(Quantity.IsAtLeast(amount, 1)
					? $"질량 1개 {QuantityFormatter.FormatMass(grams)} · 보유분 {QuantityFormatter.FormatMass(grams * amount)}"
					: $"질량 1개 {QuantityFormatter.FormatMass(grams)}");
			}

			if (item?.Note is { } note)
			{
				lines.Add(note);
			}

			return string.Join(Environment.NewLine, lines);
		}

		private void RefreshViews(bool force)
		{
			var discovered = simulation.State.Discovered.Count;
			var signature = ShowCraftableOnly ? string.Concat(Recipes.Select(x => x.IsCraftable ? '1' : '0')) : "";
			if (!force && discovered == discoveredCount && signature == craftableSignature)
			{
				return;
			}

			discoveredCount = discovered;
			craftableSignature = signature;
			RecipesView.Refresh();
			GeneratorsView.Refresh();

			var hidden = Recipes.Count(x => !x.IsDiscovered);
			HiddenRecipesText = hidden > 0 ? $"미발견 레시피 {hidden}개 · 재료를 한 번씩 모으면 나타난다" : "";
		}

		private bool FilterRecipe(RecipeCardViewModel card) =>
			card.IsDiscovered &&
			(SelectedCategory.Key is null || card.Recipe.Category == SelectedCategory.Key) &&
			(!ShowCraftableOnly || card.IsCraftable) &&
			(string.IsNullOrWhiteSpace(SearchText) || card.Matches(SearchText));

		private void RebuildQueue()
		{
			WaitingQueue.Clear();
			CurrentQueue = null;
			for (var i = 0; i < simulation.State.Queue.Count; i++)
			{
				var entry = simulation.State.Queue[i];
				var title = db.FindRecipe(entry.Recipe) is { } recipe ? db.GetName(recipe) : entry.Recipe.Value;
				var vm = new QueueEntryViewModel(i, title, Cancel);
				if (i == 0)
				{
					CurrentQueue = vm;
				}
				else
				{
					WaitingQueue.Add(vm);
				}
			}

			foreach (var vm in WaitingQueue)
			{
				vm.RemainingText = QuantityFormatter.FormatCount(simulation.State.Queue[vm.Index].Remaining, NumberStyle);
			}
		}

		private void UpdateQueueProgress()
		{
			if (CurrentQueue is null || simulation.State.Queue.Count == 0)
			{
				return;
			}

			var entry = simulation.State.Queue[0];
			var time = db.FindRecipe(entry.Recipe)?.Time ?? 0;
			var progress = time > 0 ? Math.Clamp(entry.Progress / time, 0, 1) : 1;
			var left = Math.Max(0, entry.Remaining * time - entry.Progress);

			CurrentQueue.Progress = progress;
			CurrentQueue.RemainingText = QuantityFormatter.FormatCount(entry.Remaining, NumberStyle);
			CurrentQueue.ProgressText = $"{(int)(progress * 100)}% · 남은 {QuantityFormatter.FormatCount(entry.Remaining, NumberStyle)}개 · 약 {FormatDuration(left)}";
		}

		private bool Enqueue(RecipeDef recipe, long count)
		{
			if (count <= 0 || !simulation.TryEnqueue(recipe.Id, count))
			{
				return false;
			}

			Refresh();
			return true;
		}

		private void Cancel(int index, long count)
		{
			simulation.Cancel(index, count);
			Refresh();
		}

		private static string FormatDuration(double seconds)
		{
			var time = TimeSpan.FromSeconds(Math.Max(0, Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds - 1)));
			if (time.TotalDays >= 1)
			{
				return $"{(int)time.TotalDays}일 {time.Hours}시간";
			}

			if (time.TotalHours >= 1)
			{
				return $"{(int)time.TotalHours}시간 {time.Minutes}분";
			}

			return time.TotalMinutes >= 1 ? $"{time.Minutes}분 {time.Seconds}초" : $"{time.TotalSeconds:0}초";
		}
	}
}
