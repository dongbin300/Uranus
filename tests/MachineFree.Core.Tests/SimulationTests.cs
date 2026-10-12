using Uranus;
using Uranus.Data;
using Uranus.Quantities;

namespace MachineFree.Core.Tests
{
	internal static class Fixtures
	{
		private static readonly Lazy<UranusDatabase> repository = new(() =>
		{
			var directory = UranusDataLoader.FindDataDirectory(AppContext.BaseDirectory) ?? throw new InvalidOperationException("data 폴더가 없다");
			return UranusDataLoader.Load(directory).Database;
		});

		/// <summary>
		/// 저장소의 실제 데이터
		/// </summary>
		public static UranusDatabase Repository => repository.Value;

		public static ItemId Id(string id) => new(id);

		/// <summary>
		/// 작은 테스트 데이터: 수소·산소·물, 0.2개짜리 결과물, 시간 0 레시피, 수소 합성기
		/// </summary>
		public static UranusDatabase Small(double growth = 1.15)
		{
			ItemDef Item(string id, ItemUnit unit = ItemUnit.Particle) => new(Id(id), new LocalizedText(id), "test", unit);
			RecipeDef Recipe(string id, double time, (string, double)[] inputs, (string, double)[] outputs) =>
				new(new RecipeId(id), "test", time,
					[.. inputs.Select(x => new ItemAmount(Id(x.Item1), x.Item2))],
					[.. outputs.Select(x => new ItemAmount(Id(x.Item1), x.Item2))]);

			return new UranusDatabase(
				[],
				[Item("H"), Item("O"), Item("H2O"), Item("rare"), Item("synth", ItemUnit.Piece)],
				[
					Recipe("water", 0.1, [("H", 2), ("O", 1)], [("H2O", 1)]),
					Recipe("rare", 1, [("H", 1)], [("rare", 0.2)]),
					Recipe("instant", 0, [("H", 1)], [("O", 1)]),
					Recipe("build_synth", 12, [("H", 15)], [("synth", 1)]),
				],
				[new GeneratorDef(Id("synth"), new RecipeId("build_synth"), [new ItemAmount(Id("H"), 0.25)])],
				new GameConfig([new ItemAmount(Id("H"), 15)], growth, 24)
				{
					Upgrades =
					[
						new UpgradeDef("line_2", new LocalizedText("라인 2"), UpgradeKind.Line, 1, [new ItemAmount(Id("H"), 5)], []),
						new UpgradeDef("scale_10", new LocalizedText("10배"), UpgradeKind.Scale, 10, [new ItemAmount(Id("O"), 1)], []),
						new UpgradeDef("scale_100", new LocalizedText("100배"), UpgradeKind.Scale, 100, [new ItemAmount(Id("O"), 1)], ["scale_10"]),
						new UpgradeDef("automation", new LocalizedText("자동화"), UpgradeKind.Automation, 1, [new ItemAmount(Id("O"), 1)], []),
					],
					Goals = [new GoalDef("water", new LocalizedText("물"), [new ItemAmount(Id("H2O"), 1)])],
				});
		}

		public static Simulation Start(UranusDatabase db) => new(db, GameState.CreateNew(db));
	}

	public class SimulationTests
	{
		private static readonly ItemId H = Fixtures.Id("H");
		private static readonly ItemId O = Fixtures.Id("O");
		private static readonly ItemId Water = Fixtures.Id("H2O");
		private static readonly ItemId Synth = Fixtures.Id("synth");

		[Fact]
		public void NewGame_StartsWithStartInventory()
		{
			var sim = Fixtures.Start(Fixtures.Small());

			Assert.Equal(15, sim.GetAmount(H));
			Assert.Contains(H, sim.State.Discovered);
		}

		[Fact]
		public void Generator_ProducesAfterBuildTime()
		{
			var sim = Fixtures.Start(Fixtures.Small());

			Assert.True(sim.TryEnqueue(new RecipeId("build_synth"), 1));
			Assert.Equal(0, sim.GetAmount(H));

			sim.Tick(12);
			Assert.Equal(1, sim.GetOwnedCount(Synth));

			sim.Tick(4);
			Assert.Equal(1, Quantity.Floor(sim.GetAmount(H)));
		}

