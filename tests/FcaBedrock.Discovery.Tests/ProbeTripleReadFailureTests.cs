using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The M5-IP-008 failure boundary and the D-112 cancellation posture, for the triple engine:
/// which exceptions become <c>ProbeSourceReadFailed</c>, which propagate as the bugs they are,
/// and why cancellation is neither.
/// <para>
/// The triple path has its own four provider-owned calls — <c>GetSchemaAsync</c>,
/// <c>ReadRowsAsync</c>, <c>GetAsyncEnumerator</c>, and the <c>MoveNextAsync</c>/<c>Current</c>
/// pair — so it needs its own coverage rather than inheriting the wide suite's. The
/// counterexamples matter most: a catch-all would convert genuine bugs into polite diagnostics a
/// caller would try to handle (P-14), and ArchUnitNET does not reliably surface catch-handler
/// metadata, so behaviour is the enforcement (M5-IP-CX-002).
/// </para>
/// </summary>
public sealed class ProbeTripleReadFailureTests
{
    private static readonly SourceSchema Schema = new(3);

    private static readonly SourceReadSettings Settings = TripleProbeFixtures.TripleSettings();

    private static void AssertReadFailed(Diagnosed<SpecDocument> result, string expectedTypeName)
    {
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(DiagnosticCode.ProbeSourceReadFailed, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(expectedTypeName, diagnostic.Message, StringComparison.Ordinal);
        Assert.True(result.HasErrors);
        Assert.False(result.TryGetValue(out _));
        Assert.Null(result.Value);
    }

    public static TheoryData<string, Func<Exception>> ExpectedFailures() => new()
    {
        { nameof(SourceReadException), () => new SourceReadException("adapter-normalized read failure") },
        { nameof(IOException), () => new IOException("device not ready") },
        { nameof(UnauthorizedAccessException), () => new UnauthorizedAccessException("denied") },
        { nameof(ObjectDisposedException), () => new ObjectDisposedException("stream") },
        { nameof(DecoderFallbackException), () => new DecoderFallbackException("bad bytes") },
        { nameof(InvalidDataException), () => new InvalidDataException("corrupt") },
    };

    public static TheoryData<Func<Exception>> UnexpectedFailures()
    {
        var data = new TheoryData<Func<Exception>>();
        data.Add(() => new InvalidOperationException("engine misuse"));
        data.Add(() => new ArgumentException("bad argument"));
        data.Add(() => new ArgumentOutOfRangeException("index"));
        data.Add(() => new NotSupportedException("unrelated unsupported operation"));
        data.Add(() => new FormatException("bug"));
        data.Add(() => new InvalidCastException("bug"));
        return data;
    }

    // --- The four provider-owned calls ---------------------------------------------------------

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task ProbeTriple_WhenSchemaReadFails_ThenReportsProbeSourceReadFailed(
        string typeName, Func<Exception> failure)
    {
        var session = new TripleProbeFixtures.FakeTripleSession(Schema, [], schemaFailure: failure);

        var result = await Prober.ProbeTripleAsync(session, Settings);

        AssertReadFailed(result, typeName);
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task ProbeTriple_WhenRowEnumerationFails_ThenReportsProbeSourceReadFailed(
        string typeName, Func<Exception> failure)
    {
        var session = new TripleProbeFixtures.FakeTripleSession(
            Schema,
            TripleProbeFixtures.Rows(("s1", "p", "a"), ("s2", "p", "b")),
            rowFailure: failure,
            failAfterRows: 1);

        var result = await Prober.ProbeTripleAsync(session, Settings);

        AssertReadFailed(result, typeName);
        Assert.Equal(1, session.RowsYielded);
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task ProbeTriple_WhenReadRowsAsyncItselfThrows_ThenReportsProbeSourceReadFailed(
        string typeName, Func<Exception> failure)
    {
        // Row acquisition is two calls before the first row arrives, and this is the first. A
        // compiler-generated async iterator can never fail here — which is exactly why leaving it
        // unguarded looks safe — but a hand-written session, the whole point of the D-109 seam,
        // can.
        var session = new SyncThrowingReadSession(Schema, failure);

        var result = await Prober.ProbeTripleAsync(session, Settings);

        AssertReadFailed(result, typeName);
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task ProbeTriple_WhenGetAsyncEnumeratorThrows_ThenReportsProbeSourceReadFailed(
        string typeName, Func<Exception> failure)
    {
        var session = new ThrowingEnumeratorSession(Schema, failure);

        var result = await Prober.ProbeTripleAsync(session, Settings);

        AssertReadFailed(result, typeName);
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task ProbeTriple_WhenCurrentThrows_ThenReportsProbeSourceReadFailed(
        string typeName, Func<Exception> failure)
    {
        // An enumerator that materializes its row lazily does its real work in Current, not
        // MoveNextAsync — so a read failure can land there just as easily.
        var session = new ThrowingCurrentSession(Schema, failure, throwAt: 0);

        var result = await Prober.ProbeTripleAsync(session, Settings);

        AssertReadFailed(result, typeName);
    }

    // --- Counterexamples: programmer errors are not absorbed -----------------------------------

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task ProbeTriple_WhenSchemaReadThrowsAnUnexpectedException_ThenItPropagates(Func<Exception> failure)
    {
        var session = new TripleProbeFixtures.FakeTripleSession(Schema, [], schemaFailure: failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task ProbeTriple_WhenRowEnumerationThrowsAnUnexpectedException_ThenItPropagates(
        Func<Exception> failure)
    {
        var session = new TripleProbeFixtures.FakeTripleSession(
            Schema, TripleProbeFixtures.Rows(("s1", "p", "a")), rowFailure: failure, failAfterRows: 0);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task ProbeTriple_WhenReadRowsAsyncThrowsAnUnexpectedException_ThenItPropagates(
        Func<Exception> failure)
    {
        var session = new SyncThrowingReadSession(Schema, failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task ProbeTriple_WhenGetAsyncEnumeratorThrowsAnUnexpectedException_ThenItPropagates(
        Func<Exception> failure)
    {
        var session = new ThrowingEnumeratorSession(Schema, failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task ProbeTriple_WhenCurrentThrowsAnUnexpectedException_ThenItPropagates(Func<Exception> failure)
    {
        var session = new ThrowingCurrentSession(Schema, failure, throwAt: 0);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    // --- No partial draft, deterministic wording -----------------------------------------------

    [Fact]
    public async Task ProbeTriple_WhenReadFailsAfterPartialObservation_ThenNoDraftLeaks()
    {
        // The tempting bug: predicates and values were already observed, so "return what we have"
        // looks helpful. It is not — a draft from a truncated read understates the data while
        // reading as complete (D-112).
        var session = new TripleProbeFixtures.FakeTripleSession(
            Schema,
            TripleProbeFixtures.Rows(("s1", "p", "a"), ("s2", "q", "b"), ("s3", "p", "c")),
            rowFailure: () => new IOException("mid-stream"),
            failAfterRows: 2);

        var result = await Prober.ProbeTripleAsync(session, Settings);

        AssertReadFailed(result, nameof(IOException));
        Assert.Equal(2, session.RowsYielded);
    }

    [Fact]
    public async Task ProbeTriple_WhenCurrentThrowsAfterAGoodRow_ThenNoDraftLeaks()
    {
        var session = new ThrowingCurrentSession(Schema, () => new IOException("lazy row failed"), throwAt: 1);

        var result = await Prober.ProbeTripleAsync(session, Settings);

        AssertReadFailed(result, nameof(IOException));
    }

    [Fact]
    public async Task ProbeTriple_WhenReadFails_ThenTheMessageCarriesNoProviderTextOrPath()
    {
        var session = new TripleProbeFixtures.FakeTripleSession(
            Schema, [], schemaFailure: () => new IOException(@"C:\Users\someone\secret\data.csv is locked"));

        var result = await Prober.ProbeTripleAsync(session, Settings);

        Assert.DoesNotContain("secret", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("locked", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Null(result.Diagnostics[0].Context);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheRealCsvAdapterHitsAnUnreadableStream_ThenTheFailureIsClassified()
    {
        // Through the production adapter rather than a fake, so the mapping is proven reachable
        // from the shipping path.
        var session = new TripleCsvSession(() => new UnreadableStream(), Settings);

        var result = await Prober.ProbeTripleAsync(session, Settings);

        AssertReadFailed(result, nameof(IOException));
    }

    [Fact]
    public async Task ProbeTriple_WhenTheRealCsvAdapterReadsCleanly_ThenNoReadFailureIsReported()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,p,a\n");

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeSourceReadFailed);
    }

    // --- Cancellation: never a diagnostic, never a partial document ----------------------------

    [Fact]
    public async Task ProbeTriple_WhenAlreadyCanceled_ThenThrowsBeforeReadingTheSchema()
    {
        var session = TripleProbeFixtures.Fake(("s1", "p", "a"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeTripleAsync(session, Settings, null, null, cts.Token));

        Assert.Equal(0, session.SchemaReads);
        Assert.Equal(0, session.RowEnumerations);
    }

    [Fact]
    public async Task ProbeTriple_WhenCanceledDuringTheSchemaRead_ThenPropagatesUnwrapped()
    {
        // Cancellation must never be reclassified as a read failure: they mean different things
        // to a caller, and only one warrants a retry.
        var session = new TripleProbeFixtures.FakeTripleSession(
            Schema, [], schemaFailure: () => new OperationCanceledException());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.IsNotType<SourceReadException>(thrown);
    }

    [Fact]
    public async Task ProbeTriple_WhenCanceledAtRowAcquisition_ThenCancellationStillWins()
    {
        var session = new SyncThrowingReadSession(Schema, () => new OperationCanceledException());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.IsNotType<SourceReadException>(thrown);
    }

    [Fact]
    public async Task ProbeTriple_WhenCanceledAtGetAsyncEnumerator_ThenCancellationStillWins()
    {
        var session = new ThrowingEnumeratorSession(Schema, () => new OperationCanceledException());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.IsNotType<SourceReadException>(thrown);
    }

    [Fact]
    public async Task ProbeTriple_WhenCurrentThrowsCancellation_ThenCancellationStillWins()
    {
        var session = new ThrowingCurrentSession(Schema, () => new OperationCanceledException(), throwAt: 0);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeTripleAsync(session, Settings));

        Assert.IsNotType<SourceReadException>(thrown);
    }

    [Fact]
    public async Task ProbeTriple_WhenCanceledDuringEnumeration_ThenThrowsWithNoDiagnosticAndNoDocument()
    {
        // Cancelled after the first row: the engine holds real partial state, which is exactly
        // when returning "what we have" would be tempting and wrong.
        using var cts = new CancellationTokenSource();
        var observed = 0;
        var session = new TripleProbeFixtures.FakeTripleSession(
            Schema,
            TripleProbeFixtures.Rows(("s1", "p", "a"), ("s2", "p", "b"), ("s3", "p", "c")),
            beforeEachRow: () =>
            {
                if (++observed == 1)
                {
                    cts.Cancel();
                }
            });

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeTripleAsync(session, Settings, null, null, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.Equal(1, session.RowsYielded);
    }

    // --- Hand-written sessions the compiler-generated iterators cannot express ------------------

    private sealed class SyncThrowingReadSession(SourceSchema schema, Func<Exception> failure) : ITripleSourceSession
    {
        public SourceShape Shape => SourceShape.Triple;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schema);

        public IAsyncEnumerable<TripleRow> ReadRowsAsync(
            TripleColumns columns, CancellationToken cancellationToken = default) =>
            throw failure();
    }

    private sealed class ThrowingEnumeratorSession(SourceSchema schema, Func<Exception> failure) : ITripleSourceSession
    {
        public SourceShape Shape => SourceShape.Triple;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schema);

        public IAsyncEnumerable<TripleRow> ReadRowsAsync(
            TripleColumns columns, CancellationToken cancellationToken = default) =>
            new ThrowingEnumerable(failure);

        private sealed class ThrowingEnumerable(Func<Exception> failure) : IAsyncEnumerable<TripleRow>
        {
            public IAsyncEnumerator<TripleRow> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
                throw failure();
        }
    }

    private sealed class ThrowingCurrentSession(SourceSchema schema, Func<Exception> failure, int throwAt)
        : ITripleSourceSession
    {
        public SourceShape Shape => SourceShape.Triple;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schema);

        public IAsyncEnumerable<TripleRow> ReadRowsAsync(
            TripleColumns columns, CancellationToken cancellationToken = default) =>
            new Enumerable(failure, throwAt);

        private sealed class Enumerable(Func<Exception> failure, int throwAt) : IAsyncEnumerable<TripleRow>
        {
            public IAsyncEnumerator<TripleRow> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
                new Enumerator(failure, throwAt);
        }

        // Yields `throwAt` sound rows, then reports one more and throws when it is read.
        private sealed class Enumerator(Func<Exception> failure, int throwAt) : IAsyncEnumerator<TripleRow>
        {
            private int _index = -1;

            public TripleRow Current =>
                _index == throwAt
                    ? throw failure()
                    : new TripleRow(_index, "s", "p", "v");

            public ValueTask<bool> MoveNextAsync()
            {
                _index++;
                return ValueTask.FromResult(_index <= throwAt);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // A readable-in-name-only stream: the tokenizer's own read call fails with an IOException.
    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("read failed");

        public override int Read(Span<byte> buffer) => throw new IOException("read failed");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
