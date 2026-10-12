using Uranus.Chemistry;

namespace Uranus.Analysis
{
	/// <summary>
	/// 시작 상태에서 얻을 수 있는 아이템·레시피·생성기 (수량은 따지지 않는다)
	/// </summary>
	public sealed class Reachability
	{
		public required IReadOnlySet<ItemId> Items { get; init; }
		public required IReadOnlySet<RecipeId> Recipes { get; init; }
		public required IReadOnlySet<ItemId> Generators { get; init; }
	}

	/// <summary>
	/// 아이템 1단위의 질량(그램). 화학식이 있으면 원자량으로, 없으면 기본 레시피의 재료 질량으로 계산한다.
	/// 재료 중 질량을 모르는 것이 있으면 아는 부분만 더한 하한값이 되고 IsComplete가 false다.
	/// </summary>
	public sealed class MassTable
	{
		private readonly Dictionary<ItemId, (double Grams, bool Complete)> masses;

		internal MassTable(Dictionary<ItemId, (double Grams, bool Complete)> masses)
		{
			this.masses = masses;
		}

		public bool IsComplete(ItemId item) => masses.TryGetValue(item, out var m) && m.Complete;

		/// <summary>
		/// 완전히 계산된 질량. 모르면 null.
		/// </summary>
		public double? GetMass(ItemId item) => masses.TryGetValue(item, out var m) && m.Complete ? m.Grams : null;

		/// <summary>
		/// 아는 부분만 더한 질량 (모르는 재료는 0으로 본다)
		/// </summary>
		public double GetLowerBound(ItemId item) => masses.TryGetValue(item, out var m) ? m.Grams : 0;
	}

	public static class RecipeGraph
	{
		/// <summary>
		/// 시작 아이템에서 출발해, 재료가 모두 있는 레시피와 생성기를 반복해서 열어 간다.
		/// </summary>
		public static Reachability ComputeReachability(UranusDatabase db, IEnumerable<ItemId> startItems)
		{
			var items = new HashSet<ItemId>(startItems);
			var recipes = new HashSet<RecipeId>();
			var generators = new HashSet<ItemId>();

			var changed = true;
			while (changed)
			{
				changed = false;
				foreach (var recipe in db.RecipeList)
				{
					if (recipes.Contains(recipe.Id) || recipe.Inputs.Count == 0 || !recipe.Inputs.All(x => items.Contains(x.Item)))
					{
						continue;
					}

					recipes.Add(recipe.Id);
					changed = true;
					foreach (var output in recipe.Outputs)
					{
						items.Add(output.Item);
					}
				}

				foreach (var generator in db.GeneratorList)
				{
					if (!items.Contains(generator.Item) || !generators.Add(generator.Item))
					{
						continue;
					}

					changed = true;
					foreach (var output in generator.OutputPerSecond)
					{
						items.Add(output.Item);
					}
				}
			}

			return new Reachability { Items = items, Recipes = recipes, Generators = generators };
		}

		/// <summary>
		/// 아이템을 만드는 기본 레시피: 그 아이템이 첫 번째 결과물인 레시피를 먼저, 그다음 부산물로 내는 레시피 (각각 데이터 순서).
		/// 예: 물은 단백질 합성의 부산물이 아니라 물 합성이 기본이다.
		/// </summary>
		public static RecipeDef? GetDefaultRecipe(UranusDatabase db, ItemId item)
		{
			var producers = db.GetProducers(item);
			return producers.FirstOrDefault(x => x.Outputs.Count > 0 && x.Outputs[0].Item == item) ?? producers.FirstOrDefault();
		}

		/// <summary>
		/// 화학식의 질량(그램/개). 화학식이 없거나 잘못됐으면 null.
		/// </summary>
		public static double? GetFormulaMass(UranusDatabase db, ItemDef item)
		{
			if (item.Formula is null || !Formula.TryParse(item.Formula, out var composition, out _))
			{
				return null;
			}

			return Formula.MolarMass(composition, db.Elements) / Formula.Avogadro;
		}

