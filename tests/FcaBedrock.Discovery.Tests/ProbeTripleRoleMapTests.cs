using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The binding-only role-map preflight: before a single row is read, Discovery
/// resolves the exact <c>[binding]</c> the draft would author and forwards whatever the resolver
/// says.
/// <para>
/// <b>Why a preflight rather than a probe-phase check.</b> §5.3's role-map rules already have an
/// owner — <c>spec validate</c> — and a second implementation in Discovery would be a second
/// thing to keep correct, with its own wording and its own drift (D-067). Resolving the real
/// binding instead makes single ownership structural: a bad map produces the <em>same</em>
/// diagnostics here as from <c>validate</c>, no probe code is minted, and no §16.4 phase cell
/// moves. It also proves the binding half of the D-107 resolve guarantee up front.
/// </para>
/// <para>
/// <b>Zero reads on failure</b> is the other half of the claim, and it is asserted everywhere
/// below: an invalid map costs no I/O at all, which is what "preflight" has to mean.
/// </para>
/// </summary>
public sealed class ProbeTripleRoleMapTests
{
    private static readonly SourceReadSettings Settings = TripleProbeFixtures.TripleSettings();

    private static readonly SourceReadSettings HeaderSettings = TripleProbeFixtures.TripleSettings(hasHeader: true);

    private static TripleProbeFixtures.FakeTripleSession Session(SourceSchema schema) =>
        new(schema, TripleProbeFixtures.Rows(("s1", "p1", "v1"), ("s2", "p2", "v2")));

    private static void AssertRejectedBeforeAnyRead(
        Diagnosed<SpecDocument> result, TripleProbeFixtures.FakeTripleSession session)
    {
        Assert.False(result.TryGetValue(out _));
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);

