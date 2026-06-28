using System.Globalization;
using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Tests.Discretization;

public sealed class ManualCutsDiscretizerTests
{
    private static ManualCutsDiscretizer Open(params double[] cuts) =>
        ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!;

    private static ManualCutsDiscretizer Closed(params double[] cuts) =>
        ManualCutsDiscretizer.Create(cuts, BinEnds.Closed, CultureInfo.InvariantCulture).Value!;

    [Fact]
    public void Kind_WhenManualCuts_ThenIsTheManualCutsString() =>
        Assert.Equal("manual_cuts", Open(30, 40, 50).Kind);

    [Theory]
    [InlineData("25", "<30")]
    [InlineData("30", "[30, 40)")] // a value equal to a cut goes into the upper bin (half-open)
    [InlineData("35", "[30, 40)")]
    [InlineData("40", "[40, 50)")]
    [InlineData("49", "[40, 50)")]
    [InlineData("50", ">=50")]
    [InlineData("80", ">=50")]
    public void Discretize_WhenOpenEnds_ThenLocatesHalfOpenBin(string raw, string expected) =>
        Assert.Equal(BinResult.Bin(expected), Open(30, 40, 50).Discretize(raw));

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void Discretize_WhenUnparseableOrNonFinite_ThenUnparseable(string raw) =>
        Assert.Equal(BinResult.Unparseable(raw), Open(30, 40, 50).Discretize(raw)); // §11.5 / D-050

    [Theory]
    [InlineData("25")]
    [InlineData("50")]
    [InlineData("80")]
    public void Discretize_WhenClosedEndsAndOutOfRange_ThenNoBin(string raw) =>
        Assert.Equal(BinResult.NoBin, Closed(30, 40, 50).Discretize(raw)); // §11.2: silent no-cross

    [Fact]
    public void Discretize_WhenClosedEndsAndInRange_ThenInteriorBin() =>
        Assert.Equal(BinResult.Bin("[30, 40)"), Closed(30, 40, 50).Discretize("35"));

    [Fact]
    public void BinLabels_WhenOpenEnds_ThenOrderedWithOpenOuterBins() =>
        Assert.Equal(["<30", "[30, 40)", "[40, 50)", ">=50"], Open(30, 40, 50).BinLabels([]));

    [Fact]
    public void Discretize_WhenAnyValue_ThenResultIsAlwaysAKnownBinLabel()
    {
        var discretizer = Open(30, 40, 50);
        var labels = discretizer.BinLabels([]);

        foreach (var raw in new[] { "10", "30", "39", "40", "50", "99" })
        {
            Assert.True(discretizer.Discretize(raw).TryGetLabel(out var label));
            Assert.Contains(label, labels);
        }
    }

    [Theory]
    [InlineData("[30, 40)", "30to<40")]
    [InlineData("[40, 50)", "40to<50")]
    [InlineData("<30", "<30")]   // open ends are style-independent
    [InlineData(">=50", ">=50")]
    public void RenderBinLabel_WhenV2Compat_ThenOnlyInteriorBecomesToLess(string canonical, string expected) =>
        Assert.Equal(expected, Open(30, 40, 50).RenderBinLabel(canonical, LabelStyle.V2Compat));

    [Fact]
    public void RenderBinLabel_WhenNative_ThenUnchanged() =>
        Assert.Equal("[30, 40)", Open(30, 40, 50).RenderBinLabel("[30, 40)", LabelStyle.Native));

    [Fact]
    public void BinLabels_WhenIntegralCuts_ThenLabelsCarryNoDecimalPoint() =>
        Assert.Equal(["<30", ">=30"], Open(30).BinLabels([])); // not "<30.0"

    [Fact]
    public void Discretize_WhenLocaleUsesCommaDecimal_ThenParsesWithInjectedCulture()
    {
        var deDe = ManualCutsDiscretizer.Create([30], BinEnds.Open, CultureInfo.GetCultureInfo("de-DE")).Value!;

        Assert.Equal(BinResult.Bin(">=30"), deDe.Discretize("30,5")); // "30,5" == 30.5 in de-DE
        Assert.Equal(BinResult.Bin("<30"), deDe.Discretize("29,5"));
    }
}
