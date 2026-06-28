using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Tests.Scaling;

public sealed class OrdinalScaleTests
{
    // age cuts 30/40/50, open both ends — the v2 progressive shape.
    private static BinScheme AgeScheme() =>
        new(["<30", "[30, 40)", "[40, 50)", ">=50"], ["30", "40", "50"], OpenLow: true, OpenHigh: true);

    [Fact]
    public void Kind_WhenOrdinal_ThenIsTheOrdinalString() =>
        Assert.Equal("ordinal", new OrdinalScale().Kind);

    [Fact]
    public void BuildShapes_WhenLe_ThenBelowThresholdsAscendingThenAll()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Le).BuildShapes(AgeScheme());

        Assert.Equal(["30", "40", "50", "all"], shapes.Select(s => s.ValueLabel));
        Assert.Equal(["<", "<", "<", ""], shapes.Select(s => s.ScaleOp));
    }

    [Fact]
    public void BuildShapes_WhenLe_ThenCrossingsAreCumulativeFromBelow()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Le).BuildShapes(AgeScheme());

        Assert.Equal(["<30"], shapes[0].CrossingBins);
        Assert.Equal(["<30", "[30, 40)"], shapes[1].CrossingBins);
        Assert.Equal(["<30", "[30, 40)", "[40, 50)"], shapes[2].CrossingBins);
        Assert.Equal(["<30", "[30, 40)", "[40, 50)", ">=50"], shapes[3].CrossingBins); // all
    }

    [Fact]
    public void BuildShapes_WhenLeWithDropTop_ThenOmitsTheOpenEndAllColumn()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Le, DropTop: true).BuildShapes(AgeScheme());

        Assert.Equal(["30", "40", "50"], shapes.Select(s => s.ValueLabel));
        Assert.DoesNotContain("all", shapes.Select(s => s.ValueLabel));
    }

    [Fact]
    public void BuildShapes_WhenGe_ThenAllThenAtOrAboveThresholdsWithCumulativeCrossings()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Ge).BuildShapes(AgeScheme());

        Assert.Equal(["all", "30", "40", "50"], shapes.Select(s => s.ValueLabel));
        Assert.Equal(["", ">=", ">=", ">="], shapes.Select(s => s.ScaleOp));
        Assert.Equal(["<30", "[30, 40)", "[40, 50)", ">=50"], shapes[0].CrossingBins); // all
        Assert.Equal(["[30, 40)", "[40, 50)", ">=50"], shapes[1].CrossingBins);        // >=30
        Assert.Equal(["[40, 50)", ">=50"], shapes[2].CrossingBins);                    // >=40
        Assert.Equal([">=50"], shapes[3].CrossingBins);                                // >=50
    }

    [Fact]
    public void BuildShapes_WhenSingleCutLe_ThenBelowCutThenAll()
    {
        // employment cut at Managerial, open: <Managerial / >=Managerial bins.
        var scheme = new BinScheme(["<Managerial", ">=Managerial"], ["Managerial"], OpenLow: true, OpenHigh: true);

        var shapes = new OrdinalScale(OrdinalDirection.Le).BuildShapes(scheme);

        Assert.Equal(["Managerial", "all"], shapes.Select(s => s.ValueLabel));
        Assert.Equal(["<Managerial"], shapes[0].CrossingBins);
        Assert.Equal(["<Managerial", ">=Managerial"], shapes[1].CrossingBins);
    }

    [Fact]
    public void BuildShapes_WhenLe_ThenBinKeyEqualsValueLabelSoIdentityIsStyleIndependent()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Le).BuildShapes(AgeScheme());

        Assert.Equal(shapes.Select(s => s.ValueLabel), shapes.Select(s => s.BinKey));
    }

    [Fact]
    public void BuildShapes_WhenDefaultBoundaryOverCutBins_ThenOperatorIsGeometryAligned()
    {
        // §12.3 (D-047): over half-open cut bins the cut geometry decides the operator — le
        // pairs with the strict "<", ge with the inclusive ">=". This is the default-boundary
        // behavior the spec says is "simply honored". An explicit *straddling* boundary
        // (le+inclusive / ge+strict) is rejected with OrdinalBoundaryIncompatibleWithCuts only
        // once the value-bin path and the boundary field land at M2 — there is no M1 producer,
        // so there is nothing straddling to assert here yet.
        var scheme = AgeScheme();

        Assert.All(
            new OrdinalScale(OrdinalDirection.Le).BuildShapes(scheme).Where(s => s.ScaleOp.Length > 0),
            s => Assert.Equal("<", s.ScaleOp));
        Assert.All(
            new OrdinalScale(OrdinalDirection.Ge).BuildShapes(scheme).Where(s => s.ScaleOp.Length > 0),
            s => Assert.Equal(">=", s.ScaleOp));
    }
}
