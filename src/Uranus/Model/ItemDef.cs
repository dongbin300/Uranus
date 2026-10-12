namespace Uranus
{
	/// <summary>
	/// 수량을 세는 단위
	/// </summary>
	public enum ItemUnit
	{
		/// <summary>
		/// 원자·분자·화학식 단위 하나
		/// </summary>
		Particle,

		/// <summary>
		/// 부품·장치 한 개
		/// </summary>
		Piece,
	}

	/// <summary>
	/// 실제 세계에서의 질량 범위(그램). 검증기가 레시피로 계산한 질량과 비교한다.
	/// </summary>
	public readonly record struct MassRange(double MinGrams, double MaxGrams)
	{
		public bool Contains(double grams) => grams >= MinGrams && grams <= MaxGrams;
	}

	/// <summary>
	/// 아이템 정의
	/// </summary>
	public sealed record ItemDef(ItemId Id, LocalizedText Name, string Category, ItemUnit Unit)
	{
		/// <summary>
		/// 화학식. 있으면 원자 보존 검사와 질량 계산에 쓴다.
		/// </summary>
		public string? Formula { get; init; }

		/// <summary>
		/// 아직 레시피가 없는 장기 목표
		/// </summary>
		public bool IsGoal { get; init; }

		/// <summary>
		/// 세상에 없는(가상의) 아이템
		/// </summary>
		public bool IsFictional { get; init; }

		public MassRange? ExpectedMass { get; init; }

		/// <summary>
		/// 출처·근거 메모
		/// </summary>
		public string? Note { get; init; }

		public string? Description { get; init; }

		/// <summary>
		/// 정의된 데이터 파일
		/// </summary>
		public string? Source { get; init; }
	}
}
