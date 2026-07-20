using System.Text;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources.Tests;

// The D-109 unbound source-session seam: streaming cleaned records with no spec, no binding,
// and no CSV concept on the interface. Covers the seam shape, unbound-vs-bound parity, per-read
// triple roles, replayability, cancellation, and the typed read-failure channel.
public sealed class UnboundSessionTests
{
    private static Func<Stream> Opener(string text) => () => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static WideCsvSession WideSession(string text, bool hasHeader = false, string missingToken = "?") =>
        new(Opener(text), SourceReadSettings.CreateWide(hasHeader: hasHeader, missingToken: missingToken));

    private static TripleCsvSession TripleSession(string text, bool hasHeader = false, string missingToken = "?") =>
        new(Opener(text), SourceReadSettings.CreateTriple(hasHeader: hasHeader, missingToken: missingToken));

    private static async Task<List<ObjectRecord>> DrainAsync(IAsyncEnumerable<ObjectRecord> records)
    {
        var drained = new List<ObjectRecord>();
        await foreach (var record in records)
        {
            drained.Add(record);
        }

        return drained;
    }

    private static async Task<List<TripleRow>> DrainAsync(IAsyncEnumerable<TripleRow> rows)
    {
        var drained = new List<TripleRow>();
        await foreach (var row in rows)
        {
            drained.Add(row);
        }

        return drained;
    }

    // Renders a record as "name:[f0|f1|...]" with missing fields as <null>, so a parity
    // assertion compares the whole cleaned shape rather than a hand-picked field.
    private static string Render(ObjectRecord record)
    {
        var fields = new List<string>();
        for (var i = 0; i < record.FieldCount; i++)
        {
            fields.Add(record.Field(i) ?? "<null>");
        }

        return $"{record.Name}:[{string.Join("|", fields)}]";
    }

    private static string Render(TripleRow row) =>
        $"{row.RecordIndex}:{row.Subject ?? "<null>"}/{row.Predicate ?? "<null>"}/{row.Value ?? "<null>"}";

    // --- 1. The seam shape ---

    [Fact]
    public void WideSession_ImplementsTheWideSeamAndReportsItsShapeAndSettings()
    {
        var settings = SourceReadSettings.CreateWide(delimiter: '\t', missingToken: "NA");
        var session = new WideCsvSession(Opener("a\tb"), settings);

        Assert.IsAssignableFrom<IWideSourceSession>(session);
        Assert.IsAssignableFrom<ISourceSession>(session);
        Assert.Equal(SourceShape.Wide, ((ISourceSession)session).Shape);
        Assert.Same(settings, session.ReadSettings);
    }

    [Fact]
    public void TripleSession_ImplementsTheTripleSeamAndReportsItsShapeAndSettings()
    {
        var settings = SourceReadSettings.CreateTriple(missingToken: "NA");
        var session = new TripleCsvSession(Opener("s,p,v"), settings);

        Assert.IsAssignableFrom<ITripleSourceSession>(session);
        Assert.IsAssignableFrom<ISourceSession>(session);
        Assert.Equal(SourceShape.Triple, ((ISourceSession)session).Shape);
        Assert.Same(settings, session.ReadSettings);
    }

    [Fact]
    public void Seam_DoesNotExposeReadSettings() =>
        // Source-neutrality is the point of the seam: a future SQL/SPARQL adapter has
        // no delimiter/quote/header to report, so read settings must not be reachable through it.
        Assert.Null(typeof(ISourceSession).GetProperty(nameof(WideCsvSession.ReadSettings)));

    // --- 2. Unbound-vs-bound parity, wide ---

