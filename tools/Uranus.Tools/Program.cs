using System.Text;

using MachineFree.Core;

using Uranus;
using Uranus.Analysis;
using Uranus.Data;
using Uranus.Quantities;
using Uranus.Validation;

Console.OutputEncoding = Encoding.UTF8;

var arguments = args.ToList();
var command = arguments.Count > 0 ? arguments[0] : "help";
var verbose = arguments.Remove("--verbose");
var dataDirectory = TakeOption("--data") ?? UranusDataLoader.FindDataDirectory(Environment.CurrentDirectory, AppContext.BaseDirectory);

if (command is "help" or "-h" or "--help")
{
	return Help();
}

if (dataDirectory is null)
{
	Console.Error.WriteLine("data 폴더를 찾지 못했다. --data <경로>로 지정한다.");
	return 2;
}

return command switch
{
	"validate" => Validate(),
	"bom" => Bom(),
	"tree" => Tree(),
	"import-save" => ImportSave(),
	"simulate" => Simulate(),
	_ => Help(),
};

int Simulate()
{
	var db = LoadOrExit();
	if (db is null)
	{
		return 2;
	}

	var hours = double.TryParse(TakeOption("--hours"), out var h) ? h : 8;
	var step = double.TryParse(TakeOption("--step"), out var s) ? s : 1;
	var bot = new Uranus.Tools.BalanceBot(db);
	var watch = System.Diagnostics.Stopwatch.StartNew();
	bot.Run(hours, step);

	Console.WriteLine($"자동 플레이 {hours}시간 (스텝 {step}초, 계산 {watch.Elapsed.TotalSeconds:0.0}초)");
	Console.WriteLine();
	foreach (var milestone in bot.Milestones)
	{
		var time = TimeSpan.FromSeconds(milestone.Time);
		Console.WriteLine($"  {(int)time.TotalHours,3}:{time.Minutes:00}:{time.Seconds:00}  {milestone.Kind,-5}  {milestone.Name}");
	}

	var sim = bot.Simulation;
	var goals = db.Config.Goals;
	Console.WriteLine();
	Console.WriteLine($"목표 {sim.State.CompletedGoals.Count}/{goals.Count} · 업그레이드 {sim.State.Upgrades.Count}/{db.Config.Upgrades.Count} · 제작 {sim.State.TotalCrafts:N0}회");
	var pending = goals.Where(x => !sim.State.CompletedGoals.ContainsKey(x.Id)).Select(x => x.Name.Ko).ToList();
	if (pending.Count > 0)
	{
		Console.WriteLine($"남은 목표: {string.Join(", ", pending)}");
	}

	Console.WriteLine("보유: " + string.Join(", ", sim.State.Inventory
		.OrderByDescending(x => x.Value)
		.Take(12)
		.Select(x => $"{db.GetName(x.Key)} {QuantityFormatter.FormatCount(x.Value, NumberStyle.Scientific)}")));
	Console.WriteLine("대기열: " + string.Join(", ", sim.State.Queue.Select(x => $"{x.Recipe}×{x.Remaining}")));

	var next = db.Config.Upgrades.FirstOrDefault(x => !sim.IsUpgradeOwned(x));
	if (next is not null)
	{
		var lines = next.Cost.Select(x => $"{db.GetName(x.Item)} {QuantityFormatter.FormatCount(sim.GetAmount(x.Item), NumberStyle.Scientific)}/{QuantityFormatter.FormatCount(x.Amount, NumberStyle.Scientific)}");
		Console.WriteLine($"다음 업그레이드 '{next.Name.Ko}': {string.Join(", ", lines)}");
	}

	return 0;
}

int ImportSave()
{
	var db = LoadOrExit();
	var source = arguments.Skip(1).FirstOrDefault(x => !x.StartsWith("--"));
	if (db is null || source is null || !File.Exists(source))
	{
		Console.Error.WriteLine($"세이브 파일을 찾지 못했다: {source}");
		return 2;
	}

	var target = TakeOption("--out") ?? SaveFile.DefaultPath;
	if (File.Exists(target) && !arguments.Contains("--force"))
	{
		Console.Error.WriteLine($"이미 세이브가 있다: {target} (덮어쓰려면 --force)");
		return 2;
	}

	var state = SaveFile.Deserialize(File.ReadAllText(source), db, out var warnings);
	state.SavedAt = DateTimeOffset.UtcNow;
	SaveFile.Write(target, state);

	foreach (var warning in warnings)
	{
		Console.WriteLine($"경고: {warning}");
	}

	Console.WriteLine($"변환 완료: 아이템 {state.Inventory.Count}종, 플레이 {TimeSpan.FromSeconds(state.PlayTime):h\\:mm\\:ss} → {target}");
	return 0;
}

int Help()
{
	Console.WriteLine("""
		Uranus.Tools — 데이터 도구

		  validate [--verbose]              데이터를 불러와 검증한다. 오류가 있으면 종료 코드 1
		  bom <아이템> [--amount N]          기본 레시피를 따라 펼친 필요 원료와 질량
		  tree <아이템> [--depth N]          제작 트리를 Mermaid 그래프로 출력
		  import-save <예전 Save.json>       예전 세이브를 새 형식으로 바꿔 저장 위치에 쓴다 [--out 경로] [--force]
		  simulate [--hours 8] [--step 1]    자동 플레이로 목표·업그레이드 도달 시각을 잰다 (밸런스 확인)

		  공통 옵션: --data <data 폴더>  (기본: 현재 폴더에서 위로 찾기)
		  <아이템>은 ID(H2O) 또는 한국어 이름(물)
		""");
	return 0;
}

