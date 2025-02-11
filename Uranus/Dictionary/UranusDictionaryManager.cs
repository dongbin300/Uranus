using OfficeOpenXml;

using System.Text;

namespace Uranus.Dictionary
{
	public class UranusDictionaryManager
	{
		public static readonly string RecipeXlsxPath = "Dictionary/Uranus-Recipe.xlsx";
		public static readonly string GeneratorXlsxPath = "Dictionary/Uranus-Generator.xlsx";

		public static readonly string BaseItemFilePath = "Dictionary/Uranus-BaseItem.csv";
		public static readonly string RecipeFilePath = "Dictionary/Uranus-Recipe.csv";
		public static readonly string GeneratorFilePath = "Dictionary/Uranus-Generator.csv";
		public static readonly string SaveFilePath = "Dictionary/Save.json";

		public static HashSet<UranusRecipe> Recipes = [];
		public static HashSet<UranusRecipe> Generators = [];
		public static HashSet<UranusItem> Items = [];

		public static void ConvertXlsxToCsv(string xlsxPath, string csvPath)
		{
			using var package = new ExcelPackage(new FileInfo(xlsxPath));
			var worksheet = package.Workbook.Worksheets[0];
			var rowCount = worksheet.Dimension.End.Row;
			var colCount = worksheet.Dimension.End.Column;

			var sb = new StringBuilder();
			for (int row = 1; row <= rowCount; row++)
			{
				for (int col = 1; col <= colCount; col++)
				{
					var cellValue = worksheet.Cells[row, col].Text;
					sb.Append(cellValue);

					if (col < colCount)
					{
						sb.Append(',');
					}
				}
				sb.AppendLine();
			}

			File.WriteAllText(csvPath, sb.ToString());
		}

		public static void ConvertDictionaryToCsv()
		{
			ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
			ConvertXlsxToCsv(RecipeXlsxPath, RecipeFilePath);
			ConvertXlsxToCsv(GeneratorXlsxPath, GeneratorFilePath);
		}

		public static void LoadUranusDictionary()
		{
			// Load base item
			var baseItemData = File.ReadAllLines(BaseItemFilePath);
			var baseItems = new HashSet<UranusItem>(baseItemData.Select(x =>
			{
				var item = new UranusItem();
				item.LoadBaseItem(x);
				return item;
			}));

			// Load recipe
			var recipeData = File.ReadAllLines(RecipeFilePath).Skip(1);
			Recipes = new HashSet<UranusRecipe>(recipeData.Select(x => new UranusRecipe(x)));

			// Load generator
			var generatorData = File.ReadAllLines(GeneratorFilePath).Skip(1);
			Generators = new HashSet<UranusRecipe>(generatorData.Select(x =>
			{
				var recipe = new UranusRecipe();
				recipe.LoadGenerator(x);
				return recipe;
			}));

			// Make item collection from recipe data
			Items = [];
			var producableItems = new HashSet<UranusItem>();

			foreach (var recipe in Recipes)
			{
				var materials = recipe.Materials.Select(x => x.Item);
				var products = recipe.Products.Select(x => x.Item);
				Items.UnionWith(materials);
				Items.UnionWith(products);
				producableItems.UnionWith(products);
			}	

			// Compute improducable items
			var improducableItems = Items.Except(producableItems, new UranusItemComparer()).Except(baseItems, new UranusItemComparer()).ToHashSet();
		}

		public static UranusRecipe GetRecipe(string name)
		{
			return Recipes.First(x => x.Name.Equals(name)) ?? default!;
		}

		public static UranusItem GetItem(string koreanName)
		{
			return Items.First(x => x.KoreanName.Equals(koreanName)) ?? default!;
		}
	}
}
