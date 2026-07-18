using System.Globalization;
using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The M5-IP-008 failure boundary: which exceptions become <c>ProbeSourceReadFailed</c>, and —
/// just as load-bearing — which do not.
/// <para>
/// A catch-all would be the easy implementation and the wrong one: it converts genuine bugs
/// (a null dereference, a misused API, a violated invariant) into polite diagnostics a caller
/// would try to handle, hiding the defect (P-14). So the engine catches a closed, explicit set
/// of expected provider/read failures, and the counterexamples below are what prove the filter
/// is actually narrow rather than merely described as narrow. They also cover what an
/// architecture rule cannot see: ArchUnitNET does not reliably surface catch-handler metadata,
/// so behaviour is the enforcement (M5-IP-CX-002).
/// </para>
/// </summary>
public sealed class ProbeReadFailureTests
{
    private static readonly SourceSchema Schema = new(1, ["a"]);

    private static void AssertReadFailed(Diagnosed<SpecDocument> result, string expectedTypeName)
    {
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(DiagnosticCode.ProbeSourceReadFailed, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(expectedTypeName, diagnostic.Message, StringComparison.Ordinal);

        // No draft, ever — not even a partial one.
        Assert.True(result.HasErrors);
        Assert.False(result.TryGetValue(out _));
        Assert.Null(result.Value);
    }

    // Every family the settled boundary admits. Constructed as factories so each test gets a
    // fresh instance and no exception is reused across cases.
    public static TheoryData<string, Func<Exception>> ExpectedFailures() => new()
    {
        { nameof(SourceReadException), () => new SourceReadException("adapter-normalized read failure") },
        { nameof(IOException), () => new IOException("device not ready") },
        { nameof(UnauthorizedAccessException), () => new UnauthorizedAccessException("denied") },
        { nameof(ObjectDisposedException), () => new ObjectDisposedException("stream") },
        { nameof(DecoderFallbackException), () => new DecoderFallbackException("bad bytes") },
        { nameof(InvalidDataException), () => new InvalidDataException("corrupt") },
    };

    // Failures that must NOT be absorbed. Each names a real hazard: an invalid operation or a
    // bad argument is a contract bug in the caller or the adapter, a null dereference is a bug
    // in either, and NotSupportedException is deliberately excluded because the CSV adapter
    // already normalizes Sep's row/buffer ceiling to SourceReadException — catching it here
    // would silently swallow genuine "this source cannot do that" errors instead.
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

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task Probe_WhenSchemaReadFails_ThenReportsProbeSourceReadFailed(string typeName, Func<Exception> failure)
    {
        var session = new ProbeFixtures.FakeWideSession(Schema, [], schemaFailure: failure);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        AssertReadFailed(result, typeName);
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task Probe_WhenRecordEnumerationFails_ThenReportsProbeSourceReadFailed(string typeName, Func<Exception> failure)
    {
        var session = new ProbeFixtures.FakeWideSession(
            Schema, ProbeFixtures.Records(["x"], ["y"]), recordFailure: failure, failAfterRecords: 1);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        AssertReadFailed(result, typeName);
        Assert.Equal(1, session.RecordsYielded);
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task Probe_WhenReadAsyncItselfThrows_ThenReportsProbeSourceReadFailed(string typeName, Func<Exception> failure)
    {
        // Record acquisition is two calls, not one, and the first is ReadAsync. A compiler-
        // generated async iterator can never fail here — its body does not run until the first
        // MoveNextAsync — so every adapter in this repo hides the gap. A hand-written session,
        // which the D-109 seam exists to permit, can fail on the call itself.
        var session = new SyncThrowingReadSession(Schema, failure);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        AssertReadFailed(result, typeName);
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task Probe_WhenGetAsyncEnumeratorThrows_ThenReportsProbeSourceReadFailed(string typeName, Func<Exception> failure)
    {
        // The second acquisition call. A session that opens its underlying reader eagerly when
        // enumeration begins fails exactly here, and that is a read failure however early it lands.
        var session = new ThrowingEnumeratorSession(Schema, failure);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        AssertReadFailed(result, typeName);
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task Probe_WhenReadAsyncThrowsAnUnexpectedException_ThenItPropagates(Func<Exception> failure)
    {
        var session = new SyncThrowingReadSession(Schema, failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task Probe_WhenGetAsyncEnumeratorThrowsAnUnexpectedException_ThenItPropagates(Func<Exception> failure)
    {
        var session = new ThrowingEnumeratorSession(Schema, failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task Probe_WhenCurrentThrows_ThenReportsProbeSourceReadFailed(string typeName, Func<Exception> failure)
    {
        // The third provider-owned call in the pass. An enumerator that materializes its row
        // lazily does its real work in Current, not MoveNextAsync — so a read failure can land
        // here just as easily. Compiler-generated iterators cache the value and never throw from
        // Current, which is why only a hand-written enumerator can express this.
        var session = new ThrowingCurrentSession(Schema, failure, throwAt: 0);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        AssertReadFailed(result, typeName);
    }

    [Fact]
    public async Task Probe_WhenCurrentThrowsAfterAGoodRecord_ThenNoDraftLeaks()
    {
        // Partial observation followed by a Current failure: the engine holds real state, which is
        // exactly when returning "what we have" would be tempting and wrong (D-112).
        var session = new ThrowingCurrentSession(Schema, () => new IOException("lazy row failed"), throwAt: 1);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        AssertReadFailed(result, nameof(IOException));
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task Probe_WhenCurrentThrowsAnUnexpectedException_ThenItPropagates(Func<Exception> failure)
    {
        var session = new ThrowingCurrentSession(Schema, failure, throwAt: 0);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Fact]
    public async Task Probe_WhenCurrentThrowsCancellation_ThenCancellationStillWins()
    {
        var session = new ThrowingCurrentSession(Schema, () => new OperationCanceledException(), throwAt: 0);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.IsNotType<SourceReadException>(thrown);
    }

    // The other side of the boundary — that OBSERVING a record is outside the classification
    // region, so an engine defect propagates rather than becoming a polite diagnostic — is not
    // expressible as a test here: ObjectRecord is sealed, so no record can be built whose
    // accessor throws, and nothing in the observation body can raise an admitted exception type.
    // It is verified by direct inspection of the catch boundary instead (M5-IP-CX-002's posture).

    [Fact]
    public async Task Probe_WhenCanceledAtAcquisition_ThenCancellationStillWins()
    {
        // The acquisition guard must not swallow cancellation any more than the iteration guard
        // does: OperationCanceledException matches none of the admitted families, so it
        // propagates from ReadAsync exactly as it would from MoveNextAsync.
        var session = new SyncThrowingReadSession(Schema, () => new OperationCanceledException());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.IsNotType<SourceReadException>(thrown);
    }

    [Fact]
    public async Task Probe_WhenReadFailsAfterPartialObservation_ThenNoDraftLeaks()
    {
        // The tempting bug: records were already observed, so "return what we have" looks
        // helpful. It is not — a draft built from a truncated read understates the data while
        // reading as complete (D-112).
        var session = new ProbeFixtures.FakeWideSession(
            Schema,
            ProbeFixtures.Records(["x"], ["y"], ["z"]),
            recordFailure: () => new IOException("mid-stream"),
            failAfterRecords: 2);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        AssertReadFailed(result, nameof(IOException));
        Assert.Equal(2, session.RecordsYielded);
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task Probe_WhenSchemaReadThrowsAnUnexpectedException_ThenItPropagates(Func<Exception> failure)
    {
        var session = new ProbeFixtures.FakeWideSession(Schema, [], schemaFailure: failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public async Task Probe_WhenRecordEnumerationThrowsAnUnexpectedException_ThenItPropagates(Func<Exception> failure)
    {
        var session = new ProbeFixtures.FakeWideSession(
            Schema, ProbeFixtures.Records(["x"]), recordFailure: failure, failAfterRecords: 0);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Prober.ProbeAsync(session, ProbeFixtures.WideSettings()));

        Assert.Equal(failure().GetType(), thrown.GetType());
    }

    [Fact]
    public async Task Probe_WhenReadFails_ThenTheMessageCarriesNoProviderTextOrPath()
    {
        // Deterministic wording: the exception TYPE says which channel failed, but its message
        // may name a machine-specific path, a locale-formatted number, or a stack trace — none
        // of which may reach a diagnostic that must be byte-repeatable (D-112).
        var session = new ProbeFixtures.FakeWideSession(
            Schema, [], schemaFailure: () => new IOException(@"C:\Users\someone\secret\data.csv is locked"));

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        Assert.DoesNotContain("secret", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("locked", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenReadFails_ThenTheDiagnosticIsRepeatable()
    {
        // No Context payload and no interpolated instance state, so two runs over the same
        // failure produce value-equal diagnostics.
        static ProbeFixtures.FakeWideSession Session() =>
            new(Schema, [], schemaFailure: () => new IOException("x"));

        var first = await Prober.ProbeAsync(Session(), ProbeFixtures.WideSettings());
        var second = await Prober.ProbeAsync(Session(), ProbeFixtures.WideSettings());

        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Null(first.Diagnostics[0].Context);
    }

    [Fact]
    public async Task Probe_WhenTheRealCsvAdapterHitsAnUnreadableStream_ThenTheFailureIsClassified()
    {
        // End-to-end through the production adapter rather than a fake: a stream that refuses to
        // be read surfaces as an ordinary read failure, proving the mapping is reachable from
        // the shipping path and not only from test doubles.
        var settings = ProbeFixtures.WideSettings();
        var session = new WideCsvSession(() => new UnreadableStream(), settings);

        var result = await Prober.ProbeAsync(session, settings);

        AssertReadFailed(result, nameof(IOException));
    }

    [Fact]
    public async Task Probe_WhenTheRealCsvAdapterReadsCleanly_ThenNoReadFailureIsReported()
    {
        // The control for the case above: the same adapter over a healthy stream must not
        // produce the diagnostic, so the classification cannot be firing for an unrelated reason.
        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\n");

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeSourceReadFailed);
    }

    // A session whose ReadAsync is an ordinary method, so its body runs — and can fail — on the
    // call itself rather than on the first MoveNextAsync. Written by hand precisely because the
    // `async IAsyncEnumerable` iterators used everywhere else structurally cannot reach this case.
    private sealed class SyncThrowingReadSession(SourceSchema schema, Func<Exception> failure) : IWideSourceSession
    {
        public SourceShape Shape => SourceShape.Wide;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schema);

        public IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default) =>
            throw failure();
    }

    // A session that returns an enumerable fine but fails when enumeration is actually opened —
    // the shape of an adapter that acquires its underlying reader in GetAsyncEnumerator.
    private sealed class ThrowingEnumeratorSession(SourceSchema schema, Func<Exception> failure) : IWideSourceSession
    {
        public SourceShape Shape => SourceShape.Wide;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schema);

        public IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default) =>
            new ThrowingEnumerable(failure);

        private sealed class ThrowingEnumerable(Func<Exception> failure) : IAsyncEnumerable<ObjectRecord>
        {
            public IAsyncEnumerator<ObjectRecord> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
                throw failure();
        }
    }

    // A session whose enumerator fails from Current rather than from MoveNextAsync — the shape of
    // an adapter that materializes each row lazily. Compiler-generated iterators cache the yielded
    // value, so Current cannot throw in them; only a hand-written enumerator reaches this path.
    private sealed class ThrowingCurrentSession(SourceSchema schema, Func<Exception> failure, int throwAt)
        : IWideSourceSession
    {
        public SourceShape Shape => SourceShape.Wide;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schema);

        public IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default) =>
            new Enumerable(failure, throwAt);

        private sealed class Enumerable(Func<Exception> failure, int throwAt) : IAsyncEnumerable<ObjectRecord>
        {
            public IAsyncEnumerator<ObjectRecord> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
                new Enumerator(failure, throwAt);
        }

        // Yields `throwAt` sound records, then reports one more and throws when it is read.
        private sealed class Enumerator(Func<Exception> failure, int throwAt) : IAsyncEnumerator<ObjectRecord>
        {
            private int _index = -1;

            public ObjectRecord Current =>
                _index == throwAt
                    ? throw failure()
                    : new ObjectRecord(_index.ToString(CultureInfo.InvariantCulture), ["value"]);

            public ValueTask<bool> MoveNextAsync()
            {
                _index++;
                return ValueTask.FromResult(_index <= throwAt);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // A readable-in-name-only stream: the tokenizer's own read call fails with an IOException,
    // which is exactly the "storage misbehaved" class M5-IP-008 admits.
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