		[Fact]
		public void Generator_OutputScalesWithOwnedCount()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(Synth, 2);

			sim.Tick(10);

			// 15 + 2대 × 0.25/초 × 10초
			Assert.Equal(20, sim.GetAmount(H), 9);
			Assert.Equal(0.5, sim.GetProductionRate(H));
		}

		[Fact]
		public void GeneratorCost_GrowsWithOwnedAndQueued()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			var build = sim.Database.FindRecipe(new RecipeId("build_synth"))!;
			sim.State.Add(Synth, 1);

			Assert.Equal(15 * 1.15, sim.GetCost(build, 1).Single().Amount, 9);
			Assert.Equal(15 * (1.15 + 1.15 * 1.15), sim.GetCost(build, 2).Single().Amount, 9);

			sim.State.Add(H, 100);
			Assert.True(sim.TryEnqueue(build.Id, 1));
			Assert.Equal(15 * 1.15 * 1.15, sim.GetCost(build, 1).Single().Amount, 9);
		}

		[Fact]
		public void MaxCraftable_RespectsGeneratorGrowth()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			var build = sim.Database.FindRecipe(new RecipeId("build_synth"))!;
			sim.State.Add(H, 85);

			// 수소 100: 15 × (1.15^c − 1)/0.15 ≤ 100 → c = 4
			Assert.Equal(4, sim.GetMaxCraftable(build));
			Assert.True(sim.TryEnqueue(build.Id, 4));
			Assert.False(sim.TryEnqueue(build.Id, 1));
		}

		[Fact]
		public void MaxCraftable_IsLimitedByScarcestInput()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 3);

			Assert.Equal(3, sim.GetMaxCraftable(sim.Database.FindRecipe(new RecipeId("water"))!));
		}

		[Fact]
		public void FractionalOutput_BecomesOneItemAfterFiveCrafts()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			var rare = Fixtures.Id("rare");

			Assert.True(sim.TryEnqueue(new RecipeId("rare"), 5));
			sim.Tick(4);
			Assert.Equal(0, Quantity.Floor(sim.GetAmount(rare)));
			Assert.Equal(0.8, Quantity.Fraction(sim.GetAmount(rare)), 9);

			sim.Tick(1);
			Assert.Equal(1, Quantity.Floor(sim.GetAmount(rare)));
			Assert.Contains(rare, sim.State.Discovered);
		}

		[Fact]
		public void LargeTick_CompletesManyCraftsAndKeepsProgress()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 10);
			sim.State.Add(H, 5);

			Assert.True(sim.TryEnqueue(new RecipeId("water"), 10));
			sim.Tick(0.55);

			Assert.Equal(5, Quantity.Floor(sim.GetAmount(Water)));
			var entry = Assert.Single(sim.State.Queue);
			Assert.Equal(5, entry.Remaining);
			Assert.Equal(0.05, entry.Progress, 9);
			Assert.Equal(5, sim.State.TotalCrafts);
		}

		[Fact]
		public void SmallTicks_MatchOneLargeTick()
		{
			var a = Fixtures.Start(Fixtures.Small());
			var b = Fixtures.Start(Fixtures.Small());
			foreach (var sim in new[] { a, b })
			{
				sim.State.Add(O, 7);
				sim.TryEnqueue(new RecipeId("water"), 7);
			}

			for (var i = 0; i < 10; i++)
			{
				a.Tick(0.1);
			}

			b.Tick(1);

			Assert.Equal(Quantity.Floor(b.GetAmount(Water)), Quantity.Floor(a.GetAmount(Water)));
			Assert.Empty(a.State.Queue);
			Assert.Empty(b.State.Queue);
		}

		[Fact]
		public void ZeroTimeRecipe_CompletesImmediately()
		{
			var sim = Fixtures.Start(Fixtures.Small());

			Assert.True(sim.TryEnqueue(new RecipeId("instant"), 3));
			sim.Tick(0.001);

			Assert.Equal(3, sim.GetAmount(O));
			Assert.Empty(sim.State.Queue);
		}

		[Fact]
		public void Enqueue_MergesWithLastEntryOfSameRecipe()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 4);

			sim.TryEnqueue(new RecipeId("water"), 2);
			sim.TryEnqueue(new RecipeId("water"), 2);

			Assert.Equal(4, Assert.Single(sim.State.Queue).Remaining);
		}

		[Fact]
		public void Enqueue_FailsWithoutMaterials()
		{
			var sim = Fixtures.Start(Fixtures.Small());

			Assert.False(sim.TryEnqueue(new RecipeId("water"), 1));
			Assert.Equal(15, sim.GetAmount(H));
			Assert.Empty(sim.State.Queue);
		}

		[Fact]
		public void Cancel_RefundsPaidMaterials()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 6);
			sim.TryEnqueue(new RecipeId("water"), 6);

			sim.Cancel(0, 2);
			Assert.Equal(15 - 12 + 4, sim.GetAmount(H), 9);
			Assert.Equal(2, sim.GetAmount(O), 9);
			Assert.Equal(4, sim.State.Queue[0].Remaining);

			sim.Cancel(0, 100);
			Assert.Equal(15, sim.GetAmount(H), 9);
			Assert.Equal(6, sim.GetAmount(O), 9);
			Assert.Empty(sim.State.Queue);
		}

		[Fact]
		public void Cancel_AfterPartialProgress_RefundsOnlyUnfinishedCrafts()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 4);
			sim.TryEnqueue(new RecipeId("water"), 4);
			sim.Tick(0.25);

			sim.Cancel(0, 100);

			Assert.Equal(2, Quantity.Floor(sim.GetAmount(Water)));
			Assert.Equal(15 - 8 + 4, sim.GetAmount(H), 9);
			Assert.Equal(2, sim.GetAmount(O), 9);
		}

		[Fact]
		public void SecondLine_CraftsTwoEntriesAtOnce()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(H, 100);
			sim.State.Add(O, 10);
			Assert.True(sim.TryBuyUpgrade("line_2"));
			Assert.Equal(2, sim.LineCount);

			sim.TryEnqueue(new RecipeId("rare"), 1);   // 1초
			sim.TryEnqueue(new RecipeId("water"), 5);  // 0.1초 × 5
			sim.Tick(0.5);

			Assert.Equal(5, Quantity.Floor(sim.GetAmount(Water)));
			Assert.Equal(0.5, sim.State.Queue[0].Progress, 9);

			sim.Tick(0.5);
			Assert.Empty(sim.State.Queue);
			Assert.Equal(0.2, sim.GetAmount(Fixtures.Id("rare")), 9);
		}

		[Fact]
		public void SingleLine_CraftsInOrder()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 10);
			sim.TryEnqueue(new RecipeId("rare"), 1);
			sim.TryEnqueue(new RecipeId("water"), 5);

			sim.Tick(1);
			Assert.Equal(0, sim.GetAmount(Water));

			sim.Tick(0.5);
			Assert.Equal(5, Quantity.Floor(sim.GetAmount(Water)));
		}

		[Fact]
		public void Upgrade_NeedsPrerequisiteAndCost()
		{
			var sim = Fixtures.Start(Fixtures.Small());

			Assert.False(sim.TryBuyUpgrade("scale_100"));
			Assert.False(sim.TryBuyUpgrade("scale_10"));

			sim.State.Add(O, 2);
			Assert.True(sim.TryBuyUpgrade("scale_10"));
			Assert.False(sim.TryBuyUpgrade("scale_10"));
			Assert.True(sim.TryBuyUpgrade("scale_100"));
			Assert.Equal(100, sim.ScaleFactor);
			Assert.Equal(0, sim.GetAmount(O));
		}

		[Fact]
		public void Scale_MultipliesParticleRecipesAndGenerators()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 1);
			sim.TryBuyUpgrade("scale_10");
			var water = sim.Database.FindRecipe(new RecipeId("water"))!;
			var build = sim.Database.FindRecipe(new RecipeId("build_synth"))!;

			// 입자 레시피: 1회에 수소 20, 산소 10 → 물 10
			Assert.Equal(20, sim.GetCost(water, 1).First(x => x.Item == H).Amount);
			Assert.Equal(10, sim.GetOutputs(water).Single().Amount);

			// 생성기(부품)는 규모와 상관없이 1대
			Assert.Equal(15, sim.GetCost(build, 1).Single().Amount);
			Assert.Equal(1, sim.GetOutputs(build).Single().Amount);

			// 생성기가 내는 입자는 규모만큼
			sim.State.Add(Synth, 1);
			Assert.Equal(2.5, sim.GetProductionRate(H), 9);
		}

		[Fact]
		public void CraftScale_CanBeLoweredWhileGeneratorsKeepMaxScale()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 2);
			sim.TryBuyUpgrade("scale_10");
			sim.TryBuyUpgrade("scale_100");
			sim.State.Add(Synth, 1);
			var water = sim.Database.FindRecipe(new RecipeId("water"))!;
			Assert.Equal(100, sim.GetRecipeScale(water));

			Assert.True(sim.SetCraftScale("scale_10"));
			Assert.Equal(10, sim.GetRecipeScale(water));
			Assert.True(sim.SetCraftScale(Simulation.UnitScaleId));
			Assert.Equal(1, sim.GetRecipeScale(water));
			Assert.Equal(25, sim.GetProductionRate(H), 9);

			Assert.False(sim.SetCraftScale("line_2"));
			Assert.False(sim.SetCraftScale("unknown"));
			Assert.True(sim.SetCraftScale(null));
			Assert.Equal(100, sim.GetRecipeScale(water));
		}

		[Fact]
		public void CraftScale_FallsBackToMaxWhenChosenScaleIsGone()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 1);
			sim.TryBuyUpgrade("scale_10");
			sim.SetCraftScale("scale_10");
			sim.State.Upgrades.Remove("scale_10");

			Assert.Null(sim.CraftScale);
			Assert.Equal(1, sim.CraftScaleFactor);
		}

		[Fact]
		public void QueuedEntry_KeepsScaleItWasPaidAt()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 2);
			sim.TryEnqueue(new RecipeId("water"), 1);   // 분자 단위로 냄: 수소 2, 산소 1
			sim.TryBuyUpgrade("scale_10");
			sim.State.Add(O, 10);
			sim.State.Add(H, 20);
			Assert.True(sim.TryEnqueue(new RecipeId("water"), 1));   // 10배로 냄: 수소 20, 산소 10 → 새 칸

			Assert.Equal(2, sim.State.Queue.Count);
			sim.Tick(1);

			Assert.Equal(11, sim.GetAmount(Water), 9);
		}

		[Fact]
		public void AutoRepeat_NeedsAutomationAndSkipsGeneratorsAndInstantRecipes()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			var water = new RecipeId("water");

			Assert.False(sim.SetAutoRepeat(water, true));

			sim.State.Add(O, 1);
			Assert.True(sim.TryBuyUpgrade("automation"));
			Assert.True(sim.SetAutoRepeat(water, true));
			Assert.False(sim.SetAutoRepeat(new RecipeId("build_synth"), true));
			Assert.False(sim.SetAutoRepeat(new RecipeId("instant"), true));
			Assert.Equal([water], sim.State.AutoRepeat);
			Assert.True(sim.AutoRepeatChanged);
		}

		[Theory]
		[InlineData(1)]
		[InlineData(1000)]
		public void AutoRepeat_ReordersAsGeneratorOutputArrives(int ticks)
		{
			// 합성기가 초당 수소 0.25개를 내고, 물 레시피(수소 2 + 산소 1)를 자동 반복한다
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Remove(H, 15);
			sim.State.Add(O, 101);
			sim.State.Add(Synth, 1);
			sim.TryBuyUpgrade("automation");
			Assert.True(sim.SetAutoRepeat(new RecipeId("water"), true));

			for (var i = 0; i < ticks; i++)
			{
				sim.Tick(100.0 / ticks);
			}

			// 100초 동안 수소 25개 → 물 12개, 수소 1개 남음
			Assert.Equal(12, Quantity.Floor(sim.GetAmount(Water)));
			Assert.Equal(1, sim.GetAmount(H), 6);
			Assert.Equal(88, sim.GetAmount(O), 6);
		}

		[Fact]
		public void AutoRepeat_LargeTickUsesAllMaterials()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(H, 10_000);
			sim.State.Add(O, 10_001);
			sim.TryBuyUpgrade("automation");
			sim.SetAutoRepeat(new RecipeId("water"), true);
			Assert.Equal(1, Assert.Single(sim.State.Queue).Remaining);

			// 60초씩 끊어 주문하며 수소 10,015개를 다 쓴다
			sim.Tick(1000);
			Assert.Equal(5007, Quantity.Floor(sim.GetAmount(Water)));
			Assert.Equal(1, sim.GetAmount(H), 9);
			Assert.Empty(sim.State.Queue);
		}

		[Fact]
		public void CancellingAutoRepeatEntry_StopsAutoRepeat()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 10);
			sim.TryBuyUpgrade("automation");
			sim.SetAutoRepeat(new RecipeId("water"), true);
			Assert.Single(sim.State.Queue);
			sim.AutoRepeatChanged = false;

			sim.Cancel(0, 1);
			sim.Tick(1);

			Assert.Empty(sim.State.Queue);
			Assert.Empty(sim.State.AutoRepeat);
			Assert.True(sim.AutoRepeatChanged);
			Assert.Equal(15, sim.GetAmount(H), 9);
		}

		[Fact]
		public void Goals_AreRecordedWithPlayTime()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(O, 1);
			sim.TryEnqueue(new RecipeId("water"), 1);

			sim.Tick(0.1);

			Assert.Equal(0.1, sim.State.CompletedGoals["water"], 9);
			Assert.Equal("water", Assert.Single(sim.NewGoals).Id);

			// 다시 없어져도 달성 기록은 남는다
			sim.State.Remove(Water, 1);
			sim.Tick(1);
			Assert.Single(sim.State.CompletedGoals);
		}

		[Fact]
		public void UpdateGoals_WithoutNotify_RecordsSilently()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(Water, 1);

			sim.UpdateGoals(notify: false);

			Assert.True(sim.State.CompletedGoals.ContainsKey("water"));
			Assert.Empty(sim.NewGoals);
		}

		[Fact]
		public void HugeGeneratorOrder_IsNotAffordable()
		{
			// 비용이 double 범위를 넘어 무한대가 되어도 살 수 있다고 판정하면 안 된다
			var sim = Fixtures.Start(Fixtures.Small());
			var build = new RecipeId("build_synth");

			Assert.False(sim.TryEnqueue(build, 6000));
			Assert.Equal(15, sim.GetAmount(H));
			Assert.Empty(sim.State.Queue);
			Assert.Equal(1, sim.GetMaxCraftable(sim.Database.FindRecipe(build)!));
		}

		[Fact]
		public void CancellingGeneratorBuilds_RefundsMostExpensiveFirst()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(H, 985);
			var build = new RecipeId("build_synth");
			for (var i = 0; i < 10; i++)
			{
				Assert.True(sim.TryEnqueue(build, 1));
			}

			Assert.Equal(10, Assert.Single(sim.State.Queue).Remaining);
			for (var i = 0; i < 9; i++)
			{
				sim.Cancel(0, 1);
			}

			// 남은 1대는 첫 번째 가격(15)만 낸 셈이어야 한다
			Assert.Equal(1000 - 15, sim.GetAmount(H), 6);
		}

		[Fact]
		public void FinishedGeneratorBuild_UsesCheapestPrice()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(H, 985);
			var build = new RecipeId("build_synth");
			sim.TryEnqueue(build, 10);

			sim.Tick(12);
			sim.Cancel(0, 100);

			// 1대 완성(15) 외에는 모두 돌려받고, 그 사이 1대가 생산한 수소는 없다(완성 직후 취소)
			Assert.Equal(1, sim.GetOwnedCount(Synth));
			Assert.Equal(1000 - 15, sim.GetAmount(H), 6);
		}

		[Fact]
		public void ProgressBeyondShortenedRecipeTime_GivesNoFreeTime()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(Synth, 10);
			sim.State.Add(O, 1);
			sim.TryEnqueue(new RecipeId("water"), 1);
			sim.State.Queue[0].Progress = 50;
			var before = sim.GetAmount(H);

			sim.Tick(0.1);

			// 물은 즉시 완성되고, 생성기는 0.1초 × 10대 × 0.25만 생산한다
			Assert.Equal(before + 0.25, sim.GetAmount(H), 9);
			Assert.Equal(1, sim.GetAmount(Water));
		}

		[Fact]
		public void CatchUp_WithZeroCap_SimulatesNothing()
		{
			var db = Fixtures.Small();
			var config = db.Config with { OfflineCapHours = 0 };
			var sim = Fixtures.Start(new UranusDatabase(db.ElementList, db.ItemList, db.RecipeList, db.GeneratorList, config));
			sim.State.Add(Synth, 1);
			sim.State.SavedAt = DateTimeOffset.UtcNow.AddHours(-1);

			var report = sim.CatchUp(DateTimeOffset.UtcNow);

			Assert.Equal(TimeSpan.Zero, report.Simulated);
			Assert.Empty(report.Gained);

			sim.Tick(4);
			Assert.Equal(16, sim.GetAmount(H), 9);
		}

		[Fact]
		public void CatchUp_IsCappedAndReportsGains()
		{
			var sim = Fixtures.Start(Fixtures.Small());
			sim.State.Add(Synth, 1);
			var saved = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
			sim.State.SavedAt = saved;

			var report = sim.CatchUp(saved.AddHours(48));

			Assert.Equal(TimeSpan.FromHours(48), report.Elapsed);
			Assert.Equal(TimeSpan.FromHours(24), report.Simulated);
			Assert.Equal(0.25 * 24 * 3600, report.Gained[H], 6);
			Assert.Equal(saved.AddHours(48), sim.State.SavedAt);
		}

		[Fact]
		public void ScriptedPlay_IsDeterministic()
		{
			static string Play()
			{
				var sim = Fixtures.Start(Fixtures.Repository);
				var build = new RecipeId("build_hydrogen_synthesizer");
				sim.TryEnqueue(build, 1);
				for (var minute = 0; minute < 60; minute++)
				{
					for (var i = 0; i < 600; i++)
					{
						sim.Tick(0.1);
					}

					var max = sim.GetMaxCraftable(sim.Database.FindRecipe(build)!);
					if (max > 0)
					{
						sim.TryEnqueue(build, max);
					}
				}

				// 저장 시각은 벽시계 값이므로 비교에서 뺀다
				sim.State.SavedAt = DateTimeOffset.UnixEpoch;
				return SaveFile.Serialize(sim.State);
			}

			Assert.Equal(Play(), Play());
		}
	}

	public class SaveFileTests : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), "machinefree-tests-" + Guid.NewGuid().ToString("N"));

		public void Dispose()
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}

		[Fact]
		public void RoundTrip_KeepsState()
		{
			var db = Fixtures.Small();
			var sim = Fixtures.Start(db);
			sim.State.Add(Fixtures.Id("O"), 21.75);
			sim.TryBuyUpgrade("scale_10");
			sim.State.Add(Fixtures.Id("H"), 40);
			sim.TryEnqueue(new RecipeId("water"), 2);
			sim.SetCraftScale(Simulation.UnitScaleId);
			sim.State.Add(Fixtures.Id("H2O"), 1);
			sim.UpdateGoals();
			sim.Tick(0.05);
			sim.State.SavedAt = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

			var json = SaveFile.Serialize(sim.State);
			var loaded = SaveFile.Deserialize(json, db, out var warnings);

			Assert.Empty(warnings);
			Assert.Equal(json, SaveFile.Serialize(loaded));
			Assert.Equal(0.75, loaded.GetAmount(Fixtures.Id("O")), 9);
			Assert.Equal(0.05, loaded.Queue[0].Progress, 9);
			Assert.Equal(10, loaded.Queue[0].Scale);
			Assert.Contains("scale_10", loaded.Upgrades);
			Assert.Equal(Simulation.UnitScaleId, loaded.CraftScale);
			Assert.True(loaded.CompletedGoals.ContainsKey("water"));
		}

		[Fact]
		public void RoundTrip_KeepsAutoRepeatAndDropsUnknownRecipes()
		{
			var db = Fixtures.Small();
			var state = GameState.CreateNew(db);
			state.AutoRepeat.Add(new RecipeId("water"));
			state.AutoRepeat.Add(new RecipeId("removed_recipe"));

			var loaded = SaveFile.Deserialize(SaveFile.Serialize(state), db, out _);

			Assert.Equal([new RecipeId("water")], loaded.AutoRepeat);
		}

		[Fact]
		public void Write_KeepsBackupAndReadsBack()
		{
			var db = Fixtures.Small();
			var path = Path.Combine(directory, "save.json");
			var state = GameState.CreateNew(db);

			SaveFile.Write(path, state);
			state.Add(Fixtures.Id("H"), 1);
			SaveFile.Write(path, state);

			Assert.True(File.Exists(path + ".bak"));
			Assert.Equal(16, SaveFile.Read(path, db).State!.GetAmount(Fixtures.Id("H")));
		}

		[Fact]
		public void Read_FallsBackToBackupWhenCorrupted()
		{
			var db = Fixtures.Small();
			var path = Path.Combine(directory, "save.json");
			SaveFile.Write(path, GameState.CreateNew(db));
			SaveFile.Write(path, GameState.CreateNew(db));
			File.WriteAllText(path, "{ broken");

			var result = SaveFile.Read(path, db);

			Assert.Equal(SaveReadStatus.Loaded, result.Status);
			Assert.Contains(result.Warnings, x => x.Contains("백업"));
		}

		[Fact]
		public void Read_ReportsMissing()
		{
			Assert.Equal(SaveReadStatus.Missing, SaveFile.Read(Path.Combine(directory, "none.json"), Fixtures.Small()).Status);
		}

		[Theory]
		[InlineData("""{ "version": 3, "inventory": { "H": 1 } }""")]
		[InlineData("[1, 2, 3]")]
		[InlineData("{ broken")]
		public void Read_ReportsUnreadableInsteadOfMissing(string json)
		{
			var path = Path.Combine(directory, "save.json");
			Directory.CreateDirectory(directory);
			File.WriteAllText(path, json);

			var result = SaveFile.Read(path, Fixtures.Small());

			Assert.Equal(SaveReadStatus.Unreadable, result.Status);
			Assert.NotNull(result.Error);
		}

		[Fact]
		public void Read_ToleratesNullEntries()
		{
			var path = Path.Combine(directory, "save.json");
			Directory.CreateDirectory(directory);
			File.WriteAllText(path, """{ "version": 2, "inventory": null, "discovered": [null, "H"], "queue": [null, { "recipe": "water", "remaining": 1, "paid": null }] }""");

			var result = SaveFile.Read(path, Fixtures.Small());

			Assert.Equal(SaveReadStatus.Loaded, result.Status);
			Assert.Single(result.State!.Queue);
			Assert.Contains(Fixtures.Id("H"), result.State.Discovered);
		}

		[Fact]
		public void MoveAside_KeepsUnreadableFiles()
		{
			var path = Path.Combine(directory, "save.json");
			Directory.CreateDirectory(directory);
			File.WriteAllText(path, """{ "version": 3 }""");
			File.WriteAllText(path + ".bak", """{ "version": 3 }""");

			var moved = SaveFile.MoveAside(path, new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));

			Assert.Equal(2, moved.Count);
			Assert.False(File.Exists(path));
			Assert.All(moved, x => Assert.True(File.Exists(x)));
		}

		[Fact]
		public void LegacySave_IsConvertedByKoreanNames()
		{
			// 예전 GameManager가 쓰던 Save.json 형식
			const string json = """
				{
				  "InventoryItems": [
				    { "Count": 1.0, "Item": { "KoreanName": "수소 합성기" } },
				    { "Count": 298.0, "Item": { "KoreanName": "염화 나트륨" } },
				    { "Count": 215.0, "Item": { "KoreanName": "수소" } },
				    { "Count": 3.0, "Item": { "KoreanName": "사라진 아이템" } }
				  ],
				  "QueueItems": [
				    { "Recipe": { "Name": "", "Materials": [ { "Item": { "KoreanName": "수소" }, "Count": 2.0 }, { "Item": { "KoreanName": "산소" }, "Count": 1.0 } ],
				      "Products": [ { "Item": { "KoreanName": "물" }, "Count": 1.0 } ], "Generations": [], "Time": 0.1 }, "Count": 6.0, "TimeElapsed": 0.0 }
				  ],
				  "ElapsedTime": 4466.9,
				  "ProduceCount": 1733.0
				}
				""";

			var state = SaveFile.Deserialize(json, Fixtures.Repository, out var warnings);

			Assert.Equal(1, state.GetAmount(Fixtures.Id("hydrogen_synthesizer")));
			Assert.Equal(298, state.GetAmount(Fixtures.Id("NaCl")));
			Assert.Equal(215 + 12, state.GetAmount(Fixtures.Id("H")));
			Assert.Equal(6, state.GetAmount(Fixtures.Id("O")));
			Assert.Equal(4466.9, state.PlayTime);
			Assert.Equal(1733, state.TotalCrafts);
			Assert.Empty(state.Queue);
			Assert.Contains(warnings, x => x.Contains("사라진 아이템"));
		}
	}
}

