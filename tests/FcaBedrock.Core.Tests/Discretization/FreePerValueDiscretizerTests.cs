using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Discretization;

public sealed class FreePerValueDiscretizerTests
{
    private static FreePerValueDiscretizer String() =>
        new(SourceValueType.String, CultureInfo.InvariantCulture);

    private static FreePerValueDiscretizer Number(CultureInfo? culture = null) =>
        new(SourceValueType.Number, culture ?? CultureInfo.InvariantCulture);

    [Fact]
    public void Kind_WhenFreePerValue_ThenIsTheFreePerValueString() =>
        Assert.Equal("free_per_value", String().Kind);

    // --- string mode: the raw spelling is the bin identity, verbatim (§11.3, D-061) ---

    [Theory]
    [InlineData("broad")]
    [InlineData("90.0")] // string mode does NOT parse — distinct spellings stay distinct bins
    [InlineData("9e1")]
    [InlineData("")]
    public void Discretize_WhenStringMode_ThenValueIsItsOwnBin(string raw) =>
        Assert.Equal(BinResult.Bin(raw), String().Discretize(raw));

    // --- numeric mode: the bin identity is the canonical numeric value (§11.3/D-096) ---

    [Theory]
    [InlineData("90", "90")]
    [InlineData("90.0", "90")]
    [InlineData("9e1", "90")] // 90, 90.0, 9e1 collapse to one bin
    [InlineData("34.25", "34.25")]
    [InlineData("-2.5", "-2.5")]
    public void Discretize_WhenNumberMode_ThenBinIsCanonicalNumericIdentity(string raw, string expected) =>
        Assert.Equal(BinResult.Bin(expected), Number().Discretize(raw));

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-0")]
    [InlineData("+0")]
    [InlineData("0e0")]
    [InlineData("-0.0")]
    public void Discretize_WhenNumberModeZeroSpelling_ThenBinIsCanonicalZero(string raw) =>
        // Every zero spelling — signed zero included — collapses to the identity "0" (D-096).
        Assert.Equal(BinResult.Bin("0"), Number().Discretize(raw));

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    public void Discretize_WhenNumberModeUnparseableOrNonFinite_ThenUnparseable(string raw) =>
        Assert.Equal(BinResult.Unparseable(raw), Number().Discretize(raw)); // §11.5 / D-050

    [Fact]
    public void Discretize_WhenNumberModeLocaleCommaDecimal_ThenParsesWithInjectedCultureAndCanonicalizesInvariant()
    {
        var deDe = Number(CultureInfo.GetCultureInfo("de-DE"));

        // "30,5" is 30.5 under de-DE; the canonical identity is the invariant-formatted "30.5".
        Assert.Equal(BinResult.Bin("30.5"), deDe.Discretize("30,5"));
        // A dot is not the de-DE decimal separator, and thousands are not allowed under Float → unparseable.
        Assert.Equal(BinResult.Unparseable("30.5"), deDe.Discretize("30.5"));
    }

    // --- domain / value-label consultation flags (§10.3 / §10.8) ---

    [Fact]
    public void BinLabels_WhenAnyMode_ThenAreTheDeclaredDomainVerbatim()
    {
        // Value bins: the ordered bin universe IS the declared domain (canonical keys for numeric).
        Assert.Equal(["90", "0", "5"], Number().BinLabels(["90", "0", "5"]));
        Assert.Equal(["b", "n"], String().BinLabels(["b", "n"]));
    }

    [Fact]
    public void ConsultsValueLabels_And_ConsumesDeclaredDomain_WhenFreePerValue_ThenBothTrue()
    {
        var discretizer = Number();

        Assert.True(discretizer.ConsultsValueLabels);   // its bin label IS the raw value (§10.8)
        Assert.True(discretizer.ConsumesDeclaredDomain); // its bin universe IS the domain (§10.3)
    }

    [Fact]
    public void Discretize_WhenValueOutsideAnyDomain_ThenStillABin()
    {
        // The discretizer never gates on the domain — the emitter's KnownBins gate turns an
        // out-of-domain (but parseable) bin into an unknown value (§10.6), covered at the emit level.
        Assert.True(Number().Discretize("999").TryGetLabel(out var numericLabel));
        Assert.Equal("999", numericLabel);
        Assert.True(String().Discretize("unlisted").TryGetLabel(out _));
    }

    // --- public constructor contract ---

    [Fact]
    public void Constructor_WhenCultureNull_ThenThrows() =>
        Assert.Throws<ArgumentNullException>(() => new FreePerValueDiscretizer(SourceValueType.Number, null!));

    [Fact]
    public void Constructor_WhenValueTypeAndCulture_ThenExposesThem()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var discretizer = new FreePerValueDiscretizer(SourceValueType.Number, culture);

        Assert.Equal(SourceValueType.Number, discretizer.ValueType);
        Assert.Same(culture, discretizer.Culture);
    }
}
