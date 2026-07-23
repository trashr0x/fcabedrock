using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Calibration;

public sealed class CalibratedSpecTests
{
    private static ResolvedSpec Resolve(BedrockSpec spec, int columns) =>
        SpecFixtures.Resolve(spec, new SourceSchema(columns));

    private static BedrockSpec With(params AttributeSpec[] attributes) =>
        new(SpecFixtures.WideRowIndex(), attributes);

    // --- FromFullyDeclared ---

    [Fact]
    public void FromFullyDeclared_WhenSpecIsFullyDeclared_ThenReturnsStateWithEmptyCalibrations()
    {
        var resolved = SpecFixtures.Resolve(SpecFixtures.MiniMushroom(), new SourceSchema(5));

        var calibrated = CalibratedSpec.FromFullyDeclared(resolved);

        Assert.Empty(calibrated.Calibrations);
        Assert.Same(resolved, calibrated.Resolution);
        Assert.Same(resolved.Schema, calibrated.Schema);
        Assert.Same(resolved.Spec, calibrated.Spec);
    }

    [Fact]
    public void FromFullyDeclared_WhenSpecRequiresData_ThenThrows()
    {
        // An omitted-domain identity attribute is data-dependent; calling the fast path is a
        // mis-sequenced call that skipped the calibrator (D-093 programmer-error posture).
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, null)), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.FromFullyDeclared(resolved));
    }

    // --- RequiresData ---

    [Fact]
    public void RequiresData_WhenOmittedDomainIdentity_ThenTrue() =>
        Assert.True(CalibratedSpec.RequiresData(With(SpecFixtures.Nominal("g", 0, null))));

    [Fact]
    public void RequiresData_WhenAuthoredEmptyDomainIdentityUnderWarn_ThenFalse() =>
        // D-122 §15: an authored [] is a complete fixed empty domain — it requests no calibration.
        Assert.False(CalibratedSpec.RequiresData(With(SpecFixtures.Nominal("g", 0, []))));

    [Fact]
    public void RequiresData_WhenAuthoredEmptyDomainIdentityUnderInclude_ThenTrue() =>
        // include still reads data to discover additions, even over an authored [] (D-122 §15).
        Assert.True(CalibratedSpec.RequiresData(
            With(SpecFixtures.Nominal("g", 0, []) with { UnknownValuePolicy = UnknownValuePolicy.Include })));

    [Fact]
    public void RequiresData_WhenExplicitDomainIdentityUnderInclude_ThenTrue() =>
        Assert.True(CalibratedSpec.RequiresData(
            With(SpecFixtures.Nominal("g", 0, ["x"]) with { UnknownValuePolicy = UnknownValuePolicy.Include })));

    [Fact]
    public void RequiresData_WhenExplicitDomainIdentityUnderWarn_ThenFalse() =>
        Assert.False(CalibratedSpec.RequiresData(With(SpecFixtures.Nominal("g", 0, ["x"]))));

    [Fact]
    public void RequiresData_WhenExcludedAbsentDomain_ThenFalse() =>
        // Parked config never counts (D-049): an excluded attribute never triggers a data pass.
        Assert.False(CalibratedSpec.RequiresData(
            new BedrockSpec(SpecFixtures.WideRowIndex(),
                [SpecFixtures.Excluded("x", 0), SpecFixtures.Nominal("g", 1, ["b"])])));

    [Fact]
    public void RequiresData_WhenOnlyRestrictTo_ThenFalse() =>
        // §6.1/§15-A: restrict_to never triggers a calibration pass (D-065) — a fully-declared
        // attribute whose only extra config is a filter stays fully-declared.
        Assert.False(CalibratedSpec.RequiresData(
            With(SpecFixtures.Nominal("g", 0, ["b"]) with { RestrictTo = [new RestrictToValue("b")] })));

    [Fact]
    public void RequiresData_WhenCutDiscretizerNoDomain_ThenFalse() =>
        // Cut discretizers ignore declared_domain (§10.3).
        Assert.False(CalibratedSpec.RequiresData(With(SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale()))));

    // --- Create: observed domain ---

    [Fact]
    public void Create_WhenOmittedDomainGivenObservedDomain_ThenEffectiveDomainIsObserved()
    {
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, null)), 1);

        Assert.True(CalibratedSpec.Create(resolved, [new ObservedDomain("g", ["b", "n"])]).TryGetValue(out var calibrated));
        Assert.Equal(["b", "n"], calibrated.Spec.Attributes[0].DeclaredDomain);
        Assert.IsType<ObservedDomain>(Assert.Single(calibrated.Calibrations));
    }

    [Fact]
    public void Create_WhenOmittedDomainWithEmptyObservedDomain_ThenLegalAndEmptyEffectiveDomain()
    {
        // An empty observed domain is legal (§10.1/§10.3): the attribute yields zero columns.
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, null)), 1);

        Assert.True(CalibratedSpec.Create(resolved, [new ObservedDomain("g", [])]).TryGetValue(out var calibrated));
        var domain = calibrated.Spec.Attributes[0].DeclaredDomain;
        Assert.NotNull(domain); // an omitted domain is filled to a concrete (here empty) list, not left null
        Assert.Empty(domain);
    }

    [Fact]
    public void Create_WhenOmittedDomainOutcomeMissing_ThenThrows()
    {
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, null)), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, []));
    }

    [Fact]
    public void Create_WhenOmittedDomainGivenIncludeAdditions_ThenThrows()
    {
        // ObservedDomain represents the complete population and never co-occurs with IncludeAdditions.
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, null)), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, [new IncludeAdditions("g", ["b"])]));
    }

    // --- Create: authored empty domain (D-122 §15) ---

    [Fact]
    public void Create_WhenAuthoredEmptyDomainUnderWarn_ThenNoOutcomeNeededAndDomainStaysEmpty()
    {
        // An authored [] is complete: it needs no outcome (providing one throws), and with none the
        // effective domain stays the authored empty universe (zero value bins).
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, [])), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, [new ObservedDomain("g", ["b"])]));

        Assert.True(CalibratedSpec.Create(resolved, []).TryGetValue(out var calibrated));
        var domain = calibrated.Spec.Attributes[0].DeclaredDomain;
        Assert.NotNull(domain);
        Assert.Empty(domain);
        Assert.Empty(calibrated.Calibrations);
    }

    [Fact]
    public void Create_WhenAuthoredEmptyDomainUnderInclude_ThenRequiresIncludeAdditionsNeverObserved()
    {
        // Authored [] under include takes the IncludeAdditions path, never ObservedDomain: the empty
        // domain contributes nothing, so the effective domain is exactly the additions (D-122 §15).
        var resolved = Resolve(
            With(SpecFixtures.Nominal("g", 0, []) with { UnknownValuePolicy = UnknownValuePolicy.Include }), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, [new ObservedDomain("g", ["b"])]));

        Assert.True(CalibratedSpec.Create(resolved, [new IncludeAdditions("g", ["b", "n"])]).TryGetValue(out var calibrated));
        Assert.Equal(["b", "n"], calibrated.Spec.Attributes[0].DeclaredDomain);
        Assert.IsType<IncludeAdditions>(Assert.Single(calibrated.Calibrations));
    }

    // --- Create: include additions ---

    [Fact]
    public void Create_WhenIncludeGivenAdditions_ThenEffectiveDomainAppendsThem()
    {
        var resolved = Resolve(
            With(SpecFixtures.Nominal("g", 0, ["x"]) with { UnknownValuePolicy = UnknownValuePolicy.Include }), 1);

        Assert.True(CalibratedSpec.Create(resolved, [new IncludeAdditions("g", ["y", "z"])]).TryGetValue(out var calibrated));
        Assert.Equal(["x", "y", "z"], calibrated.Spec.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void Create_WhenIncludeGivenEmptyAdditions_ThenLegalZeroAdditionsMarker()
    {
        var resolved = Resolve(
            With(SpecFixtures.Nominal("g", 0, ["x"]) with { UnknownValuePolicy = UnknownValuePolicy.Include }), 1);

        Assert.True(CalibratedSpec.Create(resolved, [new IncludeAdditions("g", [])]).TryGetValue(out var calibrated));
        Assert.Equal(["x"], calibrated.Spec.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void Create_WhenIncludeMarkerMissing_ThenThrows()
    {
        var resolved = Resolve(
            With(SpecFixtures.Nominal("g", 0, ["x"]) with { UnknownValuePolicy = UnknownValuePolicy.Include }), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, []));
    }

    [Fact]
    public void Create_WhenDuplicateOutcomeForOneAttribute_ThenThrows()
    {
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, null)), 1);

        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(resolved, [new ObservedDomain("g", ["b"]), new ObservedDomain("g", ["n"])]));
    }

    [Fact]
    public void Create_WhenOutcomeNamesUnknownAttribute_ThenThrows()
    {
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, null)), 1);

        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(resolved, [new ObservedDomain("g", ["b"]), new ObservedDomain("nope", ["x"])]));
    }

    [Fact]
    public void Create_WhenOutcomeNamesExcludedAttribute_ThenThrows()
    {
        var resolved = Resolve(
            new BedrockSpec(SpecFixtures.WideRowIndex(),
                [SpecFixtures.Excluded("x", 0), SpecFixtures.Nominal("g", 1, ["b"])]), 2);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, [new ObservedDomain("x", ["a"])]));
    }

    [Fact]
    public void Create_WhenUnexpectedOutcomeOnFullyDeclaredAttribute_ThenThrows()
    {
        // A fully-declared (warn, explicit domain) attribute needs no outcome.
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, ["b"])), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, [new ObservedDomain("g", ["b"])]));
    }

    // --- Pending equal_width → executable substitution (M4 Slice C, D-102) ---

    private static BedrockSpec PendingEqualWidthSpec(
        int bins = 4, CutPrecision? precision = null, EqualWidthRange range = EqualWidthRange.MinMax) =>
        With(SpecFixtures.EqualWidthPending("score", 0, bins, new NominalScale(), range, precision));

    [Fact]
    public void RequiresData_WhenPendingEqualWidth_ThenTrue() =>
        // A data-derived range cannot be planned from the spec text alone (§7/D-089).
        Assert.True(CalibratedSpec.RequiresData(PendingEqualWidthSpec()));

    [Fact]
    public void RequiresData_WhenManualEqualWidth_ThenFalse() =>
        // range = "manual" is spec-determined: it skips Calibrate entirely (§7/§11.4/D-089).
        Assert.False(CalibratedSpec.RequiresData(
            With(SpecFixtures.EqualWidthManual("score", 0, 4, 0, 100, new NominalScale()))));

    [Fact]
    public void FromFullyDeclared_WhenManualEqualWidth_ThenAccepted()
    {
        var spec = With(SpecFixtures.EqualWidthManual("score", 0, 4, 0, 100, new NominalScale()));

        var calibrated = CalibratedSpec.FromFullyDeclared(Resolve(spec, 1));

        Assert.Empty(calibrated.Calibrations);
        Assert.IsType<EqualWidthDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);
    }

    [Fact]
    public void FromFullyDeclared_WhenPendingEqualWidth_ThenThrows() =>
        // The min/max carrier is data-dependent, so the fast path is a mis-sequenced call that
        // skipped the calibrator (the established RequiresData contract, D-093).
        Assert.Throws<ArgumentException>(() => CalibratedSpec.FromFullyDeclared(Resolve(PendingEqualWidthSpec(), 1)));

    [Fact]
    public void Create_WhenPendingEqualWidthGivenCalibratedCuts_ThenExecutableDiscretizerSubstituted()
    {
        var resolved = Resolve(PendingEqualWidthSpec(), 1);

        var result = CalibratedSpec.Create(resolved, [new CalibratedCuts("score", [25, 50, 75])]);

        Assert.True(result.TryGetValue(out var calibrated));
        var discretizer = Assert.IsType<EqualWidthDiscretizer>(calibrated!.Spec.Attributes[0].Discretizer);
        Assert.Equal([25.0, 50.0, 75.0], discretizer.Cuts);

        // The authored configuration survives the substitution — the fingerprint hashes it (D-094).
        Assert.Equal(4, discretizer.Bins);
        Assert.Equal(EqualWidthRange.MinMax, discretizer.Range);
        Assert.Equal(CutPrecision.Exact, discretizer.Precision);
        Assert.Null(discretizer.VMin);
    }

    [Fact]
    public void Create_WhenPendingEqualWidthSubstituted_ThenNoCalibrationPendingSurvives()
    {
        var result = CalibratedSpec.Create(Resolve(PendingEqualWidthSpec(), 1), [new CalibratedCuts("score", [25, 50, 75])]);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.DoesNotContain(calibrated!.Spec.Attributes, a => a.Discretizer is CalibrationPending);
    }

    [Fact]
    public void Create_WhenPendingEqualWidthCalibrated_ThenOutcomeRetainedInSpecAttributeOrder()
    {
        // D-093's retention boundary: the freeze path and the §15 manifest read these outcomes
        // rather than re-deriving them, so they must come back in a deterministic order.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.EqualWidthPending("a", 0, 2, new NominalScale()),
            SpecFixtures.Nominal("g", 1, null),
            SpecFixtures.EqualWidthPending("z", 2, 2, new NominalScale()),
        ]);

        var result = CalibratedSpec.Create(Resolve(spec, 3), [
            new CalibratedCuts("z", [7]),          // supplied out of spec order on purpose
            new ObservedDomain("g", ["b"]),
            new CalibratedCuts("a", [3]),
        ]);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal(["a", "g", "z"], calibrated!.Calibrations.Select(c => c.AttributeName));
        Assert.Equal([3.0], Assert.IsType<CalibratedCuts>(calibrated.Calibrations[0]).Cuts);
    }

    [Fact]
    public void Create_WhenCalibratedCutsInvalid_ThenCalibrationCutsInvalidDiagnosticNotException()
    {
        // P-14: cut invalidity is a DATA-derived expected failure, so it returns through Diagnosed
        // for the calibrator to aggregate — it is not a calibrator-contract violation. The list is
        // correctly sized for bins = 4; only its ordering is wrong.
        var result = CalibratedSpec.Create(Resolve(PendingEqualWidthSpec(), 1), [new CalibratedCuts("score", [25, 75, 50])]);

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.CalibrationCutsInvalid, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("score", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void Create_WhenCalibratedCutCountDisagreesWithBins_ThenThrows() =>
        // The contract split: a wrong-SIZED outcome is a calibrator-contract violation (throw),
        // while wrong-VALUED cuts of the right size are a data error (diagnostic, above). Without
        // this the state would plan and fingerprint as "bins":4 over a two-bin schema (D-093/P-10).
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PendingEqualWidthSpec(), 1), [new CalibratedCuts("score", [50])]));

    [Fact]
    public void Create_WhenEqualWidthCalibratedCutsAreEmpty_ThenThrows() =>
        // The zero end of the size guard, at the calibrated-state boundary. `bins >= 2` is
        // enforced at the pending carrier, so an EMPTY cuts outcome is always the wrong size —
        // an empty cut list is never a legitimate zero-discovery outcome, unlike the empty
        // observed-domain / include-additions / passthrough-bins outcomes retained elsewhere in
        // this file as completeness markers (D-098/D-104).
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PendingEqualWidthSpec(), 1), [new CalibratedCuts("score", [])]));

    // --- equal_frequency substitution (M4 Slice D / D-103) --------------------

    private static BedrockSpec PendingEqualFrequencySpec(
        int bins = 3, TiePolicy tie = TiePolicy.Left, CutPlacement placement = CutPlacement.RightValue) =>
        With(SpecFixtures.EqualFrequencyPending("score", 0, bins, new NominalScale(), tie, placement));

    [Fact]
    public void RequiresData_WhenPendingEqualFrequency_ThenTrue() =>
        // Unlike equal_width there is no spec-determined mode: EVERY equal_frequency spec draws
        // its cuts from the population, so it can never be planned from its own text (§7/§11.5).
        Assert.True(CalibratedSpec.RequiresData(PendingEqualFrequencySpec()));

    [Fact]
    public void FromFullyDeclared_WhenEqualFrequency_ThenThrows() =>
        // The fast path is for specs determined by their own text; calling it here skipped the
        // calibrator (D-093 programmer-error posture).
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.FromFullyDeclared(Resolve(PendingEqualFrequencySpec(), 1)));

    [Fact]
    public void Create_WhenPendingEqualFrequencyGivenCalibratedCuts_ThenExecutableDiscretizerSubstituted()
    {
        var resolved = Resolve(PendingEqualFrequencySpec(tie: TiePolicy.Right, placement: CutPlacement.Midpoint), 1);

        var created = CalibratedSpec.Create(resolved, [new CalibratedCuts("score", [2, 3])]);

        Assert.True(created.TryGetValue(out var calibrated));
        var discretizer = Assert.IsType<EqualFrequencyDiscretizer>(calibrated!.Spec.Attributes[0].Discretizer);
        Assert.Equal([2.0, 3.0], discretizer.Cuts);

        // The authored configuration survives substitution — the §14 fingerprint encodes it (D-094).
        Assert.Equal(3, discretizer.Bins);
        Assert.Equal(TiePolicy.Right, discretizer.TiePolicy);
        Assert.Equal(CutPlacement.Midpoint, discretizer.CutPlacement);

        // No CalibrationPending survives a successful Create, and the token is carried forward.
        Assert.DoesNotContain(calibrated.Spec.Attributes, a => a.Discretizer is CalibrationPending);
        Assert.Same(resolved, calibrated.Resolution);
    }

    [Fact]
    public void Create_WhenPendingEqualFrequencyCalibrated_ThenTheOutcomeIsRetained()
    {
        var created = CalibratedSpec.Create(
            Resolve(PendingEqualFrequencySpec(), 1), [new CalibratedCuts("score", [2, 3])]);

        Assert.True(created.TryGetValue(out var calibrated));
        Assert.Equal([2.0, 3.0], Assert.IsType<CalibratedCuts>(Assert.Single(calibrated!.Calibrations)).Cuts);
    }

    [Fact]
    public void Create_WhenPendingEqualFrequencyHasNoOutcome_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PendingEqualFrequencySpec(), 1), []));

    [Fact]
    public void Create_WhenPendingEqualFrequencyGivenTheWrongOutcomeKind_ThenThrows() =>
        // A domain outcome cannot resolve cuts: a kind-mismatched outcome is a calibrator-contract
        // violation, not something to diagnose.
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PendingEqualFrequencySpec(), 1), [new ObservedDomain("score", ["1"])]));

    [Fact]
    public void Create_WhenPendingEqualFrequencyGivenDuplicateOutcomes_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(
                Resolve(PendingEqualFrequencySpec(), 1),
                [new CalibratedCuts("score", [2, 3]), new CalibratedCuts("score", [4, 5])]));

    [Fact]
    public void Create_WhenEqualFrequencyCutCountDisagreesWithBins_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PendingEqualFrequencySpec(bins: 3), 1), [new CalibratedCuts("score", [2])]));

    [Fact]
    public void Create_WhenEqualFrequencyCalibratedCutsAreEmpty_ThenThrows() =>
        // The equal_frequency sibling of the empty-cuts backstop above: every configuration draws
        // its cuts from the population (§11.5), and `bins >= 2` makes [] always the wrong size, so
        // an empty outcome can never become an executable discretizer by this route either.
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PendingEqualFrequencySpec(bins: 3), 1), [new CalibratedCuts("score", [])]));

    [Fact]
    public void Create_WhenEqualFrequencyCutsAreInvalid_ThenCalibrationCutsInvalidDiagnosticNotException()
    {
        var created = CalibratedSpec.Create(
            Resolve(PendingEqualFrequencySpec(), 1), [new CalibratedCuts("score", [3, 2])]);

        Assert.False(created.IsOk);
        Assert.Equal(DiagnosticCode.CalibrationCutsInvalid, Assert.Single(created.Diagnostics).Code);
    }

    [Fact]
    public void Create_WhenEqualFrequencyCutsAreSuppliedFromAMutableList_ThenTheGraphSnapshotsThem()
    {
        var mutable = new List<double> { 2, 3 };
        var created = CalibratedSpec.Create(Resolve(PendingEqualFrequencySpec(), 1), [new CalibratedCuts("score", mutable)]);
        Assert.True(created.TryGetValue(out var calibrated));

        mutable[0] = 99;

        // Caller mutation must not reach planning, emission, or the fingerprint (D-098).
        var discretizer = Assert.IsType<EqualFrequencyDiscretizer>(calibrated!.Spec.Attributes[0].Discretizer);
        Assert.Equal([2.0, 3.0], discretizer.Cuts);
        Assert.Equal([2.0, 3.0], Assert.IsType<CalibratedCuts>(calibrated.Calibrations[0]).Cuts);
        Assert.IsNotType<List<double>>(discretizer.Cuts);
        Assert.IsNotType<double[]>(discretizer.Cuts);
    }

    [Fact]
    public void Create_WhenPendingPercentileRange_ThenExecutableDiscretizerSubstituted()
    {
        // D-103 narrows the D-102/G-8 guard DELIBERATELY: percentile_p1_p99 was rejected here
        // while it had no calibration, and now that Slice D lands one it substitutes exactly like
        // min_max. The narrowing is to the two data-derived ranges by name, not to "any non-manual
        // range" — so a future range mode cannot become executable by merely existing in the enum.
        var spec = PendingEqualWidthSpec(range: EqualWidthRange.PercentileP1P99);

        var created = CalibratedSpec.Create(Resolve(spec, 1), [new CalibratedCuts("score", [25, 50, 75])]);

        Assert.True(created.TryGetValue(out var calibrated));
        var discretizer = Assert.IsType<EqualWidthDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);
        Assert.Equal(EqualWidthRange.PercentileP1P99, discretizer.Range);
        Assert.Equal([25, 50, 75], discretizer.Cuts);

        // The authored data-derived range authors no span, so vmin/vmax stay absent and the §14
        // fingerprint omits them (D-094).
        Assert.Null(discretizer.VMin);
        Assert.Null(discretizer.VMax);
    }

    [Fact]
    public void Create_WhenRoundingCollapsesCalibratedCuts_ThenCalibrationCutsInvalid() =>
        // The rounding-collapse case reaches the same owner: duplicate cuts are not ascending.
        Assert.Equal(
            DiagnosticCode.CalibrationCutsInvalid,
            Assert.Single(CalibratedSpec.Create(
                Resolve(PendingEqualWidthSpec(precision: RoundToPrecision.Create(1)), 1),
                [new CalibratedCuts("score", [1, 1, 2])]).Diagnostics).Code);

    [Fact]
    public void Create_WhenPendingEqualWidthOutcomeMissing_ThenThrows() =>
        // A leftover pending carrier is a calibrator-contract violation (programmer error), not a
        // data error — the calibrator is Create's only production caller (D-093).
        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(Resolve(PendingEqualWidthSpec(), 1), []));

    [Fact]
    public void Create_WhenPendingEqualWidthGivenWrongOutcomeKind_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PendingEqualWidthSpec(), 1), [new ObservedDomain("score", ["1"])]));

    [Fact]
    public void Create_WhenManualEqualWidthGivenAnOutcome_ThenThrows() =>
        // Spec-determined attributes need no outcome; supplying one means the calibrator
        // calibrated something it should not have.
        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(
            Resolve(With(SpecFixtures.EqualWidthManual("score", 0, 4, 0, 100, new NominalScale())), 1),
            [new CalibratedCuts("score", [25, 50, 75])]));

    [Fact]
    public void Create_WhenCalibratedCutsInputMutated_ThenSubstitutedCutsUnaffected()
    {
        var cuts = new List<double> { 25, 50, 75 };
        var result = CalibratedSpec.Create(Resolve(PendingEqualWidthSpec(), 1), [new CalibratedCuts("score", cuts)]);
        Assert.True(result.TryGetValue(out var calibrated));

        cuts[0] = 999; // the caller keeps its list

        var discretizer = Assert.IsType<EqualWidthDiscretizer>(calibrated!.Spec.Attributes[0].Discretizer);
        Assert.Equal([25.0, 50.0, 75.0], discretizer.Cuts);
        AssertImmutableList(discretizer.Cuts);
        AssertImmutableList(Assert.IsType<CalibratedCuts>(calibrated.Calibrations[0]).Cuts);
    }

    // --- value_groups passthrough substitution (M4 Slice E / D-090/D-104) -----

    private static BedrockSpec PassthroughSpec(params ValueGroup[] groups) =>
        With(SpecFixtures.ValueGroupsPassthrough(
            "edu", 0, new NominalScale(), groups.Length > 0 ? groups : [SpecFixtures.Group("School", "11th", "HS-grad")]));

    private static ValueGroupsDiscretizer SubstitutedPassthrough(Diagnosed<CalibratedSpec> result)
    {
        Assert.True(result.TryGetValue(out var calibrated));
        return Assert.IsType<ValueGroupsDiscretizer>(calibrated!.Spec.Attributes[0].Discretizer);
    }

    [Fact]
    public void Create_WhenPassthroughBinsOutcome_ThenPendingBecomesAnExecutableDiscretizer()
    {
        var result = CalibratedSpec.Create(
            Resolve(PassthroughSpec(), 1), [new PassthroughBins("edu", ["PhD", "Masters"])]);

        var discretizer = SubstitutedPassthrough(result);
        Assert.Equal(ValueGroupsUnmatched.Passthrough, discretizer.Unmatched);
        Assert.Equal(["PhD", "Masters"], discretizer.PassthroughBins);

        // The authored groups survive the substitution verbatim, and the discovered bins follow
        // them in discovery order (§17 rule 3).
        Assert.Equal(["School"], discretizer.Groups.Select(g => g.Label));
        Assert.Equal(["School", "PhD", "Masters"], discretizer.DescribeBins([]).Labels);
    }

    [Fact]
    public void Create_WhenPassthroughSubstituted_ThenNoCalibrationPendingRemains()
    {
        var result = CalibratedSpec.Create(Resolve(PassthroughSpec(), 1), [new PassthroughBins("edu", ["PhD"])]);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.All(calibrated!.Spec.Attributes, a => Assert.IsNotType<CalibrationPending>(a.Discretizer));
    }

    [Fact]
    public void Create_WhenPassthroughSubstituted_ThenTheOutcomeIsRetainedAndTokenSchemaPreserved()
    {
        var resolved = Resolve(PassthroughSpec(), 1);

        var result = CalibratedSpec.Create(resolved, [new PassthroughBins("edu", ["PhD"])]);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Same(resolved, calibrated!.Resolution);
        Assert.Same(resolved.Schema, calibrated.Schema);
        var retained = Assert.IsType<PassthroughBins>(Assert.Single(calibrated.Calibrations));
        Assert.Equal("edu", retained.AttributeName);
        Assert.Equal(["PhD"], retained.Values);
    }

    [Fact]
    public void Create_WhenPassthroughDiscoveredNothing_ThenTheEmptyOutcomeIsAValidCompletenessMarker()
    {
        // An empty PassthroughBins is the zero-discovery marker — every value matched a group. It
        // must substitute (not be treated as a missing outcome) and must be RETAINED, since the
        // freeze/manifest layer reads these outcomes and a dropped one would read as a skipped
        // calibration.
        var result = CalibratedSpec.Create(Resolve(PassthroughSpec(), 1), [new PassthroughBins("edu", [])]);

        var discretizer = SubstitutedPassthrough(result);
        Assert.Empty(discretizer.PassthroughBins);
        Assert.Equal(ValueGroupsUnmatched.Passthrough, discretizer.Unmatched);
        Assert.Empty(Assert.IsType<PassthroughBins>(Assert.Single(SubstitutedCalibrations(result))).Values);
    }

    private static IReadOnlyList<AttributeCalibration> SubstitutedCalibrations(Diagnosed<CalibratedSpec> result)
    {
        Assert.True(result.TryGetValue(out var calibrated));
        return calibrated!.Calibrations;
    }

    [Fact]
    public void Create_WhenPassthroughOutcomeMissing_ThenThrows() =>
        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(Resolve(PassthroughSpec(), 1), []));

    [Fact]
    public void Create_WhenPassthroughOutcomeIsWrongKind_ThenThrows() =>
        // A CalibratedCuts cannot resolve a passthrough carrier: kind-mismatched outcomes are a
        // calibrator-contract violation, not a data error (D-093).
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PassthroughSpec(), 1), [new CalibratedCuts("edu", [1.0])]));

    [Fact]
    public void Create_WhenPassthroughOutcomeNamesAnUnknownAttribute_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(PassthroughSpec(), 1), [new PassthroughBins("nope", ["PhD"])]));

    [Fact]
    public void Create_WhenTwoPassthroughOutcomesForOneAttribute_ThenThrows() =>
        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(
            Resolve(PassthroughSpec(), 1),
            [new PassthroughBins("edu", ["PhD"]), new PassthroughBins("edu", ["Masters"])]));

    [Fact]
    public void Create_WhenPassthroughOutcomeNamesAnExcludedAttribute_ThenThrows()
    {
        // Excluded attributes are parked config and never calibrate (D-049), so an outcome for one
        // means the calibrator observed something it should not have.
        var spec = With(SpecFixtures.ValueGroupsPassthrough("edu", 0, new NominalScale(), SpecFixtures.Group("School", "11th")),
            SpecFixtures.Excluded("parked", 1));

        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(spec, 2), [new PassthroughBins("edu", ["PhD"]), new PassthroughBins("parked", ["x"])]));
    }

    [Fact]
    public void Create_WhenPassthroughOutcomeGoesToAnAttributeThatNeedsNone_ThenThrows()
    {
        // A skip-policy value_groups attribute is spec-determined; handing it a PassthroughBins
        // means the calibrator mis-paired its outcomes.
        var spec = With(SpecFixtures.ValueGroups("edu", 0, ValueGroupsUnmatched.Skip, new NominalScale(), SpecFixtures.Group("School", "11th")));

        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(Resolve(spec, 1), [new PassthroughBins("edu", ["PhD"])]));
    }

    [Fact]
    public void FromFullyDeclared_WhenPassthroughPends_ThenThrowsThroughRequiresData() =>
        // Passthrough is data-dependent, so the no-data fast path must refuse it rather than
        // certify an uncalibrated schema (D-093).
        Assert.Throws<ArgumentException>(() => CalibratedSpec.FromFullyDeclared(Resolve(PassthroughSpec(), 1)));

    [Fact]
    public void FromFullyDeclared_WhenValueGroupsIsSkipOrOther_ThenStillAccepted()
    {
        // The complement of the rule above: skip/other value_groups are fully determined by the
        // spec text, so they must keep passing through the no-data path (§7).
        foreach (var unmatched in new[] { ValueGroupsUnmatched.Skip, ValueGroupsUnmatched.Other })
        {
            var spec = With(SpecFixtures.ValueGroups("edu", 0, unmatched, new NominalScale(), SpecFixtures.Group("School", "11th")));

            var calibrated = CalibratedSpec.FromFullyDeclared(Resolve(spec, 1));

            Assert.Empty(calibrated.Calibrations);
            Assert.IsType<ValueGroupsDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);
        }
    }

    [Fact]
    public void Create_WhenPassthroughBinsInputMutated_ThenSubstitutedBinsUnaffected()
    {
        var bins = new List<string> { "PhD" };
        var result = CalibratedSpec.Create(Resolve(PassthroughSpec(), 1), [new PassthroughBins("edu", bins)]);
        Assert.True(result.TryGetValue(out var calibrated));

        bins.Add("Masters"); // the caller keeps its list

        var discretizer = Assert.IsType<ValueGroupsDiscretizer>(calibrated!.Spec.Attributes[0].Discretizer);
        Assert.Equal(["PhD"], discretizer.PassthroughBins);
        AssertImmutableList(discretizer.PassthroughBins);
        AssertImmutableList(Assert.IsType<PassthroughBins>(calibrated.Calibrations[0]).Values);
    }

    // --- Immutability of the effective spec graph (D-098) ---

    [Fact]
    public void Create_WhenRetainedInputMutated_ThenEffectiveDomainUnaffected()
    {
        var domain = new List<string> { "b", "n" };
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.Nominal("g", 0, domain)]);
        var calibrated = CalibratedSpec.FromFullyDeclared(Resolve(spec, 1));

        domain.Add("mutated");

        Assert.Equal(["b", "n"], calibrated.Spec.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void Create_WhenGraphInspected_ThenNoReachableListIsACastableMutableArray()
    {
        // Every reachable IReadOnlyList on the effective graph is immutable-backed — not a T[]
        // or List<T> a caller could downcast and mutate (D-098 recursive immutability).
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
        [
            SpecFixtures.NumericCuts("age", 0, [30, 40], new NominalScale()),
            SpecFixtures.OrdinalValueBins("edu", 1, ["a", "b"],
                new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, ["a", "b"])),
        ]);
        var calibrated = CalibratedSpec.FromFullyDeclared(Resolve(spec, 2));

        foreach (var attribute in calibrated.Spec.Attributes)
        {
            if (attribute.DeclaredDomain is { } declaredDomain)
            {
                AssertImmutableList(declaredDomain);
            }

            AssertImmutableList(attribute.RestrictTo);
            switch (attribute.Discretizer)
            {
                case ManualCutsDiscretizer cuts:
                    AssertImmutableList(cuts.Cuts);
                    break;
                case OrderedCutsDiscretizer ordered:
                    AssertImmutableList(ordered.Cuts);
                    AssertImmutableList(ordered.Order);
                    break;
                case EqualWidthDiscretizer equalWidth:
                    AssertImmutableList(equalWidth.Cuts);
                    break;
                case EqualFrequencyDiscretizer equalFrequency:
                    AssertImmutableList(equalFrequency.Cuts);
                    break;
            }

            if (attribute.Scale is OrdinalScale { Order: { } order })
            {
                AssertImmutableList(order);
            }
        }
    }

    private static void AssertImmutableList<T>(IReadOnlyList<T> list)
    {
        Assert.False(list is T[], "a reachable list is a castable T[]");
        Assert.False(list is List<T>, "a reachable list is a castable List<T>");
        Assert.IsType<ImmutableArray<T>>(list);
    }

    // --- restrict_to through the calibrated-state boundary (§10.4/D-091/D-105) ---

    [Fact]
    public void RequiresData_WhenOnlyRestrictToIsPresent_ThenFalse()
    {
        // §7/D-065: restrictions never trigger calibration. Calibration and the column vocabulary
        // are computed over the INPUT UNIVERSE, before restrict_to selects objects — so a
        // restriction-only spec is still fully determined by its own text and skips Calibrate.
        var gene = SpecFixtures.Excluded("Gene", 0) with { RestrictTo = [new RestrictToValue("Bmp5")] };
        var tissue = SpecFixtures.Nominal("Tissue", 1, ["endoderm"]) with
        {
            RestrictTo = [new RestrictToValue("endoderm")],
        };

        Assert.False(CalibratedSpec.RequiresData(With(gene, tissue)));
    }

    [Fact]
    public void FromFullyDeclared_WhenAttributesRestrict_ThenTheEntriesArePreservedExactly()
    {
        // Calibration never consumes or rewrites restrict_to: authored order and duplicates
        // survive to the effective spec verbatim (only the fingerprint projects a sorted,
        // deduplicated view, §14).
        var age = SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale()) with
        {
            RestrictTo = [new RestrictToRange(10.0, 20.0), new RestrictToNumber(30.0), new RestrictToRange(10.0, 20.0)],
        };

        var calibrated = CalibratedSpec.FromFullyDeclared(Resolve(With(age), 1));

        Assert.Equal(
            [new RestrictToRange(10.0, 20.0), new RestrictToNumber(30.0), new RestrictToRange(10.0, 20.0)],
            calibrated.Spec.Attributes[0].RestrictTo);
    }

    [Fact]
    public void Create_WhenACalibratedAttributeAlsoRestricts_ThenTheEntriesSurviveTheSubstitution()
    {
        // The pending → executable substitution rebuilds the attribute; its restrict_to must ride
        // through untouched. An included-AND-restricted attribute is the case that proves it — the
        // substitution and the restriction live on the same attribute.
        var domainless = SpecFixtures.Nominal("g", 0, null) with
        {
            RestrictTo = [new RestrictToValue("b")],
        };

        var result = CalibratedSpec.Create(Resolve(With(domainless), 1), [new ObservedDomain("g", ["b", "c"])]);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal(["b", "c"], calibrated.Spec.Attributes[0].DeclaredDomain);      // substituted
        Assert.Equal([new RestrictToValue("b")], calibrated.Spec.Attributes[0].RestrictTo); // untouched
    }

    [Fact]
    public void FromFullyDeclared_WhenRestrictionsPresent_ThenTheirStorageIsNotCastableToAMutableList()
    {
        var age = SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale()) with
        {
            RestrictTo = [new RestrictToNumber(30.0)],
        };

        var calibrated = CalibratedSpec.FromFullyDeclared(Resolve(With(age), 1));

        AssertImmutableList(calibrated.Spec.Attributes[0].RestrictTo);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FromFullyDeclared_WhenANonFiniteExactValueSurvivedPastTheSeam_ThenThrows(double value)
    {
        // The calibrated-state contract's own numeric boundary (D-105): the last gate before
        // Plan/Emit/fingerprints. Unreachable through an honest chain — ResolvedSpec.Create is
        // the primary boundary and the only way to mint a token — so this is asserted through a
        // token whose entries were swapped afterwards, i.e. exactly the hand-built graph the
        // contract calls programmer error.
        var resolved = Resolve(With(SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale())), 1);
        var tampered = TamperRestrictions(resolved, [new RestrictToNumber(value)]);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.FromFullyDeclared(tampered));
    }

    [Fact]
    public void Create_WhenANonFiniteRangeBoundSurvivedPastTheSeam_ThenThrows()
    {
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, null)), 1);
        var tampered = TamperRestrictions(resolved, [new RestrictToRange(double.NegativeInfinity, 20.0)]);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(tampered, [new ObservedDomain("g", ["b"])]));
    }

    [Fact]
    public void FromFullyDeclared_WhenARestrictionEntryIsNull_ThenThrows()
    {
        // The boundary is exhaustive over the three recognized variants, not merely a finiteness
        // test: waving a null through would surface as a NullReferenceException inside the
        // emitter's matcher or the fingerprint encoder — far from the cause. Reject at the
        // boundary, trust the type inward (P-10).
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, ["b"])), 1);
        var tampered = TamperRestrictions(resolved, [null!]);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.FromFullyDeclared(tampered));
    }

    [Fact]
    public void FromFullyDeclared_WhenARestrictionEntryIsAnUnknownVariant_ThenThrows()
    {
        // RestrictToEntry is deliberately not mechanically closed (the Spec document model reuses
        // it, D-057), so an unknown variant is representable and must be rejected explicitly —
        // the matcher and the fingerprint encoder can only answer it with an "unreachable" throw.
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, ["b"])), 1);
        var tampered = TamperRestrictions(resolved, [new UnknownRestrictToEntry()]);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.FromFullyDeclared(tampered));
    }

    [Fact]
    public void FromFullyDeclared_WhenRestrictionEntriesAreValid_ThenTheBoundaryAcceptsAllThreeVariants()
    {
        // The positive half: the exhaustive check must not reject legitimate state.
        var age = SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale()) with
        {
            RestrictTo = [new RestrictToNumber(30.0), new RestrictToRange(10.0, 20.0), new RestrictToRange(null, null)],
        };
        var gene = SpecFixtures.Nominal("Gene", 1, ["Bmp5"]) with { RestrictTo = [new RestrictToValue("Bmp5")] };

        var calibrated = CalibratedSpec.FromFullyDeclared(Resolve(With(age, gene), 2));

        Assert.Equal(3, calibrated.Spec.Attributes[0].RestrictTo.Count);
        Assert.Single(calibrated.Spec.Attributes[1].RestrictTo);
    }

    private sealed record UnknownRestrictToEntry : RestrictToEntry;

    // Builds a token whose first attribute carries `entries`, bypassing the seam AND
    // ResolvedSpec.Create's own check — the only way to reach the calibrated-state boundary with
    // invalid numeric state, which is the point: it models a caller that hand-built the graph.
    private static ResolvedSpec TamperRestrictions(ResolvedSpec resolved, IReadOnlyList<RestrictToEntry> entries)
    {
        var attributes = resolved.Spec.Attributes.ToArray();
        attributes[0] = attributes[0] with { RestrictTo = entries };
        var spec = new BedrockSpec(resolved.Spec.Binding, attributes);

        // Reconstruct the token by reflection: Create would reject this graph (as it should), so
        // the private constructor is used to model state that only a corrupted chain could hold.
        var constructor = typeof(ResolvedSpec).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        return (ResolvedSpec)constructor.Invoke([spec, resolved.Schema, resolved.Settings]);
    }
}
