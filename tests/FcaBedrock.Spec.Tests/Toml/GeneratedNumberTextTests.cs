using System.Globalization;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

// GeneratedNumberText (spec §7, D-122 part 10): the generated entries of a frozen numeric domain.
// The per-culture witnesses live in SpecFreezerTests; these pin the mechanism itself with cultures
// built here, so each result is the same under every culture-data provider.
public sealed class GeneratedNumberTextTests
{
    [Fact]
    public void Format_WhenOnlySomeNumbersReadBackWithAsciiSigns_ThenOnlyThoseNumbersUseTheCultureSymbols()
    {
        // A culture whose minus is '~': the parser accepts '-' only for a dash-like minus, so every
        // negative mantissa or exponent needs the culture's own sign, while the rest keep ASCII.
        var culture = WritableClone(CultureInfo.InvariantCulture);
        culture.NumberFormat.NegativeSign = "~";

        var texts = GeneratedNumberText.Format(["1.5", "-2.25", "1E+20", "1E-05", "0"], culture);

        Assert.Equal(["1.5", "~2.25", "1E+20", "1E~05", "0"], texts);
    }

    [Theory]
    [InlineData("invariant", "90.0", "90")]
    [InlineData("invariant", "-0", "0")]
    [InlineData("invariant", "9e1", "90")]
    [InlineData("de-DE", "2.50", "2,5")]
    [InlineData("de-DE", "-0.0", "0")]
    public void Format_WhenTheRetainedTextIsNotShortest_ThenTheCanonicalNumberIsWritten(string locale, string retained, string expected)
    {
        var culture = locale == "invariant" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(locale, predefinedOnly: true);

        Assert.Equal([expected], GeneratedNumberText.Format([retained], culture));
    }

    [Fact]
    public void Format_WhenTheBindingCultureIsWritable_ThenItsNumberFormatIsUnchanged()
    {
        var culture = WritableClone(CultureInfo.GetCultureInfo("sv-SE", predefinedOnly: true));
        var negative = culture.NumberFormat.NegativeSign;
        var positive = culture.NumberFormat.PositiveSign;

        _ = GeneratedNumberText.Format(["-1.5", "1E+20"], culture);

        Assert.Equal(negative, culture.NumberFormat.NegativeSign);
        Assert.Equal(positive, culture.NumberFormat.PositiveSign);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("1e400")]
    public void Format_WhenARetainedEntryIsNotAFiniteNumber_ThenItThrows(string retained)
    {
        Assert.Throws<InvalidOperationException>(() => GeneratedNumberText.Format([retained], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Format_WhenNeitherSpellingReadsBack_ThenItThrowsInsteadOfWritingAnotherNumber()
    {
        // A decimal mark of "0" makes 1.25 print as "1025", which reads back as 1025 under either
        // spelling. No predefined culture does this; the guard turns such a defect into an exception
        // rather than a spec that silently names another number.
        var culture = WritableClone(CultureInfo.InvariantCulture);
        culture.NumberFormat.NumberDecimalSeparator = "0";

        var exception = Assert.Throws<InvalidOperationException>(() => GeneratedNumberText.Format(["1.25"], culture));
        Assert.Contains("1025", exception.Message, StringComparison.Ordinal);
    }

    private static CultureInfo WritableClone(CultureInfo culture) => (CultureInfo)culture.Clone();
}
