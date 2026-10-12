namespace Uranus.Extensions
{
	public static class DecimalExtension
	{
		public static string ToFormatLargeNumber(this decimal number)
		{
			string[] suffixes = { "", "k", "m", "b", "t" };

			if (number < 1000)
			{
				return number % 1 == 0 ? number.ToString("0") : number.ToString("0.##");
			}

			int magnitude = (int)(Math.Floor(Math.Log10((double)Math.Abs(number)) / 3));
			decimal divisor = (decimal)Math.Pow(1000, magnitude);

			decimal shortNumber = number / divisor;
			string suffix = suffixes[magnitude];

			return shortNumber % 1 == 0 ? shortNumber.ToString("0") : shortNumber.ToString("0.##") + suffix;
		}
	}
}
