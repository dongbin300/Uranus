namespace Uranus
{
	/// <summary>
	/// 원소·아이템·레시피·생성기 데이터와 조회용 색인.
	/// 잘못된 데이터도 그대로 담아 두고, 문제는 검증기(UranusValidator)가 찾는다.
	/// </summary>
	public sealed class UranusDatabase
	{
		private static readonly IReadOnlyList<RecipeDef> NoRecipes = [];

		private readonly Dictionary<string, ElementDef> elements = [];
		private readonly Dictionary<ItemId, ItemDef> items = [];
		private readonly Dictionary<RecipeId, RecipeDef> recipes = [];
		private readonly Dictionary<ItemId, GeneratorDef> generators = [];
		private readonly Dictionary<RecipeId, GeneratorDef> generatorsByRecipe = [];
		private readonly Dictionary<ItemId, List<RecipeDef>> producers = [];
		private readonly Dictionary<ItemId, List<RecipeDef>> consumers = [];
		private readonly Dictionary<string, ItemDef> itemsByName = [];

		public UranusDatabase(
			IEnumerable<ElementDef> elements,
			IEnumerable<ItemDef> items,
			IEnumerable<RecipeDef> recipes,
			IEnumerable<GeneratorDef> generators,
			GameConfig? config = null)
		{
			ElementList = [.. elements];
			ItemList = [.. items];
			RecipeList = [.. recipes];
			GeneratorList = [.. generators];
			Config = config ?? GameConfig.Default;

			foreach (var element in ElementList)
			{
				this.elements.TryAdd(element.Symbol, element);
			}

			foreach (var item in ItemList)
			{
				this.items.TryAdd(item.Id, item);
				itemsByName.TryAdd(NormalizeName(item.Name.Ko), item);
			}

			foreach (var recipe in RecipeList)
			{
				if (!this.recipes.TryAdd(recipe.Id, recipe))
				{
					continue;
				}

				foreach (var output in recipe.Outputs)
				{
					AddIndex(producers, output.Item, recipe);
				}

				foreach (var input in recipe.Inputs)
				{
					AddIndex(consumers, input.Item, recipe);
				}
			}

			foreach (var generator in GeneratorList)
			{
				this.generators.TryAdd(generator.Item, generator);
				generatorsByRecipe.TryAdd(generator.BuildRecipe, generator);
			}
		}

		/// <summary>
		/// 데이터 파일 순서를 유지한 목록
		/// </summary>
		public IReadOnlyList<ElementDef> ElementList { get; }
		public IReadOnlyList<ItemDef> ItemList { get; }
		public IReadOnlyList<RecipeDef> RecipeList { get; }
		public IReadOnlyList<GeneratorDef> GeneratorList { get; }

		public IReadOnlyDictionary<string, ElementDef> Elements => elements;
		public IReadOnlyDictionary<ItemId, ItemDef> Items => items;
		public IReadOnlyDictionary<RecipeId, RecipeDef> Recipes => recipes;
		public IReadOnlyDictionary<ItemId, GeneratorDef> Generators => generators;

		public GameConfig Config { get; }

		public ItemDef? FindItem(ItemId id) => items.GetValueOrDefault(id);

		public RecipeDef? FindRecipe(RecipeId id) => recipes.GetValueOrDefault(id);

		/// <summary>
		/// 한국어 이름으로 아이템을 찾는다. 공백은 무시한다(예전 세이브 변환용).
		/// </summary>
		public ItemDef? FindItemByName(string name) => itemsByName.GetValueOrDefault(NormalizeName(name));

		/// <summary>
		/// 이 아이템을 결과물로 내는 레시피 (데이터 순서)
		/// </summary>
		public IReadOnlyList<RecipeDef> GetProducers(ItemId item) => producers.TryGetValue(item, out var list) ? list : NoRecipes;

		/// <summary>
		/// 이 아이템을 재료로 쓰는 레시피 (데이터 순서)
		/// </summary>
		public IReadOnlyList<RecipeDef> GetConsumers(ItemId item) => consumers.TryGetValue(item, out var list) ? list : NoRecipes;

		/// <summary>
		/// 레시피가 생성기를 만드는 레시피면 그 생성기를 돌려준다.
		/// </summary>
		public GeneratorDef? FindGeneratorBuiltBy(RecipeId recipe) => generatorsByRecipe.GetValueOrDefault(recipe);

		public string GetName(ItemId id) => items.TryGetValue(id, out var item) ? item.Name.Ko : id.Value;

		public string GetName(RecipeDef recipe) => recipe.Name?.Ko ?? string.Join(", ", recipe.Outputs.Select(x => GetName(x.Item)));

		public static string NormalizeName(string name) => string.Concat(name.Where(c => !char.IsWhiteSpace(c)));

		private static void AddIndex(Dictionary<ItemId, List<RecipeDef>> index, ItemId item, RecipeDef recipe)
		{
			if (!index.TryGetValue(item, out var list))
			{
				list = [];
				index[item] = list;
			}

			if (!list.Contains(recipe))
			{
				list.Add(recipe);
			}
		}
	}
}
