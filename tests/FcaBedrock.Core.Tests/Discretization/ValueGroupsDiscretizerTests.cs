using System.Collections.Immutable;
using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Tests.Discretization;

/// <summary>
/// The <c>value_groups</c> discretizer contract (§11.6, D-055/D-090): first-match-wins
/// classification, the three unmatched policies, bin order (§17 rule 3), and the construction
/// backstops that keep passthrough and duplicate labels unrepresentable.
/// </summary>
public sealed class ValueGroupsDiscretizerTests
{
    private static ValueGroup Group(string label, params string[] values) => ValueGroup.Create(label, values, null);

    private static ValueGroupsDiscretizer Education(ValueGroupsUnmatched unmatched) =>
        ValueGroupsDiscretizer.Create(
            [Group("School", "11th", "HS-grad"), Group("Undergrad", "Bachelors")], unmatched);

    private static string? BinOf(ValueGroupsDiscretizer discretizer, string raw) =>
        discretizer.Discretize(raw).TryGetLabel(out var label) ? label : null;

    // --- Kind and the dormant-config defaults --------------------------------

    [Fact]
    public void Kind_WhenInspected_ThenValueGroups() =>
        Assert.Equal("value_groups", Education(ValueGroupsUnmatched.Skip).Kind);

    [Fact]
    public void DescribeBins_WhenInspected_ThenOrdinaryValueBinGeometryNotCutGeometry()
    {
        // §11.6: group labels are VALUE bins — there is no cut geometry, so an ordinal scale
        // thresholds on scale.order rather than on edges, and there are no open ends.
        var scheme = Education(ValueGroupsUnmatched.Other).DescribeBins([]);

        Assert.False(scheme.CutBins);
        Assert.False(scheme.OpenLow);
        Assert.False(scheme.OpenHigh);
        Assert.Equal(["School", "Undergrad", "Other"], scheme.Labels);
        Assert.Equal(scheme.Labels, scheme.Thresholds);
    }

    [Fact]
    public void BinLabels_WhenDeclaredDomainAuthored_ThenIgnoredEntirely() =>
        // D-055: value_groups does not consult declared_domain — the groups plus the unmatched
        // policy are the whole bin universe, so a (dormant) domain cannot perturb it.
        Assert.Equal(
            ["School", "Undergrad"],
            Education(ValueGroupsUnmatched.Skip).DescribeBins(["wildly", "unrelated", "values"]).Labels);

    // --- Bin order (§17 rule 3) ----------------------------------------------

    [Fact]
    public void BinLabels_WhenSkip_ThenDeclaredGroupsInDeclarationOrder() =>
        Assert.Equal(["School", "Undergrad"], Education(ValueGroupsUnmatched.Skip).DescribeBins([]).Labels);

    [Fact]
    public void BinLabels_WhenOther_ThenSyntheticOtherIsOrderedAfterAllDeclaredGroups() =>
        Assert.Equal(["School", "Undergrad", "Other"], Education(ValueGroupsUnmatched.Other).DescribeBins([]).Labels);

    // --- Classification: first match wins ------------------------------------

    [Fact]
    public void Discretize_WhenValueMatchesOneGroup_ThenThatGroupsLabel()
    {
        var discretizer = Education(ValueGroupsUnmatched.Skip);

        Assert.Equal("School", BinOf(discretizer, "HS-grad"));
        Assert.Equal("Undergrad", BinOf(discretizer, "Bachelors"));
    }

    [Fact]
    public void Discretize_WhenValueMatchesSeveralGroups_ThenTheFirstDeclaredWins()
    {
        // §11.6: "a value matching multiple groups falls into the first matching group in
        // declaration order". Both groups claim "Bachelors"; declaration order decides — and
        // reversing the declaration reverses the outcome, which is what proves order is read
        // rather than, say, label order.
        var firstWins = ValueGroupsDiscretizer.Create(
            [Group("Alpha", "Bachelors"), Group("Beta", "Bachelors")], ValueGroupsUnmatched.Skip);
        var reversed = ValueGroupsDiscretizer.Create(
            [Group("Beta", "Bachelors"), Group("Alpha", "Bachelors")], ValueGroupsUnmatched.Skip);

        Assert.Equal("Alpha", BinOf(firstWins, "Bachelors"));
        Assert.Equal("Beta", BinOf(reversed, "Bachelors"));
    }

