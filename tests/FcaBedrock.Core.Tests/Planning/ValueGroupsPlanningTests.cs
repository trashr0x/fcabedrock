using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Planning;

/// <summary>
/// Planning <c>value_groups</c> (§11.6/§12.3/§17, D-090/D-104): ordinary <b>value-bin</b>
/// planning over group labels — not cut geometry and not the declared domain — under each scale.
/// <para>
/// Assertions are on canonical <b>identities and crossing incidence</b>, not rendered names
/// alone: a name is a display concern (P-15), whereas the identity and the crosses are what the
/// output actually means. Expectations are hand-derived from §12.3, never produced by calling
/// the scale under test.
/// </para>
/// </summary>
public sealed class ValueGroupsPlanningTests
{
    private static Diagnosed<ConversionPlan> Plan(BedrockSpec spec) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(SpecFixtures.Resolve(spec, new SourceSchema(1))));

    private static BedrockSpec Spec(ValueGroupsUnmatched unmatched, Scale scale) =>
        new(SpecFixtures.WideRowIndex(), [
            SpecFixtures.ValueGroups("edu", 0, unmatched, scale,
                SpecFixtures.Group("School", "11th", "HS-grad"),
                SpecFixtures.Group("Undergrad", "Bachelors"),
                SpecFixtures.Group("Postgrad", "Masters", "PhD")),
        ]);

    private static OrdinalScale Ordinal(
        IReadOnlyList<string>? order,
        OrdinalDirection direction = OrdinalDirection.Ge,
        OrdinalBoundary boundary = OrdinalBoundary.Inclusive,
        bool dropTop = false) => new(direction, dropTop, boundary, order);

    private static ConversionPlan Ok(Diagnosed<ConversionPlan> result)
    {
        Assert.True(result.TryGetValue(out var plan), string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return plan!;
    }

    // The (op, binKey) identity pairs the plan produced, in plan order — the canonical identity,
    // independent of how any of it renders.
    private static (string Op, string Bin)[] Identities(ConversionPlan plan) =>
        [.. plan.FormalAttributes.Select(a => (a.Identity.Operator, a.Identity.BinKey))];

    // The formal-attribute ids a given bin crosses.
    private static IReadOnlyList<int> CrossesOf(ConversionPlan plan, string bin) =>
        plan.Attributes[0].CrossesByBin.TryGetValue(bin, out var ids) ? ids : [];

    // --- nominal --------------------------------------------------------------

    [Fact]
    public void Plan_WhenNominalOverGroups_ThenOneColumnPerGroupInDeclarationOrder()
    {
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip, new NominalScale())));

        Assert.Equal([("", "School"), ("", "Undergrad"), ("", "Postgrad")], Identities(plan));
        Assert.Equal(["edu-School", "edu-Undergrad", "edu-Postgrad"], plan.FormalAttributes.Select(a => a.RenderedName));

        // Each group bin crosses exactly its own column.
        Assert.Equal([0], CrossesOf(plan, "School"));
        Assert.Equal([1], CrossesOf(plan, "Undergrad"));
        Assert.Equal([2], CrossesOf(plan, "Postgrad"));
    }

    [Fact]
    public void Plan_WhenNominalAndOther_ThenTheSyntheticOtherColumnIsLastAndCrossesOnlyItself()
    {
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Other, new NominalScale())));

        Assert.Equal([("", "School"), ("", "Undergrad"), ("", "Postgrad"), ("", "Other")], Identities(plan));
        Assert.Equal([3], CrossesOf(plan, "Other"));
    }

    // --- dichotomic -----------------------------------------------------------

    [Fact]
    public void Plan_WhenDichotomicOverGroups_ThenOneColumnCrossedByTheTrueGroupOnly()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.ValueGroups("edu", 0, ValueGroupsUnmatched.Skip, new DichotomicScale("Postgrad"),
                SpecFixtures.Group("School", "11th"), SpecFixtures.Group("Postgrad", "PhD")),
        ]);

        var plan = Ok(Plan(spec));

        // One column whose identity carries the dichotomic empty bin key (the attribute itself IS
        // the column — §12.2); the true group crosses it and the other group does not.
        var column = Assert.Single(plan.FormalAttributes);
        Assert.Equal(("", ""), (column.Identity.Operator, column.Identity.BinKey));
        Assert.Equal("dichotomic", column.Identity.Scale);
        Assert.Equal([0], CrossesOf(plan, "Postgrad"));
        Assert.Empty(CrossesOf(plan, "School")); // the false group crosses nothing
    }

    [Fact]
    public void Plan_WhenDichotomicTrueValueIsTheSyntheticOther_ThenTheOtherBinCrossesIt()
    {
        // `Other` is an ordinary bin once it exists, so it can be the dichotomic true value.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.ValueGroups("edu", 0, ValueGroupsUnmatched.Other, new DichotomicScale("Other"),
                SpecFixtures.Group("School", "11th")),
        ]);

        var plan = Ok(Plan(spec));

        Assert.Equal([0], CrossesOf(plan, "Other"));
        Assert.Empty(CrossesOf(plan, "School"));
    }

    // --- ordinal: the full-permutation requirement ----------------------------

    [Fact]
    public void Plan_WhenOrdinalOverGroupsWithAFullPermutation_ThenThresholdsFollowTheAuthoredOrder()
    {
        // §12.3: the order is over GROUP LABELS, and it — not declaration order — drives the
        // ordinal enumeration. Authoring School/Undergrad/Postgrad ascending with ge+inclusive
        // gives ≥School (tautological), ≥Undergrad, ≥Postgrad.
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip, Ordinal(["School", "Undergrad", "Postgrad"]))));

        Assert.Equal([(">=", "School"), (">=", "Undergrad"), (">=", "Postgrad")], Identities(plan));

        // ge+inclusive: order[i] crosses order[i..].
        Assert.Equal([0], CrossesOf(plan, "School"));
        Assert.Equal([0, 1], CrossesOf(plan, "Undergrad"));
        Assert.Equal([0, 1, 2], CrossesOf(plan, "Postgrad"));
    }

    [Fact]
    public void Plan_WhenOrdinalOrderIsNotDeclarationOrder_ThenTheAuthoredOrderWins()
    {
        // The discriminating case: a permutation that differs from declaration order must be
        // honoured, or the authored order would be silently ignored (the D-081 failure mode).
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip, Ordinal(["Postgrad", "Undergrad", "School"]))));

        Assert.Equal([(">=", "Postgrad"), (">=", "Undergrad"), (">=", "School")], Identities(plan));
        Assert.Equal([0, 1, 2], CrossesOf(plan, "School"));
        Assert.Equal([0], CrossesOf(plan, "Postgrad"));
    }

    [Fact]
    public void Plan_WhenOrdinalAndOther_ThenOtherMustBeInTheOrderAndItsPositionIsAuthored()
    {
        // §11.6/§12.3: the synthetic Other joins the permutation universe. Its ordinal POSITION is
        // whatever the author gives it — here first, not last — even though its BIN order (§17 r3)
        // is always after the declared groups. The two orders are independent, which is exactly
        // what this pins.
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Other, Ordinal(["Other", "School", "Undergrad", "Postgrad"]))));

        Assert.Equal([(">=", "Other"), (">=", "School"), (">=", "Undergrad"), (">=", "Postgrad")], Identities(plan));
        Assert.Equal([0], CrossesOf(plan, "Other"));
        Assert.Equal([0, 1, 2, 3], CrossesOf(plan, "Postgrad"));
    }

    [Fact]
    public void Plan_WhenOrdinalOrderMissingEntirely_ThenOrdinalOrderMissing()
    {
        // Unlike numeric free_per_value there is no natural order to derive: group labels are
        // strings, so §12.3 requires an explicit order — always.
        var result = Plan(Spec(ValueGroupsUnmatched.Skip, Ordinal(order: null)));

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.OrdinalOrderMissing, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Plan_WhenOneGroupLabelOmittedFromTheOrder_ThenOrdinalOrderMissingForThatLabel()
    {
        var result = Plan(Spec(ValueGroupsUnmatched.Skip, Ordinal(["School", "Undergrad"])));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.OrdinalOrderMissing, diagnostic.Code);
        Assert.Contains("Postgrad", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WhenOtherIsOmittedFromTheOrderUnderOther_ThenOrdinalOrderMissing()
    {
        // The Other-specific half of the permutation rule: a full permutation of the DECLARED
        // groups is still incomplete when the synthetic bin exists.
        var result = Plan(Spec(ValueGroupsUnmatched.Other, Ordinal(["School", "Undergrad", "Postgrad"])));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.OrdinalOrderMissing, diagnostic.Code);
        Assert.Contains("Other", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WhenOrderNamesSomethingThatIsNotAGroupLabel_ThenOrdinalOrderHasUnknownValue()
    {
        var result = Plan(Spec(ValueGroupsUnmatched.Skip, Ordinal(["School", "Undergrad", "Postgrad", "Doctorate"])));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.OrdinalOrderHasUnknownValue, diagnostic.Code);
        Assert.Contains("Doctorate", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WhenOrderNamesARawValueInsteadOfItsGroupLabel_ThenOrdinalOrderHasUnknownValue()
    {
        // §12.3: the order lists GROUP LABELS, never the raw values the groups match — an easy
        // authoring mistake that must not silently half-work.
        var result = Plan(Spec(ValueGroupsUnmatched.Skip, Ordinal(["11th", "Bachelors", "PhD"])));

        Assert.False(result.IsOk);
        Assert.Equal(3, result.Diagnostics.Count(d => d.Code == DiagnosticCode.OrdinalOrderHasUnknownValue));
        Assert.Equal(3, result.Diagnostics.Count(d => d.Code == DiagnosticCode.OrdinalOrderMissing));
    }

    // --- ordinal: direction × boundary over value bins -------------------------

    [Theory]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, ">=")]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Strict, ">")]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Inclusive, "<=")]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Strict, "<")]
    public void Plan_WhenOrdinalOverGroups_ThenAllFourDirectionBoundaryCombinationsAreLive(
        OrdinalDirection direction, OrdinalBoundary boundary, string op)
    {
        // §12.3: value bins have no half-open geometry, so all four combinations are well-defined
        // and `boundary` is fully live — unlike over cut bins, where the geometry fixes the
        // operator and the straddling pairs are invalid.
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip, Ordinal(["School", "Undergrad", "Postgrad"], direction, boundary))));

        Assert.All(plan.FormalAttributes, a => Assert.Equal(op, a.Identity.Operator));
    }

    [Fact]
    public void Plan_WhenGeStrict_ThenEachThresholdCrossesStrictlyAboveIt()
    {
        // ge+strict: order[i] crosses order[i+1..], so the LAST threshold is statically empty.
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip,
            Ordinal(["School", "Undergrad", "Postgrad"], OrdinalDirection.Ge, OrdinalBoundary.Strict))));

        Assert.Empty(CrossesOf(plan, "School"));
        Assert.Equal([0], CrossesOf(plan, "Undergrad"));
        Assert.Equal([0, 1], CrossesOf(plan, "Postgrad"));
    }

    [Fact]
    public void Plan_WhenLeInclusive_ThenEachThresholdCrossesAtOrBelowIt()
    {
        // le+inclusive: order[i] crosses order[..i+1].
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip,
            Ordinal(["School", "Undergrad", "Postgrad"], OrdinalDirection.Le, OrdinalBoundary.Inclusive))));

        Assert.Equal([0, 1, 2], CrossesOf(plan, "School"));
        Assert.Equal([1, 2], CrossesOf(plan, "Undergrad"));
        Assert.Equal([2], CrossesOf(plan, "Postgrad"));
    }

    [Fact]
    public void Plan_WhenLeStrict_ThenTheLowestThresholdIsStaticallyEmptyAndKept()
    {
        // le+strict: threshold order[i] means "< order[i]", so it is crossed by order[..i] —
        // "<School" (column 0) is crossed by nothing, since no group is below the lowest. §12.3
        // KEEPS that statically-empty column (an empty column is legal, §10.1) rather than
        // dropping it, which is why drop_top is a no-op under strict.
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip,
            Ordinal(["School", "Undergrad", "Postgrad"], OrdinalDirection.Le, OrdinalBoundary.Strict))));

        Assert.Equal([("<", "School"), ("<", "Undergrad"), ("<", "Postgrad")], Identities(plan));
        Assert.DoesNotContain(0, plan.Attributes[0].CrossesByBin.SelectMany(e => e.Value));

        Assert.Equal([1, 2], CrossesOf(plan, "School"));    // School < Undergrad, < Postgrad
        Assert.Equal([2], CrossesOf(plan, "Undergrad"));    // Undergrad < Postgrad
        Assert.Empty(CrossesOf(plan, "Postgrad"));          // nothing is above the highest
    }

    // --- ordinal: drop_top ----------------------------------------------------

    [Fact]
    public void Plan_WhenDropTopAndGeInclusive_ThenTheTautologicalLowestThresholdIsSuppressed()
    {
        // §12.3: over value bins drop_top removes the INCLUSIVE tautological threshold — for `ge`
        // that is the first order position (≥lowest is true for everything).
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip,
            Ordinal(["School", "Undergrad", "Postgrad"], OrdinalDirection.Ge, OrdinalBoundary.Inclusive, dropTop: true))));

        Assert.Equal([(">=", "Undergrad"), (">=", "Postgrad")], Identities(plan));
    }

    [Fact]
    public void Plan_WhenDropTopAndLeInclusive_ThenTheTautologicalHighestThresholdIsSuppressed()
    {
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip,
            Ordinal(["School", "Undergrad", "Postgrad"], OrdinalDirection.Le, OrdinalBoundary.Inclusive, dropTop: true))));

        Assert.Equal([("<=", "School"), ("<=", "Undergrad")], Identities(plan));
    }

    [Fact]
    public void Plan_WhenDropTopAndStrict_ThenItIsANoOpBecauseNoThresholdIsTautological()
    {
        // §12.3: under strict the extreme threshold is statically EMPTY rather than tautological,
        // so it is kept (an empty column is legal) and drop_top does nothing.
        var plan = Ok(Plan(Spec(ValueGroupsUnmatched.Skip,
            Ordinal(["School", "Undergrad", "Postgrad"], OrdinalDirection.Ge, OrdinalBoundary.Strict, dropTop: true))));

        Assert.Equal([(">", "School"), (">", "Undergrad"), (">", "Postgrad")], Identities(plan));
    }

    // --- Passthrough collision (D-090) ----------------------------------------

    [Fact]
    public void Plan_WhenADiscoveredPassthroughBinEqualsAnAuthoredGroupLabel_ThenFormalAttributeCollision()
    {
        // §11.6/D-090: a pass-through value merely OBSERVED to equal an authored label is
        // data-dependent, so it surfaces here at plan — not as a static ValueGroupsLabelDuplicate.
        // The observed bin is forced to equal the authored label, which is what makes this a real
        // collision rather than an assertion about nothing.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.ValueGroupsPassthrough("edu", 0, new NominalScale(), SpecFixtures.Group("School", "11th")),
        ]);
        var calibrated = CalibratedSpec.Create(
            SpecFixtures.Resolve(spec, new SourceSchema(1)), [new PassthroughBins("edu", ["School"])]);
        Assert.True(calibrated.TryGetValue(out var state));

        var result = ConversionPlanner.Plan(state!);

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeCollision);
    }

    [Fact]
    public void Plan_WhenPassthroughDiscoversDistinctBins_ThenTheyBecomeColumnsAfterTheGroups()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.ValueGroupsPassthrough("edu", 0, new NominalScale(), SpecFixtures.Group("School", "11th")),
        ]);
        var calibrated = CalibratedSpec.Create(
            SpecFixtures.Resolve(spec, new SourceSchema(1)), [new PassthroughBins("edu", ["PhD", "Masters"])]);
        Assert.True(calibrated.TryGetValue(out var state));

        var plan = Ok(ConversionPlanner.Plan(state!));

        Assert.Equal([("", "School"), ("", "PhD"), ("", "Masters")], Identities(plan));
    }

    // --- value_labels stays dormant (D-049/D-055) ------------------------------

    [Fact]
    public void Plan_WhenValueLabelsAuthoredUnderValueGroups_ThenDormantInNamingAndNeverAnError()
    {
        // §10.8/D-049: value_groups does not consult value_labels — a group label already IS the
        // display label — so an authored map is inert: it must not rename a column and must not
        // fail validation, even when a key matches a group label exactly.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            new AttributeSpec("edu", new ColumnSource(0, SourceValueType.String), Include: true,
                ValueGroupsDiscretizer.Create([SpecFixtures.Group("School", "11th")], ValueGroupsUnmatched.Skip),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [],
                new Dictionary<string, string> { ["School"] = "Secondary" },
                MissingPolicy.Skip, UnknownValuePolicy.Warn),
        ]);

        var plan = Ok(Plan(spec));

        Assert.Equal("edu-School", Assert.Single(plan.FormalAttributes).RenderedName);
    }
}
