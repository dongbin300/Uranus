using System.Globalization;

namespace Uranus.Chemistry
{
	/// <summary>
	/// 화학식 파서. "H2O", "Ca5(PO4)3OH", "CuSO4·5H2O", "OH-", "SO4^2-" 같은 표기를 원소별 원자 수로 바꾼다.
	/// 전하 표기는 원자 수에 영향이 없으므로 떼어 낸다.
	/// </summary>
	public static class Formula
	{
		/// <summary>
		/// 아보가드로 수(1/mol)
		/// </summary>
		public const double Avogadro = 6.02214076e23;

		public static IReadOnlyDictionary<string, double> Parse(string formula)
		{
			if (!TryParse(formula, out var composition, out var error))
			{
				throw new FormatException(error);
			}

			return composition;
		}

		public static bool TryParse(string? formula, out IReadOnlyDictionary<string, double> composition, out string? error)
		{
			composition = new Dictionary<string, double>();
			error = null;

			if (string.IsNullOrWhiteSpace(formula))
			{
				error = "화학식이 비어 있다";
				return false;
			}

			var result = new Dictionary<string, double>(StringComparer.Ordinal);
			foreach (var part in StripCharge(formula.Trim()).Split('·', '*'))
			{
				var reader = new Reader(part.Trim());
				var multiplier = reader.ReadNumber() ?? 1.0;
				var group = reader.ReadGroup(null);

				if (reader.Error is not null)
				{
					error = $"'{formula}': {reader.Error}";
					return false;
				}

				if (group.Count == 0)
				{
					error = $"'{formula}': 원소가 없다";
					return false;
				}

				foreach (var (symbol, count) in group)
				{
					result[symbol] = result.GetValueOrDefault(symbol) + count * multiplier;
				}
			}

			composition = result;
			return true;
		}

		/// <summary>
		/// 몰 질량(g/mol). 원소표에 없는 원소가 있으면 null.
		/// </summary>
		public static double? MolarMass(IReadOnlyDictionary<string, double> composition, IReadOnlyDictionary<string, ElementDef> elements)
		{
			var mass = 0.0;
			foreach (var (symbol, count) in composition)
			{
				if (!elements.TryGetValue(symbol, out var element))
				{
					return null;
				}

				mass += element.AtomicMass * count;
			}

			return mass;
		}

		private static string StripCharge(string text)
		{
			var caret = text.LastIndexOf('^');
			if (caret > 0)
			{
				return text[..caret];
			}

			var end = text.Length;
			while (end > 0 && text[end - 1] is '+' or '-')
			{
				end--;
			}

			return text[..end];
		}

		private sealed class Reader(string text)
		{
			private int position;

			public string? Error { get; private set; }

			public Dictionary<string, double> ReadGroup(char? closing)
			{
				var result = new Dictionary<string, double>(StringComparer.Ordinal);

				while (position < text.Length && Error is null)
				{
					var c = text[position];
					if (c == closing)
					{
						return result;
					}

					if (c is '(' or '[')
					{
						position++;
						var inner = ReadGroup(c == '(' ? ')' : ']');
						if (Error is not null)
						{
							return result;
						}

						position++;
						var count = ReadNumber() ?? 1.0;
						foreach (var (symbol, n) in inner)
						{
							result[symbol] = result.GetValueOrDefault(symbol) + n * count;
						}
					}
					else if (char.IsAsciiLetterUpper(c))
					{
						var start = position++;
						while (position < text.Length && char.IsAsciiLetterLower(text[position]))
						{
							position++;
						}

						var symbol = text[start..position];
						var count = ReadNumber() ?? 1.0;
						result[symbol] = result.GetValueOrDefault(symbol) + count;
					}
					else
					{
						Error = $"알 수 없는 문자 '{c}' (위치 {position})";
					}
				}

				if (closing is not null && Error is null)
				{
					Error = "괄호가 닫히지 않았다";
				}

				return result;
			}

			public double? ReadNumber()
			{
				var start = position;
				while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.'))
				{
					position++;
				}

				if (start == position)
				{
					return null;
				}

				if (!double.TryParse(text.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				{
					Error = $"숫자를 읽을 수 없다 '{text[start..position]}'";
					return null;
				}

				return value;
			}
		}
	}
}