    [Fact]
    public void Discretize_WhenPatternGroupPrecedesValueGroup_ThenTheEarlierPatternStillWins()
    {
        // First-match is by declaration position, not by matcher kind: a pattern group declared
        // first beats an explicit-value group declared later, even though the later one is an
        // exact hit.
        var discretizer = ValueGroupsDiscretizer.Create(
            [ValueGroup.Create("Cardiac", null, "^I[0-9]{2}"), Group("Exact", "I21")], ValueGroupsUnmatched.Skip);

        Assert.Equal("Cardiac", BinOf(discretizer, "I21"));
    }

    // --- Unmatched policies --------------------------------------------------

    [Fact]
    public void Discretize_WhenSkipAndUnmatched_ThenUnknownCarryingTheRawValue()
    {
        // §11.6: no bin — unknown_value_policy governs it. Carrying the raw value is what lets the
        // emitter's aggregate sample it.
        var result = Education(ValueGroupsUnmatched.Skip).Discretize("PhD");

        Assert.Equal(BinOutcome.Unknown, result.Outcome);
        Assert.Equal("PhD", result.Value);
    }

    [Fact]
    public void Discretize_WhenOtherAndUnmatched_ThenTheSyntheticOtherBin() =>
        Assert.Equal("Other", BinOf(Education(ValueGroupsUnmatched.Other), "PhD"));

    [Fact]
    public void Discretize_WhenPassthroughAndUnmatched_ThenTheRawValueIsItsOwnBin() =>
        Assert.Equal(
            "PhD",
            BinOf(ValueGroupsDiscretizer.CreatePassthrough([Group("School", "11th")], ["PhD"]), "PhD"));

    [Fact]
    public void Discretize_WhenPassthroughAndValueIsGrouped_ThenTheGroupStillWins() =>
        Assert.Equal(
            "School",
            BinOf(ValueGroupsDiscretizer.CreatePassthrough([Group("School", "11th")], ["PhD"]), "11th"));

    // --- Calibrated passthrough form -----------------------------------------

    [Fact]
    public void CreatePassthrough_WhenBinsDiscovered_ThenTheyFollowTheDeclaredGroupsInDiscoveryOrder() =>
        // §17 rule 3: declared groups first, then discovered bins in first-observation order —
        // NOT sorted, so an out-of-alphabetical discovery order must survive verbatim.
        Assert.Equal(
            ["School", "zeta", "alpha"],
            ValueGroupsDiscretizer.CreatePassthrough([Group("School", "11th")], ["zeta", "alpha"])
                .DescribeBins([]).Labels);

    [Fact]
    public void CreatePassthrough_WhenNoBinsDiscovered_ThenOnlyTheDeclaredGroupsAndAnEmptyBinSet()
    {
        // The zero-discovery outcome is legal, not an error: every value matched a group.
        var discretizer = ValueGroupsDiscretizer.CreatePassthrough([Group("School", "11th")], []);

        Assert.Empty(discretizer.PassthroughBins);
        Assert.Equal(["School"], discretizer.DescribeBins([]).Labels);
        Assert.Equal(ValueGroupsUnmatched.Passthrough, discretizer.Unmatched);
    }

    [Fact]
    public void CreatePassthrough_WhenADiscoveredBinEqualsAnAuthoredLabel_ThenAcceptedHereForPlanToDiagnose()
    {
        // D-090: that collision is DATA-dependent, so it belongs to plan (FormalAttributeCollision),
        // not to a construction backstop — construction must not pre-empt it.
        var discretizer = ValueGroupsDiscretizer.CreatePassthrough([Group("School", "11th")], ["School"]);

        Assert.Equal(["School", "School"], discretizer.DescribeBins([]).Labels);
    }

    [Fact]
    public void PassthroughBins_WhenSkipOrOther_ThenEmpty()
    {
        Assert.Empty(Education(ValueGroupsUnmatched.Skip).PassthroughBins);
        Assert.Empty(Education(ValueGroupsUnmatched.Other).PassthroughBins);
    }

    // --- Construction backstops (P-10) ---------------------------------------

    [Fact]
    public void Create_WhenPassthrough_ThenThrowsBecauseItIsDataDependent() =>
        // The public factory cannot mint a passthrough discretizer: its bins come from calibration,
        // so it must travel as CalibrationPending and be substituted by CalibratedSpec.Create
        // (D-093). This is what makes "passthrough that skipped calibration" unrepresentable.
        Assert.Throws<ArgumentException>(() =>
            ValueGroupsDiscretizer.Create([Group("School", "11th")], ValueGroupsUnmatched.Passthrough));

