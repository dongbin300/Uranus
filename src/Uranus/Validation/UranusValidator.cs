using Uranus.Analysis;
using Uranus.Chemistry;
using Uranus.Quantities;

namespace Uranus.Validation
{
	public enum IssueSeverity
	{
		Info,
		Warning,
		Error,
	}

	/// <summary>
	/// 데이터 문제 하나. Code로 종류를 묶어 보고한다.
	/// </summary>
	public sealed record DataIssue(IssueSeverity Severity, string Code, string Message, string? Source = null)
	{
		public override string ToString() => Source is null ? $"[{Code}] {Message}" : $"[{Code}] {Message} ({Source})";
	}

	/// <summary>
	/// 데이터 검증기. 파일 형식이 아니라 불러온 모델을 검사한다.
	/// </summary>
	public static class UranusValidator
	{
		public static class Codes
		{
			public const string UndefinedItem = "U001";
			public const string InvalidAmount = "U002";
			public const string InvalidFormula = "U003";
			public const string AtomImbalance = "U004";
			public const string EmptyRecipe = "U005";
			public const string InvalidTime = "U006";
			public const string InvalidGenerator = "U007";
			public const string InvalidUpgrade = "U008";
			public const string InvalidGoal = "U009";
			public const string SimilarNames = "U101";
			public const string Unobtainable = "U102";
			public const string Unreachable = "U103";
			public const string ZeroTime = "U104";
			public const string MassOutOfRange = "U105";
			public const string RecipeMassMismatch = "U106";
			public const string MassGain = "U107";
			public const string UnusedItem = "U108";
			public const string UnitMismatch = "U110";
			public const string CycleRisk = "U111";
		}

		public static IReadOnlyList<DataIssue> Validate(UranusDatabase db)
		{
			var issues = new List<DataIssue>();
			CheckReferences(db, issues);
			CheckRecipes(db, issues);
			CheckGenerators(db, issues);
			CheckUpgradesAndGoals(db, issues);
			CheckFormulas(db, issues);
			CheckAtomBalance(db, issues);
			CheckNames(db, issues);
			CheckProgression(db, issues);
			CheckMasses(db, issues);
			CheckUnits(db, issues);
			CheckCycles(db, issues);
			return issues;
		}

		/// <summary>
		/// 화학 물질은 입자 단위여야 하고, 부품(개수 단위)은 정수 개로만 다룬다.
		/// </summary>
		private static void CheckUnits(UranusDatabase db, List<DataIssue> issues)
		{
			foreach (var item in db.ItemList.Where(x => x.Formula is not null && x.Unit != ItemUnit.Particle))
			{
				issues.Add(Warning(Codes.UnitMismatch, $"'{item.Name.Ko}'({item.Id})는 화학식이 있는데 단위가 {item.Unit}이다. 화학 물질은 particle이어야 한다", item.Source));
			}

			foreach (var recipe in db.RecipeList)
			{
				foreach (var x in recipe.Inputs.Concat(recipe.Outputs))
				{
					if (db.FindItem(x.Item)?.Unit == ItemUnit.Piece && x.Amount != Math.Floor(x.Amount))
					{
						issues.Add(Warning(Codes.UnitMismatch, $"레시피 '{recipe.Id}'가 부품 '{db.GetName(x.Item)}'을 {x.Amount}개처럼 정수가 아닌 수로 다룬다", recipe.Source));
					}
				}
			}
		}