namespace MachineFree.Core.Tests
{
	public class InventoryHistoryTests
	{
		private static readonly ItemId H = new("H");
		private static readonly ItemId O = new("O");

		[Fact]
		public void Record_KeepsIntervalAndBackfillsNewItemsWithZero()
		{
			var history = new InventoryHistory(interval: 5, capacity: 10);

			Assert.True(history.Record(0, new Dictionary<ItemId, double> { [H] = 1 }));
			Assert.False(history.Record(4, new Dictionary<ItemId, double> { [H] = 2 }));
			Assert.True(history.Record(5, new Dictionary<ItemId, double> { [H] = 3, [O] = 7 }));

			Assert.Equal([0.0, 5.0], history.Times);
			Assert.Equal([1.0, 3.0], history.GetAmounts(H));
			Assert.Equal([0.0, 7.0], history.GetAmounts(O));
		}

		[Fact]
		public void Record_DropsOldestBeyondCapacity()
		{
			var history = new InventoryHistory(interval: 1, capacity: 3);
			for (var t = 0; t < 5; t++)
			{
				history.Record(t, new Dictionary<ItemId, double> { [H] = t });
			}

			Assert.Equal([2.0, 3.0, 4.0], history.Times);
			Assert.Equal([2.0, 3.0, 4.0], history.GetAmounts(H));
		}

		[Fact]
		public void GetRate_UsesSamplesWithinWindow()
		{
			var history = new InventoryHistory(interval: 10, capacity: 100);
			Assert.Null(history.GetRate(H, 60));

			// 처음 100초는 초당 1개, 그 뒤 60초는 초당 3개
			for (var t = 0; t <= 160; t += 10)
			{
				history.Record(t, new Dictionary<ItemId, double> { [H] = t <= 100 ? t : 100 + 3 * (t - 100) });
			}

			Assert.Equal(3, history.GetRate(H, 60)!.Value, 9);
			Assert.Equal(0, history.GetRate(O, 60)!.Value);
		}

		[Fact]
		public void Record_StartsOverWhenTimeGoesBack()
		{
			var history = new InventoryHistory(interval: 1, capacity: 10);
			history.Record(100, new Dictionary<ItemId, double> { [H] = 1 });
			history.Record(0, new Dictionary<ItemId, double> { [O] = 1 });

			Assert.Equal([0.0], history.Times);
			Assert.Empty(history.GetAmounts(H));
		}
	}
}
