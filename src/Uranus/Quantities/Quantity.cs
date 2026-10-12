using System.Globalization;

namespace Uranus.Quantities
{
	/// <summary>
	/// 수량 비교 규칙. 소수를 누적하면 0.1을 10번 더해도 0.9999999999999999가 되므로
	/// 비교와 내림에는 상대 허용오차를 둔다.
	/// </summary>
	public static class Quantity
	{
		public const double RelativeTolerance = 1e-9;

		public static double Tolerance(double magnitude) => RelativeTolerance * Math.Max(1.0, Math.Abs(magnitude));

		/// <summary>
		/// have가 need 이상인가 (허용오차 포함). need가 무한대나 NaN이면 항상 false.
		/// </summary>
		public static bool IsAtLeast(double have, double need) => double.IsFinite(need) && have + Tolerance(need) >= need;

		/// <summary>
		/// 정수 부분. 0.9999999999999999는 1로 본다.
		/// </summary>
		public static double Floor(double value) => Math.Floor(value + Tolerance(value));

		/// <summary>
		/// 다음 1개까지 쌓인 소수 부분 (0 이상 1 미만)
		/// </summary>
		public static double Fraction(double value)
		{
			var fraction = value - Floor(value);
			return fraction < 0 ? 0 : fraction;
		}

		/// <summary>
		/// value에서 amount를 뺀 값. 허용오차 안의 찌꺼기는 0으로 정리한다.
		/// </summary>
		public static double Subtract(double value, double amount)
		{
			var result = value - amount;
			return Math.Abs(result) <= Tolerance(amount) ? 0 : result;
		}
	}

	/// <summary>
	/// 데이터 파일의 수량 표기: "2", "0.25", "1.133e24", "1/3", 예전 엑셀 표기 "1.133^24"
	/// </summary>
	public static class AmountText
	{
		public static bool TryParse(string? text, out double value)
		{
			value = 0;
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}

			var s = text.Trim();

			var slash = s.IndexOf('/');
			if (slash > 0)
			{
				if (!TryParseNumber(s[..slash], out var numerator) ||
					!TryParseNumber(s[(slash + 1)..], out var denominator) ||
					denominator == 0)
				{
					return false;
				}

				value = numerator / denominator;
				return double.IsFinite(value);
			}

			var caret = s.IndexOf('^');
			if (caret > 0)
			{
				if (!int.TryParse(s[(caret + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent))
				{
					return false;
				}

				return TryParseNumber($"{s[..caret].Trim()}e{exponent}", out value);
			}

			return TryParseNumber(s, out value);
		}

		private static bool TryParseNumber(string s, out double value) =>
			double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
	}

	/// <summary>
	/// 질량 표기: "53g", "500mg", "2.5 kg", "1t"
	/// </summary>
	public static class MassText
	{
		private static readonly (string Unit, double Grams)[] Units =
		[
			("kg", 1e3), ("mg", 1e-3), ("ug", 1e-6), ("µg", 1e-6), ("ng", 1e-9), ("pg", 1e-12), ("fg", 1e-15), ("ag", 1e-18), ("t", 1e6), ("g", 1.0),
		];

		public static bool TryParse(string? text, out double grams)
		{
			grams = 0;
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}

			var s = text.Trim();
			foreach (var (unit, factor) in Units)
			{
				if (s.EndsWith(unit, StringComparison.Ordinal) &&
					double.TryParse(s[..^unit.Length].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				{
					grams = value * factor;
					return double.IsFinite(grams);
				}
			}

			return false;
		}
	}
}
