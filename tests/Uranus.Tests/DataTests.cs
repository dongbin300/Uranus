using Uranus.Analysis;
using Uranus.Data;
using Uranus.Validation;

namespace Uranus.Tests
{
	/// <summary>
	/// 저장소의 실제 data 폴더 검사. 데이터를 고쳤을 때 오류가 생기면 CI에서 막힌다.
	/// </summary>
	public class RepositoryDataTests
	{
		private static UranusDatabase Db => TestData.Repository.Database;

		[Fact]
		public void Loads_WithoutErrors()
		{
			Assert.DoesNotContain(TestData.Repository.Issues, x => x.Severity == IssueSeverity.Error);
		}

		[Fact]
		public void Validates_WithoutErrors()
		{
			var errors = UranusValidator.Validate(Db).Where(x => x.Severity == IssueSeverity.Error).ToList();

			Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		}

		[Fact]
		public void Loads_Generators_AsItemsWithBuildRecipes()
		{
			Assert.All(Db.GeneratorList, x => Assert.NotNull(Db.FindRecipe(x.BuildRecipe)));

			var synthesizer = Db.Generators[new ItemId("hydrogen_synthesizer")];
			var build = Db.FindRecipe(synthesizer.BuildRecipe);
			Assert.NotNull(build);
			Assert.Equal(12, build.Time);
			Assert.Equal(new ItemAmount(new ItemId("H"), 15), Assert.Single(build.Inputs));
			Assert.Equal(new ItemAmount(new ItemId("H"), 0.25), Assert.Single(synthesizer.OutputPerSecond));
		}

		[Fact]
		public void Loads_IonAndParenthesisIds()
		{
			var electrolysis = Db.FindRecipe(new RecipeId("water_electrolysis"));

			Assert.NotNull(electrolysis);
			Assert.Contains(new ItemAmount(new ItemId("OH-"), 4), electrolysis.Outputs);
			Assert.NotNull(Db.FindItem(new ItemId("Ca(OH)2")));
		}

		[Fact]
		public void Loads_FractionalOutputs()
		{
			var separation = Db.FindRecipe(new RecipeId("apatite_separation"));

			Assert.NotNull(separation);
			Assert.All(separation.Outputs, x => Assert.Equal(1.0 / 3, x.Amount));
		}

		[Fact]
		public void FindItemByName_IgnoresSpaces()
		{
			Assert.Equal(new ItemId("NaCl"), Db.FindItemByName("염화나트륨")?.Id);
			Assert.Equal(new ItemId("NaCl"), Db.FindItemByName("염화 나트륨")?.Id);
		}

		[Fact]
		public void StartState_ReachesEveryGenerator()
		{
			var reach = RecipeGraph.ComputeReachability(Db, Db.Config.StartInventory.Select(x => x.Item));

			Assert.Equal(Db.GeneratorList.Count, reach.Generators.Count);
		}

		[Fact]
		public void Masses_MatchRealWorld()
		{
			var masses = RecipeGraph.ComputeMasses(Db);

			// 물 1몰 = 18.015g, 8인치 웨이퍼 약 53g
			Assert.Equal(18.015, masses.GetMass(new ItemId("H2O"))!.Value * Chemistry.Formula.Avogadro, 3);
			Assert.InRange(masses.GetMass(new ItemId("wafer_8in"))!.Value, 52, 54);
		}
	}

