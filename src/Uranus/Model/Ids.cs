namespace Uranus
{
	/// <summary>
	/// 아이템 고유 ID. 세이브와 데이터가 이 값으로 아이템을 가리키므로 한 번 정하면 바꾸지 않는다.
	/// </summary>
	public readonly record struct ItemId(string Value)
	{
		public override string ToString() => Value;
	}

	/// <summary>
	/// 레시피 고유 ID
	/// </summary>
	public readonly record struct RecipeId(string Value)
	{
		public override string ToString() => Value;
	}
}
