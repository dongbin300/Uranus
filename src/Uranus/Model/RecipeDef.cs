namespace Uranus
{
	/// <summary>
	/// 아이템과 수량. 1보다 작은 수량은 소수로 누적된다.
	/// </summary>
	public readonly record struct ItemAmount(ItemId Item, double Amount);

	/// <summary>
	/// 레시피: 재료와 시간을 들여 결과물을 만든다.
	/// </summary>
	public sealed record RecipeDef(RecipeId Id, string Category, double Time, IReadOnlyList<ItemAmount> Inputs, IReadOnlyList<ItemAmount> Outputs)
	{
		/// <summary>
		/// 표시 이름. 없으면 결과물 이름을 쓴다.
		/// </summary>
		public LocalizedText? Name { get; init; }

		/// <summary>
		/// 게임식 비율. 원자 보존 검사를 건너뛴다.
		/// </summary>
		public bool IsApproximate { get; init; }

		public string? Note { get; init; }

		public string? Source { get; init; }
	}

	/// <summary>
	/// 생성기: 보유한 대수만큼 초당 OutputPerSecond를 생산한다. 생성기 자체도 아이템이고 BuildRecipe로 만든다.
	/// </summary>
	public sealed record GeneratorDef(ItemId Item, RecipeId BuildRecipe, IReadOnlyList<ItemAmount> OutputPerSecond);

	/// <summary>
	/// 원소. AtomicMass는 표준 원자량(g/mol).
	/// </summary>
	public sealed record ElementDef(string Symbol, LocalizedText Name, double AtomicMass);

	/// <summary>
	/// 게임 설정 (data/game.yaml)
	/// </summary>
	public sealed record GameConfig(IReadOnlyList<ItemAmount> StartInventory, double GeneratorCostGrowth, double OfflineCapHours)
	{
		public static GameConfig Default { get; } = new([], 1.0, 24.0);

		public IReadOnlyList<UpgradeDef> Upgrades { get; init; } = [];

		public IReadOnlyList<GoalDef> Goals { get; init; } = [];
	}

	public enum UpgradeKind
	{
		/// <summary>
		/// 생산 라인을 하나 늘린다 (대기열 앞쪽을 동시에 만든다)
		/// </summary>
		Line,

		/// <summary>
		/// 입자 단위 레시피·생성기의 규모를 Factor배로 키운다 (예: 몰 = 6.022e23)
		/// </summary>
		Scale,
	}

	/// <summary>
	/// 아이템을 내고 사는 업그레이드
	/// </summary>
	public sealed record UpgradeDef(string Id, LocalizedText Name, UpgradeKind Kind, double Factor, IReadOnlyList<ItemAmount> Cost, IReadOnlyList<string> Requires)
	{
		public string? Note { get; init; }
	}

	/// <summary>
	/// 목표: Requirements를 한꺼번에 보유하면 달성
	/// </summary>
	public sealed record GoalDef(string Id, LocalizedText Name, IReadOnlyList<ItemAmount> Requirements)
	{
		public string? Note { get; init; }
	}
}
