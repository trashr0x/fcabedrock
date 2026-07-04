using System.Globalization;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Unit tests for the writer's pure TOML text primitives (D-075): escaping,
/// bare-key detection, and invariant value formatting.
/// </summary>
public sealed class TomlLiteralTests
{
    [Fact]
    public void FormatString_WhenPlain_ThenQuotedVerbatim() =>
        Assert.Equal("\"edible\"", TomlLiteral.FormatString("edible"));

    [Fact]
    public void FormatString_WhenQuoteBackslashAndNewline_ThenEscaped() =>
        Assert.Equal("\"a\\\"b\\\\c\\nd\"", TomlLiteral.FormatString("a\"b\\c\nd"));

    [Theory]
    [InlineData("\u0001", "\"\\u0001\"")]
    [InlineData("\u007F", "\"\\u007F\"")]
    [InlineData("\t", "\"\\t\"")]
    public void FormatString_WhenControlCharacter_ThenEscaped(string value, string expected) =>
        Assert.Equal(expected, TomlLiteral.FormatString(value));

    [Fact]
    public void FormatString_WhenPrintableNonAscii_ThenRawPassthrough() =>
        Assert.Equal("\"café 日本\"", TomlLiteral.FormatString("café 日本"));

    [Theory]
    [InlineData("odor")]
    [InlineData("HS-grad")]
    [InlineData("11th")]
    [InlineData("a_b")]
    public void FormatKey_WhenBare_ThenUnquoted(string key) =>
        Assert.Equal(key, TomlLiteral.FormatKey(key));

    [Theory]
    [InlineData("bruises?", "\"bruises?\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a.b", "\"a.b\"")]
    [InlineData("", "\"\"")]
    [InlineData("café", "\"café\"")]
    public void FormatKey_WhenNotBare_ThenQuoted(string key, string expected) =>
        Assert.Equal(expected, TomlLiteral.FormatKey(key));

    [Theory]
    [InlineData(30d, "30")]
    [InlineData(-5d, "-5")]
    [InlineData(0d, "0")]
    public void FormatDouble_WhenIntegral_ThenBareInteger(double value, string expected) =>
        Assert.Equal(expected, TomlLiteral.FormatDouble(value));

    [Theory]
    [InlineData(30.5d, "30.5")]
    [InlineData(0.1d, "0.1")]
    public void FormatDouble_WhenFractional_ThenInvariantShortest(double value, string expected) =>
        Assert.Equal(expected, TomlLiteral.FormatDouble(value));

    [Fact]
    public void FormatDouble_WhenCurrentCultureUsesCommas_ThenStillInvariant()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("30.5", TomlLiteral.FormatDouble(30.5d));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(double.NaN, "nan")]
    [InlineData(double.PositiveInfinity, "inf")]
    [InlineData(double.NegativeInfinity, "-inf")]
    public void FormatDouble_WhenNonFinite_ThenTomlSpelling(double value, string expected) =>
        Assert.Equal(expected, TomlLiteral.FormatDouble(value));

    [Fact]
    public void FormatDouble_WhenIntegralBeyondExactRange_ThenFloatForm()
    {
        var text = TomlLiteral.FormatDouble(1e17);
        Assert.True(text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal));
        Assert.Equal(1e17, double.Parse(text, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatLong_WhenValue_ThenInvariantDigits() =>
        Assert.Equal("1073741824", TomlLiteral.FormatLong(1_073_741_824));

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void FormatBool_WhenValue_ThenTomlSpelling(bool value, string expected) =>
        Assert.Equal(expected, TomlLiteral.FormatBool(value));

    [Theory]
    [InlineData(',', "\",\"")]
    [InlineData('\t', "\"\\t\"")]
    [InlineData('"', "\"\\\"\"")]
    public void FormatChar_WhenValue_ThenSingleCharBasicString(char value, string expected) =>
        Assert.Equal(expected, TomlLiteral.FormatChar(value));

    [Fact]
    public void FormatDateTime_WhenUtcWholeSecond_ThenZSuffix() =>
        Assert.Equal(
            "2026-05-09T10:00:00Z",
            TomlLiteral.FormatDateTime(new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void FormatDateTime_WhenFractionalSeconds_ThenTrailingZerosTrimmed() =>
        Assert.Equal(
            "2026-05-09T10:00:00.5Z",
            TomlLiteral.FormatDateTime(new DateTimeOffset(2026, 5, 9, 10, 0, 0, 500, TimeSpan.Zero)));

    [Theory]
    [InlineData(2, 0, "2026-05-09T10:00:00+02:00")]
    [InlineData(-5, -30, "2026-05-09T10:00:00-05:30")]
    public void FormatDateTime_WhenNonZeroOffset_ThenSignedOffset(int hours, int minutes, string expected) =>
        Assert.Equal(
            expected,
            TomlLiteral.FormatDateTime(new DateTimeOffset(2026, 5, 9, 10, 0, 0, new TimeSpan(hours, minutes, 0))));
}
