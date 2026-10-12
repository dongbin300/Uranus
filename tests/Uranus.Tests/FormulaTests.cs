using Uranus.Chemistry;

namespace Uranus.Tests
{
	public class FormulaTests
	{
		[Theory]
		[InlineData("H2O", "H:2 O:1")]
		[InlineData("CH3COOH", "C:2 H:4 O:2")]
		[InlineData("Ca(OH)2", "Ca:1 H:2 O:2")]
		[InlineData("Ca5(PO4)3OH", "Ca:5 H:1 O:13 P:3")]
		[InlineData("Ca3(PO4)2", "Ca:3 O:8 P:2")]
		[InlineData("CuSO4·5H2O", "Cu:1 H:10 O:9 S:1")]
		[InlineData("OH-", "H:1 O:1")]
		[InlineData("SO4^2-", "O:4 S:1")]
		[InlineData("InGaAs", "As:1 Ga:1 In:1")]
		public void Parse_CountsAtoms(string formula, string expected)
		{
			var composition = Formula.Parse(formula);

			var actual = string.Join(" ", composition.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}:{x.Value}"));
			Assert.Equal(expected, actual);
		}

		[Theory]
		[InlineData("")]
		[InlineData("h2o")]
		[InlineData("Ca(OH")]
		[InlineData("H2O)")]
		[InlineData("H2 O")]
		public void TryParse_RejectsInvalidFormula(string formula)
		{
			Assert.False(Formula.TryParse(formula, out _, out var error));
			Assert.NotNull(error);
		}

		[Fact]
		public void MolarMass_UsesAtomicMasses()
		{
			var elements = TestData.Elements.ToDictionary(x => x.Symbol);

			var mass = Formula.MolarMass(Formula.Parse("H2O"), elements);

			Assert.NotNull(mass);
			Assert.Equal(18.015, mass.Value, 3);
		}

		[Fact]
		public void MolarMass_IsNullForUnknownElement()
		{
			var elements = TestData.Elements.ToDictionary(x => x.Symbol);

			Assert.Null(Formula.MolarMass(Formula.Parse("NaCl"), elements));
		}
	}
}
