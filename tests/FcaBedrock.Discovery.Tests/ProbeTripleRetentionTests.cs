using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// D-108 retention and D-110 boundedness for the triple shape.
/// <para>
/// The retention semantics are shape-independent by construction — one <c>RetainedDomain</c>, one
/// budget, shared by both engines — so this suite is not a second copy of the wide arithmetic. It
/// pins the parts that only triple can express: the attribute guard charged as predicates are
/// <b>discovered</b> rather than counted from a schema, and the guard precedence at the moment a
/// brand-new predicate arrives carrying a brand-new value.
/// </para>
/// </summary>
public sealed class ProbeTripleRetentionTests
{
    private static readonly SourceReadSettings Settings = TripleProbeFixtures.TripleSettings();

    private static void AssertLimitExceeded(Diagnosed<SpecDocument> result)
    {
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(DiagnosticCode.ProbeLimitExceeded, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(result.HasErrors);
        Assert.False(result.TryGetValue(out _));
        Assert.Null(result.Value);
    }

    // --- Per-attribute retention: below, at, and above the limit ------------------------------

    [Fact]
    public async Task ProbeTriple_WhenDomainsFitTheLimit_ThenTheyAreCompleteAndUnmarked()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,a\ns,p,b\ns,q,c\n", options: ProbeOptions.Create(valueRetentionLimit: 2));
        var draft = ProbeFixtures.Draft(result);

        // Exactly at the limit is NOT truncation (D-108's strictly-greater rule).
        Assert.Empty(result.Diagnostics);
        Assert.Equal(["a", "b"], draft.Attributes[0].DeclaredDomain);
        Assert.All(draft.Attributes, a =>
        {
            Assert.Null(a.Description);
            Assert.Null(a.UnknownValuePolicy);
        });
    }

    [Fact]
    public async Task ProbeTriple_WhenADomainExceedsTheLimit_ThenItAuthorsThePrefixIncludeAndMarker()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,a\ns,p,b\ns,p,c\ns,q,z\n", options: ProbeOptions.Create(valueRetentionLimit: 2));
        var draft = ProbeFixtures.Draft(result);

        var truncatedAttribute = draft.Attributes[0];
        Assert.Equal(["a", "b"], truncatedAttribute.DeclaredDomain);
        Assert.Equal(UnknownValuePolicy.Include, truncatedAttribute.UnknownValuePolicy);
        Assert.Equal(ProbeDraftExpectations.MarkerFor(2), truncatedAttribute.Description);

        // The untouched sibling stays untouched: truncation is per attribute.
        Assert.Equal(["z"], draft.Attributes[1].DeclaredDomain);
        Assert.Null(draft.Attributes[1].UnknownValuePolicy);

