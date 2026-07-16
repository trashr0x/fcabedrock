using System.Globalization;
using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Tests.Discretization;

/// <summary>
/// The shared numeric-cut engine (D-093): <c>manual_cuts</c> and <c>equal_width</c>
/// execute through one <see cref="NumericCutBins"/>, so identical effective cuts give
/// identical classification, structure, labels, and rendering. These tests assert that
/// equivalence <b>directly</b> — it is what makes the D-088 auto/frozen byte-equivalence
/// structural rather than a property two code paths must independently maintain.
/// <para>
/// The manual-cut side's own unchanged behaviour is covered by
/// <see cref="ManualCutsDiscretizerTests"/> and the nine golden fixtures, which the
/// extraction left byte-identical.
/// </para>
/// </summary>
public sealed class NumericCutBinsTests
{
    // The same effective cuts reached two ways: authored (frozen) vs computed (auto).
    // equal_width over [0, 100] with 4 bins derives exactly 25/50/75.
    private static ManualCutsDiscretizer Frozen() =>
        ManualCutsDiscretizer.Create([25, 50, 75], BinEnds.Open, CultureInfo.InvariantCulture).Value!;

    private static EqualWidthDiscretizer Auto() =>
        EqualWidthDiscretizer.CreateManual(4, 0, 100, CutPrecision.Exact, CultureInfo.InvariantCulture).Value!;

    [Theory]
    [InlineData("-1000")]
    [InlineData("0")]
    [InlineData("24.999")]
    [InlineData("25")]     // the half-open lower edge
    [InlineData("49.5")]
    [InlineData("50")]
    [InlineData("74.999")]
    [InlineData("75")]
    [InlineData("1e9")]
    [InlineData("abc")]    // unparseable classifies identically too
    [InlineData("NaN")]
    public void Discretize_WhenSameEffectiveCuts_ThenManualAndEqualWidthAgree(string raw) =>
        Assert.Equal(Frozen().Discretize(raw), Auto().Discretize(raw));

    [Fact]
    public void BinLabels_WhenSameEffectiveCuts_ThenIdentical() =>
        Assert.Equal(Frozen().BinLabels([]), Auto().BinLabels([]));

    [Fact]
    public void DescribeBins_WhenSameEffectiveCuts_ThenIdenticalStructureAndGeometry()
    {
        var frozen = Frozen().DescribeBins([]);
        var auto = Auto().DescribeBins([]);

        Assert.Equal(frozen.Labels, auto.Labels);
        Assert.Equal(frozen.Thresholds, auto.Thresholds);
        Assert.Equal(frozen.Bins, auto.Bins);          // the canonical bin structure the fingerprint encodes
        Assert.Equal(frozen.OpenLow, auto.OpenLow);
        Assert.Equal(frozen.OpenHigh, auto.OpenHigh);
        Assert.Equal(frozen.CutBins, auto.CutBins);
    }

    [Theory]
    [InlineData(LabelStyle.Native)]
    [InlineData(LabelStyle.V2Compat)]
    public void RenderBinLabel_WhenSameEffectiveCuts_ThenIdenticalInEveryStyle(LabelStyle style)
    {
        var frozen = Frozen();
        var auto = Auto();

        foreach (var label in frozen.BinLabels([]))
        {
            Assert.Equal(frozen.RenderBinLabel(label, style), auto.RenderBinLabel(label, style));
        }
    }

    // --- The engine's own contract -------------------------------------------

    [Fact]
    public void Cuts_WhenEngineBuiltFromAMutableList_ThenSnapshotIsolatedAndNotCastable()
    {
        var input = new List<double> { 30, 40 };
        var bins = new NumericCutBins(input, BinEnds.Open, CultureInfo.InvariantCulture);

        input[0] = 999; // the caller keeps its list

        Assert.Equal([30.0, 40.0], bins.Cuts);
        Assert.IsNotType<double[]>(bins.Cuts);
        Assert.IsNotType<List<double>>(bins.Cuts);
        Assert.IsNotType<List<string>>(bins.BinLabels);
        Assert.IsNotType<string[]>(bins.BinLabels);
    }

    [Fact]
    public void Discretize_WhenClosedEndsAndValueOutside_ThenNoBin()
    {
        // The engine still serves manual_cuts' closed-ends geometry (equal_width never uses it).
        var bins = new NumericCutBins([30, 40], BinEnds.Closed, CultureInfo.InvariantCulture);

        Assert.Equal(BinResult.NoBin, bins.Discretize("10"));
        Assert.Equal(BinResult.Bin("[30, 40)"), bins.Discretize("35"));
        Assert.Equal(BinResult.NoBin, bins.Discretize("40"));
    }

    [Fact]
    public void CutLabels_WhenIntegralCuts_ThenCanonicalNumbersWithoutDecimalPoint() =>
        // §14 canonical numbers: the labels are invariant schema strings, so a cut authored 30.0
        // labels "30" — the byte-level behaviour the extraction had to preserve exactly.
        Assert.Equal(["30", "40"], new NumericCutBins([30.0, 40.0], BinEnds.Open, CultureInfo.InvariantCulture).CutLabels);
}
