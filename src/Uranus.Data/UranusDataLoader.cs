using Uranus.Quantities;
using Uranus.Validation;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Uranus.Data
{
	public sealed class LoadResult
	{
		public required UranusDatabase Database { get; init; }

		/// <summary>
		/// 파일을 읽으면서 생긴 문제 (문법 오류, 중복 ID, 잘못된 수량)
		/// </summary>
		public required IReadOnlyList<DataIssue> Issues { get; init; }

		public bool HasErrors => Issues.Any(x => x.Severity == IssueSeverity.Error);
	}

	/// <summary>
	/// data 폴더의 YAML을 읽어 UranusDatabase를 만든다.
	/// elements.yaml, game.yaml, generators.yaml, items/*.yaml, recipes/*.yaml
	/// </summary>
	public static class UranusDataLoader
	{
		public const string SyntaxErrorCode = "L001";
		public const string DuplicateIdCode = "L002";
		public const string InvalidValueCode = "L003";

		public static LoadResult Load(string dataDirectory)
		{
			var context = new LoadContext(dataDirectory);

			var elements = context.Entries(context.Read<ElementFileDto>("elements.yaml")?.Elements, "elements.yaml")
				.Select(x => new ElementDef(x.Symbol ?? "", ToText(x.Name, x.Symbol ?? ""), x.Mass))
				.ToList();

			var items = new List<ItemDef>();
			var recipes = new List<RecipeDef>();
			var generators = new List<GeneratorDef>();

			foreach (var path in context.ListFiles("items"))
			{
				if (context.Read<ItemFileDto>(path) is { } file)
				{
					items.AddRange(context.Entries(file.Items, path).Select(x => context.ToItem(x, file, path)).OfType<ItemDef>());
				}
			}

			if (context.Read<GeneratorFileDto>("generators.yaml") is { } generatorFile)
			{
				foreach (var dto in context.Entries(generatorFile.Generators, "generators.yaml"))
				{
					context.AddGenerator(dto, items, recipes, generators);
				}
			}

			foreach (var path in context.ListFiles("recipes"))
			{
				if (context.Read<RecipeFileDto>(path) is { } file)
				{
					recipes.AddRange(context.Entries(file.Recipes, path).Select(x => context.ToRecipe(x, file.Category ?? "misc", path)).OfType<RecipeDef>());
				}
			}

			var config = context.Read<GameFileDto>("game.yaml") is { } game ? context.ToConfig(game) : GameConfig.Default;

			context.CheckDuplicates(items.Select(x => (x.Id.Value, x.Source)), "아이템");
			context.CheckDuplicates(recipes.Select(x => (x.Id.Value, x.Source)), "레시피");
			context.CheckDuplicates(elements.Select(x => (x.Symbol, (string?)"elements.yaml")), "원소");

			return new LoadResult
			{
				Database = new UranusDatabase(elements, items, recipes, generators, config),
				Issues = context.Issues,
			};
		}

		/// <summary>
		/// 주어진 폴더들에서 위로 올라가며 data/elements.yaml이 있는 data 폴더를 찾는다.
		/// </summary>
		public static string? FindDataDirectory(params string?[] startDirectories)
		{
			foreach (var start in startDirectories.OfType<string>())
			{
				for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
				{
					var candidate = Path.Combine(dir.FullName, "data");
					if (File.Exists(Path.Combine(candidate, "elements.yaml")))
					{
						return candidate;
					}
				}
			}

			return null;
		}

		private static LocalizedText ToText(NameDto? name, string fallback) =>
			new(string.IsNullOrWhiteSpace(name?.Ko) ? fallback : name.Ko, name?.En);

		private sealed class LoadContext(string dataDirectory)
		{
			private readonly IDeserializer deserializer = new DeserializerBuilder()
				.WithNamingConvention(UnderscoredNamingConvention.Instance)
				.WithDuplicateKeyChecking()
				.Build();

			public List<DataIssue> Issues { get; } = [];

			public IEnumerable<string> ListFiles(string folder)
			{
				var directory = Path.Combine(dataDirectory, folder);
				return Directory.Exists(directory)
					? Directory.GetFiles(directory, "*.yaml").Order(StringComparer.Ordinal).Select(x => Path.GetRelativePath(dataDirectory, x))
					: [];
			}

			public T? Read<T>(string relativePath) where T : class
			{
				var path = Path.Combine(dataDirectory, relativePath);
				if (!File.Exists(path))
				{
					Issues.Add(new DataIssue(IssueSeverity.Error, SyntaxErrorCode, "파일이 없다", Display(relativePath)));
					return null;
				}

				try
				{
					return deserializer.Deserialize<T>(File.ReadAllText(path));
				}
				catch (YamlException e)
				{
					var message = e.InnerException?.Message ?? e.Message;
					Issues.Add(new DataIssue(IssueSeverity.Error, SyntaxErrorCode, $"{e.Start.Line}행 {e.Start.Column}열: {message}", Display(relativePath)));
					return null;
				}
			}

			/// <summary>
			/// 목록의 항목. 목록이 비어 있으면(null) 빈 목록, 빈 항목(-)은 문제로 보고하고 건너뛴다.
			/// </summary>
			public IEnumerable<T> Entries<T>(List<T?>? list, string path) where T : class
			{
				foreach (var entry in list ?? [])
				{
					if (entry is null)
					{
						Invalid("빈 항목(-)이 있다", Display(path));
						continue;
					}

					yield return entry;
				}
			}

			public ItemDef? ToItem(ItemDto dto, ItemFileDto file, string path)
			{
				var source = Display(path);
				if (string.IsNullOrWhiteSpace(dto.Id))
				{
					Invalid("id가 없는 아이템이 있다", source);
					return null;
				}

				var unitText = dto.Unit ?? file.Unit ?? "particle";
				if (!Enum.TryParse<ItemUnit>(unitText, ignoreCase: true, out var unit))
				{
					Invalid($"아이템 '{dto.Id}'의 단위 '{unitText}'를 알 수 없다(particle, piece)", source);
				}

				MassRange? expectedMass = null;
				if (dto.ExpectedMass is { } range)
				{
					if (range.Count == 2 && MassText.TryParse(range[0], out var min) && MassText.TryParse(range[1], out var max) && min <= max)
					{
						expectedMass = new MassRange(min, max);
					}
					else
					{
						Invalid($"아이템 '{dto.Id}'의 expected_mass는 [최소, 최대] 형식이어야 한다(예: [45g, 60g])", source);
					}
				}

				return new ItemDef(new ItemId(dto.Id), ToText(dto.Name, dto.Id), dto.Category ?? file.Category ?? "misc", unit)
				{
					Formula = dto.Formula,
					IsGoal = dto.Goal,
					IsFictional = dto.Fictional,
					ExpectedMass = expectedMass,
					Note = dto.Note,
					Description = dto.Description,
					Source = source,
				};
			}

			public RecipeDef? ToRecipe(RecipeDto dto, string category, string path)
			{
				var source = Display(path);
				if (string.IsNullOrWhiteSpace(dto.Id))
				{
					Invalid("id가 없는 레시피가 있다", source);
					return null;
				}

				return new RecipeDef(new RecipeId(dto.Id), dto.Category ?? category, dto.Time,
					ToAmounts(dto.In, $"레시피 '{dto.Id}'", source),
					ToAmounts(dto.Out, $"레시피 '{dto.Id}'", source))
				{
					Name = dto.Name is null ? null : ToText(dto.Name, dto.Id),
					IsApproximate = dto.Approximate,
					Note = dto.Note,
					Source = source,
				};
			}

			public void AddGenerator(GeneratorDto dto, List<ItemDef> items, List<RecipeDef> recipes, List<GeneratorDef> generators)
			{
				const string path = "generators.yaml";
				var source = Display(path);
				if (string.IsNullOrWhiteSpace(dto.Id))
				{
					Invalid("id가 없는 생성기가 있다", source);
					return;
				}

				var item = new ItemId(dto.Id);
				var build = new RecipeId("build_" + dto.Id);
				items.Add(new ItemDef(item, ToText(dto.Name, dto.Id), "generator", ItemUnit.Piece) { Note = dto.Note, Source = source });
				recipes.Add(new RecipeDef(build, "generator", dto.Build?.Time ?? 0,
					ToAmounts(dto.Build?.In, $"생성기 '{dto.Id}'", source),
					[new ItemAmount(item, 1)])
				{
					Source = source,
				});
				generators.Add(new GeneratorDef(item, build, ToAmounts(dto.Produces, $"생성기 '{dto.Id}'", source)));
			}

			public GameConfig ToConfig(GameFileDto dto) => new(
				ToAmounts(dto.Start?.Inventory, "시작 인벤토리", Display("game.yaml")),
				dto.GeneratorCostGrowth ?? 1.0,
				dto.OfflineCapHours ?? 24.0);

			public void CheckDuplicates(IEnumerable<(string Id, string? Source)> entries, string kind)
			{
				foreach (var group in entries.GroupBy(x => x.Id).Where(g => g.Count() > 1))
				{
					var sources = string.Join(", ", group.Select(x => x.Source).Distinct());
					Issues.Add(new DataIssue(IssueSeverity.Error, DuplicateIdCode, $"{kind} ID '{group.Key}'가 {group.Count()}번 정의됐다", sources));
				}
			}

			private List<ItemAmount> ToAmounts(Dictionary<string, string>? amounts, string owner, string source)
			{
				var result = new List<ItemAmount>();
				foreach (var (id, text) in amounts ?? [])
				{
					if (AmountText.TryParse(text, out var value))
					{
						result.Add(new ItemAmount(new ItemId(id), value));
					}
					else
					{
						Invalid($"{owner}의 '{id}' 수량 '{text}'를 읽을 수 없다", source);
					}
				}

				return result;
			}

			private void Invalid(string message, string source) =>
				Issues.Add(new DataIssue(IssueSeverity.Error, InvalidValueCode, message, source));

			private static string Display(string relativePath) => "data/" + relativePath.Replace('\\', '/');
		}

		// YAML 매핑용 DTO. 키 이름은 snake_case(expected_mass)로 쓴다.

		private sealed class NameDto
		{
			public string? Ko { get; set; }
			public string? En { get; set; }
		}

		private sealed class ElementFileDto
		{
			public List<ElementDto?>? Elements { get; set; } = [];
		}

		private sealed class ElementDto
		{
			public string? Symbol { get; set; }
			public NameDto? Name { get; set; }
			public double Mass { get; set; }
		}

		private sealed class ItemFileDto
		{
			public string? Category { get; set; }
			public string? Unit { get; set; }
			public List<ItemDto?>? Items { get; set; } = [];
		}

		private sealed class ItemDto
		{
			public string? Id { get; set; }
			public NameDto? Name { get; set; }
			public string? Category { get; set; }
			public string? Unit { get; set; }
			public string? Formula { get; set; }
			public bool Goal { get; set; }
			public bool Fictional { get; set; }
			public List<string>? ExpectedMass { get; set; }
			public string? Note { get; set; }
			public string? Description { get; set; }
		}

		private sealed class RecipeFileDto
		{
			public string? Category { get; set; }
			public List<RecipeDto?>? Recipes { get; set; } = [];
		}

		private sealed class RecipeDto
		{
			public string? Id { get; set; }
			public NameDto? Name { get; set; }
			public string? Category { get; set; }
			public double Time { get; set; }
			public Dictionary<string, string>? In { get; set; }
			public Dictionary<string, string>? Out { get; set; }
			public bool Approximate { get; set; }
			public string? Note { get; set; }
		}

		private sealed class GeneratorFileDto
		{
			public List<GeneratorDto?>? Generators { get; set; } = [];
		}

		private sealed class GeneratorDto
		{
			public string? Id { get; set; }
			public NameDto? Name { get; set; }
			public BuildDto? Build { get; set; }
			public Dictionary<string, string>? Produces { get; set; }
			public string? Note { get; set; }
		}

		private sealed class BuildDto
		{
			public double Time { get; set; }
			public Dictionary<string, string>? In { get; set; }
		}

		private sealed class GameFileDto
		{
			public StartDto? Start { get; set; }
			public double? GeneratorCostGrowth { get; set; }
			public double? OfflineCapHours { get; set; }
		}

		private sealed class StartDto
		{
			public Dictionary<string, string>? Inventory { get; set; }
		}
	}
}
