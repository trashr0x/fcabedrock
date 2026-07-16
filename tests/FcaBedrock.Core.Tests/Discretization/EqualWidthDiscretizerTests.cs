using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Discretization;

public sealed class EqualWidthDiscretizerTests
{
    private static EqualWidthDiscretizer Manual(
        int bins, double vmin, double vmax, CutPrecision? precision = null, CultureInfo? culture = null)
    {
        var result = EqualWidthDiscretizer.CreateManual(
            bins, vmin, vmax, precision ?? CutPrecision.Exact, culture ?? CultureInfo.InvariantCulture);
        Assert.True(result.TryGetValue(out var discretizer), $"CreateManual failed: {Codes(result)}");
        return discretizer!;
    }

    private static string Codes<T>(Diagnosed<T> result) => string.Join(", ", result.Diagnostics.Select(d => d.Code));

    private static DiagnosticCode SoleCode<T>(Diagnosed<T> result)
    {
        Assert.False(result.IsOk);
        return Assert.Single(result.Diagnostics).Code;
    }

    // --- §9.1 numeric vectors -------------------------------------------------
    //
    // Each expected cut list below is an independently-stated concrete result, never a
    // re-run of the production formula — a test that recomputed the interpolation could
    // not catch the formula changing (P-7/P-11).

    [Fact]
    public void DeriveCuts_WhenSimpleSpan_ThenEvenlySpacedInteriorCuts() =>
        Assert.Equal([25.0, 50.0, 75.0], EqualWidthDiscretizer.DeriveCuts(4, 0, 100, CutPrecision.Exact));

    [Fact]
    public void DeriveCuts_WhenUnitSpanInThirds_ThenBinary64Thirds() =>
        // The exact binary64 results: 1/3 and 2/3 are not representable, so these literals pin
        // what the pinned expression order actually produces.
        Assert.Equal(
            [0.3333333333333333, 0.6666666666666666],
            EqualWidthDiscretizer.DeriveCuts(3, 0, 1, CutPrecision.Exact));

    [Fact]
    public void DeriveCuts_WhenSymmetricExtremeSpan_ThenPositiveZeroNotOverflow()
    {
        // G-7: [-1.7e308, 1.7e308] is a FINITE increasing range and must be accepted. A naive
        // vmin + (vmax - vmin) * t would overflow the subtraction to +∞; the sign-aware convex
        // combination gives the true midpoint, 0 — canonicalized to POSITIVE zero (G-6).
        var cuts = EqualWidthDiscretizer.DeriveCuts(2, -1.7e308, 1.7e308, CutPrecision.Exact);

        var cut = Assert.Single(cuts);
        Assert.Equal(0.0, cut);
        Assert.False(double.IsNegative(cut));
    }

    [Fact]
    public void DeriveCuts_WhenAsymmetricExtremeSpan_ThenExactBinary64Midpoint()
    {
        // The opposite-sign branch again, off-centre. The value is pinned EXACTLY, not to a
        // tolerance: the whole point of the vector is to lock the pinned expression order, and a
        // reordered or contracted expression lands a few ULPs away — which would silently move cut
        // identities, labels, and fingerprints while a tolerant assert stayed green (P-7/P-11).
        // -4.999999999999998e306 is the arithmetic result, not the ideal -5e306.
        var cut = Assert.Single(EqualWidthDiscretizer.DeriveCuts(2, -1.7e308, 1.6e308, CutPrecision.Exact));

        Assert.True(double.IsFinite(cut));
        Assert.Equal(-4.999999999999998E+306, cut);
        Assert.Equal(0xFF9C7B1F3CAC7430, BitConverter.DoubleToUInt64Bits(cut));
        Assert.NotEqual(-5e306, cut); // the ideal midpoint is NOT representable here
    }

    [Fact]
    public void CreateManual_WhenAsymmetricExtremeSpan_ThenTheSameExactCutReachesTheLabel() =>
        // The same vector through the approved public boundary, so the pinned value is locked on
        // the path that actually produces bin identities.
        Assert.Equal(
            ["<-4.999999999999998E+306", ">=-4.999999999999998E+306"],
            Manual(2, -1.7e308, 1.6e308).BinLabels([]));

