using System.Globalization;
using FcaBedrock.Core.Fingerprinting;

namespace FcaBedrock.Core.Tests.Fingerprinting;

public sealed class CanonicalNumberTests
{
    // Format reproduces the existing canonical encoder byte-for-byte (invariant, shortest
    // round-trippable): the same pins CanonicalJson.AppendNumber carries (G-6).
    [Theory]
    [InlineData(90.0, "90")]
    [InlineData(0.1, "0.1")]
    [InlineData(34.25, "34.25")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(1e-5, "1E-05")]
    [InlineData(1e300, "1E+300")]
    [InlineData(5e-324, "5E-324")] // double.Epsilon, the smallest positive subnormal
    public void Format_WhenFinite_ThenInvariantShortestRoundTrippable(double value, string expected) =>
        Assert.Equal(expected, CanonicalNumber.Format(value));

    [Fact]
    public void Format_When90SpellingsParsed_ThenAllCollapseTo90()
    {
        // 90, 90.0, 9e1 are one double, so Format renders one identity "90" (§11.3/§14).
        Assert.True(CanonicalNumber.TryParse("90", CultureInfo.InvariantCulture, out var a));
        Assert.True(CanonicalNumber.TryParse("90.0", CultureInfo.InvariantCulture, out var b));
        Assert.True(CanonicalNumber.TryParse("9e1", CultureInfo.InvariantCulture, out var c));
        Assert.Equal("90", CanonicalNumber.Format(a));
        Assert.Equal("90", CanonicalNumber.Format(b));
        Assert.Equal("90", CanonicalNumber.Format(c));
    }

    [Fact]
    public void Format_WhenNegativeZero_ThenStillMinusZero() =>
        // Format itself NEVER canonicalizes — it must reproduce the fp_format = 1 encoder, which
        // renders -0.0 as "-0" (G-6). Callers apply CanonicalizeZero first for new M4 identities.
        Assert.Equal("-0", CanonicalNumber.Format(-0.0));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Format_WhenNonFinite_ThenThrows(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalNumber.Format(value));