        // The point of the preflight: the schema was read (a name map needs it), the rows were not.
        Assert.Equal(0, session.RowEnumerations);
        Assert.Equal(0, session.RowsYielded);
        Assert.Empty(session.RolesRead);
    }

    // --- Valid maps: resolved indices drive the read, authored spelling drives the draft -------

    [Fact]
    public async Task ProbeTriple_WhenGivenACustomIndexMap_ThenReadsThroughItAndAuthorsItVerbatim()
    {
        var session = new TripleProbeFixtures.FakeTripleSession(
            new SourceSchema(3), TripleProbeFixtures.Rows(("s", "p", "v")));
        var columns = TripleProbeFixtures.Indexes(subject: 2, predicate: 1, value: 0);

        var draft = ProbeFixtures.Draft(await Prober.ProbeTripleAsync(session, Settings, columns));

        Assert.Equal(new TripleColumns(2, 1, 0), Assert.Single(session.RolesRead));
        Assert.Same(columns, draft.Binding!.Columns);
    }

    [Fact]
    public async Task ProbeTriple_WhenGivenANameMap_ThenResolvesItForTheReadButKeepsNamesInTheDraft()
    {
        // The heart of "addressing mode is preserved": names resolve to indices for THIS read,
        // and the document still says names. The resolved map is read machinery, not a rewrite of
        // what the caller authored.
        var session = new TripleProbeFixtures.FakeTripleSession(
            new SourceSchema(3, ["val", "pred", "subj"]), TripleProbeFixtures.Rows(("s", "p", "v")));
        var columns = TripleProbeFixtures.Names("subj", "pred", "val");

        var draft = ProbeFixtures.Draft(await Prober.ProbeTripleAsync(session, HeaderSettings, columns));

        Assert.Equal(new TripleColumns(2, 1, 0), Assert.Single(session.RolesRead));
        Assert.Equal(columns, draft.Binding!.Columns);
        Assert.All(
            new ColumnRef?[] { draft.Binding.Columns!.Subject, draft.Binding.Columns.Predicate, draft.Binding.Columns.Value },
            role => Assert.IsType<NameColumnRef>(role));
    }

    [Fact]
    public async Task ProbeTriple_WhenGivenANameMapOverRealCsv_ThenReadsTheRightColumns()
    {
        // End-to-end through the production adapter, not a fake: a header row plus a name map
        // must actually select the right physical columns.
        const string csv = "value,predicate,subject\ncat,species,s1\nblack,colour,s1\n";

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            csv, hasHeader: true, columns: TripleProbeFixtures.Names("subject", "predicate", "value"));
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["species", "colour"], draft.Attributes.Select(a => a.Name));
        Assert.Equal(["cat"], draft.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheDefaultMapIsUsed_ThenThePreflightStillPasses()
    {
        // The control: the defaulted map goes through exactly the same preflight, so a failure
        // above cannot be an artifact of "supplying a map at all".
        var result = await Prober.ProbeTripleAsync(
            Session(new SourceSchema(3)), Settings);

        Assert.True(result.IsOk, ProbeFixtures.Describe(result.Diagnostics));
    }

    // --- Invalid maps: forwarded spec-validate diagnostics, and no read ------------------------

    public static TheoryData<string, TripleColumnsSection> InvalidIndexMaps() => new()
    {
        // A partial table: §5.3 requires all three roles or none.
        { "subject only", new TripleColumnsSection(new IndexColumnRef(0), null, null) },
        { "value missing", new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), null) },

        // Mixed addressing: one mode across the three roles, never a blend.
        {
            "index and name mixed",
            new TripleColumnsSection(new IndexColumnRef(0), new NameColumnRef("p"), new IndexColumnRef(2))
        },

        // Negative and out-of-range indices, checked against the schema the probe just read.
        { "negative subject", TripleProbeFixtures.Indexes(-1, 1, 2) },
        { "out of range value", TripleProbeFixtures.Indexes(0, 1, 99) },

        // Non-distinct roles: two roles cannot be the same physical column.
        { "subject equals predicate", TripleProbeFixtures.Indexes(1, 1, 2) },
    };

    [Theory]
    [MemberData(nameof(InvalidIndexMaps))]
    public async Task ProbeTriple_WhenTheIndexMapIsInvalid_ThenFailsBeforeReadingAnyRow(
        string because, TripleColumnsSection columns)
    {
        Assert.NotEmpty(because);
        var session = Session(new SourceSchema(3));

        var result = await Prober.ProbeTripleAsync(session, Settings, columns);

        AssertRejectedBeforeAnyRead(result, session);
    }

    [Fact]
    public async Task ProbeTriple_WhenANameMapIsUsedWithoutAHeader_ThenFailsBeforeReadingAnyRow()
    {
        // §5.3: name addressing needs a header to resolve against. The settings say there is
        // none, so the map cannot mean anything — diagnosed, not guessed at.
        var session = Session(new SourceSchema(3));

        var result = await Prober.ProbeTripleAsync(
            session, Settings, TripleProbeFixtures.Names("s", "p", "v"));

        AssertRejectedBeforeAnyRead(result, session);
    }

    [Fact]
    public async Task ProbeTriple_WhenANameIsNotInTheHeader_ThenFailsBeforeReadingAnyRow()
    {
        var session = Session(new SourceSchema(3, ["subj", "pred", "val"]));

        var result = await Prober.ProbeTripleAsync(
            session, HeaderSettings, TripleProbeFixtures.Names("subj", "pred", "absent"));

        AssertRejectedBeforeAnyRead(result, session);
    }

    [Fact]
    public async Task ProbeTriple_WhenANameMatchesTwoHeaderColumns_ThenFailsBeforeReadingAnyRow()
    {
        // §10.2: a name binding must resolve to exactly one column. A duplicate header is
        // readable (the tolerant open) but not addressable by name — which is precisely why the
        // wide matrix falls back to index binding for duplicates.
        var session = Session(new SourceSchema(3, ["dup", "dup", "val"]));

        var result = await Prober.ProbeTripleAsync(
            session, HeaderSettings, TripleProbeFixtures.Names("dup", "val", "dup"));

        AssertRejectedBeforeAnyRead(result, session);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheMapIsInvalid_ThenTheResolverDiagnosticsAreForwardedUnchanged()
    {
        // The forwarding claim, made exact: probe's diagnostics must be the resolver's own —
        // same codes, same severities, same messages, same order — not a re-emission, not a
        // relabelling into a probe phase, and not wrapped in ProbeSourceReadFailed.
        var schema = new SourceSchema(3);
        var columns = TripleProbeFixtures.Indexes(1, 1, 2);
        var session = Session(schema);

        var result = await Prober.ProbeTripleAsync(session, Settings, columns);

        var expected = SpecResolver.Resolve(
            new SpecDocument(
                new SpecSection(1, null, null, null, null, ProbeDraftExpectations.Description),
                null,
                new BindingSection(
                    SourceShape.Triple, "utf-8", ',', '"', false, "invariant", "?",
                    TripleOrdering.Unordered, columns, null),
                null, null, [], [], []),
            schema);

        Assert.Equal(expected.Diagnostics, result.Diagnostics);
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code is DiagnosticCode.ProbeSourceReadFailed
                or DiagnosticCode.ProbeNoAttributesDiscovered
                or DiagnosticCode.ProbeLimitExceeded);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheMapIsInvalid_ThenTheDiagnosticIsRepeatable()
    {
        var columns = TripleProbeFixtures.Indexes(0, 0, 0);

        var first = await Prober.ProbeTripleAsync(Session(new SourceSchema(3)), Settings, columns);
        var second = await Prober.ProbeTripleAsync(Session(new SourceSchema(3)), Settings, columns);

        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheSchemaReadFails_ThenThePreflightNeverRuns()
    {
        // Ordering matters: the schema is what a name map resolves against and what an index map
        // is range-checked against, so a schema failure must surface as a read failure rather
        // than as a bogus binding diagnostic.
        var session = new TripleProbeFixtures.FakeTripleSession(
            new SourceSchema(3), [], schemaFailure: () => new IOException("device not ready"));

        var result = await Prober.ProbeTripleAsync(
            session, Settings, TripleProbeFixtures.Names("s", "p", "v"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ProbeSourceReadFailed, diagnostic.Code);
        Assert.Equal(0, session.RowEnumerations);
    }
}
