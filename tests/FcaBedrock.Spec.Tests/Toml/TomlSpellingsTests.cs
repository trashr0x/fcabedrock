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

    [Theory]
    [InlineData("equal_frequency", true)]
    [InlineData("value_groups", true)]
    [InlineData("free_per_value", false)] // left the deferred set at M4 Slice B (D-101)
    [InlineData("equal_width", false)]    // left the deferred set at M4 Slice C (D-102)
    [InlineData("identity", false)]
    [InlineData("Equal_Frequency", false)]
    [InlineData("equal_frequenc", false)]
    public void IsIn_WhenProbingDeferredDiscretizerKinds_ThenExactOrdinalMatchOnly(string kind, bool expected) =>
        Assert.Equal(expected, TomlSpellings.IsIn(TomlSpellings.DeferredDiscretizerKinds, kind));

    [Fact]
    public void DeferredDiscretizerKinds_WhenSliceCLanded_ThenExactlyEqualFrequencyAndValueGroups() =>
        // The transitional set narrows kind by kind (D-070) and the member retires with the last
        // one. Pinning the whole set — not just membership — is what makes an accidental
        // re-deferral or an early retirement visible (D-102).
        Assert.Equal(["equal_frequency", "value_groups"], TomlSpellings.DeferredDiscretizerKinds);

    [Theory]
    [InlineData("interordinal", true)]
    [InlineData("biordinal", true)]
    [InlineData("contranominal", true)]
    [InlineData("ordinal", false)]
    public void IsIn_WhenProbingDeferredScaleKinds_ThenExactOrdinalMatchOnly(string kind, bool expected) =>
        Assert.Equal(expected, TomlSpellings.IsIn(TomlSpellings.DeferredScaleKinds, kind));

    [Fact]
    public void DeferredSurfaceSets_WhenInspected_ThenClosedPerOwningTable()
    {
        // D-075: the sets are exact and per-table — the transitional reject must
        // never absorb typo-like unknown keys or a listed name in another table.
        // Slice F retired extends/template/matcher (D-078); the remaining
        // entries belong to the naming-fidelity slice.
        Assert.Equal(["formal_attribute_format"], TomlSpellings.DefaultsDeferredKeys);
        Assert.Equal(["display_name", "formal_attribute_format"], TomlSpellings.AttributeDeferredKeys);
    }

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
