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
				new GameConfig([new ItemAmount(Id("H"), 15)], growth, 24));
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
			sim.State.Add(Fixtures.Id("O"), 2.75);
			sim.TryEnqueue(new RecipeId("water"), 2);
			sim.Tick(0.05);
			sim.State.SavedAt = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

			var json = SaveFile.Serialize(sim.State);
			var loaded = SaveFile.Deserialize(json, db, out var warnings);

			Assert.Empty(warnings);
			Assert.Equal(json, SaveFile.Serialize(loaded));
			Assert.Equal(0.75, loaded.GetAmount(Fixtures.Id("O")), 9);
			Assert.Equal(0.05, loaded.Queue[0].Progress, 9);
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
			Assert.Equal(16, SaveFile.TryRead(path, db, out _)!.GetAmount(Fixtures.Id("H")));
		}

		[Fact]
		public void TryRead_FallsBackToBackupWhenCorrupted()
		{
			var db = Fixtures.Small();
			var path = Path.Combine(directory, "save.json");
			SaveFile.Write(path, GameState.CreateNew(db));
			SaveFile.Write(path, GameState.CreateNew(db));
			File.WriteAllText(path, "{ broken");

			var state = SaveFile.TryRead(path, db, out var warnings);

			Assert.NotNull(state);
			Assert.Contains(warnings, x => x.Contains("백업"));
		}

		[Fact]
		public void TryRead_ReturnsNullWhenMissing()
		{
			Assert.Null(SaveFile.TryRead(Path.Combine(directory, "none.json"), Fixtures.Small(), out _));
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