    [Fact]
    public void DeriveCuts_WhenDenormalSpan_ThenInterpolationRoundsToZero()
    {
        // vmax = double.Epsilon (5e-324, the smallest subnormal): Epsilon * 0.5 sits exactly
        // halfway between 0 and Epsilon and banks to even → cut 0 EXACTLY. A single cut is
        // trivially strictly ascending, so this is valid, not a collapse.
        var cuts = EqualWidthDiscretizer.DeriveCuts(2, 0, 5e-324, CutPrecision.Exact);

        Assert.Equal([0.0], cuts);
        Assert.True(CutValidation.AreUsableCuts(cuts));
    }

    [Fact]
    public void DeriveCuts_WhenRoundToCollapsesNothing_ThenBanksToEven() =>
        // Unrounded 0.5/1/1.5 over [0, 2] with 4 bins; round_to = 1 banks the halves to even,
        // giving 0/1/2 — still strictly ascending, so still valid.
        Assert.Equal([0.0, 1.0, 2.0], EqualWidthDiscretizer.DeriveCuts(4, 0, 2, RoundToPrecision.Create(1)));

    [Fact]
    public void DeriveCuts_WhenComputedCutWouldBeNegativeZero_ThenCanonicalizedToPositiveZero()
    {
        // G-6: the midpoint of [-1, 0.6] is -0.2, which round_to = 1 banks onto NEGATIVE zero.
        // Canonicalization must turn it positive so the sign never reaches a bin identity, label,
        // or hash — and so the cut labels "0", never "-0".
        var cuts = EqualWidthDiscretizer.DeriveCuts(2, -1.0, 0.6, RoundToPrecision.Create(1));

        var cut = Assert.Single(cuts);
        Assert.Equal(0.0, cut);
        Assert.False(double.IsNegative(cut), "a computed -0 must be canonicalized to +0 (G-6)");
    }

    [Fact]
    public void CreateManual_WhenComputedCutIsNegativeZero_ThenLabelsAsZero() =>
        // The same vector through the executable discretizer: the canonical label is "0".
        Assert.Equal(["<0", ">=0"], Manual(2, -1.0, 0.6, RoundToPrecision.Create(1)).BinLabels([]));