		public static MassTable ComputeMasses(UranusDatabase db)
		{
			var masses = new Dictionary<ItemId, (double Grams, bool Complete)>();
			foreach (var item in db.ItemList)
			{
				if (GetFormulaMass(db, item) is { } grams)
				{
					masses[item.Id] = (grams, true);
				}
			}

			// 기본 레시피를 따라 질량을 위로 전파한다. 그래프 깊이만큼 반복하면 수렴한다.
			for (var iteration = 0; iteration < 200; iteration++)
			{
				var changed = false;
				foreach (var item in db.ItemList)
				{
					if (masses.TryGetValue(item.Id, out var known) && known.Complete && item.Formula is not null)
					{
						continue;
					}

					if (GetDefaultRecipe(db, item.Id) is not { } recipe ||
						EstimateFromRecipe(recipe, item.Id, masses) is not { } estimate)
					{
						continue;
					}

					if (!masses.TryGetValue(item.Id, out var current) ||
						current.Complete != estimate.Complete ||
						Math.Abs(current.Grams - estimate.Grams) > 1e-12 * Math.Max(Math.Abs(current.Grams), 1e-300))
					{
						masses[item.Id] = estimate;
						changed = true;
					}
				}

				if (!changed)
				{
					break;
				}
			}

			return new MassTable(masses);
		}

		/// <summary>
		/// 이 레시피로 만들었을 때 결과물 1단위의 질량. 다른 결과물(부산물)의 질량은 뺀다.
		/// </summary>
		public static (double Grams, bool Complete)? EstimateFromRecipe(RecipeDef recipe, ItemId output, MassTable table) =>
			EstimateFromRecipe(recipe, output, recipe.Inputs.Concat(recipe.Outputs)
				.Select(x => x.Item)
				.Distinct()
				.Where(x => x != output)
				.ToDictionary(x => x, x => (table.GetLowerBound(x), table.IsComplete(x))));

		private static (double Grams, bool Complete)? EstimateFromRecipe(
			RecipeDef recipe,
			ItemId output,
			IReadOnlyDictionary<ItemId, (double Grams, bool Complete)> masses)
		{
			var amount = recipe.Outputs.Where(x => x.Item == output).Sum(x => x.Amount);
			if (amount <= 0)
			{
				return null;
			}

			var grams = 0.0;
			var complete = true;
			foreach (var input in recipe.Inputs)
			{
				var m = masses.GetValueOrDefault(input.Item);
				grams += input.Amount * m.Grams;
				complete &= m.Complete;
			}

			foreach (var other in recipe.Outputs.Where(x => x.Item != output))
			{
				var m = masses.GetValueOrDefault(other.Item);
				grams -= other.Amount * m.Grams;
				complete &= m.Complete;
			}

			return (Math.Max(grams, 0) / amount, complete);
		}

		/// <summary>
		/// 기본 레시피를 따라 내려가 더 이상 만들 수 없는 재료까지 펼친다. 부산물은 무시한다.
		/// </summary>
		public static IReadOnlyDictionary<ItemId, double> ComputeBillOfMaterials(UranusDatabase db, ItemId item, double amount)
		{
			var leaves = new Dictionary<ItemId, double>();
			Expand(item, amount, []);
			return leaves;

			void Expand(ItemId current, double need, HashSet<ItemId> path)
			{
				var recipe = db.Generators.ContainsKey(current) ? null : GetDefaultRecipe(db, current);
				var produced = recipe?.Outputs.Where(x => x.Item == current).Sum(x => x.Amount) ?? 0;
				if (recipe is null || produced <= 0 || IsRaw(db, current) || !path.Add(current))
				{
					leaves[current] = leaves.GetValueOrDefault(current) + need;
					return;
				}

				foreach (var input in recipe.Inputs)
				{
					Expand(input.Item, need * input.Amount / produced, path);
				}

				path.Remove(current);
			}
		}

		/// <summary>
		/// 생성기가 직접 생산하는 원료인가
		/// </summary>
		public static bool IsRaw(UranusDatabase db, ItemId item) =>
			db.GeneratorList.Any(g => g.OutputPerSecond.Any(x => x.Item == item));
	}
}