    [Fact]
    public async Task WideUnbound_YieldsTheSameCleanedRecordsAsTheBoundPath()
    {
        // Quote-aware trim, a quoted interior-space value, an escaped quote, the missing token,
        // an empty cell, and a ragged short row — all in one input.
        const string Text = "  a  ,\" b \",\"x\"\"y\"\n?,,plain\nshort\n";
        var session = WideSession(Text);
        var schema = await session.GetSchemaAsync();
        var bound = session.Bind(WideResolution(schema));

        var unboundRecords = await DrainAsync(session.ReadAsync());
        var boundRecords = await DrainAsync(bound.ReadAsync());

        Assert.Equal(boundRecords.Select(Render), unboundRecords.Select(Render));
        Assert.Equal(
            ["0:[a| b |x\"y]", "1:[<null>|<null>|plain]", "2:[short]"],
            unboundRecords.Select(Render));
    }

    [Fact]
    public async Task WideUnbound_WhenHeadered_ThenHeaderIsNotDataAndFirstRecordIsIndexZero()
    {
        var session = WideSession("colour,size\nred,big\nblue,small\n", hasHeader: true);

        var records = await DrainAsync(session.ReadAsync());

        Assert.Equal(["0:[red|big]", "1:[blue|small]"], records.Select(Render));
    }

    // --- 3. Unbound-vs-bound parity, triple, with per-read roles ---

    [Fact]
    public async Task TripleUnbound_WithDefaultRoles_YieldsTheSameRowsAsTheBoundPath()
    {
        const string Text = "s1,species,cat\ns2,colour,?\ns3\n";
        var roles = new TripleColumns(0, 1, 2);
        var session = TripleSession(Text);
        var schema = await session.GetSchemaAsync();
        var bound = session.Bind(TripleResolution(schema, roles));

        var unbound = await DrainAsync(session.ReadRowsAsync(roles));
        var boundRows = await DrainAsync(bound.ReadRowsAsync());

        Assert.Equal(boundRows.Select(Render), unbound.Select(Render));
        // The missing token normalizes; an absent mapped role on a ragged row is null, not an error.
        Assert.Equal(
            ["0:s1/species/cat", "1:s2/colour/<null>", "2:s3/<null>/<null>"],
            unbound.Select(Render));
    }

    [Fact]
    public async Task TripleUnbound_WithNonDefaultRoles_ReadsThroughThatMap()
    {
        var session = TripleSession("v0,p,s0\n");

        var rows = await DrainAsync(session.ReadRowsAsync(new TripleColumns(2, 1, 0)));

        Assert.Equal(["0:s0/p/v0"], rows.Select(Render));
    }

    [Fact]
    public async Task TripleUnbound_RoleMapIsPerReadAndNeverSessionIdentity()
    {
        // The same session read under two different maps, then bound under a third — proving roles
        // are an argument, not state, and that an unbound read constrains no later Bind.
        var session = TripleSession("v0,p,s0\n");
        var schema = await session.GetSchemaAsync();

        var reversed = await DrainAsync(session.ReadRowsAsync(new TripleColumns(2, 1, 0)));
        var natural = await DrainAsync(session.ReadRowsAsync(new TripleColumns(0, 1, 2)));
        var bound = session.Bind(TripleResolution(schema, new TripleColumns(2, 1, 0)));
        var boundRows = await DrainAsync(bound.ReadRowsAsync());

        Assert.Equal(["0:s0/p/v0"], reversed.Select(Render));
        Assert.Equal(["0:v0/p/s0"], natural.Select(Render));
        Assert.Equal(["0:s0/p/v0"], boundRows.Select(Render));
    }

    [Fact]
    public async Task TripleUnbound_WhenRoleNegative_ThenThrowsArgumentOutOfRange()
    {
        var session = TripleSession("s,p,v\n");

        // A programmer error at the boundary, not an obscure index failure mid-enumeration.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => DrainAsync(session.ReadRowsAsync(new TripleColumns(-1, 1, 2))));
    }