		/// <summary>
		/// 무한 증식(아무것도 쓰지 않고 무언가를 얻는 레시피 조합)은 순환이 있어야 생긴다.
		/// 모든 아이템의 질량이 양수이고 순환 안 레시피가 하나도 질량을 늘리지 않으면 어떤 조합도 순이익을 낼 수 없다.
		/// 그래서 순환 안에서 질량이 늘어나는 레시피(근사 레시피도 포함)는 경고, 질량을 몰라 따질 수 없는 순환은 정보로 알린다.
		/// </summary>
		private static void CheckCycles(UranusDatabase db, List<DataIssue> issues)
		{
			var masses = RecipeGraph.ComputeMasses(db);
			foreach (var cycle in FindItemCycles(db))
			{
				var members = cycle.ToHashSet();
				var names = string.Join(", ", cycle.Select(db.GetName));
				var unknown = cycle.Where(x => !masses.IsComplete(x)).ToList();
				if (unknown.Count > 0)
				{
					issues.Add(Info(Codes.CycleRisk,
						$"순환 [{names}]에 질량을 모르는 아이템({string.Join(", ", unknown.Select(db.GetName))})이 있어 무한 증식 여부를 확인하지 못했다"));
					continue;
				}

				var inside = db.RecipeList.Where(r => r.Inputs.Any(x => members.Contains(x.Item)) && r.Outputs.Any(x => members.Contains(x.Item)));
				foreach (var recipe in inside)
				{
					var all = recipe.Inputs.Concat(recipe.Outputs).ToList();
					if (!all.All(x => masses.IsComplete(x.Item)))
					{
						continue;
					}

					var input = recipe.Inputs.Sum(x => x.Amount * masses.GetLowerBound(x.Item));
					var output = recipe.Outputs.Sum(x => x.Amount * masses.GetLowerBound(x.Item));
					if (output > input * (1 + 1e-6))
					{
						issues.Add(Warning(Codes.CycleRisk,
							$"순환 [{names}] 안의 레시피 '{recipe.Id}'가 질량을 늘린다({QuantityFormatter.FormatMass(input)} → {QuantityFormatter.FormatMass(output)}). 순환을 돌려 무한히 늘릴 수 있다",
							recipe.Source));
					}
				}
			}
		}

		/// <summary>
		/// 재료 → 결과물 간선으로 본 아이템 그래프의 순환(강한 연결 요소)들 (Tarjan)
		/// </summary>
		public static IReadOnlyList<IReadOnlyList<ItemId>> FindItemCycles(UranusDatabase db)
		{
			var edges = new Dictionary<ItemId, HashSet<ItemId>>();
			foreach (var recipe in db.RecipeList)
			{
				foreach (var input in recipe.Inputs)
				{
					if (!edges.TryGetValue(input.Item, out var targets))
					{
						targets = [];
						edges[input.Item] = targets;
					}

					targets.UnionWith(recipe.Outputs.Select(x => x.Item));
				}
			}

			var index = new Dictionary<ItemId, int>();
			var low = new Dictionary<ItemId, int>();
			var stack = new Stack<ItemId>();
			var onStack = new HashSet<ItemId>();
			var result = new List<IReadOnlyList<ItemId>>();
			var counter = 0;

			foreach (var node in edges.Keys.ToList())
			{
				if (!index.ContainsKey(node))
				{
					Visit(node);
				}
			}

			return result;

			// 재귀 대신 명시적 스택으로 깊이 우선 탐색한다
			void Visit(ItemId start)
			{
				var work = new Stack<(ItemId Node, IEnumerator<ItemId> Next)>();
				Enter(start);
				while (work.Count > 0)
				{
					var (node, next) = work.Peek();
					if (next.MoveNext())
					{
						var target = next.Current;
						if (!index.ContainsKey(target))
						{
							Enter(target);
						}
						else if (onStack.Contains(target))
						{
							low[node] = Math.Min(low[node], index[target]);
						}

						continue;
					}

					work.Pop();
					if (work.Count > 0)
					{
						var parent = work.Peek().Node;
						low[parent] = Math.Min(low[parent], low[node]);
					}

					if (low[node] == index[node])
					{
						var component = new List<ItemId>();
						ItemId member;
						do
						{
							member = stack.Pop();
							onStack.Remove(member);
							component.Add(member);
						}
						while (member != node);

						var selfLoop = component.Count == 1 && edges.TryGetValue(node, out var own) && own.Contains(node);
						if (component.Count > 1 || selfLoop)
						{
							result.Add(component);
						}
					}
				}

				void Enter(ItemId node)
				{
					index[node] = low[node] = counter++;
					stack.Push(node);
					onStack.Add(node);
					var targets = edges.TryGetValue(node, out var set) ? set : [];
					work.Push((node, targets.ToList().GetEnumerator()));
				}
			}
		}

