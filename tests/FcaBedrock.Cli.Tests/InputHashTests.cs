using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The input-stability policy at its own seam (D-122 part 5). The digest is
/// always checked against an <b>independent</b> <see cref="SHA256"/> over the exact bytes the
/// stream handed out — never against another run of the same code.
/// </summary>
public sealed class InputHashTests
{
    private static string ExpectedDigest(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static InputHashTracker TrackerOver(params byte[][] passes)
    {
        var index = 0;
        return new InputHashTracker(
            _ => new MemoryStream(passes[Math.Min(index++, passes.Length - 1)], writable: false), "data.csv");
    }

    private static void DrainSync(Stream stream, int chunk)
    {
        var buffer = new byte[chunk];
        while (stream.Read(buffer, 0, buffer.Length) > 0)
        {
        }
    }

    private static async Task DrainAsync(Stream stream, int chunk)
    {
        var buffer = new byte[chunk];
        while (await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)) > 0)
        {
        }
    }

    // ---- the digest is over raw consumed bytes ----------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(4096)]
    public void Digest_WhenReadInAwkwardChunks_ThenMatchesAnIndependentHashOfTheRawBytes(int chunk)
    {
        // A byte-order mark, CRLF endings, a quote, a delimiter, and multi-byte UTF-8 — every
        // one of which a decoder would alter and a raw hash must not.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("a,\"b\"\r\nré中,\t\r\n"))
            .ToArray();
        var tracker = TrackerOver(bytes);

        using (var stream = tracker.OpenHashed())
        {
            DrainSync(stream, chunk);
        }

        Assert.Equal(1, tracker.CompletedPasses);
        Assert.Equal(ExpectedDigest(bytes), tracker.Digest);
        Assert.False(tracker.HasMismatch);
    }

    [Fact]
    public async Task Digest_WhenReadAsynchronously_ThenMatchesTheSynchronousRawHash()
    {
        var bytes = Encoding.UTF8.GetBytes("colour,size\nred,1\ngreen,2\n");
        var tracker = TrackerOver(bytes);

        await using (var stream = tracker.OpenHashed())
        {
            await DrainAsync(stream, 5);
        }

        Assert.Equal(ExpectedDigest(bytes), tracker.Digest);
    }

    [Fact]
    public void Digest_WhenReadThroughEveryReadOverload_ThenAllAgree()
    {
        // Sources reaches the stream through a StreamReader, which picks its overload by
        // framework version and buffer state; every one of them must feed the same hash.
        var bytes = Encoding.UTF8.GetBytes("0123456789");
        var expected = ExpectedDigest(bytes);

        var byArray = TrackerOver(bytes);
        using (var stream = byArray.OpenHashed())
        {
            DrainSync(stream, 4);
        }

        var bySpan = TrackerOver(bytes);
        using (var stream = bySpan.OpenHashed())
        {
            Span<byte> buffer = stackalloc byte[3];
            while (stream.Read(buffer) > 0)
            {
            }
        }

        var byByte = TrackerOver(bytes);
        using (var stream = byByte.OpenHashed())
        {
            while (stream.ReadByte() >= 0)
            {
            }
        }

        Assert.Equal(expected, byArray.Digest);
        Assert.Equal(expected, bySpan.Digest);
        Assert.Equal(expected, byByte.Digest);
    }

    [Fact]
    public async Task Digest_WhenReadThroughTheLegacyAsyncOverload_ThenMatches()
    {
        var bytes = Encoding.UTF8.GetBytes("abcdefgh");
        var tracker = TrackerOver(bytes);

        await using (var stream = tracker.OpenHashed())
        {
            var buffer = new byte[3];

            // The array overload is exactly what this test exists to exercise: it is a real
            // Stream API a consumer may pick, so the wrapper must hash through it too.
#pragma warning disable CA1835 // Prefer the Memory-based overload — deliberately not, here.
            while (await stream.ReadAsync(buffer, 0, buffer.Length, TestContext.Current.CancellationToken) > 0)
            {
            }
#pragma warning restore CA1835
        }

        Assert.Equal(ExpectedDigest(bytes), tracker.Digest);
    }

    [Fact]
    public void Digest_WhenTheInputIsEmpty_ThenTheEmptyHashCompletesOnePass()
    {
        var empty = Array.Empty<byte>();
        var tracker = TrackerOver(empty);

        using (var stream = tracker.OpenHashed())
        {
            DrainSync(stream, 8);
        }

        Assert.Equal(1, tracker.CompletedPasses);
        Assert.Equal(ExpectedDigest(empty), tracker.Digest);
    }

    // ---- what counts as a completed pass ----------------------------------------------

    [Fact]
    public void CompletedPasses_WhenAZeroLengthReadIsRequested_ThenItIsNotEndOfStream()
    {
        // A caller asking for nothing also gets nothing back; treating that as EOF would
        // finalize a pass that had consumed no bytes at all.
        var tracker = TrackerOver(Encoding.UTF8.GetBytes("abc"));

        using var stream = tracker.OpenHashed();
        Assert.Equal(0, stream.Read([], 0, 0));

        Assert.Equal(0, tracker.CompletedPasses);
        Assert.Null(tracker.Digest);
    }

    [Fact]
    public void CompletedPasses_WhenAPassStopsShortAndIsDisposed_ThenItIsDiscarded()
    {
        // The shape of schema acquisition on a source larger than one buffer: some bytes are
        // consumed, end of stream is never seen, and the prefix must not become a digest.
        var tracker = TrackerOver(Encoding.UTF8.GetBytes("colour\nred\ngreen\n"));

        using (var stream = tracker.OpenHashed())
        {
            var buffer = new byte[4];
            Assert.Equal(4, stream.Read(buffer, 0, buffer.Length));
        }

        Assert.Equal(0, tracker.CompletedPasses);
        Assert.Null(tracker.Digest);
        Assert.False(tracker.HasMismatch);
    }

    [Fact]
    public void CompletedPasses_WhenAPassFailsMidStream_ThenItIsDiscardedAndDisposed()
    {
        var inner = new FailingStream(Encoding.UTF8.GetBytes("abcdefgh"), 4);
        var tracker = new InputHashTracker(_ => inner, "data.csv");

        using (var stream = tracker.OpenHashed())
        {
            Assert.Throws<IOException>(() => DrainSync(stream, 4));
        }

        Assert.Equal(0, tracker.CompletedPasses);
        Assert.Null(tracker.Digest);
        Assert.True(inner.Disposed);
    }

    [Fact]
    public void CompletedPasses_WhenAPassIsCancelledMidStream_ThenItIsDiscardedAndDisposed()
    {
        using var cancellation = new CancellationTokenSource();
        var inner = new TrackingStream(Encoding.UTF8.GetBytes("abcdefgh"));
        var tracker = new InputHashTracker(_ => inner, "data.csv");

        using (var stream = tracker.OpenHashed())
        {
            var buffer = new byte[4];
            Assert.Equal(4, stream.Read(buffer, 0, buffer.Length));
            cancellation.Cancel();
            Assert.True(cancellation.IsCancellationRequested);
        }

        Assert.Equal(0, tracker.CompletedPasses);
        Assert.True(inner.Disposed);
    }

    // ---- stability across passes -------------------------------------------------------

    [Fact]
    public void HasMismatch_WhenASinglePassCompletes_ThenTheRunIsStable()
    {
        var tracker = TrackerOver(Encoding.UTF8.GetBytes("stable"));

        using (var stream = tracker.OpenHashed())
        {
            DrainSync(stream, 8);
        }

        Assert.False(tracker.HasMismatch);
    }

    [Fact]
    public void HasMismatch_WhenReplayPassesAgree_ThenTheRunIsStable()
    {
        var bytes = Encoding.UTF8.GetBytes("stable across passes");
        var tracker = TrackerOver(bytes, bytes, bytes);

        for (var pass = 0; pass < 3; pass++)
        {
            using var stream = tracker.OpenHashed();
            DrainSync(stream, 6);
        }

        Assert.Equal(3, tracker.CompletedPasses);
        Assert.False(tracker.HasMismatch);
        Assert.Equal(ExpectedDigest(bytes), tracker.Digest);
    }

    [Fact]
    public void HasMismatch_WhenAReplayPassDiffers_ThenTheRunIsUnstable()
    {
        var tracker = TrackerOver(Encoding.UTF8.GetBytes("first"), Encoding.UTF8.GetBytes("secnd"));

        for (var pass = 0; pass < 2; pass++)
        {
            using var stream = tracker.OpenHashed();
            DrainSync(stream, 8);
        }

        Assert.Equal(2, tracker.CompletedPasses);
        Assert.True(tracker.HasMismatch);

        // The first completed pass stays the reference; a later one never replaces it.
        Assert.Equal(ExpectedDigest(Encoding.UTF8.GetBytes("first")), tracker.Digest);
    }

    [Fact]
    public void HasMismatch_WhenAnIncompletePassDiffers_ThenItIsIgnored()
    {
        var tracker = TrackerOver(Encoding.UTF8.GetBytes("aaaaaaaa"), Encoding.UTF8.GetBytes("bbbbbbbb"));

        using (var stream = tracker.OpenHashed())
        {
            DrainSync(stream, 4);
        }

        using (var stream = tracker.OpenHashed())
        {
            var buffer = new byte[2];
            Assert.Equal(2, stream.Read(buffer, 0, buffer.Length));
        }

        Assert.Equal(1, tracker.CompletedPasses);
        Assert.False(tracker.HasMismatch);
    }

    // ---- the wrapper's own contract ----------------------------------------------------

    [Fact]
    public void HashingStream_ShouldRefuseToSeek()
    {
        // Repositioning would skip or repeat bytes, so the digest would stop describing what
        // the pass actually consumed. Reporting CanSeek = false is what stops a consumer trying.
        var tracker = TrackerOver(Encoding.UTF8.GetBytes("abc"));

        using var stream = tracker.OpenHashed();

        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.True(stream.CanRead);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
    }

    [Fact]
    public void HashingStream_WhenDisposed_ThenTheUnderlyingStreamIsDisposedToo()
    {
        var inner = new TrackingStream(Encoding.UTF8.GetBytes("abc"));
        var tracker = new InputHashTracker(_ => inner, "data.csv");

        using (var stream = tracker.OpenHashed())
        {
            DrainSync(stream, 8);
        }

        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task HashingStream_WhenDisposedAsynchronously_ThenTheUnderlyingStreamIsDisposedToo()
    {
        var inner = new TrackingStream(Encoding.UTF8.GetBytes("abc"));
        var tracker = new InputHashTracker(_ => inner, "data.csv");

        await using (var stream = tracker.OpenHashed())
        {
            await DrainAsync(stream, 8);
        }

        Assert.True(inner.Disposed);
    }

    [Fact]
    public void HashingStream_WhenDisposedTwice_ThenNothingIsCompletedTwice()
    {
        var tracker = TrackerOver(Encoding.UTF8.GetBytes("abc"));

        var stream = tracker.OpenHashed();
        DrainSync(stream, 8);
        stream.Dispose();
        stream.Dispose();

        Assert.Equal(1, tracker.CompletedPasses);
    }

    /// <summary>A stream that hands out a prefix and then fails, like a truncated read.</summary>
    private sealed class FailingStream(byte[] bytes, int failAfter) : MemoryStream(bytes, writable: false)
    {
        public bool Disposed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) =>
            Position >= failAfter ? throw new IOException("the read failed.") : base.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            Position >= failAfter ? throw new IOException("the read failed.") : base.Read(buffer);

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>A stream that records whether it was disposed.</summary>
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
