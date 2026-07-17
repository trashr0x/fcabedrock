using System.Collections.Immutable;
using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Calibration;

/// <summary>
/// The <c>value_groups</c> <c>unmatched = "passthrough"</c> pending carrier (§11.6, M4 Slice E /
/// D-090/D-104): the authored groups calibration carries, and the one state that can represent an
/// uncalibrated passthrough attribute — a state that can never plan or emit.
/// </summary>
public sealed class PendingValueGroupsPassthroughTests
{
    private static ValueGroup Group(string label, params string[] values) => ValueGroup.Create(label, values, null);

    private static CalibrationPending Pending(params ValueGroup[] groups) =>
        new(new PendingValueGroupsPassthrough(groups), CultureInfo.InvariantCulture);

    [Fact]
    public void Kind_WhenBuilt_ThenExactlyValueGroups() =>
        Assert.Equal("value_groups", new PendingValueGroupsPassthrough([Group("G", "a")]).Kind);

    [Fact]
    public void Groups_WhenBuilt_ThenCarriedInDeclarationOrder() =>
        // Declaration order is semantic (first match wins), so the carrier must preserve it into
        // calibration rather than treating the groups as a set.
        Assert.Equal(
            ["Beta", "Alpha"],
            new PendingValueGroupsPassthrough([Group("Beta", "b"), Group("Alpha", "a")]).Groups.Select(g => g.Label));

    [Fact]
    public void Create_WhenGroupsNull_ThenThrows() =>
        Assert.Throws<ArgumentNullException>(() => new PendingValueGroupsPassthrough(null!));

    [Fact]
    public void Create_WhenAGroupIsNull_ThenThrows() =>
        Assert.Throws<ArgumentNullException>(() => new PendingValueGroupsPassthrough([null!]));

    [Fact]
    public void Create_WhenNoGroups_ThenAcceptedBecauseValidityConstrainsEachGroupNotTheirCount() =>
        Assert.Empty(new PendingValueGroupsPassthrough([]).Groups);

    // --- Recursive immutability (D-098) --------------------------------------

    [Fact]
    public void Create_WhenCallerMutatesTheGroupListAfterwards_ThenTheCarrierIsUnaffected()
    {
        var groups = new List<ValueGroup> { Group("School", "11th") };
        var config = new PendingValueGroupsPassthrough(groups);

        groups.Add(Group("Undergrad", "Bachelors"));

        Assert.Equal(["School"], config.Groups.Select(g => g.Label));
    }

    [Fact]
    public void Create_WhenCallerMutatesAnInnerValuesListAfterwards_ThenTheCarrierIsUnaffected()
    {
        // The state is recursively immutable because each ValueGroup already snapshots its own
        // values — so the carrier does not have to (and must not need to) copy them again.
        var values = new List<string> { "11th" };
        var config = new PendingValueGroupsPassthrough([ValueGroup.Create("School", values, null)]);

        values.Add("HS-grad");

        Assert.Equal(["11th"], config.Groups[0].Values);
    }

    [Fact]
    public void Groups_WhenInspected_ThenNotCastableToAMutableCollection()
    {
        var groups = new PendingValueGroupsPassthrough([Group("School", "11th")]).Groups;

        Assert.IsNotType<ValueGroup[]>(groups);
        Assert.IsNotType<List<ValueGroup>>(groups);
        Assert.IsType<ImmutableArray<ValueGroup>>(groups);
    }

    // --- The carrier can never execute ---------------------------------------

    [Fact]
    public void CalibrationPending_WhenCarryingValueGroups_ThenItsKindIsTheConfigsKind() =>
        Assert.Equal("value_groups", Pending(Group("G", "a")).Kind);

    [Fact]
    public void CalibrationPending_WhenDiscretizeIsCalled_ThenThrowsBecauseCalibrationWasSkipped()
    {
        // No public path makes an uncalibrated passthrough executable — ValueGroupsDiscretizer.Create
        // rejects the policy outright — so reaching Discretize is a mis-sequenced call (D-093).
        var ex = Assert.Throws<InvalidOperationException>(() => Pending(Group("G", "a")).Discretize("x"));

        Assert.Contains("value_groups", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresData_WhenPassthroughPends_ThenTrue()
    {
        // The carrier is recognized through the existing CalibrationPending contract, so a
        // passthrough spec routes to the calibrator rather than to FromFullyDeclared (§7).
        var spec = new BedrockSpec(
            SpecFixtures.WideRowIndex(),
            [SpecFixtures.ValueGroupsPassthrough("g", 0, new NominalScale(), Group("G", "a"))]);

        Assert.True(CalibratedSpec.RequiresData(spec));
    }

    [Fact]
    public void RequiresData_WhenSkipOrOther_ThenFalseBecauseTheGroupsFixTheBins()
    {
        // The complement: only passthrough is data-dependent. skip/other are fully determined by
        // the spec text, so they must NOT drag an otherwise-declared spec into a data pass (§7).
        foreach (var unmatched in new[] { ValueGroupsUnmatched.Skip, ValueGroupsUnmatched.Other })
        {
            var spec = new BedrockSpec(
                SpecFixtures.WideRowIndex(),
                [SpecFixtures.ValueGroups("g", 0, unmatched, new NominalScale(), Group("G", "a"))]);

            Assert.False(CalibratedSpec.RequiresData(spec));
        }
    }
}