int Validate()
{
	var result = UranusDataLoader.Load(dataDirectory);
	var db = result.Database;
	var issues = result.Issues.Concat(UranusValidator.Validate(db)).ToList();

	var reach = RecipeGraph.ComputeReachability(db, db.Config.StartInventory.Select(x => x.Item));
	Console.WriteLine($"데이터: {dataDirectory}");
	Console.WriteLine($"원소 {db.ElementList.Count} · 아이템 {db.ItemList.Count} · 레시피 {db.RecipeList.Count} · 생성기 {db.GeneratorList.Count}");
	Console.WriteLine($"시작 상태에서 도달 가능: 아이템 {reach.Items.Count}/{db.ItemList.Count}, 레시피 {reach.Recipes.Count}/{db.RecipeList.Count}, 생성기 {reach.Generators.Count}/{db.GeneratorList.Count}");
	Console.WriteLine();

	foreach (var severity in new[] { IssueSeverity.Error, IssueSeverity.Warning, IssueSeverity.Info })
	{
		foreach (var group in issues.Where(x => x.Severity == severity).GroupBy(x => x.Code).OrderBy(g => g.Key))
		{
			var label = severity switch { IssueSeverity.Error => "오류", IssueSeverity.Warning => "경고", _ => "정보" };
			Console.WriteLine($"{label} {group.Key} × {group.Count()}");
			var shown = severity == IssueSeverity.Error || verbose ? group.ToList() : group.Take(3).ToList();
			foreach (var issue in shown)
			{
				Console.WriteLine($"  - {issue.Message}{(issue.Source is null ? "" : $"  ({issue.Source})")}");
			}

			if (shown.Count < group.Count())
			{
				Console.WriteLine($"  … 외 {group.Count() - shown.Count}개 (--verbose로 전체 보기)");
			}
		}
	}

	var errors = issues.Count(x => x.Severity == IssueSeverity.Error);
	var warnings = issues.Count(x => x.Severity == IssueSeverity.Warning);
	var infos = issues.Count(x => x.Severity == IssueSeverity.Info);
	Console.WriteLine();
	Console.WriteLine($"오류 {errors} · 경고 {warnings} · 정보 {infos}");
	return errors > 0 ? 1 : 0;
}

int Bom()
{
	var db = LoadOrExit();
	if (db is null || FindItem(db) is not { } item)
	{
		return 2;
	}

	var amount = TakeOption("--amount") is { } text && AmountText.TryParse(text, out var parsed) ? parsed : 1.0;
	var masses = RecipeGraph.ComputeMasses(db);
	var leaves = RecipeGraph.ComputeBillOfMaterials(db, item.Id, amount);

	Console.WriteLine($"{item.Name.Ko}({item.Id}) {QuantityFormatter.FormatNumber(amount)}개를 만드는 데 필요한 원료 (기본 레시피 기준, 부산물 무시)");
	var total = 0.0;
	var complete = true;
	foreach (var (id, need) in leaves.OrderByDescending(x => x.Value * masses.GetLowerBound(x.Key)).ThenByDescending(x => x.Value))
	{
		var grams = need * masses.GetLowerBound(id);
		total += grams;
		complete &= masses.IsComplete(id);
		var mass = masses.IsComplete(id) ? QuantityFormatter.FormatMass(grams) : "질량 모름";
		Console.WriteLine($"  {db.GetName(id),-16} {QuantityFormatter.FormatCount(need, NumberStyle.Scientific),12}  {mass}");
	}

	Console.WriteLine($"합계 질량: {(complete ? "" : "최소 ")}{QuantityFormatter.FormatMass(total)}");
	return 0;
}

int Tree()
{
	var db = LoadOrExit();
	if (db is null || FindItem(db) is not { } item)
	{
		return 2;
	}

	var depth = int.TryParse(TakeOption("--depth"), out var d) ? d : 3;
	var lines = new List<string> { "graph TD" };
	var visited = new HashSet<ItemId>();
	Walk(item.Id, depth);
	lines.ForEach(Console.WriteLine);
	return 0;

	void Walk(ItemId id, int remaining)
	{
		if (remaining <= 0 || !visited.Add(id) || RecipeGraph.IsRaw(db, id) || RecipeGraph.GetDefaultRecipe(db, id) is not { } recipe)
		{
			return;
		}

		// 간선 숫자는 결과물 1개당 재료 수량
		var produced = recipe.Outputs.Where(x => x.Item == id).Sum(x => x.Amount);
		foreach (var input in recipe.Inputs)
		{
			lines.Add($"    {Node(input.Item)} -->|{QuantityFormatter.FormatNumber(input.Amount / produced, NumberStyle.Scientific)}| {Node(id)}");
			Walk(input.Item, remaining - 1);
		}
	}

	string Node(ItemId id) => $"{Sanitize(id.Value)}[\"{db.GetName(id)}\"]";

	static string Sanitize(string id) => new(id.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
}

UranusDatabase? LoadOrExit()
{
	var result = UranusDataLoader.Load(dataDirectory);
	foreach (var issue in result.Issues.Where(x => x.Severity == IssueSeverity.Error))
	{
		Console.Error.WriteLine(issue);
	}

	return result.HasErrors ? null : result.Database;
}

ItemDef? FindItem(UranusDatabase db)
{
	var key = arguments.Skip(1).FirstOrDefault(x => !x.StartsWith("--"));
	var item = key is null ? null : db.FindItem(new ItemId(key)) ?? db.FindItemByName(key);
	if (item is null)
	{
		Console.Error.WriteLine($"아이템을 찾지 못했다: {key}");
	}

	return item;
}

string? TakeOption(string name)
{
	var index = arguments.IndexOf(name);
	if (index < 0 || index + 1 >= arguments.Count)
	{
		return null;
	}

	var value = arguments[index + 1];
	arguments.RemoveRange(index, 2);
	return value;
}
