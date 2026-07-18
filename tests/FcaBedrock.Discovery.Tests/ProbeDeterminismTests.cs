using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// D-112: probe's input is a <b>record sequence</b>, not a file, so its determinism must be
/// defined over that sequence — otherwise a future in-memory or SQL adapter could not inherit
/// the guarantee. Same ordered schema + same records + same settings + same options ⇒ identical
/// document, identical canonical bytes, identical diagnostics in identical order with identical
/// bounded samples.
/// <para>
/// Also here: the two structural claims that make the guarantee affordable — exactly one cleaned
/// record pass, and no coupling to CSV, streams, or Sep.
/// </para>
/// </summary>
public sealed class ProbeDeterminismTests
{
    private const string Messy = "keep,,dup,dup\nq,1,x,y\nr,2,x,z\nq,3,w,y\n";

    [Fact]
    public async Task Probe_WhenRunTwiceOverTheSameRecords_ThenProducesIdenticalBytesAndDiagnostics()
    {
        // Deliberately a source that trips naming adjustments AND truncation, so the repeated
        // run has ordering, counts, and bounded samples to disagree about.
        var options = ProbeOptions.Create(valueRetentionLimit: 2);

        var first = await ProbeFixtures.ProbeCsvAsync(Messy, options: options);
        var second = await ProbeFixtures.ProbeCsvAsync(Messy, options: options);

        Assert.Equal(ProbeFixtures.Toml(first), ProbeFixtures.Toml(second));
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Equal(
            first.Diagnostics.Select(d => d.Code),
            second.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public async Task Probe_WhenDiagnosticsAreProduced_ThenTheirOrderIsFixed()
    {
        // Both aggregates flush at end of pass in a fixed order (naming, then truncation), so a
        // caller may rely on the sequence rather than sorting defensively.
        var result = await ProbeFixtures.ProbeCsvAsync(Messy, options: ProbeOptions.Create(valueRetentionLimit: 2));

        Assert.Equal(
            [DiagnosticCode.ProbeAttributeNameAdjusted, DiagnosticCode.ProbeDomainTruncated],
            result.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public async Task Probe_WhenTheSameRecordsArriveFromDifferentSources_ThenTheDraftsAgree()
    {
        // The heart of record-sequence determinism: a CSV session and an in-memory session that
        // yield the SAME cleaned records must produce the same draft. If probe were coupled to
        // the tokenizer, or observed anything the seam does not expose, these would diverge.
        var settings = ProbeFixtures.WideSettings();
        var fromCsv = await ProbeFixtures.ProbeCsvAsync("a,b\nx,1\ny,\nx,2\n");
        var fromMemory = await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(2, ["a", "b"]), ["x", "1"], ["y", null], ["x", "2"]),
            settings);

        Assert.Equal(ProbeFixtures.Toml(fromCsv), ProbeFixtures.Toml(fromMemory));
    }

    [Fact]
    public async Task Probe_WhenDrivenByANonFileSession_ThenNoCsvOrStreamIsInvolved()
    {
        // The in-memory session has no stream, file, delimiter, encoding, or Sep anywhere: it
        // implements only the D-109 seam. That it works at all is the proof that Discovery
        // consumes records, not bytes — and the shape a future SQL/SPARQL adapter would take.
        // The second column's null is a missing value the seam already normalized — probe redoes
        // no cleaning, so it simply is not an observation and "big" stands alone.
        var session = ProbeFixtures.Fake(new SourceSchema(2, ["colour", "size"]), ["red", "big"], ["blue", null]);

        var draft = ProbeFixtures.Draft(await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.Equal(["colour", "size"], draft.Attributes.Select(a => a.Name));
        Assert.Equal(["red", "blue"], draft.Attributes[0].DeclaredDomain);
        Assert.Equal(["big"], draft.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public async Task Probe_WhenSuccessful_ThenEnumeratesRecordsExactlyOnce()
    {
        // "One pass" is a claim about record ENUMERATIONS (D-106). The schema read is metadata
        // and is counted separately — mistaking it for a second data pass is the exact spurious
        // failure this separation avoids.
        var session = ProbeFixtures.Fake(new SourceSchema(2, ["a", "b"]), ["1", "2"], ["3", "4"]);

        await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        Assert.Equal(1, session.RecordEnumerations);
        Assert.Equal(1, session.SchemaReads);
        Assert.Equal(2, session.RecordsYielded);
    }

    [Fact]
    public async Task Probe_WhenTruncating_ThenStillEnumeratesRecordsExactlyOnce()
    {
        // No grouped or count-sensitive second pass, ever — not even to discover what was
        // dropped. Probe is set-based, so a second pass could tell it nothing new.
        var session = ProbeFixtures.Fake(
            new SourceSchema(1, ["a"]), ["p"], ["q"], ["r"], ["s"]);

        await Prober.ProbeAsync(session, ProbeFixtures.WideSettings(), ProbeOptions.Create(valueRetentionLimit: 2));

        Assert.Equal(1, session.RecordEnumerations);
    }

    [Fact]
    public async Task Probe_WhenSourceIsEmpty_ThenStillMakesAtMostOnePass()
    {
        var session = ProbeFixtures.Fake(new SourceSchema(1, ["a"]));

        await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        Assert.Equal(1, session.RecordEnumerations);
    }

    // --- Cancellation (never a diagnostic, never a partial document) ----------------

    [Fact]
    public async Task Probe_WhenAlreadyCanceled_ThenThrowsBeforeReadingTheSchema()
    {
        // Cancellation wins over everything, including over a source that would have completed
        // trivially. Checked before any observable work rather than only inside the loop.
        var session = ProbeFixtures.Fake(new SourceSchema(1, ["a"]), ["x"]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings(), null, cts.Token));

        Assert.Equal(0, session.SchemaReads);
        Assert.Equal(0, session.RecordEnumerations);
    }

    [Fact]
    public async Task Probe_WhenCanceledDuringEnumeration_ThenThrowsWithNoDiagnosticAndNoDocument()
    {
        // Cancelled after the first record: the engine has real partial state, which is exactly
        // when returning "what we have" would be tempting and wrong (D-112).
        using var cts = new CancellationTokenSource();
        var observed = 0;
        var session = new ProbeFixtures.FakeWideSession(
            new SourceSchema(1, ["a"]),
            ProbeFixtures.Records(["x"], ["y"], ["z"]),
            beforeEachRecord: () =>
            {
                if (++observed == 1)
                {
                    cts.Cancel();
                }
            });

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings(), null, cts.Token));

        // Nothing but the exception: no Diagnosed result exists to carry a diagnostic or a
        // half-authored draft, because the method never returns one.
        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.Equal(1, session.RecordsYielded);
    }

    [Fact]
    public async Task Probe_WhenCanceledDuringTheSchemaRead_ThenPropagatesUnwrapped()
    {
        // A cancellation must never be reclassified as a read failure: the two mean completely
        // different things to a caller, and only one of them warrants a retry.
        var session = new ProbeFixtures.FakeWideSession(
            new SourceSchema(1, ["a"]), [], schemaFailure: () => new OperationCanceledException());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.IsNotType<FcaBedrock.Sources.SourceReadException>(thrown);
    }

    [Fact]
    public async Task Probe_WhenNotCanceled_ThenTheTokenChangesNothing()
    {
        // Control for the cancellation suite: a live, uncancelled token must not perturb the
        // draft, so the assertions above cannot be passing for an unrelated reason.
        using var cts = new CancellationTokenSource();
        var settings = ProbeFixtures.WideSettings();

        var withToken = await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(1, ["a"]), ["x"]), settings, null, cts.Token);
        var withoutToken = await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(1, ["a"]), ["x"]), settings);

        Assert.Equal(ProbeFixtures.Toml(withoutToken), ProbeFixtures.Toml(withToken));
    }

    // --- No-draft outcomes ---------------------------------------------------------

    [Fact]
    public async Task Probe_WhenSourceHasZeroColumns_ThenReportsNoAttributesDiscoveredAndNoDraft()
    {
        var result = await ProbeFixtures.ProbeCsvAsync(string.Empty);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ProbeNoAttributesDiscovered, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(result.TryGetValue(out _));
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Probe_WhenSourceHasColumnsButNoRecords_ThenSucceeds()
    {
        // The contrast that gives the zero-column failure its meaning: "no columns" is a failure
        // (nothing to author), "no rows" is a success with domains omitted (D-107).
        var result = await ProbeFixtures.ProbeCsvAsync("a,b\n");

        Assert.True(result.IsOk, ProbeFixtures.Describe(result.Diagnostics));
        Assert.Equal(2, ProbeFixtures.Draft(result).Attributes.Count);
    }

    [Fact]
    public async Task Probe_WhenEveryValueIsMissing_ThenSucceedsWithNoDomains()
    {
        var result = await ProbeFixtures.ProbeCsvAsync("a,b\n?,?\n?,?\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.All(draft.Attributes, a => Assert.Null(a.DeclaredDomain));
    }
}