		private static void CheckReferences(UranusDatabase db, List<DataIssue> issues)
		{
			foreach (var recipe in db.RecipeList)
			{
				foreach (var x in recipe.Inputs.Concat(recipe.Outputs).Where(x => !db.Items.ContainsKey(x.Item)))
				{
					issues.Add(Error(Codes.UndefinedItem, $"레시피 '{recipe.Id}'가 정의되지 않은 아이템 '{x.Item}'을 쓴다", recipe.Source));
				}
			}

			foreach (var generator in db.GeneratorList)
			{
				foreach (var x in generator.OutputPerSecond.Where(x => !db.Items.ContainsKey(x.Item)))
				{
					issues.Add(Error(Codes.UndefinedItem, $"생성기 '{generator.Item}'가 정의되지 않은 아이템 '{x.Item}'을 생산한다"));
				}
			}

			foreach (var x in db.Config.StartInventory.Where(x => !db.Items.ContainsKey(x.Item)))
			{
				issues.Add(Error(Codes.UndefinedItem, $"시작 인벤토리에 정의되지 않은 아이템 '{x.Item}'이 있다"));
			}
		}

		private static void CheckRecipes(UranusDatabase db, List<DataIssue> issues)
		{
			foreach (var recipe in db.RecipeList)
			{
				if (recipe.Inputs.Count == 0 || recipe.Outputs.Count == 0)
				{
					issues.Add(Error(Codes.EmptyRecipe, $"레시피 '{recipe.Id}'에 재료나 결과물이 없다", recipe.Source));
				}

				foreach (var x in recipe.Inputs.Concat(recipe.Outputs).Where(x => !(x.Amount > 0) || !double.IsFinite(x.Amount)))
				{
					issues.Add(Error(Codes.InvalidAmount, $"레시피 '{recipe.Id}'의 '{x.Item}' 수량이 0 이하다: {x.Amount}", recipe.Source));
				}

				if (recipe.Time < 0 || !double.IsFinite(recipe.Time))
				{
					issues.Add(Error(Codes.InvalidTime, $"레시피 '{recipe.Id}'의 시간이 잘못됐다: {recipe.Time}", recipe.Source));
				}
				else if (recipe.Time == 0)
				{
					issues.Add(Warning(Codes.ZeroTime, $"레시피 '{recipe.Id}'의 시간이 0이다(미정)", recipe.Source));
				}
			}
		}

		private static void CheckGenerators(UranusDatabase db, List<DataIssue> issues)
		{
			foreach (var generator in db.GeneratorList)
			{
				if (generator.OutputPerSecond.Count == 0 || generator.OutputPerSecond.Any(x => !(x.Amount > 0)))
				{
					issues.Add(Error(Codes.InvalidGenerator, $"생성기 '{generator.Item}'의 생산량이 없거나 0 이하다"));
				}

				if (db.FindRecipe(generator.BuildRecipe) is null)
				{
					issues.Add(Error(Codes.InvalidGenerator, $"생성기 '{generator.Item}'를 만드는 레시피 '{generator.BuildRecipe}'가 없다"));
				}
			}

			if (!(db.Config.GeneratorCostGrowth >= 1) || !double.IsFinite(db.Config.GeneratorCostGrowth))
			{
				issues.Add(Error(Codes.InvalidGenerator, $"generator_cost_growth는 1 이상의 유한한 값이어야 한다: {db.Config.GeneratorCostGrowth}"));
			}

			if (!(db.Config.OfflineCapHours >= 0) || !double.IsFinite(db.Config.OfflineCapHours))
			{
				issues.Add(Error(Codes.InvalidGenerator, $"offline_cap_hours는 0 이상의 유한한 값이어야 한다: {db.Config.OfflineCapHours}"));
			}
		}