    [Fact]
    public void Create_WhenUnmatchedIsUndefined_ThenThrows() =>
        // No undefined enum value may reach matching, planning, emission, or fingerprints.
        Assert.Throws<ArgumentException>(() =>
            ValueGroupsDiscretizer.Create([Group("School", "11th")], (ValueGroupsUnmatched)99));

    [Fact]
    public void Create_WhenDuplicateLabels_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            ValueGroupsDiscretizer.Create([Group("School", "11th"), Group("School", "Bachelors")], ValueGroupsUnmatched.Skip));

    [Fact]
    public void Create_WhenLabelCollidesWithSyntheticOtherUnderOther_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            ValueGroupsDiscretizer.Create([Group("Other", "11th")], ValueGroupsUnmatched.Other));

    [Fact]
    public void Create_WhenLabelIsOtherButPolicyIsSkip_ThenAllowedBecauseNoSyntheticBinExists() =>
        // The collision is with the SYNTHETIC bin, which only `other` adds — so "Other" is an
        // ordinary label under skip.
        Assert.Equal(["Other"], ValueGroupsDiscretizer.Create([Group("Other", "11th")], ValueGroupsUnmatched.Skip)
            .DescribeBins([]).Labels);

    [Fact]
    public void Create_WhenLabelIsLowercaseOtherUnderOther_ThenAllowedBecauseComparisonIsOrdinal() =>
        // P-12: ordinal, so "other" does not collide with the synthetic "Other".
        Assert.Equal(
            ["other", "Other"],
            ValueGroupsDiscretizer.Create([Group("other", "11th")], ValueGroupsUnmatched.Other).DescribeBins([]).Labels);

    [Fact]
    public void Create_WhenGroupsNull_ThenThrows() =>
        Assert.Throws<ArgumentNullException>(() => ValueGroupsDiscretizer.Create(null!, ValueGroupsUnmatched.Skip));

    [Fact]
    public void Create_WhenNoGroups_ThenAllowedBecauseTheContractConstrainsGroupsNotTheirCount()
    {
        // D-090/G-11 make each authored group and its matcher the unit of validity; there is no
        // non-empty-groups rule, and an empty group list is a coherent (if degenerate) spec —
        // under `other` every value bins to Other.
        var discretizer = ValueGroupsDiscretizer.Create([], ValueGroupsUnmatched.Other);

        Assert.Equal(["Other"], discretizer.DescribeBins([]).Labels);
        Assert.Equal("Other", BinOf(discretizer, "anything"));
    }

    // --- Mutation isolation (D-098) ------------------------------------------

    [Fact]
    public void Create_WhenCallerMutatesTheGroupListAfterwards_ThenTheDiscretizerIsUnaffected()
    {
        var groups = new List<ValueGroup> { Group("School", "11th") };
        var discretizer = ValueGroupsDiscretizer.Create(groups, ValueGroupsUnmatched.Skip);

        groups.Add(Group("Undergrad", "Bachelors"));

        Assert.Equal(["School"], discretizer.DescribeBins([]).Labels);
        Assert.Equal(BinOutcome.Unknown, discretizer.Discretize("Bachelors").Outcome);
    }

    [Fact]
    public void CreatePassthrough_WhenCallerMutatesTheBinListAfterwards_ThenTheDiscretizerIsUnaffected()
    {
        var bins = new List<string> { "PhD" };
        var discretizer = ValueGroupsDiscretizer.CreatePassthrough([Group("School", "11th")], bins);

        bins.Add("Masters");

        Assert.Equal(["PhD"], discretizer.PassthroughBins);
    }

    [Fact]
    public void PublicLists_WhenInspected_ThenNotCastableToMutableCollections()
    {
        var discretizer = ValueGroupsDiscretizer.CreatePassthrough([Group("School", "11th")], ["PhD"]);

        Assert.IsNotType<ValueGroup[]>(discretizer.Groups);
        Assert.IsNotType<List<ValueGroup>>(discretizer.Groups);
        Assert.IsType<ImmutableArray<ValueGroup>>(discretizer.Groups);
        Assert.IsNotType<string[]>(discretizer.PassthroughBins);
        Assert.IsNotType<List<string>>(discretizer.PassthroughBins);
        Assert.IsType<ImmutableArray<string>>(discretizer.PassthroughBins);
    }
}
