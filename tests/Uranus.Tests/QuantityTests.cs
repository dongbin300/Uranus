using Uranus.Quantities;

namespace Uranus.Tests
{
	public class QuantityTests
	{
		[Theory]
		[InlineData("2", 2)]
		[InlineData("0.25", 0.25)]
		[InlineData("1.133e24", 1.133e24)]
		[InlineData("1.133^24", 1.133e24)]
		[InlineData(" 6.022E23 ", 6.022e23)]
		public void AmountText_ParsesNumbers(string text, double expected)
		{
			Assert.True(AmountText.TryParse(text, out var value));
			Assert.Equal(expected, value);
		}

		[Fact]
		public void AmountText_ParsesFraction()
		{
			Assert.True(AmountText.TryParse("1/3", out var value));
			Assert.Equal(1.0 / 3, value);
		}

		[Theory]
		[InlineData("")]
		[InlineData("abc")]
		[InlineData("1/0")]
		[InlineData("1^x")]
		public void AmountText_RejectsInvalidText(string text)
		{
			Assert.False(AmountText.TryParse(text, out _));
		}

		[Theory]
		[InlineData(0.1, 10)]
		[InlineData(0.2, 5)]
		[InlineData(0.04, 25)]
		[InlineData(0.02, 50)]
		public void Floor_AccumulatedFractionsBecomeWholeUnit(double step, int times)
		{
			var total = 0.0;
			for (var i = 0; i < times; i++)
			{
				total += step;
			}

			Assert.Equal(1, Quantity.Floor(total));
			Assert.True(Quantity.IsAtLeast(total, 1));
			Assert.Equal(0, Quantity.Subtract(total, 1));
		}

		[Fact]
		public void Fraction_IsProgressTowardNextUnit()
		{
			Assert.Equal(0.6, Quantity.Fraction(2.6), 9);
			Assert.Equal(0, Quantity.Fraction(0.9999999999999999));
		}

		[Fact]
		public void IsAtLeast_RejectsNonFiniteNeed()
		{
			Assert.False(Quantity.IsAtLeast(1e300, double.PositiveInfinity));
			Assert.False(Quantity.IsAtLeast(1, double.NaN));
		}

		[Fact]
		public void IsAtLeast_RejectsClearShortfall()
		{
			Assert.False(Quantity.IsAtLeast(0.99, 1));
			Assert.False(Quantity.IsAtLeast(1.133e24 * 0.999, 1.133e24));
		}

		[Theory]
		[InlineData("45g", 45)]
		[InlineData("500mg", 0.5)]
		[InlineData("2.5 kg", 2500)]
		[InlineData("1t", 1e6)]
		public void MassText_ParsesUnits(string text, double grams)
		{
			Assert.True(MassText.TryParse(text, out var value));
			Assert.Equal(grams, value, 9);
		}
	}

	public class QuantityFormatterTests
	{
		[Theory]
		[InlineData(0, "0")]
		[InlineData(0.6, "0")]
		[InlineData(0.9999999999999999, "1")]
		[InlineData(999.9, "999")]
		[InlineData(1000, "1k")]
		[InlineData(2000, "2k")]
		[InlineData(2500, "2.5k")]
		[InlineData(1999, "1.99k")]
		[InlineData(12_345, "12.3k")]
		[InlineData(1_000_000, "1m")]
		[InlineData(1_234_567, "1.23m")]
		[InlineData(999_000_000_000_000, "999t")]
		[InlineData(1e15, "1e15")]
		[InlineData(1.133e24, "1.13e24")]
		[InlineData(1e300, "1e300")]
		public void FormatCount_Short(double value, string expected)
		{
			Assert.Equal(expected, QuantityFormatter.FormatCount(value));
		}

		[Theory]
		[InlineData(1234, "1.23e3")]
		[InlineData(6.02214076e23, "6.02e23")]
		public void FormatCount_Scientific(double value, string expected)
		{
			Assert.Equal(expected, QuantityFormatter.FormatCount(value, NumberStyle.Scientific));
		}

		[Theory]
		[InlineData(9_999, "9999")]
		[InlineData(12_340_000, "1234만")]
		[InlineData(1e8, "1억")]
		[InlineData(1.133e24, "1.13자")]
		[InlineData(1e60, "1e60")]
		public void FormatCount_Korean(double value, string expected)
		{
			Assert.Equal(expected, QuantityFormatter.FormatCount(value, NumberStyle.Korean));
		}

		[Theory]
		[InlineData(0.25, "0.25")]
		[InlineData(0.0625, "0.063")]
		[InlineData(12.5, "12.5")]
		[InlineData(0.00001, "1e-5")]
		public void FormatNumber_KeepsDecimals(double value, string expected)
		{
			Assert.Equal(expected, QuantityFormatter.FormatNumber(value));
		}

		[Theory]
		[InlineData(52.84, "52.8 g")]
		[InlineData(978_000, "978 kg")]
		[InlineData(1_210_000, "1.21 t")]
		[InlineData(0.0286, "28.6 mg")]
		[InlineData(9.9e-13, "990 fg")]
		[InlineData(5.03e-15, "5.03 fg")]
		[InlineData(2.99e-23, "2.99e-23 g")]
		public void FormatMass_PicksUnit(double grams, string expected)
		{
			Assert.Equal(expected, QuantityFormatter.FormatMass(grams));
		}
	}
}