		private static void CheckUpgradesAndGoals(UranusDatabase db, List<DataIssue> issues)
		{
			const string source = "data/game.yaml";
			var upgrades = db.Config.Upgrades.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
			foreach (var upgrade in db.Config.Upgrades)
			{
				if (upgrade.Cost.Count == 0 || upgrade.Cost.Any(x => !(x.Amount > 0) || !double.IsFinite(x.Amount)))
				{
					issues.Add(Error(Codes.InvalidUpgrade, $"업그레이드 '{upgrade.Id}'의 비용이 없거나 0 이하다", source));
				}

				foreach (var x in upgrade.Cost.Where(x => !db.Items.ContainsKey(x.Item)))
				{
					issues.Add(Error(Codes.UndefinedItem, $"업그레이드 '{upgrade.Id}'가 정의되지 않은 아이템 '{x.Item}'을 쓴다", source));
				}

				if (upgrade.Kind == UpgradeKind.Scale && (!(upgrade.Factor > 1) || !double.IsFinite(upgrade.Factor)))
				{
					issues.Add(Error(Codes.InvalidUpgrade, $"규모 업그레이드 '{upgrade.Id}'의 factor는 1보다 큰 유한한 값이어야 한다: {upgrade.Factor}", source));
				}

				foreach (var required in upgrade.Requires.Where(x => !upgrades.ContainsKey(x)))
				{
					issues.Add(Error(Codes.InvalidUpgrade, $"업그레이드 '{upgrade.Id}'가 없는 업그레이드 '{required}'를 요구한다", source));
				}

				// 선행 조건을 따라가다 자기 자신으로 돌아오면 영원히 살 수 없다
				var seen = new HashSet<string>();
				var pending = new Stack<string>(upgrade.Requires);
				while (pending.TryPop(out var next))
				{
					if (next == upgrade.Id)
					{
						issues.Add(Error(Codes.InvalidUpgrade, $"업그레이드 '{upgrade.Id}'의 선행 조건이 순환한다", source));
						break;
					}

					if (seen.Add(next) && upgrades.TryGetValue(next, out var parent))
					{
						foreach (var x in parent.Requires)
						{
							pending.Push(x);
						}
					}
				}
			}

			foreach (var goal in db.Config.Goals)
			{
				if (goal.Requirements.Count == 0 || goal.Requirements.Any(x => !(x.Amount > 0) || !double.IsFinite(x.Amount)))
				{
					issues.Add(Error(Codes.InvalidGoal, $"목표 '{goal.Id}'의 조건이 없거나 0 이하다", source));
				}

				foreach (var x in goal.Requirements.Where(x => !db.Items.ContainsKey(x.Item)))
				{
					issues.Add(Error(Codes.UndefinedItem, $"목표 '{goal.Id}'가 정의되지 않은 아이템 '{x.Item}'을 쓴다", source));
				}
			}
		}

		private static void CheckFormulas(UranusDatabase db, List<DataIssue> issues)
		{
			foreach (var item in db.ItemList.Where(x => x.Formula is not null))
			{
				if (!Formula.TryParse(item.Formula, out var composition, out var error))
				{
					issues.Add(Error(Codes.InvalidFormula, $"아이템 '{item.Id}'의 화학식을 읽을 수 없다: {error}", item.Source));
					continue;
				}

				foreach (var symbol in composition.Keys.Where(x => !db.Elements.ContainsKey(x)))
				{
					issues.Add(Error(Codes.InvalidFormula, $"아이템 '{item.Id}'의 화학식에 원소표에 없는 원소 '{symbol}'가 있다", item.Source));
				}
			}
		}

