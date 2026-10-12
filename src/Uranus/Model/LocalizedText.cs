namespace Uranus
{
	/// <summary>
	/// 표시용 이름. 한국어는 필수, 영어는 선택.
	/// </summary>
	public sealed record LocalizedText(string Ko, string? En = null)
	{
		public override string ToString() => Ko;
	}
}
