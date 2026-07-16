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
        // An absent-domain identity attribute is data-dependent; calling the fast path is a
        // mis-sequenced call that skipped the calibrator (D-093 programmer-error posture).
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, [])), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.FromFullyDeclared(resolved));
    }

    // --- RequiresData ---

    [Fact]
    public void RequiresData_WhenAbsentDomainIdentity_ThenTrue() =>
        Assert.True(CalibratedSpec.RequiresData(With(SpecFixtures.Nominal("g", 0, []))));

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
    public void Create_WhenAbsentDomainGivenObservedDomain_ThenEffectiveDomainIsObserved()
    {
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, [])), 1);

        Assert.True(CalibratedSpec.Create(resolved, [new ObservedDomain("g", ["b", "n"])]).TryGetValue(out var calibrated));
        Assert.Equal(["b", "n"], calibrated.Spec.Attributes[0].DeclaredDomain);
        Assert.IsType<ObservedDomain>(Assert.Single(calibrated.Calibrations));
    }

    [Fact]
    public void Create_WhenAbsentDomainWithEmptyObservedDomain_ThenLegalAndEmptyEffectiveDomain()
    {
        // An empty observed domain is legal (§10.1/§10.3): the attribute yields zero columns.
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, [])), 1);

        Assert.True(CalibratedSpec.Create(resolved, [new ObservedDomain("g", [])]).TryGetValue(out var calibrated));
        Assert.Empty(calibrated.Spec.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void Create_WhenAbsentDomainOutcomeMissing_ThenThrows()
    {
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, [])), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, []));
    }

    [Fact]
    public void Create_WhenAbsentDomainGivenIncludeAdditions_ThenThrows()
    {
        // ObservedDomain represents the complete population and never co-occurs with IncludeAdditions.
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, [])), 1);

        Assert.Throws<ArgumentException>(() => CalibratedSpec.Create(resolved, [new IncludeAdditions("g", ["b"])]));
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
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, [])), 1);

        Assert.Throws<ArgumentException>(() =>
            CalibratedSpec.Create(resolved, [new ObservedDomain("g", ["b"]), new ObservedDomain("g", ["n"])]));
    }

    [Fact]
    public void Create_WhenOutcomeNamesUnknownAttribute_ThenThrows()
    {
        var resolved = Resolve(With(SpecFixtures.Nominal("g", 0, [])), 1);

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
            SpecFixtures.Nominal("g", 1, []),
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
            AssertImmutableList(attribute.DeclaredDomain);
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
}