        Assert.Equal(
            ProbeDraftExpectations.NotesFor(2, truncatedAttributes: 1),
            draft.Provenance!.Notes);
    }

    [Fact]
    public async Task ProbeTriple_WhenDomainsAreTruncated_ThenOneAggregatedWarningNamesThemInOrder()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p1,a\ns,p1,b\ns,p2,a\ns,p2,b\ns,p3,x\ns,p4,a\ns,p4,b\n",
            options: ProbeOptions.Create(valueRetentionLimit: 1));

        var diagnostic = Assert.Single(
            result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("truncated 3 attribute domain(s)", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("p1, p2, p4", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenNothingIsTruncated_ThenTheNotesStillRecordTheLimitAndZero()
    {
        // Written even at zero, so every draft explains which limit produced it (D-108).
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync("s,p,a\n"));

        Assert.Equal(
            ProbeDraftExpectations.NotesFor(ProbeOptions.Default.ValueRetentionLimit, 0),
            draft.Provenance!.Notes);
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateHasOnlyMissingValues_ThenItIsNeitherTruncatedNorMarked()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,?\ns,p,?\n", options: ProbeOptions.Create(valueRetentionLimit: 1));
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Null(Assert.Single(draft.Attributes).DeclaredDomain);
        Assert.Null(draft.Attributes[0].Description);
    }

    // --- Guard 1, charged as predicates are discovered ----------------------------------------

    [Fact]
    public async Task ProbeTriple_WhenPredicateCountEqualsTheAttributeGuard_ThenSucceeds()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,1\ns,q,2\ns,r,3\n", options: ProbeOptions.Create(maxDiscoveredAttributes: 3));

        Assert.Equal(3, ProbeFixtures.Draft(result).Attributes.Count);
    }

    [Fact]
    public async Task ProbeTriple_WhenAFurtherPredicateAppears_ThenTheAttributeGuardBreaches()
    {
        // Unlike wide, this cannot be decided from the schema: the vocabulary is only known as it
        // is read, so the guard is charged at discovery and fails on the first predicate past it.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,1\ns,q,2\ns,r,3\ns,t,4\n", options: ProbeOptions.Create(maxDiscoveredAttributes: 3));

        AssertLimitExceeded(result);
        Assert.Contains(
            "would discover 4 attributes, above the maximum of 3",
            result.Diagnostics[0].Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheAttributeGuardBreaches_ThenTheProbeStopsImmediately()
    {
        // Reading on would retain nothing more and produce nothing usable, so the pass ends at
        // the offending row rather than draining the source.
        var session = TripleProbeFixtures.Fake(
            ("s", "p", "1"), ("s", "q", "2"), ("s", "r", "3"), ("s", "t", "4"), ("s", "u", "5"));

        var result = await Prober.ProbeTripleAsync(
            session, Settings, null, ProbeOptions.Create(maxDiscoveredAttributes: 3));

        AssertLimitExceeded(result);
        Assert.Equal(4, session.RowsYielded);
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateOnlyRepeats_ThenItIsChargedOnce()
    {
        // The guard counts DISTINCT predicates, so a source that mentions three predicates a
        // thousand times each still discovers three attributes.
        var rows = string.Concat(Enumerable.Repeat("s,p,1\ns,q,2\ns,r,3\n", 200));

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            rows, options: ProbeOptions.Create(maxDiscoveredAttributes: 3));

        Assert.Equal(3, ProbeFixtures.Draft(result).Attributes.Count);
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateIsFallbackNamed_ThenItStillCountsTowardTheGuard()
    {
        // An unusable predicate still authors an attribute (D-107), so it is charged like any
        // other. "Usable predicates only" would under-count exactly the pathological vocabularies
        // the guard exists for.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,1\ns,\"q\"\"x\",2\ns,r,3\n", options: ProbeOptions.Create(maxDiscoveredAttributes: 2));

        AssertLimitExceeded(result);
        Assert.Contains("would discover 3 attributes", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateIsIgnored_ThenItDoesNotCountTowardTheGuard()
    {
        // Missing predicates name no attribute, so they cannot consume the attribute budget.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,?,1\ns,p,2\ns,,3\ns,q,4\n", options: ProbeOptions.Create(maxDiscoveredAttributes: 2));

        Assert.Equal(["p", "q"], ProbeFixtures.Draft(result).Attributes.Select(a => a.Name));
    }

    // --- Guards 2 and 3, and precedence -------------------------------------------------------

    [Fact]
    public async Task ProbeTriple_WhenRetainedValuesEqualTheValueGuard_ThenSucceeds()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,1\ns,p,2\ns,q,3\n", options: ProbeOptions.Create(maxTotalRetainedValues: 3L));

        Assert.Equal(["1", "2"], ProbeFixtures.Draft(result).Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenRetainedValuesExceedTheValueGuard_ThenFailsWithNoDraft()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,1\ns,p,2\ns,q,3\n", options: ProbeOptions.Create(maxTotalRetainedValues: 2L));

        AssertLimitExceeded(result);
        Assert.Contains(
            "maximum of 2 total retained distinct values", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheSameValueAppearsUnderTwoPredicates_ThenItIsChargedOncePerPredicate()
    {
        // No cross-predicate deduplication (D-110): each retaining attribute really does keep its
        // own copy in its own domain.
        var tooTight = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,same\ns,q,same\n", options: ProbeOptions.Create(maxTotalRetainedValues: 1L));
        var exact = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,same\ns,q,same\n", options: ProbeOptions.Create(maxTotalRetainedValues: 2L));

        AssertLimitExceeded(tooTight);
        Assert.Equal(2, ProbeFixtures.Draft(exact).Attributes.Count);
    }

    [Fact]
    public async Task ProbeTriple_WhenValuesRepeatOrAreMissingOrTruncated_ThenTheyAreNotCharged()
    {
        // Three non-charging cases at once: a repeat, a missing value, and a per-attribute
        // truncated tail. Only genuine retentions may consume the aggregate budget.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,x\ns,p,x\ns,p,?\ns,p,y\ns,p,z\n",
            options: ProbeOptions.Create(valueRetentionLimit: 2, maxTotalRetainedValues: 2L));

        Assert.Equal(["x", "y"], ProbeFixtures.Draft(result).Attributes[0].DeclaredDomain);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
    }

    [Fact]
    public async Task ProbeTriple_WhenTextIsMeasuredInUtf16CodeUnits_ThenSurrogatePairsCountAsTwo()
    {
        var emoji = "\U0001F600";

        var fits = await TripleProbeFixtures.ProbeTripleCsvAsync(
            $"s,p,{emoji}\n", options: ProbeOptions.Create(maxTotalRetainedValueText: 2L));
        var breaches = await TripleProbeFixtures.ProbeTripleCsvAsync(
            $"s,p,{emoji}\n", options: ProbeOptions.Create(maxTotalRetainedValueText: 1L));

        Assert.Equal([emoji], ProbeFixtures.Draft(fits).Attributes[0].DeclaredDomain);
        AssertLimitExceeded(breaches);
        Assert.Contains("total retained value text", breaches.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenBothRecordGuardsWouldBreach_ThenTheValueGuardIsReportedFirst()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,x\ns,p,yy\n",
            options: ProbeOptions.Create(maxTotalRetainedValues: 1L, maxTotalRetainedValueText: 1L));

        AssertLimitExceeded(result);
        Assert.Contains("total retained distinct values", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenANewPredicateAndANewValueWouldBothBreach_ThenAttributesWin()
    {
        // The precedence case only triple can produce: one row introduces a new predicate AND a
        // new value, so both guards are consulted on the same row. Attributes are decided first
        // (D-110), and the attribute is never created — so nothing is retained under it either.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,x\ns,q,y\n",
            options: ProbeOptions.Create(maxDiscoveredAttributes: 1, maxTotalRetainedValues: 1L));

        AssertLimitExceeded(result);
        Assert.Contains("would discover 2 attributes", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenAGuardBreaches_ThenNoOtherAttributeIsSilentlyTruncated()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s,p,1\ns,p,2\ns,q,3\n", options: ProbeOptions.Create(maxTotalRetainedValues: 2L));

        AssertLimitExceeded(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
    }

    [Fact]
    public async Task ProbeTriple_WhenGuardsAreAtTheirMaximumValues_ThenChargingCannotOverflow()
    {
        var options = ProbeOptions.Create(
            maxTotalRetainedValues: long.MaxValue, maxTotalRetainedValueText: long.MaxValue);

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s,p,x\ns,p,y\n", options: options);

        Assert.Equal(["x", "y"], ProbeFixtures.Draft(result).Attributes[0].DeclaredDomain);
    }
}