    [Fact]
    public async Task TripleUnbound_WhenRolesNull_ThenThrowsArgumentNull() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => DrainAsync(session: TripleSession("s,p,v\n"), columns: null!));

    private static Task<List<TripleRow>> DrainAsync(TripleCsvSession session, TripleColumns columns) =>
        DrainAsync(session.ReadRowsAsync(columns));

    // --- 4. Missing tokens at the adapter boundary, both shapes ---

    [Fact]
    public async Task WideUnbound_WhenMissingTokenCustom_ThenOnlyThatTokenAndEmptyAreMissing()
    {
        var session = WideSession("NA,?,,x\n", missingToken: "NA");

        var records = await DrainAsync(session.ReadAsync());

        // "?" is now ordinary data; "NA" and the empty cell are missing.
        Assert.Equal(["0:[<null>|?|<null>|x]"], records.Select(Render));
    }

    [Fact]
    public async Task WideUnbound_WhenMissingTokenDisabled_ThenOnlyEmptyCellsAreMissing()
    {
        // §5.1: an empty missing_token disables TOKEN matching only — empty cells stay missing.
        var session = WideSession("?,,x\n", missingToken: "");

        var records = await DrainAsync(session.ReadAsync());

        Assert.Equal(["0:[?|<null>|x]"], records.Select(Render));
    }

    [Fact]
    public async Task TripleUnbound_WhenMissingTokenDisabled_ThenOnlyEmptyCellsAreMissing()
    {
        var session = TripleSession("?,,x\n", missingToken: "");

        var rows = await DrainAsync(session.ReadRowsAsync(new TripleColumns(0, 1, 2)));

        Assert.Equal(["0:?/<null>/x"], rows.Select(Render));
    }

    [Fact]
    public async Task TripleUnbound_WhenMissingTokenCustom_ThenAppliesToEveryRole()
    {
        var session = TripleSession("NA,NA,NA\n", missingToken: "NA");

        var rows = await DrainAsync(session.ReadRowsAsync(new TripleColumns(0, 1, 2)));

        Assert.Equal(["0:<null>/<null>/<null>"], rows.Select(Render));
    }

    // --- 5. Replayability ---

    [Fact]
    public async Task WideUnbound_EachEnumerationReopensAndYieldsTheSameSequence()
    {
        var opens = 0;
        var session = new WideCsvSession(
            () => { opens++; return new MemoryStream(Encoding.UTF8.GetBytes("a,b\nc,d\n")); },
            SourceReadSettings.CreateWide(hasHeader: false));

        var first = await DrainAsync(session.ReadAsync());
        var second = await DrainAsync(session.ReadAsync());

        Assert.Equal(first.Select(Render), second.Select(Render));
        Assert.Equal(["0:[a|b]", "1:[c|d]"], first.Select(Render));
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task TripleUnbound_EachEnumerationReopensAndYieldsTheSameSequence()
    {
        var opens = 0;
        var session = new TripleCsvSession(
            () => { opens++; return new MemoryStream(Encoding.UTF8.GetBytes("s,p,v\ns2,p2,v2\n")); },
            SourceReadSettings.CreateTriple());
        var roles = new TripleColumns(0, 1, 2);

        var first = await DrainAsync(session.ReadRowsAsync(roles));
        var second = await DrainAsync(session.ReadRowsAsync(roles));

        Assert.Equal(first.Select(Render), second.Select(Render));
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task Unbound_ReadsWithoutASchemaReadOrBind()
    {
        // The unbound read is independent of the D-098 lifecycle: no GetSchemaAsync first, no Bind.
        var session = WideSession("a,b\n");

        var records = await DrainAsync(session.ReadAsync());

        Assert.Equal(["0:[a|b]"], records.Select(Render));
    }

    // --- 6. Cancellation (never wrapped, nothing cached) ---

    [Fact]
    public async Task WideUnbound_WhenCancelledBeforeEnumeration_ThenPropagatesUnwrapped()
    {
        var session = WideSession("a,b\nc,d\n");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DrainAsync(session.ReadAsync(cts.Token)));

        Assert.IsNotType<SourceReadException>(thrown);
    }

    [Fact]
    public async Task WideUnbound_WhenCancelledDuringEnumeration_ThenStopsAndPropagates()
    {
        var session = WideSession("a\nb\nc\nd\n");
        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var record in session.ReadAsync(cts.Token))
            {
                seen.Add(Render(record));
                cts.Cancel();
            }
        });

        Assert.Equal(["0:[a]"], seen);
    }

    [Fact]
    public async Task TripleUnbound_WhenCancelledDuringEnumeration_ThenStopsAndPropagates()
    {
        var session = TripleSession("s1,p,v\ns2,p,v\ns3,p,v\n");
        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var row in session.ReadRowsAsync(new TripleColumns(0, 1, 2), cts.Token))
            {
                seen.Add(Render(row));
                cts.Cancel();
            }
        });

        Assert.Equal(["0:s1/p/v"], seen);
    }

    [Theory]
    [InlineData("")]                          // empty source
    [InlineData("colour,size\n")]             // header-only source
    public async Task WideUnbound_WhenCancelledAndNoDataRecords_ThenStillCancels(string text)
    {
        // A canceled read must cancel, not quietly succeed as an empty sequence: an empty
        // "success" is indistinguishable from "this source has no records", which downstream
        // would read as a real observation rather than an abandoned one (D-112).
        var session = WideSession(text, hasHeader: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DrainAsync(session.ReadAsync(cts.Token)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("s,p,v\n")]
    public async Task TripleUnbound_WhenCancelledAndNoDataRecords_ThenStillCancels(string text)
    {
        var session = TripleSession(text, hasHeader: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DrainAsync(session.ReadRowsAsync(new TripleColumns(0, 1, 2), cts.Token)));
    }

    [Fact]
    public async Task WideUnbound_WhenCancelledAndFirstRowUnreadable_ThenCancellationWins()
    {
        // Precedence: cancellation is checked before the advance that would fail, so an abandoned
        // read reports cancellation rather than a source problem it never actually hit.
        var session = WideSession(new string('x', 20 * 1024 * 1024) + "\n");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DrainAsync(session.ReadAsync(cts.Token)));

        Assert.IsNotType<SourceReadException>(thrown);
    }

    [Fact]
    public async Task TripleUnbound_WhenCancelledAndFirstRowUnreadable_ThenCancellationWins()
    {
        var session = TripleSession(new string('x', 20 * 1024 * 1024) + "\n");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DrainAsync(session.ReadRowsAsync(new TripleColumns(0, 1, 2), cts.Token)));
    }

    [Fact]
    public async Task GetSchemaAsync_WhenCancelled_ThenNothingCachedAndNextRetries()
    {
        var session = WideSession("a,b\n", hasHeader: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.GetSchemaAsync(cts.Token).AsTask());
        var schema = await session.GetSchemaAsync();

        Assert.Equal(["a", "b"], schema.Header);
    }

    // --- 7. The typed read-failure channel ---

    [Fact]
    public async Task WideUnbound_WhenRowExceedsSepsLimit_ThenSourceReadExceptionWithInnerCause()
    {
        // Sep 0.15.0 signals its row/buffer ceiling as NotSupportedException ("Buffer or row has
        // reached maximum supported length of 16777216"). The adapter normalizes it here so no
        // consumer needs to know Sep exists.
        var session = WideSession(new string('x', 20 * 1024 * 1024) + "\n");

        var thrown = await Assert.ThrowsAsync<SourceReadException>(() => DrainAsync(session.ReadAsync()));

        Assert.IsType<NotSupportedException>(thrown.InnerException);
    }

    [Fact]
    public async Task TripleUnbound_WhenRowExceedsSepsLimit_ThenSourceReadExceptionWithInnerCause()
    {
        var session = TripleSession(new string('x', 20 * 1024 * 1024) + "\n");

        var thrown = await Assert.ThrowsAsync<SourceReadException>(
            () => DrainAsync(session.ReadRowsAsync(new TripleColumns(0, 1, 2))));

        Assert.IsType<NotSupportedException>(thrown.InnerException);
    }

    [Fact]
    public async Task GetSchemaAsync_WhenRowExceedsSepsLimit_ThenSourceReadExceptionAndNothingCached()
    {
        var attempts = 0;
        var session = new WideCsvSession(
            () =>
            {
                attempts++;
                return new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 20 * 1024 * 1024) + "\n"));
            },
            SourceReadSettings.CreateWide(hasHeader: true));

        await Assert.ThrowsAsync<SourceReadException>(() => session.GetSchemaAsync().AsTask());
        await Assert.ThrowsAsync<SourceReadException>(() => session.GetSchemaAsync().AsTask());

        // A failed schema read caches nothing, so the next call genuinely retries (D-098).
        Assert.Equal(2, attempts);
    }

    // A stream that claims to be readable but throws NotSupportedException from Read — a
    // provider/programmer contract failure, NOT Sep's row/buffer ceiling.
    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("this stream cannot be read");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Unbound_WhenStreamThrowsNotSupported_ThenItIsNotAbsorbedAsAReadFailure()
    {
        // The counterexample that keeps the normalization honest: only Sep's OWN failure becomes
        // SourceReadException. A misbehaving stream surfaces as itself, so Slice 3 cannot
        // mistranslate a contract violation into ProbeSourceReadFailed.
        var session = new WideCsvSession(() => new UnreadableStream(), SourceReadSettings.CreateWide());

        var thrown = await Assert.ThrowsAsync<NotSupportedException>(() => DrainAsync(session.ReadAsync()));

        Assert.Equal("this stream cannot be read", thrown.Message);
        Assert.IsNotType<SourceReadException>(thrown);
    }

    [Fact]
    public async Task Unbound_WhenStreamThrowsNotSupported_ThenTripleReadsAlsoPropagateIt()
    {
        var session = new TripleCsvSession(() => new UnreadableStream(), SourceReadSettings.CreateTriple());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => DrainAsync(session.ReadRowsAsync(new TripleColumns(0, 1, 2))));
    }

    [Fact]
    public async Task GetSchemaAsync_WhenStreamThrowsNotSupported_ThenItIsNotAbsorbedAsAReadFailure()
    {
        var session = new WideCsvSession(() => new UnreadableStream(), SourceReadSettings.CreateWide());

        await Assert.ThrowsAsync<NotSupportedException>(() => session.GetSchemaAsync().AsTask());
    }

    [Fact]
    public async Task Unbound_WhenStreamFactoryFails_ThenThatExceptionPropagatesUnwrapped()
    {
        // A stream-factory failure is not normalized here: Sources must not manufacture a
        // catch-all, and programmer/infrastructure exceptions keep their own identity (P-14).
        var session = new WideCsvSession(
            () => throw new InvalidOperationException("transient open failure"),
            SourceReadSettings.CreateWide());

        await Assert.ThrowsAsync<InvalidOperationException>(() => DrainAsync(session.ReadAsync()));
    }

    // --- Resolutions for the Bind legs ---

    private static ResolvedSpec WideResolution(SourceSchema schema)
    {
        var spec = new BedrockSpec(
            new Binding(SourceShape.Wide, "utf-8", ',', '"', HasHeader: false, "invariant", "?", new RowIndexObjectKey()),
            [new AttributeSpec("g", new ColumnSource(0, SourceValueType.String), Include: true,
                new IdentityDiscretizer(), new NominalScale(), ["a"], RestrictTo: [],
                new Dictionary<string, string>(), MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        return ResolvedSpec.Create(spec, schema, SourceReadSettings.CreateWide(hasHeader: false), []);
    }

    private static ResolvedSpec TripleResolution(SourceSchema schema, TripleColumns roles)
    {
        var binding = new Binding(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", "?",
            new ColumnObjectKey(roles.Subject, DuplicateObjectPolicy.Fail), roles, TripleOrdering.Unordered);
        var spec = new BedrockSpec(binding,
            [new AttributeSpec("g", new PredicateSource("p", SourceValueType.String), Include: true,
                new IdentityDiscretizer(), new NominalScale(), ["a"], RestrictTo: [],
                new Dictionary<string, string>(), MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        return ResolvedSpec.Create(spec, schema, SourceReadSettings.CreateTriple(), []);
    }
}