    [Theory]
    [InlineData(double.NaN, 10.0)]
    [InlineData(0.0, double.NaN)]
    [InlineData(double.NegativeInfinity, 10.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    [InlineData(10.0, 10.0)]  // vmin == vmax
    [InlineData(10.0, 0.0)]   // vmin > vmax
    public void CreateManual_WhenRangeUnusable_ThenEqualWidthRangeInvalid(double vmin, double vmax) =>
        Assert.Equal(
            DiagnosticCode.EqualWidthRangeInvalid,
            SoleCode(EqualWidthDiscretizer.CreateManual(4, vmin, vmax, CutPrecision.Exact, CultureInfo.InvariantCulture)));

    [Fact]
    public void CreateManual_WhenPrecisionCollapsesCuts_ThenEqualWidthCutsCollapsed() =>
        // Over [0, 2] with 8 bins the unrounded cuts are 0.25..1.75; round_to = 1 maps several
        // onto the same value, so the sequence stops being strictly ascending.
        Assert.Equal(
            DiagnosticCode.EqualWidthCutsCollapsed,
            SoleCode(EqualWidthDiscretizer.CreateManual(8, 0, 2, RoundToPrecision.Create(1), CultureInfo.InvariantCulture)));

    [Fact]
    public void CreateManual_WhenExtremeRange_ThenAcceptedRatherThanRejected() =>
        // G-7 restated as behaviour: a finite increasing range is never rejected merely for being
        // wide. The derived-cut check is a backstop on the result, not a second range gate.
        Assert.True(
            EqualWidthDiscretizer.CreateManual(2, -1.7e308, 1.7e308, CutPrecision.Exact, CultureInfo.InvariantCulture).IsOk);

    [Fact]
    public void CreateManual_WhenSpanNarrowerThanItsBins_ThenCutsCollapseEvenAtExactPrecision()
    {
        // The other half of the §11.4 derived-cut rule, and the reason its wording says "at this
        // precision" rather than blaming rounding: a span of a few ULPs cannot hold bins-1
        // DISTINCT representable cuts, so "exact" collapses too. The range itself is finite and
        // increasing — it is the result that fails, exactly as the spec now states.
        var vmin = 1e308;
        var vmax = Math.BitIncrement(Math.BitIncrement(vmin)); // two ULPs wide

        var result = EqualWidthDiscretizer.CreateManual(8, vmin, vmax, CutPrecision.Exact, CultureInfo.InvariantCulture);

        Assert.Equal(DiagnosticCode.EqualWidthCutsCollapsed, SoleCode(result));
        Assert.DoesNotContain("rounding", Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
    }

    // --- Public surface and state --------------------------------------------

    [Fact]
    public void Kind_WhenRead_ThenEqualWidth() => Assert.Equal("equal_width", Manual(4, 0, 100).Kind);

    [Fact]
    public void CreateManual_WhenBuilt_ThenCarriesAuthoredConfigAndEffectiveCuts()
    {
        var discretizer = Manual(4, 0, 100, RoundToPrecision.Create(5));

        Assert.Equal(4, discretizer.Bins);
        Assert.Equal(EqualWidthRange.Manual, discretizer.Range);
        Assert.Equal(0, discretizer.VMin);
        Assert.Equal(100, discretizer.VMax);
        Assert.Equal(RoundToPrecision.Create(5), discretizer.Precision);
        Assert.Equal([25.0, 50.0, 75.0], discretizer.Cuts);
    }

    [Fact]
    public void FromCalibratedCuts_WhenBuilt_ThenBoundsStayNullAndRangePreserved()
    {
        var config = new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact);

        var result = EqualWidthDiscretizer.FromCalibratedCuts(config, [25, 50, 75], CultureInfo.InvariantCulture);

        Assert.True(result.TryGetValue(out var discretizer));
        Assert.Equal(EqualWidthRange.MinMax, discretizer!.Range);
        Assert.Null(discretizer.VMin);
        Assert.Null(discretizer.VMax);
        Assert.Equal([25.0, 50.0, 75.0], discretizer.Cuts);
    }

    // Each list below is correctly SIZED for bins = 4 (three cuts) and invalid only in its values,
    // so these exercise the validity arm rather than the count guard below.
    [Theory]
    [InlineData(new[] { 25.0, 75.0, 50.0 })]                  // not ascending
    [InlineData(new[] { 25.0, 25.0, 75.0 })]                  // rounding collapse
    [InlineData(new[] { double.NaN, 50.0, 75.0 })]            // non-finite
    [InlineData(new[] { 1.0, 2.0, double.PositiveInfinity })] // non-finite
    public void FromCalibratedCuts_WhenCutsUnusable_ThenCalibrationCutsInvalid(double[] cuts) =>
        // The calibrate-phase twin of EqualWidthCutsCollapsed — same predicate, different owner.
        Assert.Equal(
            DiagnosticCode.CalibrationCutsInvalid,
            SoleCode(EqualWidthDiscretizer.FromCalibratedCuts(
                new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact), cuts, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(new[] { 25.0 })]                              // too few
    [InlineData(new[] { 25.0, 50.0 })]                        // too few
    [InlineData(new[] { 10.0, 25.0, 50.0, 75.0 })]            // too many
    public void FromCalibratedCuts_WhenCutCountDisagreesWithBins_ThenThrows(double[] cuts) =>
        // §11.4: `bins` bins come from exactly `bins - 1` cuts. A wrong-sized outcome is a
        // calibrator-contract violation, not a data error: it would build a discretizer whose Bins
        // contradicts its own geometry, so the fingerprint would encode "bins":4 beside a schema
        // array of a different width. Unrepresentable, therefore a throw (P-10/D-093) — not the
        // CalibrationCutsInvalid channel, which is for correctly-sized but unusable cuts.
        Assert.Throws<ArgumentException>(() => EqualWidthDiscretizer.FromCalibratedCuts(
            new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact), cuts, CultureInfo.InvariantCulture));

    [Fact]
    public void FromCalibratedCuts_WhenPercentileRange_ThenUnreachableBecauseThePendingCarrierIsMinMaxOnlyThisSlice() =>
        // The Slice C transitional boundary is enforced by CalibratedSpec.Create before this
        // factory is reached (see CalibratedSpecTests) — percentile has no calibration until
        // Slice D, so it must not become executable by any route (G-8/D-102).
        Assert.Equal(
            EqualWidthRange.PercentileP1P99,
            new PendingEqualWidth(4, EqualWidthRange.PercentileP1P99, CutPrecision.Exact).Range);

    [Fact]
    public void EqualWidthDiscretizer_WhenInspected_ThenNoPublicConstructorOrSetter()
    {
        // P-10: an executable equal_width with unchecked state is UNREPRESENTABLE, not merely
        // rejected — every path goes through a validating factory, and the get-only properties
        // mean even `with` cannot desync the range mode from its vmin/vmax.
        var type = typeof(EqualWidthDiscretizer);

        Assert.Empty(type.GetConstructors()); // public constructors only
        Assert.All(
            new[] { nameof(EqualWidthDiscretizer.Bins), nameof(EqualWidthDiscretizer.Range), nameof(EqualWidthDiscretizer.VMin), nameof(EqualWidthDiscretizer.VMax), nameof(EqualWidthDiscretizer.Precision), nameof(EqualWidthDiscretizer.Cuts) },
            name => Assert.Null(type.GetProperty(name)!.SetMethod));
    }

    [Fact]
    public void Cuts_WhenRead_ThenNotACastableMutableCollection()
    {
        // D-098 recursive immutability: a caller must not be able to reach through the read-only
        // facade and mutate the resolved cuts.
        var cuts = Manual(4, 0, 100).Cuts;

        Assert.IsNotType<double[]>(cuts);
        Assert.IsNotType<List<double>>(cuts);
    }

    // --- The pending carrier (D-093) -----------------------------------------

    [Fact]
    public void PendingEqualWidth_WhenBuilt_ThenCarriesTheAuthoredConfigAndItsKind()
    {
        var config = new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact);

        Assert.Equal("equal_width", config.Kind);
        Assert.Equal(4, config.Bins);
        Assert.Equal(EqualWidthRange.MinMax, config.Range);
        Assert.Equal(CutPrecision.Exact, config.Precision);
    }

    [Fact]
    public void PendingEqualWidth_WhenRangeIsManual_ThenThrows() =>
        // P-10: a spec-determined range never pends — that state is unrepresentable, not merely
        // rejected later (§11.4/D-089).
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PendingEqualWidth(4, EqualWidthRange.Manual, CutPrecision.Exact));

