using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The three D-110 aggregate boundedness guards, each at its boundary and one step past it.
/// <para>
/// Two properties matter more than the arithmetic. First, <b>equality is legal</b> — a probe
/// that exactly fills a guard succeeds — because a "&gt;=" rule would fail runs that fit.
/// Second, a breach is a <b>hard failure with no draft</b>, never a quiet truncation of some
/// other attribute: only the per-attribute limit produces a usable, marked, truncated draft, so
/// aggregate pressure must never yield a partial draft that reads as complete.
/// </para>
/// </summary>
public sealed class ProbeGuardTests
{
    private static void AssertLimitExceeded(Diagnosed<SpecDocument> result)
    {
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(DiagnosticCode.ProbeLimitExceeded, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(result.HasErrors);
        Assert.False(result.TryGetValue(out _));
        Assert.Null(result.Value);
    }

    // --- Guard 1: maximum discovered attributes ------------------------------------

    [Fact]
    public async Task Probe_WhenColumnCountEqualsTheAttributeGuard_ThenSucceeds()
    {
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a,b,c\n1,2,3\n", options: ProbeOptions.Create(maxDiscoveredAttributes: 3));

        Assert.Equal(3, ProbeFixtures.Draft(result).Attributes.Count);
    }

    [Fact]
    public async Task Probe_WhenColumnCountExceedsTheAttributeGuard_ThenFailsWithNoDraft()
    {
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a,b,c,d\n1,2,3,4\n", options: ProbeOptions.Create(maxDiscoveredAttributes: 3));

        AssertLimitExceeded(result);
        Assert.Contains("would discover 4 attributes, above the maximum of 3", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenTheAttributeGuardIsBreached_ThenNoRecordIsEverRead()
    {
        // Guard 1 is answerable from the schema alone, so a runaway column count must fail
        // before paying for the record pass at all.
        var session = ProbeFixtures.Fake(new SourceSchema(4, ["a", "b", "c", "d"]), ["1", "2", "3", "4"]);

        var result = await Prober.ProbeAsync(
            session, ProbeFixtures.WideSettings(), ProbeOptions.Create(maxDiscoveredAttributes: 3));

        AssertLimitExceeded(result);
        Assert.Equal(0, session.RecordEnumerations);
    }

    // --- Guard 2: maximum total retained distinct values ---------------------------

    [Fact]
    public async Task Probe_WhenRetainedValuesEqualTheValueGuard_ThenSucceeds()
    {
        // Two columns x three distinct values = exactly 6 retained values.
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a,b\n1,4\n2,5\n3,6\n", options: ProbeOptions.Create(maxTotalRetainedValues: 6L));

        var draft = ProbeFixtures.Draft(result);
        Assert.Equal(["1", "2", "3"], draft.Attributes[0].DeclaredDomain);
        Assert.Equal(["4", "5", "6"], draft.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public async Task Probe_WhenRetainedValuesExceedTheValueGuard_ThenFailsWithNoDraft()
    {
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a,b\n1,4\n2,5\n3,6\n", options: ProbeOptions.Create(maxTotalRetainedValues: 5L));

        AssertLimitExceeded(result);
        Assert.Contains("maximum of 5 total retained distinct values", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenValuesRepeatAcrossRows_ThenOnlyRetentionsAreCharged()
    {
        // Duplicates cost nothing: a million repeats of two values charge two. Charging
        // observations instead of retentions would make the guard a row-count limit in disguise.
        var csv = "a\n" + string.Concat(Enumerable.Repeat("x\ny\n", 500));

        var result = await ProbeFixtures.ProbeCsvAsync(csv, options: ProbeOptions.Create(maxTotalRetainedValues: 2L));

        Assert.Equal(["x", "y"], ProbeFixtures.Draft(result).Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task Probe_WhenTheSameValueAppearsInTwoColumns_ThenItIsChargedOncePerColumn()
    {
        // No cross-attribute deduplication (D-110): each retaining attribute pays, because each
        // one really does retain its own copy in its own domain.
        var tooTight = await ProbeFixtures.ProbeCsvAsync(
            "a,b\nsame,same\n", options: ProbeOptions.Create(maxTotalRetainedValues: 1L));
        var exact = await ProbeFixtures.ProbeCsvAsync(
            "a,b\nsame,same\n", options: ProbeOptions.Create(maxTotalRetainedValues: 2L));

        AssertLimitExceeded(tooTight);
        Assert.Equal(2, ProbeFixtures.Draft(exact).Attributes.Count);
    }

    [Fact]
    public async Task Probe_WhenAnAttributeIsAlreadyTruncated_ThenItsDroppedTailIsNotCharged()
    {
        // A per-attribute-truncated tail is never retained, so it must never be charged either —
        // otherwise the per-attribute limit would silently consume the aggregate budget.
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a\nq\nr\ns\nt\nu\n",
            options: ProbeOptions.Create(valueRetentionLimit: 2, maxTotalRetainedValues: 2L));

        Assert.Equal(["q", "r"], ProbeFixtures.Draft(result).Attributes[0].DeclaredDomain);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
    }

    [Fact]
    public async Task Probe_WhenValuesAreMissing_ThenTheyAreNotCharged()
    {
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a\nx\n?\n?\ny\n", options: ProbeOptions.Create(maxTotalRetainedValues: 2L));

        Assert.Equal(["x", "y"], ProbeFixtures.Draft(result).Attributes[0].DeclaredDomain);
    }

    // --- Guard 3: maximum total retained value text --------------------------------

    [Fact]
    public async Task Probe_WhenRetainedTextEqualsTheTextGuard_ThenSucceeds()
    {
        // "abc" + "de" = 5 UTF-16 code units.
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a\nabc\nde\n", options: ProbeOptions.Create(maxTotalRetainedValueText: 5L));

        Assert.Equal(["abc", "de"], ProbeFixtures.Draft(result).Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task Probe_WhenRetainedTextExceedsTheTextGuard_ThenFailsWithNoDraft()
    {
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a\nabc\nde\n", options: ProbeOptions.Create(maxTotalRetainedValueText: 4L));

        AssertLimitExceeded(result);
        Assert.Contains("maximum of 4 total retained value text", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenOneValueIsEnormous_ThenTheTextGuardCatchesWhatTheValueGuardCannot()
    {
        // The pathology guard 2 is blind to: few values, each huge. One 5,000-character value
        // passes any sane value count and blows the text budget.
        var huge = new string('z', 5_000);
        var options = ProbeOptions.Create(maxTotalRetainedValues: 1_000L, maxTotalRetainedValueText: 4_999L);

        var result = await ProbeFixtures.ProbeCsvAsync($"a\n{huge}\n", options: options);

        AssertLimitExceeded(result);
        Assert.Contains("total retained value text", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenTextIsMeasuredInUtf16CodeUnits_ThenSurrogatePairsCountAsTwo()
    {
        // Code units, not runes and not UTF-8 bytes: the unit is fixed so the same input
        // measures the same everywhere. "\U0001F600" is one rune but two UTF-16 code units.
        var emoji = "\U0001F600";
        Assert.Equal(2, emoji.Length);

        var fits = await ProbeFixtures.ProbeCsvAsync(
            $"a\n{emoji}\n", options: ProbeOptions.Create(maxTotalRetainedValueText: 2L));
        var breaches = await ProbeFixtures.ProbeCsvAsync(
            $"a\n{emoji}\n", options: ProbeOptions.Create(maxTotalRetainedValueText: 1L));

        Assert.Equal([emoji], ProbeFixtures.Draft(fits).Attributes[0].DeclaredDomain);
        AssertLimitExceeded(breaches);
    }

    [Fact]
    public async Task Probe_WhenGuardsAreAtTheirMaximumValues_ThenChargingCannotOverflow()
    {
        // Compare-before-add: with long.MaxValue budgets, `total + increment` would be the only
        // way to overflow, and the engine never computes it. A plain success here is the proof
        // that the arithmetic is unreachable rather than merely unlikely.
        var options = ProbeOptions.Create(
            maxTotalRetainedValues: long.MaxValue, maxTotalRetainedValueText: long.MaxValue);

        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\ny\nz\n", options: options);

        Assert.Equal(["x", "y", "z"], ProbeFixtures.Draft(result).Attributes[0].DeclaredDomain);
    }

    // --- Precedence and the no-silent-truncation rule ------------------------------

    [Fact]
    public async Task Probe_WhenBothRecordGuardsWouldBreach_ThenTheValueGuardIsReportedFirst()
    {
        // Settled precedence: attributes, then values, then text (D-110). Without a fixed order
        // the reported cause would depend on evaluation accident. Both budgets are exhausted by
        // "x", so the retention of "yy" breaches BOTH — which is the only situation in which
        // precedence is observable at all.
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a\nx\nyy\n",
            options: ProbeOptions.Create(maxTotalRetainedValues: 1L, maxTotalRetainedValueText: 1L));

        AssertLimitExceeded(result);
        Assert.Contains("total retained distinct values", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenTheAttributeGuardAndAValueGuardWouldBothBreach_ThenAttributesWin()
    {
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a,b,c\n1,2,3\n",
            options: ProbeOptions.Create(maxDiscoveredAttributes: 1, maxTotalRetainedValues: 1L));

        AssertLimitExceeded(result);
        Assert.Contains("would discover 3 attributes", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenAGuardBreaches_ThenNoOtherAttributeIsSilentlyTruncated()
    {
        // The rule a partial draft would violate: under aggregate pressure probe produces NO
        // draft, so there is nothing to mistake for a complete schema. Only the per-attribute
        // limit yields a truncated-but-usable one.
        var result = await ProbeFixtures.ProbeCsvAsync(
            "a,b\n1,4\n2,5\n3,6\n", options: ProbeOptions.Create(maxTotalRetainedValues: 4L));

        AssertLimitExceeded(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
    }
}
