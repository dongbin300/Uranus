using System.Globalization;

namespace Uranus.Quantities
{
	public enum NumberStyle
	{
		/// <summary>
		/// 1.23k, 45.6m … 1조(t)까지, 그 위는 과학 표기
		/// </summary>
		Short,

		/// <summary>
		/// 1.13e24
		/// </summary>
		Scientific,

		/// <summary>
		/// 1234만, 1.13자 (만·억·조·경·해·자·양·구·간·정·재·극)
		/// </summary>
		Korean,
	}

	/// <summary>
	/// 화면 표시용 수량 문자열. 개수는 넘치게 보이지 않도록 반올림 대신 버림을 쓴다.
	/// </summary>
	public static class QuantityFormatter
	{
		private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
		private static readonly string[] ShortSuffixes = ["", "k", "m", "b", "t"];
		private static readonly string[] KoreanUnits = ["", "만", "억", "조", "경", "해", "자", "양", "구", "간", "정", "재", "극"];

		/// <summary>
		/// 개수 표시. 정수 부분만 보여 준다.
		/// </summary>
		public static string FormatCount(double value, NumberStyle style = NumberStyle.Short)
		{
			if (double.IsNaN(value))
			{
				return "?";
			}

			if (value < 0)
			{
				return "-" + FormatCount(-value, style);
			}

			var count = Quantity.Floor(value);
			if (count < 1000)
			{
				return count.ToString("0", Invariant);
			}

			return style switch
			{
				NumberStyle.Scientific => FormatScientific(count),
				NumberStyle.Korean => FormatKorean(count),
				_ => FormatShort(count),
			};
		}

		/// <summary>
		/// 소수가 의미 있는 값(초당 생산량, 시간) 표시
		/// </summary>
		public static string FormatNumber(double value, NumberStyle style = NumberStyle.Short)
		{
			if (double.IsNaN(value))
			{
				return "?";
			}

			var abs = Math.Abs(value);
			if (abs >= 1000)
			{
				return FormatCount(value, style);
			}

			if (abs == 0 || abs >= 0.001)
			{
				return value.ToString("0.###", Invariant);
			}

			return FormatScientific(value);
		}

		/// <summary>
		/// 질량 표시: 2.99e-23 g, 52.8 g, 978 kg
		/// </summary>
		public static string FormatMass(double grams)
		{
			if (double.IsNaN(grams) || grams <= 0)
			{
				return "0 g";
			}

			if (grams >= 1e12)
			{
				return $"{FormatScientific(grams / 1e6)} t";
			}

			(double Scale, string Unit) unit = grams switch
			{
				>= 1e6 => (1e6, "t"),
				>= 1e3 => (1e3, "kg"),
				>= 1 => (1, "g"),
				>= 1e-3 => (1e-3, "mg"),
				>= 1e-6 => (1e-6, "µg"),
				>= 1e-9 => (1e-9, "ng"),
				_ => (0, "g"),
			};

			if (unit.Scale == 0)
			{
				return $"{FormatScientific(grams)} g";
			}

			return $"{TruncateSignificant(grams / unit.Scale)} {unit.Unit}";
		}

		private static string FormatShort(double value)
		{
			var magnitude = (int)Math.Floor(Math.Log10(value) / 3);
			if (magnitude >= ShortSuffixes.Length)
			{
				return FormatScientific(value);
			}

			return TruncateSignificant(value / Math.Pow(1000, magnitude)) + ShortSuffixes[magnitude];
		}

		private static string FormatKorean(double value)
		{
			var magnitude = (int)Math.Floor(Math.Log10(value) / 4);
			if (magnitude >= KoreanUnits.Length)
			{
				return FormatScientific(value);
			}

			var scaled = value / Math.Pow(10000, magnitude);
			var text = scaled >= 1000 ? Math.Floor(scaled).ToString("0", Invariant) : TruncateSignificant(scaled);
			return text + KoreanUnits[magnitude];
		}

		private static string FormatScientific(double value)
		{
			if (value == 0)
			{
				return "0";
			}

			var sign = value < 0 ? "-" : "";
			var abs = Math.Abs(value);
			var exponent = (int)Math.Floor(Math.Log10(abs));
			var mantissa = abs / Math.Pow(10, exponent);
			if (mantissa >= 10)
			{
				mantissa /= 10;
				exponent++;
			}

			var digits = Math.Floor(mantissa * 100 + 1e-9) / 100;
			return $"{sign}{digits.ToString("0.##", Invariant)}e{exponent}";
		}

		/// <summary>
		/// 유효숫자 3자리에서 버림: 1.999 → 1.99, 12.37 → 12.3, 123.9 → 123
		/// </summary>
		private static string TruncateSignificant(double value)
		{
			var decimals = value >= 100 ? 0 : value >= 10 ? 1 : 2;
			var factor = Math.Pow(10, decimals);
			var truncated = Math.Floor(value * factor + 1e-9) / factor;
			return truncated.ToString(decimals switch { 0 => "0", 1 => "0.#", _ => "0.##" }, Invariant);
		}
	}
}
