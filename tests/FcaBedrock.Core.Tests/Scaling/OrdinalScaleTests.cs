using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Tests.Scaling;

public sealed class OrdinalScaleTests
{
    // age cuts 30/40/50, open both ends: the v2 progressive shape.
    private static BinScheme AgeScheme() =>
        new(
            ["<30", "[30, 40)", "[40, 50)", ">=50"],
            [new NumericCutBin(null, 30), new NumericCutBin(30, 40), new NumericCutBin(40, 50), new NumericCutBin(50, null)],
            ["30", "40", "50"],
            OpenLow: true,
            OpenHigh: true,
            CutBins: true);

    // A string value scheme (identity bins), thresholded by an explicit order (D-081).
    private static BinScheme ValueScheme(params string[] order) =>
        new(order, [.. order.Select(CanonicalBin (v) => new ValueBin(v))], order,
            OpenLow: false, OpenHigh: false, CutBins: false);

    [Fact]
    public void Kind_WhenOrdinal_ThenIsTheOrdinalString() =>
        Assert.Equal("ordinal", new OrdinalScale().Kind);

    [Fact]
    public void Ctor_WhenDefaults_ThenBoundaryInclusiveAndNoOrder()
    {
        // Carrier defaults: Boundary inclusive, Order absent. Over cut bins these
        // stay unread (geometry decides); the value-bin path reads them (§12.3, D-081).
        var scale = new OrdinalScale();

        Assert.Equal(OrdinalBoundary.Inclusive, scale.Boundary);
        Assert.Null(scale.Order);
    }

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
    public void BuildShapes_WhenGeWithDropTop_ThenOmitsTheOpenEndAllColumn()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Ge, DropTop: true).BuildShapes(AgeScheme());

        Assert.Equal(["30", "40", "50"], shapes.Select(s => s.ValueLabel));
        Assert.Equal(["[30, 40)", "[40, 50)", ">=50"], shapes[0].CrossingBins); // >=30 keeps its crossings
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
        var scheme = new BinScheme(
            ["<Managerial", ">=Managerial"],
            [new TextCutBin(null, "Managerial"), new TextCutBin("Managerial", null)],
            ["Managerial"],
            OpenLow: true,
            OpenHigh: true,
            CutBins: true);

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
        // §12.3 (D-047): over half-open cut bins the cut geometry decides the operator: le
        // pairs with the strict "<", ge with the inclusive ">=". This is the default-boundary
        // behavior the spec says is "simply honored". An explicit *straddling* boundary
        // (le+inclusive / ge+strict) over cut bins is rejected at the resolve seam with
        // OrdinalBoundaryIncompatibleWithCuts (SpecResolverTests); over value bins all four
        // combinations are live (D-081, the BuildShapes_WhenValueBins* tests below).
        var scheme = AgeScheme();

        Assert.All(
            new OrdinalScale(OrdinalDirection.Le).BuildShapes(scheme).Where(s => s.ScaleOp.Length > 0),
            s => Assert.Equal("<", s.ScaleOp));
        Assert.All(
            new OrdinalScale(OrdinalDirection.Ge).BuildShapes(scheme).Where(s => s.ScaleOp.Length > 0),
            s => Assert.Equal(">=", s.ScaleOp));
    }

    [Fact]
    public void BuildShapes_WhenBoundarySetOverCutBins_ThenStillGeometryAlignedNotStraddling()
    {
        // Regression (D-081): over cut bins the CutBins path ignores Boundary, so even an
        // (invalid, seam-rejected) straddling boundary cannot leak a wrong operator.
        var geStrict = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Strict).BuildShapes(AgeScheme());
        var leInclusive = new OrdinalScale(OrdinalDirection.Le, DropTop: false, OrdinalBoundary.Inclusive).BuildShapes(AgeScheme());

        Assert.All(geStrict.Where(s => s.ScaleOp.Length > 0), s => Assert.Equal(">=", s.ScaleOp));
        Assert.All(leInclusive.Where(s => s.ScaleOp.Length > 0), s => Assert.Equal("<", s.ScaleOp));
    }

    // --- Closed ends: interior bins only, every threshold a cut (§11.2/§12.3) ---

    // age cuts 30/40/50 with closed ends: two bins, and the outer cuts are their outer edges.
    private static BinScheme ClosedAgeScheme() =>
        new(
            ["[30, 40)", "[40, 50)"],
            [new NumericCutBin(30, 40), new NumericCutBin(40, 50)],
            ["30", "40", "50"],
            OpenLow: false,
            OpenHigh: false,
            CutBins: true);

    // ordered cuts b/c/d over a..e with closed ends: the categorical twin.
    private static BinScheme ClosedOrderedScheme() =>
        new(
            ["[b, c)", "[c, d)"],
            [new TextCutBin("b", "c"), new TextCutBin("c", "d")],
            ["b", "c", "d"],
            OpenLow: false,
            OpenHigh: false,
            CutBins: true);

    [Fact]
    public void BuildShapes_WhenClosedLe_ThenEachBinsUpperEdgeIsItsThreshold()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Le).BuildShapes(ClosedAgeScheme());

        // [30, 40) ends at 40 and [40, 50) at 50. The closed top is the finite cut 50, so
        // there is no `all`, and the bottom cut 30 is no bin's upper edge.
        Assert.Equal(["40", "50"], shapes.Select(s => s.ValueLabel));
        Assert.Equal(["<", "<"], shapes.Select(s => s.ScaleOp));
        Assert.Equal(["[30, 40)"], shapes[0].CrossingBins);
        Assert.Equal(["[30, 40)", "[40, 50)"], shapes[1].CrossingBins);
        Assert.Equal(shapes.Select(s => s.ValueLabel), shapes.Select(s => s.BinKey));
        Assert.Equal(["40", "50"], shapes.Select(s => Assert.IsType<ValueBin>(s.Bin).Label));
    }

    [Fact]
    public void BuildShapes_WhenClosedGe_ThenEachBinsLowerEdgeIsItsThreshold()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Ge).BuildShapes(ClosedAgeScheme());

        // The closed bottom is the finite cut 30, so it is a threshold rather than `all`, and
        // the top cut 50 is no bin's lower edge, so no threshold sits there.
        Assert.Equal(["30", "40"], shapes.Select(s => s.ValueLabel));
        Assert.Equal([">=", ">="], shapes.Select(s => s.ScaleOp));
        Assert.Equal(["[30, 40)", "[40, 50)"], shapes[0].CrossingBins);
        Assert.Equal(["[40, 50)"], shapes[1].CrossingBins);
    }

    [Theory]
    [InlineData(OrdinalDirection.Le, "<", "[30, 40)")]  // <50 crosses both bins and is dropped
    [InlineData(OrdinalDirection.Ge, ">=", "[40, 50)")] // >=30 crosses both bins and is dropped
    public void BuildShapes_WhenClosedWithDropTop_ThenTheOutermostCutThresholdIsSuppressed(
        OrdinalDirection direction, string op, string onlyBin)
    {
        var shape = Assert.Single(new OrdinalScale(direction, DropTop: true).BuildShapes(ClosedAgeScheme()));

        Assert.Equal("40", shape.ValueLabel);
        Assert.Equal(op, shape.ScaleOp);
        Assert.Equal([onlyBin], shape.CrossingBins);
    }

    [Theory]
    [InlineData(OrdinalDirection.Le, "40", "<")]
    [InlineData(OrdinalDirection.Ge, "30", ">=")]
    public void BuildShapes_WhenASingleClosedBin_ThenOneThresholdThatDropTopSuppresses(
        OrdinalDirection direction, string cut, string op)
    {
        var single = new BinScheme(
            ["[30, 40)"], [new NumericCutBin(30, 40)], ["30", "40"], OpenLow: false, OpenHigh: false, CutBins: true);

        var shape = Assert.Single(new OrdinalScale(direction).BuildShapes(single));
        Assert.Equal(cut, shape.ValueLabel);
        Assert.Equal(op, shape.ScaleOp);
        Assert.Equal(["[30, 40)"], shape.CrossingBins);

        // The only threshold crosses the only bin, so it is tautological: drop_top leaves none.
        Assert.Empty(new OrdinalScale(direction, DropTop: true).BuildShapes(single));
    }

    [Fact]
    public void BuildShapes_WhenClosedOrderedCutsLe_ThenTheUpperCategoryEdgesAreTheThresholds()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Le).BuildShapes(ClosedOrderedScheme());

        Assert.Equal(["c", "d"], shapes.Select(s => s.ValueLabel));
        Assert.Equal(["[b, c)"], shapes[0].CrossingBins);
        Assert.Equal(["[b, c)", "[c, d)"], shapes[1].CrossingBins);
    }

    [Fact]
    public void BuildShapes_WhenClosedOrderedCutsGe_ThenTheLowerCategoryEdgesAreTheThresholds()
    {
        var shapes = new OrdinalScale(OrdinalDirection.Ge).BuildShapes(ClosedOrderedScheme());

        Assert.Equal(["b", "c"], shapes.Select(s => s.ValueLabel));
        Assert.Equal(["[b, c)", "[c, d)"], shapes[0].CrossingBins);
        Assert.Equal(["[c, d)"], shapes[1].CrossingBins);
    }

    // --- Value-bin ordinal (identity + explicit order, D-081) ---

    private static OrdinalScale ValueOrdinal(
        OrdinalDirection direction, OrdinalBoundary boundary, bool dropTop = false) =>
        new(direction, dropTop, boundary, ["low", "mid", "high"]);

    [Fact]
    public void BuildShapes_WhenValueBinsGeInclusive_ThenAtOrAboveEachThresholdCumulativeFromTop()
    {
        var shapes = ValueOrdinal(OrdinalDirection.Ge, OrdinalBoundary.Inclusive).BuildShapes(ValueScheme("low", "mid", "high"));

        Assert.Equal(3, shapes.Count); // N bins → N shapes (no `all`, no open end)
        Assert.Equal(["low", "mid", "high"], shapes.Select(s => s.ValueLabel));
        Assert.Equal([">=", ">=", ">="], shapes.Select(s => s.ScaleOp));
        Assert.Equal(["low", "mid", "high"], shapes[0].CrossingBins); // >= low is tautological
        Assert.Equal(["mid", "high"], shapes[1].CrossingBins);
        Assert.Equal(["high"], shapes[2].CrossingBins);
    }

    [Fact]
    public void BuildShapes_WhenValueBinsGeStrict_ThenAboveEachThresholdWithStaticallyEmptyTopEnd()
    {
        var shapes = ValueOrdinal(OrdinalDirection.Ge, OrdinalBoundary.Strict).BuildShapes(ValueScheme("low", "mid", "high"));

        Assert.Equal([">", ">", ">"], shapes.Select(s => s.ScaleOp));
        Assert.Equal(["mid", "high"], shapes[0].CrossingBins);
        Assert.Equal(["high"], shapes[1].CrossingBins);
        Assert.Empty(shapes[2].CrossingBins); // > high: statically empty, kept
    }

    [Fact]
    public void BuildShapes_WhenValueBinsLeInclusive_ThenAtOrBelowEachThresholdCumulativeFromBottom()
    {
        var shapes = ValueOrdinal(OrdinalDirection.Le, OrdinalBoundary.Inclusive).BuildShapes(ValueScheme("low", "mid", "high"));

        Assert.Equal(["<=", "<=", "<="], shapes.Select(s => s.ScaleOp));
        Assert.Equal(["low"], shapes[0].CrossingBins);
        Assert.Equal(["low", "mid"], shapes[1].CrossingBins);
        Assert.Equal(["low", "mid", "high"], shapes[2].CrossingBins); // <= high is tautological
    }

    [Fact]
    public void BuildShapes_WhenValueBinsLeStrict_ThenBelowEachThresholdWithStaticallyEmptyBottomEnd()
    {
        var shapes = ValueOrdinal(OrdinalDirection.Le, OrdinalBoundary.Strict).BuildShapes(ValueScheme("low", "mid", "high"));

        Assert.Equal(["<", "<", "<"], shapes.Select(s => s.ScaleOp));
        Assert.Empty(shapes[0].CrossingBins); // < low: statically empty, kept
        Assert.Equal(["low"], shapes[1].CrossingBins);
        Assert.Equal(["low", "mid"], shapes[2].CrossingBins);
    }

    [Fact]
    public void BuildShapes_WhenValueBins_ThenBinKeyEqualsValueLabelAndBinIsValueBin()
    {
        // The identity rule: BinKey == ValueLabel (style-independent identity); the
        // structural twin is a plain ValueBin of the same raw value.
        var shapes = ValueOrdinal(OrdinalDirection.Ge, OrdinalBoundary.Inclusive).BuildShapes(ValueScheme("low", "mid", "high"));

        Assert.Equal(shapes.Select(s => s.ValueLabel), shapes.Select(s => s.BinKey));
        Assert.Equal(["low", "mid", "high"], shapes.Select(s => Assert.IsType<ValueBin>(s.Bin).Label));
    }

    [Theory]
    [InlineData(OrdinalDirection.Ge, "low")]  // >= lowest is tautological
    [InlineData(OrdinalDirection.Le, "high")] // <= highest is tautological
    public void BuildShapes_WhenValueBinsInclusiveDropTop_ThenTautologicalThresholdOmitted(
        OrdinalDirection direction, string tautological)
    {
        var full = ValueOrdinal(direction, OrdinalBoundary.Inclusive).BuildShapes(ValueScheme("low", "mid", "high"));
        var dropped = ValueOrdinal(direction, OrdinalBoundary.Inclusive, dropTop: true).BuildShapes(ValueScheme("low", "mid", "high"));

        Assert.Equal(3, full.Count);
        Assert.Equal(2, dropped.Count);
        Assert.DoesNotContain(tautological, dropped.Select(s => s.ValueLabel));
    }

    [Theory]
    [InlineData(OrdinalDirection.Ge)]
    [InlineData(OrdinalDirection.Le)]
    public void BuildShapes_WhenValueBinsStrictDropTop_ThenNoOpAllThresholdsKept(OrdinalDirection direction)
    {
        // Over value bins drop_top is a no-op under strict: there is no tautological threshold,
        // and the statically-empty end is kept and simply never crosses (D-081).
        var withoutDrop = ValueOrdinal(direction, OrdinalBoundary.Strict).BuildShapes(ValueScheme("low", "mid", "high"));
        var withDrop = ValueOrdinal(direction, OrdinalBoundary.Strict, dropTop: true).BuildShapes(ValueScheme("low", "mid", "high"));

        Assert.Equal(withoutDrop.Select(s => s.ValueLabel), withDrop.Select(s => s.ValueLabel));
        Assert.Equal(3, withDrop.Count);
    }

    [Fact]
    public void BuildShapes_WhenOrderPermutesSchemeLabelOrder_ThenThresholdsFollowOrderNotLabels()
    {
        // Order (not the scheme's Labels order) drives enumeration and crossings.
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, ["high", "low", "mid"]);
        var shapes = scale.BuildShapes(ValueScheme("low", "mid", "high"));

        Assert.Equal(["high", "low", "mid"], shapes.Select(s => s.ValueLabel));
        Assert.Equal(["high", "low", "mid"], shapes[0].CrossingBins); // order[0] crosses all
        Assert.Equal(["low", "mid"], shapes[1].CrossingBins);
        Assert.Equal(["mid"], shapes[2].CrossingBins);
    }
}