		/// <summary>
		/// 재료와 결과물이 모두 화학식을 가진 레시피는 원소별 원자 수가 같아야 한다.
		/// </summary>
		private static void CheckAtomBalance(UranusDatabase db, List<DataIssue> issues)
		{
			foreach (var recipe in db.RecipeList.Where(x => !x.IsApproximate))
			{
				var left = Sum(db, recipe.Inputs);
				var right = Sum(db, recipe.Outputs);
				if (left is null || right is null)
				{
					continue;
				}

				var differences = left.Keys.Union(right.Keys)
					.Select(symbol => (Symbol: symbol, In: left.GetValueOrDefault(symbol), Out: right.GetValueOrDefault(symbol)))
					.Where(x => Math.Abs(x.In - x.Out) > Quantity.RelativeTolerance * Math.Max(1, Math.Max(x.In, x.Out)))
					.Select(x => $"{x.Symbol} {QuantityFormatter.FormatNumber(x.In)}→{QuantityFormatter.FormatNumber(x.Out)}")
					.ToList();

				if (differences.Count > 0)
				{
					issues.Add(Error(Codes.AtomImbalance, $"레시피 '{recipe.Id}'의 원자 수가 맞지 않는다: {string.Join(", ", differences)}", recipe.Source));
				}
			}

			static Dictionary<string, double>? Sum(UranusDatabase db, IReadOnlyList<ItemAmount> amounts)
			{
				var total = new Dictionary<string, double>();
				foreach (var x in amounts)
				{
					if (db.FindItem(x.Item)?.Formula is not { } formula || !Formula.TryParse(formula, out var composition, out _))
					{
						return null;
					}

					foreach (var (symbol, count) in composition)
					{
						total[symbol] = total.GetValueOrDefault(symbol) + count * x.Amount;
					}
				}

				return total;
			}
		}

		private static void CheckNames(UranusDatabase db, List<DataIssue> issues)
		{
			foreach (var group in db.ItemList.GroupBy(x => UranusDatabase.NormalizeName(x.Name.Ko)).Where(g => g.Count() > 1))
			{
				issues.Add(Warning(Codes.SimilarNames, $"공백만 다른 이름: {string.Join(", ", group.Select(x => $"'{x.Name.Ko}'({x.Id})"))}"));
			}

			foreach (var item in db.ItemList.Where(x => x.Name.Ko != x.Name.Ko.Trim()))
			{
				issues.Add(Warning(Codes.SimilarNames, $"아이템 '{item.Id}' 이름 앞뒤에 공백이 있다", item.Source));
			}
		}

		private static void CheckProgression(UranusDatabase db, List<DataIssue> issues)
		{
			var obtainable = new HashSet<ItemId>(db.Config.StartInventory.Select(x => x.Item));
			obtainable.UnionWith(db.RecipeList.SelectMany(x => x.Outputs).Select(x => x.Item));
			obtainable.UnionWith(db.GeneratorList.SelectMany(x => x.OutputPerSecond).Select(x => x.Item));

			var used = db.RecipeList.SelectMany(x => x.Inputs).Select(x => x.Item).ToHashSet();
			foreach (var item in used.Where(x => !obtainable.Contains(x) && db.Items.ContainsKey(x)))
			{
				issues.Add(Warning(Codes.Unobtainable, $"'{db.GetName(item)}'({item})을 얻을 방법이 없다(레시피·생성기 없음)", db.FindItem(item)?.Source));
			}

			var reach = RecipeGraph.ComputeReachability(db, db.Config.StartInventory.Select(x => x.Item));
			foreach (var recipe in db.RecipeList.Where(x => !reach.Recipes.Contains(x.Id)))
			{
				var missing = recipe.Inputs.Select(x => x.Item).Where(x => !reach.Items.Contains(x)).Distinct().Select(db.GetName);
				issues.Add(Warning(Codes.Unreachable, $"레시피 '{recipe.Id}'에 시작 상태에서 도달할 수 없다(없는 재료: {string.Join(", ", missing)})", recipe.Source));
			}

			var mentioned = used.Concat(obtainable).ToHashSet();
			foreach (var item in db.ItemList.Where(x => !x.IsGoal && !mentioned.Contains(x.Id) && !db.Generators.ContainsKey(x.Id)))
			{
				issues.Add(Info(Codes.UnusedItem, $"'{item.Name.Ko}'({item.Id})을 쓰거나 만드는 곳이 없다", item.Source));
			}
		}