	public class LoaderTests : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), "uranus-tests-" + Guid.NewGuid().ToString("N"));

		public LoaderTests()
		{
			Directory.CreateDirectory(Path.Combine(directory, "items"));
			Directory.CreateDirectory(Path.Combine(directory, "recipes"));
			Write("elements.yaml", "elements:\n  - { symbol: H, name: { ko: 수소 }, mass: 1.008 }\n");
			Write("game.yaml", "start:\n  inventory: { H: 15 }\n");
			Write("generators.yaml", "generators: []\n");
		}

		public void Dispose() => Directory.Delete(directory, recursive: true);

		[Fact]
		public void ReportsSyntaxErrorWithLine()
		{
			Write("items/a.yaml", "items:\n  - { id: H, name: { ko: 수소 }\n");

			var issue = Assert.Single(UranusDataLoader.Load(directory).Issues);

			Assert.Equal(UranusDataLoader.SyntaxErrorCode, issue.Code);
			Assert.Equal("data/items/a.yaml", issue.Source);
		}

		[Fact]
		public void ReportsUnknownProperty()
		{
			Write("items/a.yaml", "items:\n  - { id: H, name: { ko: 수소 }, formla: H }\n");

			Assert.Contains(UranusDataLoader.Load(directory).Issues, x => x.Code == UranusDataLoader.SyntaxErrorCode);
		}

		[Fact]
		public void ReportsDuplicateIds()
		{
			Write("items/a.yaml", "items:\n  - { id: H, name: { ko: 수소 } }\n");
			Write("items/b.yaml", "items:\n  - { id: H, name: { ko: 수소 원자 } }\n");

			Assert.Contains(UranusDataLoader.Load(directory).Issues, x => x.Code == UranusDataLoader.DuplicateIdCode);
		}

		[Fact]
		public void ReportsDuplicateKeysInMapping()
		{
			Write("items/a.yaml", "items:\n  - { id: H, name: { ko: 수소 } }\n");
			Write("recipes/a.yaml", "recipes:\n  - { id: r, time: 1, in: { H: 1, H: 2 }, out: { H: 1 } }\n");

			Assert.Contains(UranusDataLoader.Load(directory).Issues, x => x.Code == UranusDataLoader.SyntaxErrorCode);
		}

		[Fact]
		public void ReportsInvalidAmount()
		{
			Write("items/a.yaml", "items:\n  - { id: H, name: { ko: 수소 } }\n");
			Write("recipes/a.yaml", "recipes:\n  - { id: r, time: 1, in: { H: lots }, out: { H: 1 } }\n");

			Assert.Contains(UranusDataLoader.Load(directory).Issues, x => x.Code == UranusDataLoader.InvalidValueCode);
		}

		[Fact]
		public void AppliesFileDefaults()
		{
			Write("items/a.yaml", "category: element\nunit: piece\nitems:\n  - { id: H, name: { ko: 수소, en: Hydrogen }, formula: H }\n");

			var result = UranusDataLoader.Load(directory);

			Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Issues));
			var item = result.Database.FindItem(new ItemId("H"));
			Assert.NotNull(item);
			Assert.Equal("element", item.Category);
			Assert.Equal(ItemUnit.Piece, item.Unit);
			Assert.Equal("Hydrogen", item.Name.En);
			Assert.Equal(15, Assert.Single(result.Database.Config.StartInventory).Amount);
		}

		private void Write(string relativePath, string text) => File.WriteAllText(Path.Combine(directory, relativePath), text);
	}

	public class ValidatorTests
	{
		private static readonly ItemDef[] Chemistry =
		[
			TestData.Item("H", "H"),
			TestData.Item("O", "O"),
			TestData.Item("H2O", "H2O"),
		];

		[Fact]
		public void BalancedReaction_HasNoAtomError()
		{
			var db = TestData.Create(Chemistry, [TestData.Recipe("water", [("H", 2), ("O", 1)], [("H2O", 1)])], ("H", 2), ("O", 1));

			Assert.DoesNotContain(UranusValidator.Validate(db), x => x.Code == UranusValidator.Codes.AtomImbalance);
		}

		[Fact]
		public void UnbalancedReaction_IsError()
		{
			var db = TestData.Create(Chemistry, [TestData.Recipe("water", [("H", 1), ("O", 1)], [("H2O", 1)])], ("H", 1), ("O", 1));

			var issue = Assert.Single(UranusValidator.Validate(db), x => x.Code == UranusValidator.Codes.AtomImbalance);
			Assert.Equal(IssueSeverity.Error, issue.Severity);
			Assert.Contains("H 1→2", issue.Message);
		}

		[Fact]
		public void ApproximateRecipe_SkipsAtomCheck()
		{
			var recipe = TestData.Recipe("water", [("H", 1), ("O", 1)], [("H2O", 1)]) with { IsApproximate = true };
			var db = TestData.Create(Chemistry, [recipe], ("H", 1), ("O", 1));

			Assert.DoesNotContain(UranusValidator.Validate(db), x => x.Code == UranusValidator.Codes.AtomImbalance);
		}

		[Fact]
		public void UndefinedItem_IsError()
		{
			var db = TestData.Create(Chemistry, [TestData.Recipe("water", [("H", 2), ("Ox", 1)], [("H2O", 1)])], ("H", 2));

			Assert.Contains(UranusValidator.Validate(db), x => x.Code == UranusValidator.Codes.UndefinedItem && x.Severity == IssueSeverity.Error);
		}

		[Fact]
		public void MissingSource_IsUnobtainableWarning()
		{
			var db = TestData.Create(Chemistry, [TestData.Recipe("water", [("H", 2), ("O", 1)], [("H2O", 1)])], ("H", 2));

			var issue = Assert.Single(UranusValidator.Validate(db), x => x.Code == UranusValidator.Codes.Unobtainable);
			Assert.Equal(IssueSeverity.Warning, issue.Severity);
			Assert.Contains("(O)", issue.Message);
		}

		[Fact]
		public void MassOutsideExpectedRange_IsWarning()
		{
			// 물 1몰(18g)로 만든 부품인데 실제 질량은 1~2g이라고 적어 둔 경우
			var part = TestData.Item("part", unit: ItemUnit.Piece) with { ExpectedMass = new MassRange(1, 2) };
			var db = TestData.Create([.. Chemistry, part],
				[TestData.Recipe("water", [("H", 2), ("O", 1)], [("H2O", 1)]), TestData.Recipe("part", [("H2O", 6.02214076e23)], [("part", 1)])],
				("H", 2), ("O", 1));

			var issue = Assert.Single(UranusValidator.Validate(db), x => x.Code == UranusValidator.Codes.MassOutOfRange);
			Assert.Contains("18 g", issue.Message);
		}

		[Fact]
		public void ZeroTime_IsWarning()
		{
			var db = TestData.Create(Chemistry, [TestData.Recipe("water", [("H", 2), ("O", 1)], [("H2O", 1)], time: 0)], ("H", 2), ("O", 1));

			Assert.Contains(UranusValidator.Validate(db), x => x.Code == UranusValidator.Codes.ZeroTime && x.Severity == IssueSeverity.Warning);
		}
	}

	public class RecipeGraphTests
	{
		[Fact]
		public void BillOfMaterials_StopsAtItemsWithoutRecipe()
		{
			var db = TestData.Create(
				[TestData.Item("H", "H"), TestData.Item("O", "O"), TestData.Item("H2O", "H2O")],
				[TestData.Recipe("water", [("H", 2), ("O", 1)], [("H2O", 1)])]);

			var bom = RecipeGraph.ComputeBillOfMaterials(db, new ItemId("H2O"), 3);

			Assert.Equal(6, bom[new ItemId("H")]);
			Assert.Equal(3, bom[new ItemId("O")]);
		}

		[Fact]
		public void Reachability_FollowsRecipesFromStart()
		{
			var db = TestData.Create(
				[TestData.Item("H", "H"), TestData.Item("O", "O"), TestData.Item("H2O", "H2O")],
				[TestData.Recipe("water", [("H", 2), ("O", 1)], [("H2O", 1)])],
				("H", 1));

			var reach = RecipeGraph.ComputeReachability(db, [new ItemId("H")]);

			Assert.DoesNotContain(new RecipeId("water"), reach.Recipes);
		}
	}
}