    [Fact]
    public void PendingEqualWidth_WhenRangeIsUndefinedEnum_ThenThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PendingEqualWidth(4, (EqualWidthRange)99, CutPrecision.Exact));

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void PendingEqualWidth_WhenBinsBelowTwo_ThenThrows(int bins) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PendingEqualWidth(bins, EqualWidthRange.MinMax, CutPrecision.Exact));

    [Fact]
    public void CalibrationPending_WhenEqualWidthCarrierPlanned_ThenThrowsRatherThanBinning()
    {
        // The carrier can never plan or emit: reaching it means calibration was skipped (D-093).
        var carrier = new CalibrationPending(
            new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact), CultureInfo.InvariantCulture);

        Assert.Equal("equal_width", carrier.Kind);
        Assert.Throws<InvalidOperationException>(() => carrier.Discretize("1"));
        Assert.Throws<InvalidOperationException>(() => carrier.BinLabels([]));
    }

    // Culture-mutation isolation is the ResolvedSpec trust boundary's job, not this factory's —
    // it re-homes every culture-bearing discretizer onto a read-only clone (D-098). See
    // ResolvedSpecTests.Create_WhenEqualWidthParsingCultureMutatedAfterResolution_*.

    // --- Bin geometry: open ends always (§11.4) -------------------------------

    [Fact]
    public void Discretize_WhenValuesSpanTheRange_ThenOpenEndedBinsIncludingOutsideTheSpan()
    {
        var discretizer = Manual(4, 0, 100); // cuts 25/50/75

        // Below and above the calibration span still land in the first/last bin — the reason
        // §11.4 fixes ends = "open" for the auto discretizer.
        Assert.Equal(BinResult.Bin("<25"), discretizer.Discretize("-1000"));
        Assert.Equal(BinResult.Bin("<25"), discretizer.Discretize("24.999"));
        Assert.Equal(BinResult.Bin("[25, 50)"), discretizer.Discretize("25"));   // half-open: lower edge included
        Assert.Equal(BinResult.Bin("[50, 75)"), discretizer.Discretize("50"));
        Assert.Equal(BinResult.Bin(">=75"), discretizer.Discretize("75"));
        Assert.Equal(BinResult.Bin(">=75"), discretizer.Discretize("1e9"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,5")] // the invariant culture reads no comma separator
    public void Discretize_WhenPresentButNotAFiniteNumber_ThenUnparseable(string raw) =>
        // §11.5/D-050: kept, no cross, diagnosable — never silently missing.
        Assert.Equal(BinResult.Unparseable(raw), Manual(4, 0, 100).Discretize(raw));

    [Fact]
    public void Discretize_WhenSuppliedLocale_ThenParsesUnderIt() =>
        // P-11: binding.locale governs numeric parsing, never the ambient culture.
        Assert.Equal(
            BinResult.Bin("[25, 50)"),
            Manual(4, 0, 100, culture: CultureInfo.GetCultureInfo("de-DE")).Discretize("30,5"));

    [Fact]
    public void BinLabels_WhenBuilt_ThenOpenEndedLabelsWithoutDecimalNoise() =>
        // The labels are the §14 canonical numbers: "<25", never "<25.0".
        Assert.Equal(["<25", "[25, 50)", "[50, 75)", ">=75"], Manual(4, 0, 100).BinLabels([]));

    [Fact]
    public void RenderBinLabel_WhenV2Compat_ThenInteriorBinsUseTheV2Form()
    {
        var discretizer = Manual(4, 0, 100);

        Assert.Equal("25to<50", discretizer.RenderBinLabel("[25, 50)", LabelStyle.V2Compat));
        Assert.Equal("[25, 50)", discretizer.RenderBinLabel("[25, 50)", LabelStyle.Native));
        Assert.Equal("<25", discretizer.RenderBinLabel("<25", LabelStyle.V2Compat));   // open ends pass through
        Assert.Equal(">=75", discretizer.RenderBinLabel(">=75", LabelStyle.V2Compat));
    }

    [Fact]
    public void DescribeBins_WhenBuilt_ThenCutGeometryWithBothEndsOpen()
    {
        var scheme = Manual(4, 0, 100).DescribeBins([]);

        Assert.True(scheme.CutBins);      // an ordinal scale thresholds on the geometry, not scale.order
        Assert.True(scheme.OpenLow);
        Assert.True(scheme.OpenHigh);
        Assert.Equal(["25", "50", "75"], scheme.Thresholds);
        Assert.Equal(4, scheme.Bins.Count);
    }

    [Fact]
    public void DescribeBins_WhenDeclaredDomainSupplied_ThenIgnored() =>
        // §10.3: cut discretizers never consume declared_domain.
        Assert.Equal(
            Manual(4, 0, 100).DescribeBins([]).Labels,
            Manual(4, 0, 100).DescribeBins(["nonsense", "values"]).Labels);
}
