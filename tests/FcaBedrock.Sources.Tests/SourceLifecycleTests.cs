using FcaBedrock.Core.Spec;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The delimited read's resource lifetime. From the moment the stream factory returns, the read
/// owns the stream, its decoder and the provider state; each is closed exactly once, the stream
/// last, after every other close was attempted. A selected primary failure (a read failure,
/// cancellation, a refused record) is never replaced by a close failure, which is kept in the
/// primary's <see cref="Exception.Data"/>; a close failure with no primary fails the read. A
/// failed schema read caches nothing, so the next one retries.
/// </summary>
public sealed class SourceLifecycleTests
{
    private const string SecondaryKey = "FcaBedrock.Sources.SecondaryCleanupFailures";
    private const string Data = "a,b\n1,2\n3,4\n5,6\n";

    private static IReadOnlyList<Exception>? Secondary(Exception exception) =>
        exception.Data[SecondaryKey] as IReadOnlyList<Exception>;

    [Fact]
    public async Task Read_WhenTheStreamFactoryThrows_ThenItsExceptionPropagatesAndNothingIsOwned()
    {
        var failure = new IOException("factory");

        var thrown = await Assert.ThrowsAsync<IOException>(
            () => DrainAsync(WideSession(() => throw failure, hasHeader: true).ReadAsync()));

        Assert.Same(failure, thrown);
        Assert.Null(Secondary(thrown));
    }

    [Fact]
    public async Task Read_WhenTheStreamCannotBeRead_ThenTheDecoderRefusalPropagatesAndTheStreamIsClosedOnce()
    {
        var stream = new UnreadableStream();

        await Assert.ThrowsAsync<ArgumentException>(() => DrainAsync(WideSession(() => stream, hasHeader: true).ReadAsync()));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenTheFirstReadFails_ThenTheFailureIsThePrimaryAndTheStreamIsClosedOnce()
    {
        var failure = new IOException("primary read");
        var stream = new ScriptedStream(Utf8(Data)) { ReadFailure = failure };

        var thrown = await Assert.ThrowsAsync<IOException>(() => DrainAsync(WideSession(() => stream, hasHeader: true).ReadAsync()));

        Assert.Same(failure, thrown);
        Assert.Null(Secondary(thrown));
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenTheFirstReadFailsAndTheCloseFails_ThenTheCloseFailureRidesOnThePrimary()
    {
        var failure = new IOException("primary read");
        var close = new IOException("secondary close");
        var stream = new ScriptedStream(Utf8(Data)) { ReadFailure = failure, DisposeFailure = close };

        var thrown = await Assert.ThrowsAsync<IOException>(() => DrainAsync(WideSession(() => stream, hasHeader: true).ReadAsync()));

        Assert.Same(failure, thrown);
        Assert.Same(close, Assert.Single(Secondary(thrown)!));
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenAReadFailsAfterRecordsWereYieldedAndTheCloseFails_ThenTheReadFailureIsKept()
    {
        var text = "a,b\n" + string.Concat(Enumerable.Range(0, 20_000).Select(i => $"{i},x\n"));
        var bytes = Utf8(text);
        var failure = new IOException("primary read");
        var close = new IOException("secondary close");
        var stream = new ScriptedStream(bytes) { MaxChunk = 100, ReadFailure = failure, ReadFailureOffset = bytes.Length / 2, DisposeFailure = close };

        var (records, error) = await DrainCapturingAsync(WideSession(() => stream, hasHeader: true).ReadAsync());

        Assert.NotEmpty(records);
        Assert.Same(failure, error);
        Assert.Same(close, Assert.Single(Secondary(error!)!));
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenARecordIsRefusedAndTheCloseFails_ThenTheRefusalIsKept()
    {
        var close = new IOException("secondary close");
        var stream = new ScriptedStream(Utf8("a,b\n1,x\"y\n")) { DisposeFailure = close };

        var (_, error) = await DrainCapturingAsync(WideSession(() => stream, hasHeader: true).ReadAsync());

        var refused = Assert.IsType<SourceReadException>(error);
        Assert.Same(close, Assert.Single(Secondary(refused)!));
        Assert.Equal(1, stream.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_WhenTheInputEnds_ThenTheStreamIsClosedOnceAndOnlyAFailingCloseFailsTheRead(bool closeFails)
    {
        var close = new IOException("sole close");
        var stream = new ScriptedStream(Utf8(Data)) { DisposeFailure = closeFails ? close : null };

        var (records, error) = await DrainCapturingAsync(WideSession(() => stream, hasHeader: true).ReadAsync());

        Assert.Equal(3, records.Count);
        Assert.Equal(1, stream.DisposeCount);
        if (closeFails)
        {
            Assert.Same(close, error);
        }
        else
        {
            Assert.Null(error);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_WhenTheConsumerStopsEarly_ThenTheStreamIsClosedOnceAndAFailingCloseFailsTheEnumeration(bool closeFails)
    {
        // The iterator cannot know why its consumer stopped, so a failing close is the operation's own
        // failure; the consumer decides whether something it selected wins over it.
        var close = new IOException("sole close");
        var stream = new ScriptedStream(Utf8(Data)) { DisposeFailure = closeFails ? close : null };
        var records = WideSession(() => stream, hasHeader: true).ReadAsync();

        async Task StopAfterOneAsync()
        {
            await foreach (var _ in records)
            {
                break;
            }
        }

        if (closeFails)
        {
            Assert.Same(close, await Assert.ThrowsAsync<IOException>(StopAfterOneAsync));
        }
        else
        {
            await StopAfterOneAsync();
        }

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenCancelledAndTheCloseFails_ThenCancellationIsThePrimaryAndCarriesTheCloseFailure()
    {
        using var cts = new CancellationTokenSource();
        var close = new IOException("close failed");
        var stream = new ScriptedStream(Utf8("a,b\nc,d\ne,f\n")) { DisposeFailure = close };

        var (records, error) = await DrainCapturingAsync(WideSession(() => stream).ReadAsync(cts.Token), afterItem: _ => cts.Cancel());

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Single(records);
        Assert.Equal(cts.Token, canceled.CancellationToken);
        Assert.Same(close, Assert.Single(Secondary(canceled)!));
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Schema_WhenTheCloseFails_ThenTheSchemaReadFailsCachesNothingAndTheNextReadRetries()
    {
        var close = new IOException("sole close");
        var opens = 0;
        var session = new WideCsvSession(
            () => ++opens == 1 ? new ScriptedStream(Utf8(Data)) { DisposeFailure = close } : ScriptedStream.Utf8(Data),
            SourceReadSettings.CreateWide());

        var thrown = await Assert.ThrowsAsync<IOException>(async () => await session.GetSchemaAsync());
        var schema = await session.GetSchemaAsync();
        var cached = await session.GetSchemaAsync();

        Assert.Same(close, thrown);
        Assert.Equal(["a", "b"], schema.Header);
        Assert.Same(schema, cached);
        Assert.Equal(2, opens);
    }

    private sealed class UnreadableStream : Stream
    {
        public int DisposeCount { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
