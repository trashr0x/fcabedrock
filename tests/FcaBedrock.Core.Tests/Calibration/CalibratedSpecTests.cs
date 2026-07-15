using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

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
