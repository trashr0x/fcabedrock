using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Discretization;

/// <summary>
/// The executable <c>equal_frequency</c> discretizer (§11.5, M4 Slice D / D-103): its state,
/// its construction contract, and its delegation to the shared <see cref="NumericCutBins"/>
/// engine — which is what makes it structurally identical to an open-ended
/// <c>manual_cuts</c> over the same cuts (the D-088 auto/frozen equivalence).
/// <para>
/// It is reachable only through <c>CalibratedSpec.Create</c> (an internal factory over a
/// pending carrier), so these tests drive it the way production does.
/// </para>
/// </summary>
public sealed class EqualFrequencyDiscretizerTests
{
    private static EqualFrequencyDiscretizer Build(
        IReadOnlyList<double> cuts, int bins = 3, TiePolicy tie = TiePolicy.Left,
        CutPlacement placement = CutPlacement.RightValue, CultureInfo? culture = null)
    {
        var created = CalibratedSpec.Create(
            SpecFixtures.Resolve(Spec(bins, tie, placement, culture), new SourceSchema(1)),
            [new CalibratedCuts("score", cuts)]);

        Assert.True(created.TryGetValue(out var calibrated),
            string.Join("; ", created.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.IsType<EqualFrequencyDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);
    }

    private static BedrockSpec Spec(
        int bins = 3, TiePolicy tie = TiePolicy.Left, CutPlacement placement = CutPlacement.RightValue,
        CultureInfo? culture = null) =>
        new(SpecFixtures.WideRowIndex(),
            [SpecFixtures.EqualFrequencyPending("score", 0, bins, new FcaBedrock.Core.Scaling.NominalScale(), tie, placement, culture)]);

    private static Diagnosed<CalibratedSpec> CreateWith(IReadOnlyList<double> cuts, int bins = 3) =>
        CalibratedSpec.Create(
            SpecFixtures.Resolve(Spec(bins), new SourceSchema(1)), [new CalibratedCuts("score", cuts)]);

    // --- State ---------------------------------------------------------------

    [Fact]
    public void Kind_WhenBuilt_ThenExactlyEqualFrequency() =>
        Assert.Equal("equal_frequency", Build([2, 3]).Kind);

    [Fact]
    public void Properties_WhenBuilt_ThenPreserveTheResolvedConfigurationAndCuts()
    {
        var discretizer = Build([2, 3], bins: 3, tie: TiePolicy.Right, placement: CutPlacement.Midpoint);

        // tie_policy and cut_placement are spent by now — they shaped the cuts during calibration
        // (§11.5: emit does no tie handling) — but they survive as the authored configuration the
        // §14 fingerprint encodes (D-094).
        Assert.Equal(3, discretizer.Bins);
        Assert.Equal(TiePolicy.Right, discretizer.TiePolicy);
        Assert.Equal(CutPlacement.Midpoint, discretizer.CutPlacement);
        Assert.Equal([2.0, 3.0], discretizer.Cuts);
    }

    [Fact]
    public void Culture_WhenBuilt_ThenTheResolvedReadOnlyParsingCulture()
    {
        var discretizer = Build([2, 3], culture: CultureInfo.GetCultureInfo("de-DE"));

        Assert.True(discretizer.Culture.IsReadOnly);
        Assert.Equal("de-DE", discretizer.Culture.Name);

        // The resolved culture parses data (never ambient — P-11): "2,5" is 2.5 under de-DE.
        Assert.Equal(BinResult.Bin("[2, 3)"), discretizer.Discretize("2,5"));
    }

    [Fact]
    public void Cuts_WhenTheCallerMutatesTheSourceList_ThenTheDiscretizerIsUnaffected()
    {
        var mutable = new List<double> { 2, 3 };
        var discretizer = Build(mutable);

        mutable[0] = 99;

        Assert.Equal([2.0, 3.0], discretizer.Cuts);
        Assert.IsNotType<List<double>>(discretizer.Cuts);
        Assert.IsNotType<double[]>(discretizer.Cuts);
    }

    // --- Construction contract -----------------------------------------------

    [Fact]
    public void Create_WhenTheCutCountContradictsBins_ThenThrows()
    {
        // §11.5: `bins` bins come from exactly `bins - 1` cuts. A wrong-sized outcome would build
        // a discretizer whose Bins disagrees with its own geometry — the fingerprint would encode
        // "bins":3 beside a schema array of another width. That is a calibrator-contract violation,
        // not a data error, so it throws rather than diagnosing (D-093/P-10).
        var ex = Assert.Throws<ArgumentException>(() => CreateWith([2, 3, 4], bins: 3));

        Assert.Contains("exactly 2 calibrated cuts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WhenTheCutListIsEmpty_ThenThrows()
    {
        // The zero end of the same guard, pinned explicitly: `bins >= 2` is enforced at the
        // pending carrier, so an EMPTY calibrated-cuts outcome is always the wrong size and can
        // never yield a usable discretizer. An empty cut list is therefore never a legitimate
        // zero-discovery outcome — unlike the empty observed-domain / include-additions /
        // passthrough-bins outcomes, which are retained as completeness markers (D-098/D-104).
        var ex = Assert.Throws<ArgumentException>(() => CreateWith([], bins: 3));

        Assert.Contains("exactly 2 calibrated cuts", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { 3.0, 2.0 })]                       // descending
    [InlineData(new[] { 2.0, 2.0 })]                       // not strictly ascending
    [InlineData(new[] { double.NaN, 2.0 })]                // non-finite
    [InlineData(new[] { 1.0, double.PositiveInfinity })]   // non-finite but "ascending"
    public void Create_WhenCorrectlySizedCutsAreInvalid_ThenCalibrationCutsInvalid(double[] cuts)
    {
        // Correctly sized but unusable cuts are a DATA-derived failure, so they come back through
        // the diagnostic channel (P-14) rather than throwing — the opposite of the wrong-count case.
        var created = CreateWith(cuts);

        Assert.False(created.IsOk);
        var diagnostic = Assert.Single(created.Diagnostics);
        Assert.Equal(DiagnosticCode.CalibrationCutsInvalid, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("score", diagnostic.Location?.AttributeName);
    }

    // --- Execution: delegated to the shared engine, open ends -----------------

    [Fact]
    public void Discretize_WhenValuesSpanTheCuts_ThenHalfOpenGeometryWithOpenEnds()
    {
        var discretizer = Build([2, 3]);

        // §11.5 shares §11.4's implicit open ends: values outside the calibration span still fall
        // in the first/last bin rather than vanishing.
        Assert.Equal(BinResult.Bin("<2"), discretizer.Discretize("-1000"));
        Assert.Equal(BinResult.Bin("<2"), discretizer.Discretize("1.9"));
        Assert.Equal(BinResult.Bin("[2, 3)"), discretizer.Discretize("2"));    // a value equal to a cut
        Assert.Equal(BinResult.Bin("[2, 3)"), discretizer.Discretize("2.9"));  // goes to the UPPER bin
        Assert.Equal(BinResult.Bin(">=3"), discretizer.Discretize("3"));
        Assert.Equal(BinResult.Bin(">=3"), discretizer.Discretize("1000"));
    }

    [Fact]
    public void Discretize_WhenValueIsUnparseableOrNonFinite_ThenUnparseable()
    {
        var discretizer = Build([2, 3]);

        Assert.Equal(BinResult.Unparseable("wibble"), discretizer.Discretize("wibble"));
        Assert.Equal(BinResult.Unparseable("NaN"), discretizer.Discretize("NaN"));
        Assert.Equal(BinResult.Unparseable("Infinity"), discretizer.Discretize("Infinity"));
    }

    [Fact]
    public void Execution_WhenComparedToManualCutsOverTheSameCuts_ThenIdenticalInEveryRespect()
    {
        // The structural basis of the D-088 auto/frozen equivalence (D-093): both compose the one
        // NumericCutBins engine, so the frozen manual_cuts twin cannot drift from the auto form —
        // the equality holds by construction rather than by two code paths agreeing.
        double[] cuts = [2, 3];
        var auto = Build(cuts);
        var frozen = ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!;

        foreach (var raw in new[] { "-5", "1.9", "2", "2.5", "3", "99", "wibble" })
        {
            Assert.Equal(frozen.Discretize(raw), auto.Discretize(raw));
        }

        // BinScheme is a record over ImmutableArray, whose equality is by underlying reference —
        // so the components are compared, not the scheme objects.
        var autoScheme = auto.DescribeBins([]);
        var frozenScheme = frozen.DescribeBins([]);
        Assert.Equal(frozenScheme.Labels, autoScheme.Labels);
        Assert.Equal(frozenScheme.Bins, autoScheme.Bins);
        Assert.Equal(frozenScheme.Thresholds, autoScheme.Thresholds);
        Assert.Equal(frozenScheme.OpenLow, autoScheme.OpenLow);
        Assert.Equal(frozenScheme.OpenHigh, autoScheme.OpenHigh);
        Assert.Equal(frozenScheme.CutBins, autoScheme.CutBins);
        Assert.Equal(frozen.BinLabels([]), auto.BinLabels([]));
    }

    [Theory]
    [InlineData(LabelStyle.Native)]
    [InlineData(LabelStyle.V2Compat)]
    public void RenderBinLabel_WhenAnyStyle_ThenMatchesManualCutsOverTheSameCuts(LabelStyle style)
    {
        double[] cuts = [2, 3];
        var auto = Build(cuts);
        var frozen = ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!;

        foreach (var label in auto.BinLabels([]))
        {
            Assert.Equal(frozen.RenderBinLabel(label, style), auto.RenderBinLabel(label, style));
        }
    }

    [Fact]
    public void DescribeBins_WhenBuilt_ThenCutBinsWithBothEndsOpen()
    {
        var scheme = Build([2, 3]).DescribeBins([]);

        // Cut geometry — not a value domain — is the ordering authority for an ordinal over these
        // bins (§12.3), which is why equal_frequency needs no ordinal implementation of its own.
        Assert.True(scheme.CutBins);
        Assert.True(scheme.OpenLow);
        Assert.True(scheme.OpenHigh);
        Assert.Equal(3, scheme.Labels.Count); // bins - 1 cuts → bins bins
    }

    [Fact]
    public void BinLabels_WhenDeclaredDomainIsSupplied_ThenIgnored() =>
        // §10.3: cut discretizers do not consume declared_domain, so a stray domain cannot move
        // the bins.
        Assert.Equal(Build([2, 3]).BinLabels([]), Build([2, 3]).BinLabels(["wibble", "wobble"]));

    [Fact]
    public void ConsumesDeclaredDomain_WhenBuilt_ThenFalse() =>
        Assert.False(Build([2, 3]).ConsumesDeclaredDomain);
}
