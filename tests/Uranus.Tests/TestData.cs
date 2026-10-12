using Uranus.Data;

namespace Uranus.Tests
{
	/// <summary>
	/// 저장소의 data 폴더와 테스트용 작은 데이터베이스
	/// </summary>
	internal static class TestData
	{
		private static readonly Lazy<LoadResult> repository = new(() => UranusDataLoader.Load(DataDirectory));

		public static string DataDirectory =>
			UranusDataLoader.FindDataDirectory(AppContext.BaseDirectory) ?? throw new InvalidOperationException("data 폴더를 찾지 못했다");

		public static LoadResult Repository => repository.Value;

		public static readonly ElementDef[] Elements =
		[
			new("H", new("수소"), 1.008),
			new("C", new("탄소"), 12.011),
			new("O", new("산소"), 15.999),
		];

		public static ItemDef Item(string id, string? formula = null, ItemUnit unit = ItemUnit.Particle) =>
			new(new ItemId(id), new LocalizedText(id), "test", unit) { Formula = formula };

		public static RecipeDef Recipe(string id, (string Item, double Amount)[] inputs, (string Item, double Amount)[] outputs, double time = 1) =>
			new(new RecipeId(id), "test", time,
				[.. inputs.Select(x => new ItemAmount(new ItemId(x.Item), x.Amount))],
				[.. outputs.Select(x => new ItemAmount(new ItemId(x.Item), x.Amount))]);

		public static UranusDatabase Create(IEnumerable<ItemDef> items, IEnumerable<RecipeDef> recipes, params (string Item, double Amount)[] start) =>
			new(Elements, items, recipes, [], new GameConfig([.. start.Select(x => new ItemAmount(new ItemId(x.Item), x.Amount))], 1.15, 24));
	}
}
