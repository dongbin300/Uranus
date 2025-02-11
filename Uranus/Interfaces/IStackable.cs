namespace Uranus.Interfaces
{
	public interface IStackable
	{
		decimal Count { get; set; }
		string FormatCount { get; }
	}
}
