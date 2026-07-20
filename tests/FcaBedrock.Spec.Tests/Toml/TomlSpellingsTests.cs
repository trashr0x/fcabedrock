using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Unit tests for the shared enum-spelling tables (D-075): every table maps both
/// directions consistently, so the reader and writer cannot drift (P-5).
/// </summary>
public sealed class TomlSpellingsTests
{
    [Fact]
    public void Tables_WhenRoundTrippedBothWays_ThenEveryEntryAgrees()
    {
        AssertBothWays(TomlSpellings.Shapes);
        AssertBothWays(TomlSpellings.Orderings);
        AssertBothWays(TomlSpellings.ObjectKeyModes);
        AssertBothWays(TomlSpellings.Aggregates);
        AssertBothWays(TomlSpellings.MissingPolicies);
        AssertBothWays(TomlSpellings.UnknownValuePolicies);
        AssertBothWays(TomlSpellings.DuplicateObjectPolicies);
        AssertBothWays(TomlSpellings.Directions);
        AssertBothWays(TomlSpellings.Boundaries);
        AssertBothWays(TomlSpellings.Ends);
        AssertBothWays(TomlSpellings.ValueTypes);
        AssertBothWays(TomlSpellings.LineEndingKinds);
    }

    [Fact]
    public void Tables_WhenComparedToEnums_ThenEveryMemberHasASpelling()
    {
        AssertCoversEnum<SourceShape>(TomlSpellings.Shapes);
        AssertCoversEnum<TripleOrdering>(TomlSpellings.Orderings);
        AssertCoversEnum<ObjectKeyMode>(TomlSpellings.ObjectKeyModes);
        AssertCoversEnum<CompositeAggregate>(TomlSpellings.Aggregates);
        AssertCoversEnum<MissingPolicy>(TomlSpellings.MissingPolicies);
        AssertCoversEnum<UnknownValuePolicy>(TomlSpellings.UnknownValuePolicies);
        AssertCoversEnum<DuplicateObjectPolicy>(TomlSpellings.DuplicateObjectPolicies);
        AssertCoversEnum<OrdinalDirection>(TomlSpellings.Directions);
        AssertCoversEnum<OrdinalBoundary>(TomlSpellings.Boundaries);
        AssertCoversEnum<BinEnds>(TomlSpellings.Ends);
        AssertCoversEnum<SourceValueType>(TomlSpellings.ValueTypes);
        AssertCoversEnum<LineEndings>(TomlSpellings.LineEndingKinds);
    }

    [Fact]
    public void TryParse_WhenAsAttribute_ThenMapsToTheD068Policy()
    {
        Assert.True(TomlSpellings.TryParse(TomlSpellings.MissingPolicies, "as_attribute", out var policy));
        Assert.Equal(MissingPolicy.AsAttribute, policy);
    }

    [Theory]
    [InlineData("Wide")]
    [InlineData("WIDE")]
    [InlineData("wid")]
    [InlineData("")]
    public void TryParse_WhenUnknownOrWrongCaseSpelling_ThenFalse(string text) =>
        Assert.False(TomlSpellings.TryParse(TomlSpellings.Shapes, text, out _));

    [Fact]
    public void Allowed_WhenTwoEntries_ThenOrJoined() =>
        Assert.Equal("\"wide\" or \"triple\"", TomlSpellings.Allowed(TomlSpellings.Shapes));

    [Fact]
    public void Allowed_WhenFourEntries_ThenCommaAndOrJoined() =>
        Assert.Equal(
            "\"skip\", \"warn\", \"fail\" or \"include\"",
            TomlSpellings.Allowed(TomlSpellings.UnknownValuePolicies));

    [Fact]
    public void ValueGroupsUnmatchedKinds_WhenSliceELanded_ThenTheTableEqualsTheCoreEnum() =>
        // The accepted TOML surface and the Core enum must not drift: value_groups accepts exactly
        // skip/other/passthrough (§11.6), all three executable as of Slice E (D-104) — unlike
        // equal_width's range, no spelling is modelled-but-unreachable here. The deferred-KIND set
        // this file used to pin retired with the same slice (D-070 complete, D-104): every v1
        // discretizer kind now has a carrier, so an unknown spelling is an ordinary
        // SpecFieldInvalid (tier 3) and there is no tier-2 set left to lock. IsIn's exact-ordinal
        // contract keeps its live owner in DeferredScaleKinds below.
        Assert.Equal(
            Enum.GetValues<ValueGroupsUnmatched>().Order(),
            TomlSpellings.ValueGroupsUnmatchedKinds.Select(u => u.Value).Order());

    [Fact]
    public void EqualWidthRanges_WhenSliceDLanded_ThenPercentileJoinedTheAcceptedSurface() =>
        // Slice C modelled percentile_p1_p99 in the Core enum but kept it out of the accepted TOML
        // surface until its calibration existed (D-102/G-8b). Slice D closes that gap, so the
        // spelling table now equals the enum — pinned here so neither can drift from the other.
        Assert.Equal(
            Enum.GetValues<EqualWidthRange>().Order(),
            TomlSpellings.EqualWidthRanges.Select(r => r.Value).Order());

    [Theory]
    [InlineData("interordinal", true)]
    [InlineData("biordinal", true)]
    [InlineData("contranominal", true)]
    [InlineData("ordinal", false)]
    public void IsIn_WhenProbingDeferredScaleKinds_ThenExactOrdinalMatchOnly(string kind, bool expected) =>
        Assert.Equal(expected, TomlSpellings.IsIn(TomlSpellings.DeferredScaleKinds, kind));

    // The D-075 deferred-surface sets are gone as of M6 Slice A (D-120): display_name and
    // formal_attribute_format have real carriers on all three owning tables, so there is
    // no set left to assert here. Their retirement is locked BEHAVIOURALLY instead — a
    // clean read of each naming key on each owner, in SpecReaderDiagnosticsTests — which
    // is the same substitution Slice E made when the deferred-discretizer set retired:
    // asserting a clean read, rather than the absence of a set that no longer exists, is
    // what keeps the lock able to fail.

    private static void AssertBothWays<T>((string Text, T Value)[] table)
        where T : struct
    {
        foreach (var (text, value) in table)
        {
            Assert.Equal(text, TomlSpellings.ToToml(table, value));
            Assert.True(TomlSpellings.TryParse(table, text, out var parsed));
            Assert.Equal(value, parsed);
        }
    }

    private static void AssertCoversEnum<T>((string Text, T Value)[] table)
        where T : struct, Enum
    {
        var members = Enum.GetValues<T>();
        Assert.Equal(members.Length, table.Length);
        foreach (var member in members)
        {
            Assert.Contains(table, entry => EqualityComparer<T>.Default.Equals(entry.Value, member));
        }
    }
}
