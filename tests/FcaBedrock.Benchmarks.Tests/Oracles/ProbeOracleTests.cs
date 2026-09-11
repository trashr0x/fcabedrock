using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Discovery;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// The probe outcome gate. Each case below is a draft that is the <em>wrong</em> result while still
/// having the right shape — which is the only way a probe benchmark can publish a duration for work
/// it did not do.
/// <para>
/// The three outcomes differ per attribute, so the checks are exercised per attribute: a draft in
/// which one column discovered nothing is not a complete draft because fifteen others did, and a
/// draft in which one truncated column lost its recovery policy is not a usable draft because the
/// other two kept theirs.
/// </para>
/// </summary>
public sealed class ProbeOracleTests
{
    private const string What = "probe case";

    [Fact]
    public void RequireComplete_WhenEveryAttributeAuthorsItsDomain_ThenItPasses()
    {
        var result = Ok(Draft(Complete("a"), Complete("b"), Complete("c")));

        ProbeOracle.RequireComplete(result, expectedAttributes: 3, What);
    }

    [Fact]
    public void RequireComplete_WhenOneAttributeAuthorsNoDomain_ThenItThrows()
    {
        // The first counterexample: two columns discovered their values and one discovered nothing,
        // so a draft that asks a later conversion to rediscover a third of its schema passes an
        // "any attribute has a domain" check.
        var result = Ok(Draft(Complete("a"), NoDomain("b"), Complete("c")));

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireComplete(result, expectedAttributes: 3, What));
        Assert.Contains("'b'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("authored no declared domain", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireComplete_WhenAnAttributeIsDeclaredAllMissing_ThenItIsTheOnlyExemptOne()
    {
        // An attribute whose every cell was missing authors no domain at all (§10.3/D-071), so a
        // case may name it — and naming one does not excuse any other.
        var result = Ok(Draft(Complete("a"), NoDomain("b"), Complete("c")));

        ProbeOracle.RequireComplete(result, expectedAttributes: 3, What, allMissing: ["b"]);

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireComplete(
                Ok(Draft(Complete("a"), NoDomain("b"), NoDomain("c"))),
                expectedAttributes: 3,
                What,
                allMissing: ["b"]));
        Assert.Contains("'c'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireComplete_WhenADeclaredAllMissingAttributeAuthorsADomain_ThenItThrows()
    {
        var result = Ok(Draft(Complete("a"), Complete("b")));

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireComplete(result, expectedAttributes: 2, What, allMissing: ["b"]));
        Assert.Contains("observed nothing but missing values", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireComplete_WhenADeclaredAllMissingAttributeCarriesARecoveryPolicy_ThenItThrows()
    {
        // The all-missing exemption excuses a domain, never recovery: only truncation authors
        // `include`, and an attribute that observed nothing had nothing to truncate. Its domain is
        // absent exactly as the exemption allows, so only the recovery rule can reject this draft.
        var result = Ok(Draft(Complete("a"), Attribute("b", domain: null, UnknownValuePolicy.Include)));

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireComplete(result, expectedAttributes: 2, What, allMissing: ["b"]));
        Assert.Contains("'b' carry a recovery policy", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireComplete_WhenTheDraftTruncated_ThenItThrows()
    {
        var result = Ok(Draft(Complete("a"), Truncated("b")), Truncation());

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireComplete(result, expectedAttributes: 2, What));
        Assert.Contains("the draft truncated", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireTruncated_WhenExactlyTheExpectedAttributesRecover_ThenItPasses()
    {
        var result = Ok(Draft(Truncated("a"), Complete("b"), Truncated("c")), Truncation());

        ProbeOracle.RequireTruncated(result, expectedAttributes: 3, ["a", "c"], What);
    }

    [Fact]
    public void RequireTruncated_WhenOneExpectedMemberCarriesNoRecoveryPolicy_ThenItThrows()
    {
        // The second counterexample: `c` truncated without `include`, so converting this draft over
        // the same source would lose its tail — but `a` recovered, and a check that asked whether
        // ANY attribute recovered would accept the draft and time it as a usable one.
        var result = Ok(Draft(Truncated("a"), Complete("b"), Unrecovered("c")), Truncation());

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireTruncated(result, expectedAttributes: 3, ["a", "c"], What));
        Assert.Contains("'c'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("would lose their tails", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireTruncated_WhenAnAttributeTruncatedThatWasNotExpectedTo_ThenItThrows()
    {
        // The other half of "exact": a probe that truncated a column this case expects to be
        // complete discovered less than the case says it did.
        var result = Ok(Draft(Truncated("a"), Truncated("b"), Truncated("c")), Truncation());

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireTruncated(result, expectedAttributes: 3, ["a", "c"], What));
        Assert.Contains("'b' truncated", failure.Message, StringComparison.Ordinal);
        Assert.Contains("expects exactly", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireTruncated_WhenAnUntruncatedAttributeAuthorsNoDomain_ThenItThrows()
    {
        // Truncation elsewhere is not a licence for an untruncated column to discover nothing.
        var result = Ok(Draft(Truncated("a"), NoDomain("b")), Truncation());

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireTruncated(result, expectedAttributes: 2, ["a"], What));
        Assert.Contains("'b'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("authored no declared domain", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireTruncated_WhenNothingTruncated_ThenItThrows()
    {
        var result = Ok(Draft(Complete("a"), Complete("b")));

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireTruncated(result, expectedAttributes: 2, ["a"], What));
        Assert.Contains("nothing truncated", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireTruncated_WhenTheExpectationNamesAnAttributeTheDraftDoesNotCarry_ThenItThrows()
    {
        // An expectation about an attribute that is not there is an expectation about nothing, and
        // a case that mistyped a column name would otherwise assert less than it looks like it does.
        var result = Ok(Draft(Truncated("a"), Complete("b")), Truncation());

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireTruncated(result, expectedAttributes: 2, ["a", "n_sequence"], What));
        Assert.Contains("which the draft does not carry", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireGuardBreach_WhenOnlyTheBreachIsReported_ThenItPasses() =>
        ProbeOracle.RequireGuardBreach(Failed(LimitExceeded()), What);

    [Fact]
    public void RequireGuardBreach_WhenAnUnrelatedBlockingDiagnosticAlsoArrived_ThenItThrows()
    {
        // The third counterexample: the pass ended at a read failure and the guard diagnostic was
        // there too, so the duration is the cost of the failure rather than the cost of reaching
        // the guard — and "the expected code appears somewhere" cannot tell the two apart.
        var result = Failed(
            LimitExceeded(),
            new BedrockDiagnostic(
                DiagnosticCode.ProbeSourceReadFailed, DiagnosticSeverity.Error, "read failed", null));

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireGuardBreach(result, What));
        Assert.Contains("did not measure the guard alone", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireGuardBreach_WhenTheBreachIsReportedTwice_ThenItThrows()
    {
        // Every diagnostic here is the expected code, so no code check can object: only the count
        // can. The prober returns a breach as the single diagnostic that stopped the pass, so two
        // of them is a result the guard path cannot produce.
        var result = Failed(LimitExceeded(), LimitExceeded());

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireGuardBreach(result, What));
        Assert.Contains("exactly one diagnostic, but 2 were reported", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Fatal)]
    public void RequireGuardBreach_WhenTheBreachArrivesAtAnotherSeverity_ThenItThrows(DiagnosticSeverity severity)
    {
        // The right code, alone and with no draft, so only severity separates it from the real
        // breach — and Fatal is here because "Error or worse" is not the guard's contract: every
        // guard constructor reports exactly Error.
        var result = Failed(LimitExceeded(severity));

        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireGuardBreach(result, What));
        Assert.Contains($"with severity {severity}, but", failure.Message, StringComparison.Ordinal);
        Assert.Contains("reports it with severity Error", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireGuardBreach_WhenNoBreachIsReported_ThenItThrows()
    {
        // A pass that failed for another reason, or reported nothing, never reached the guard.
        var unrelated = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireGuardBreach(
                Failed(new BedrockDiagnostic(
                    DiagnosticCode.ProbeSourceReadFailed, DiagnosticSeverity.Error, "read failed", null)),
                What));
        Assert.Contains("no guard breach was reported", unrelated.Message, StringComparison.Ordinal);

        var nothing = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireGuardBreach(Failed(), What));
        Assert.Contains("no guard breach was reported", nothing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireGuardBreach_WhenADraftWasProduced_ThenItThrows()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireGuardBreach(Ok(Draft(Complete("a"))), What));
        Assert.Contains("the guard must yield none", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireGuardBreach_WhenADraftArrivesAlongsideTheBreach_ThenItThrows()
    {
        // The breach is an Error, and an Error already hides the value from TryGetValue — so a
        // no-draft rule asked through TryGetValue could never see this document. The guard's real
        // result carries no value at all.
        var failure = Assert.Throws<InvalidOperationException>(
            () => ProbeOracle.RequireGuardBreach(Ok(Draft(Complete("a")), LimitExceeded()), What));
        Assert.Contains("the guard must yield none", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequireGuardBreach_WhenARealProbeBreachesAnAggregateGuard_ThenItPasses()
    {
        // The exact shape is only safe to demand if it is the shape the prober returns: one breach,
        // at Error, with no value. Both aggregate guards the benchmark rows breach run for real here,
        // one below the total each corpus retains, so the stricter oracle cannot reject a real row.
        const int records = 500;

        using var values = TempDirectory.Create();
        var valueBreach = await ProbeWideAsync(
            values, W16Specs.Declared, data => W16Corpus.Write(data, records),
            ProbeOptions.Create(maxTotalRetainedValues: ProbeAccountingOracle.W16(records).Values - 1));
        ProbeOracle.RequireGuardBreach(valueBreach, "real value guard");

        using var text = TempDirectory.Create();
        var textBreach = await ProbeWideAsync(
            text, LongTextSpecs.Declared, data => LongTextCorpus.Write(data, records),
            ProbeOptions.Create(maxTotalRetainedValueText: ProbeAccountingOracle.LongText(records).TextUnits - 1));
        ProbeOracle.RequireGuardBreach(textBreach, "real text guard");
    }

    [Fact]
    public async Task W16Truncating_ShouldNameTheColumnsARealProbeActuallyTruncates()
    {
        // The derivation is only worth stating if it agrees with the prober. At this limit three of
        // the sixteen columns exceed it and thirteen do not, so the expectation is a real partition
        // rather than "all" or "one", and the oracle is run against a live draft.
        const int records = 3_000;
        const int limit = 64;

        var truncating = ProbeOracle.W16Truncating(records, limit);
        Assert.Equal(["n_seq", "n_skew", "n_wide"], truncating);

        using var temp = TempDirectory.Create();
        var result = await ProbeWideAsync(
            temp, W16Specs.Declared, data => W16Corpus.Write(data, records),
            ProbeOptions.Create(valueRetentionLimit: limit));

        ProbeOracle.RequireTruncated(result, W16Corpus.ColumnCount, truncating, "w16 derivation");
    }

    [Fact]
    public async Task W16Truncating_WhenNoColumnExceedsTheLimit_ThenTheDraftIsComplete()
    {
        const int records = 3_000;

        Assert.Empty(ProbeOracle.W16Truncating(records, ProbeOptions.Default.ValueRetentionLimit));

        using var temp = TempDirectory.Create();
        var result = await ProbeWideAsync(
            temp, W16Specs.Declared, data => W16Corpus.Write(data, records), ProbeOptions.Default);

        ProbeOracle.RequireComplete(result, W16Corpus.ColumnCount, "w16 derivation (complete)");
    }

    [Fact]
    public async Task RequireComplete_WhenTheDraftIsTheWidestOne_ThenEveryOneOfItsColumnsAuthorsADomain()
    {
        // The width consumer of the complete outcome: 1,559 columns, each with a tiny domain, is
        // where "every attribute" stops being a formality. No Ads column can be all-missing — every
        // term flag is written as `1` or `0` — so the case carries no exception set, and this is the
        // proof of that rather than an assumption about it.
        using var temp = TempDirectory.Create();
        var result = await ProbeWideAsync(
            temp, AdsSpecs.Declared, data => AdsCorpus.Write(data, 500), ProbeOptions.Default);

        ProbeOracle.RequireComplete(result, AdsCorpus.ColumnCount, "probe (ads)");
    }

    [Fact]
    public async Task T10Truncating_ShouldNameOnlyTheHighCardinalityPredicate()
    {
        // The mixed triple shape the scale cases are quoted against: one predicate reaches the
        // limit while the two categorical ones and the unmatched one stay far inside it.
        const int records = 1_000;
        const int limit = 64;

        var truncating = ProbeOracle.T10Truncating(records, limit);
        Assert.Equal([T10Corpus.StagePredicate], truncating);

        using var temp = TempDirectory.Create();
        var dataPath = temp.File("t10.csv");
        await using (var data = File.Create(dataPath))
        {
            T10Corpus.Write(data, records, TripleLayout.Interleaved);
        }

        var settings = ConversionPipeline.RequireReadSettings(
            ConversionPipeline.RequireDocument(T10Specs.Declared));
        var session = (ITripleSourceSession)ConversionPipeline.CreateSession(settings, dataPath);
        var result = await Prober.ProbeTripleAsync(
            session, settings, columns: null, ProbeOptions.Create(valueRetentionLimit: limit),
            TestContext.Current.CancellationToken);

        ProbeOracle.RequireTruncated(result, expectedAttributes: 4, truncating, "t10 derivation");
    }

    private static async Task<Diagnosed<SpecDocument>> ProbeWideAsync(
        TempDirectory temp, string specText, Action<Stream> write, ProbeOptions options)
    {
        var dataPath = temp.File("wide.csv");
        await using (var data = File.Create(dataPath))
        {
            write(data);
        }

        var settings = ConversionPipeline.RequireReadSettings(ConversionPipeline.RequireDocument(specText));
        var session = (IWideSourceSession)ConversionPipeline.CreateSession(settings, dataPath);
        return await Prober.ProbeAsync(session, settings, options, TestContext.Current.CancellationToken);
    }

    private static Diagnosed<SpecDocument> Ok(SpecDocument draft, params BedrockDiagnostic[] diagnostics) =>
        Diagnosed<SpecDocument>.Ok(draft, diagnostics);

    private static Diagnosed<SpecDocument> Failed(params BedrockDiagnostic[] diagnostics) =>
        Diagnosed<SpecDocument>.Failed(diagnostics);

    private static BedrockDiagnostic Truncation() =>
        new(DiagnosticCode.ProbeDomainTruncated, DiagnosticSeverity.Warning, "truncated", null);

    /// <summary>A guard breach as every guard constructor reports one, unless a test says otherwise.</summary>
    private static BedrockDiagnostic LimitExceeded(DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        new(DiagnosticCode.ProbeLimitExceeded, severity, "limit exceeded", null);

    private static SpecDocument Draft(params AttributeSection[] attributes) =>
        new(
            Spec: null,
            Provenance: null,
            Binding: null,
            Defaults: null,
            Output: null,
            Templates: [],
            Matchers: [],
            Attributes: attributes);

    /// <summary>An attribute that discovered its whole domain: values, and no recovery policy.</summary>
    private static AttributeSection Complete(string name) => Attribute(name, ["one", "two"], policy: null);

    /// <summary>A truncated attribute as <c>ProbeDraft</c> authors one: a prefix plus <c>include</c>.</summary>
    private static AttributeSection Truncated(string name) =>
        Attribute(name, ["one"], UnknownValuePolicy.Include);

    /// <summary>A truncated attribute that lost its recovery policy, so its tail is unrecoverable.</summary>
    private static AttributeSection Unrecovered(string name) => Attribute(name, ["one"], policy: null);

    /// <summary>An attribute that authored no <c>declared_domain</c> at all — omitted, not empty.</summary>
    private static AttributeSection NoDomain(string name) => Attribute(name, domain: null, policy: null);

    private static AttributeSection Attribute(
        string name, IReadOnlyList<string>? domain, UnknownValuePolicy? policy) =>
        new(
            Name: name,
            Source: new ColumnSourceSection(Index: 0, Name: null, ValueType: SourceValueType.String),
            Description: null,
            Include: null,
            Template: null,
            Discretizer: new IdentityDiscretizerSection(),
            Scale: new NominalScaleSection(),
            DeclaredDomain: domain,
            RestrictTo: null,
            ValueLabels: null,
            MissingPolicy: null,
            UnknownValuePolicy: policy);
}