		private static void CheckMasses(UranusDatabase db, List<DataIssue> issues)
		{
			var masses = RecipeGraph.ComputeMasses(db);

			foreach (var item in db.ItemList.Where(x => x.ExpectedMass is not null))
			{
				var range = item.ExpectedMass!.Value;
				var lower = masses.GetLowerBound(item.Id);
				var complete = masses.IsComplete(item.Id);
				if (lower > range.MaxGrams || (complete && lower < range.MinGrams))
				{
					var computed = (complete ? "" : "최소 ") + QuantityFormatter.FormatMass(lower);
					issues.Add(Warning(Codes.MassOutOfRange,
						$"'{item.Name.Ko}'({item.Id}) 1개의 질량이 {computed}로, 실제 범위 {QuantityFormatter.FormatMass(range.MinGrams)}~{QuantityFormatter.FormatMass(range.MaxGrams)}를 벗어난다",
						item.Source));
				}
			}

			foreach (var item in db.ItemList.Where(x => x.Formula is null))
			{
				var estimates = db.GetProducers(item.Id)
					.Select(recipe => (Recipe: recipe, Mass: RecipeGraph.EstimateFromRecipe(recipe, item.Id, masses)))
					.Where(x => x.Mass is { Complete: true, Grams: > 0 })
					.ToList();

				if (estimates.Count < 2)
				{
					continue;
				}

				var min = estimates.MinBy(x => x.Mass!.Value.Grams);
				var max = estimates.MaxBy(x => x.Mass!.Value.Grams);
				if (max.Mass!.Value.Grams > 100 * min.Mass!.Value.Grams)
				{
					issues.Add(Warning(Codes.RecipeMassMismatch,
						$"'{item.Name.Ko}'({item.Id})를 만드는 레시피끼리 질량이 크게 다르다: {min.Recipe.Id}={QuantityFormatter.FormatMass(min.Mass.Value.Grams)}, {max.Recipe.Id}={QuantityFormatter.FormatMass(max.Mass.Value.Grams)}. 서로 다른 아이템일 수 있다",
						item.Source));
				}
			}

			foreach (var recipe in db.RecipeList)
			{
				var all = recipe.Inputs.Concat(recipe.Outputs).ToList();
				if (all.All(x => db.FindItem(x.Item)?.Formula is not null) || !all.All(x => masses.IsComplete(x.Item)))
				{
					continue;
				}

				var input = recipe.Inputs.Sum(x => x.Amount * masses.GetLowerBound(x.Item));
				var output = recipe.Outputs.Sum(x => x.Amount * masses.GetLowerBound(x.Item));
				if (output > input * (1 + 1e-6))
				{
					var message = $"레시피 '{recipe.Id}'에서 질량이 늘어난다: {QuantityFormatter.FormatMass(input)} → {QuantityFormatter.FormatMass(output)}";
					issues.Add(recipe.IsApproximate ? Info(Codes.MassGain, message, recipe.Source) : Warning(Codes.MassGain, message, recipe.Source));
				}
			}
		}

		private static DataIssue Error(string code, string message, string? source = null) => new(IssueSeverity.Error, code, message, source);

		private static DataIssue Warning(string code, string message, string? source = null) => new(IssueSeverity.Warning, code, message, source);

		private static DataIssue Info(string code, string message, string? source = null) => new(IssueSeverity.Info, code, message, source);
	}
}