    [Fact]
    public void CanonicalizeZero_WhenNegativeZero_ThenPositiveZero()
    {
        var canonical = CanonicalNumber.CanonicalizeZero(-0.0);

        // Positive zero has an all-zero bit pattern; -0.0 has the sign bit set.
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(canonical));
        Assert.Equal("0", CanonicalNumber.Format(canonical));
    }

    [Fact]
    public void CanonicalizeZero_WhenPositiveZero_ThenUnchangedPositiveZero() =>
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(CanonicalNumber.CanonicalizeZero(0.0)));

    [Theory]
    [InlineData(90.0)]
    [InlineData(-2.5)]
    [InlineData(1e300)]
    [InlineData(5e-324)]
    public void CanonicalizeZero_WhenNonZero_ThenUnchanged(double value) =>
        Assert.Equal(value, CanonicalNumber.CanonicalizeZero(value));

    [Fact]
    public void CanonicalizeZeroThenFormat_WhenEveryZeroSpelling_ThenAllRenderZero()
    {
        // The text-sourced chain (TryParse → CanonicalizeZero → Format) folds every zero spelling
        // (0, 0.0, -0, +0, 0e0) to the single key "0" (D-096).
        foreach (var spelling in new[] { "0", "0.0", "-0", "+0", "0e0", "-0.0" })
        {
            Assert.True(CanonicalNumber.TryParse(spelling, CultureInfo.InvariantCulture, out var value), spelling);
            Assert.Equal("0", CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(value)));
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void TryParse_WhenUnparseableOrNonFinite_ThenFalseAndZero(string text)
    {
        Assert.False(CanonicalNumber.TryParse(text, CultureInfo.InvariantCulture, out var value));
        Assert.Equal(0.0, value);
    }

    [Fact]
    public void TryParse_WhenLocaleUsesCommaDecimal_ThenUsesSuppliedCulture()
    {
        var deDe = CultureInfo.GetCultureInfo("de-DE");

        Assert.True(CanonicalNumber.TryParse("30,5", deDe, out var value)); // comma decimal in de-DE
        Assert.Equal(30.5, value);
        Assert.False(CanonicalNumber.TryParse("30.5", deDe, out _)); // a dot is a thousands group, not 30.5
    }

    [Fact]
    public void TryParse_WhenCultureNull_ThenThrows() =>
        Assert.Throws<ArgumentNullException>(() => CanonicalNumber.TryParse("1", null!, out _));

    // Spec §5.1/§5.1.1: numeric interpretation ignores whitespace W (the 25 char.IsWhiteSpace
    // units) at the value's ends. NumberStyles.Float alone accepts only U+0009 to U+000D and
    // U+0020 there, so the other members of W are the ones this pins.
    [Theory]
    [InlineData(0x00A0)]
    [InlineData(0x0085)]
    [InlineData(0x1680)]
    [InlineData(0x2000)]
    [InlineData(0x2028)]
    [InlineData(0x202F)]
    [InlineData(0x205F)]
    [InlineData(0x3000)]
    [InlineData(0x0009)]
    public void TryParse_WhenSurroundedByUnicodeWhitespace_ThenParses(int code)
    {
        var pad = (char)code;

        Assert.True(CanonicalNumber.TryParse($"{pad}30{pad}{pad}", CultureInfo.InvariantCulture, out var value));
        Assert.Equal(30.0, value);
    }

    [Theory]
    [InlineData(0x00A0)]
    [InlineData(0x3000)]
    [InlineData(0x0020)]
    [InlineData(0x2029)]
    public void TryParse_WhenAllWhitespace_ThenFalseAndZero(int code)
    {
        Assert.False(CanonicalNumber.TryParse(new string((char)code, 3), CultureInfo.InvariantCulture, out var value));
        Assert.Equal(0.0, value);
    }

    [Theory]
    [InlineData(0x200B)] // zero-width space
    [InlineData(0xFEFF)] // zero-width no-break space
    [InlineData(0x180E)] // Mongolian vowel separator
    public void TryParse_WhenPrefixedByAFormatCharacterThatIsNotWhitespace_ThenFalseAndZero(int code)
    {
        Assert.False(CanonicalNumber.TryParse($"{(char)code}30", CultureInfo.InvariantCulture, out var value));
        Assert.Equal(0.0, value);
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void TryParse_WhenPaddedButNotFinite_ThenFalseAndZero(string number)
    {
        var pad = (char)0x00A0;

        Assert.False(CanonicalNumber.TryParse($"{pad}{number}{pad}", CultureInfo.InvariantCulture, out var value));
        Assert.Equal(0.0, value);
    }

    [Fact]
    public void TryParse_WhenNegativeZeroIsPadded_ThenTheSignIsKept()
    {
        // TryParse never canonicalizes zero; the caller does (signed zero has its own owner).
        Assert.True(CanonicalNumber.TryParse($"{(char)0x00A0}-0{(char)0x3000}", CultureInfo.InvariantCulture, out var value));
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(value));
    }

    [Fact]
    public void TryParse_WhenPaddedUnderAnExplicitCulture_ThenTheCultureGoverns()
    {
        var text = $"{(char)0x00A0}1,5{(char)0x00A0}";

        Assert.True(CanonicalNumber.TryParse(text, CultureInfo.GetCultureInfo("fr-FR"), out var value));
        Assert.Equal(1.5, value);
        Assert.False(CanonicalNumber.TryParse(text, CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public void TryParse_WhenTextIsNull_ThenFalseAndZero()
    {
        Assert.False(CanonicalNumber.TryParse(null!, CultureInfo.InvariantCulture, out var value));
        Assert.Equal(0.0, value);
    }
}
