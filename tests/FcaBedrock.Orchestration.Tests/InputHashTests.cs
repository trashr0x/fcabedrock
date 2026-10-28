using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The input-stability policy at its own seam (D-122 part 5). The digest is
/// always checked against an <b>independent</b> <see cref="SHA256"/> over the exact bytes the
/// stream handed out, never against another run of the same code.
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
        // A byte-order mark, CRLF endings, a quote, a delimiter, and multi-byte UTF-8: every
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
#pragma warning disable CA1835 // Prefer the Memory-based overload: deliberately not, here.
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
        // the pass consumed. Reporting CanSeek = false is what stops a consumer trying.
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

    // ---- through the delimited reader (spec §5.1.1) ------------------------------------------
    //
    // The reader decodes the bytes it is handed and never re-reads, skips or rewrites them, so a
    // pass it completes hashes exactly the original bytes: a byte order mark and CR CR included.
    // Bytes that are not UTF-8 fail the pass, so it completes no digest.

    public static TheoryData<string, byte[]> ReaderInputs() => new()
    {
        { "a byte order mark", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a,b\n1,2\n")] },
        { "quoted CR CR between multibyte characters", Encoding.UTF8.GetBytes("x,\"é\r\r中" + char.ConvertFromUtf32(0x1F600) + "\",z\n \n") },
    };

    private static async Task<List<string>> ReadThroughReaderAsync(InputHashTracker tracker)
    {
        var session = new FcaBedrock.Sources.WideCsvSession(
            tracker.OpenHashed, FcaBedrock.Core.Spec.SourceReadSettings.CreateWide(hasHeader: false, missingToken: string.Empty));
        var records = new List<string>();
        await foreach (var record in session.ReadAsync(TestContext.Current.CancellationToken))
        {
            records.Add(string.Join("|", Enumerable.Range(0, record.FieldCount).Select(record.Field)));
        }

        return records;
    }

    [Theory]
    [MemberData(nameof(ReaderInputs))]
    public async Task Digest_WhenTheReaderDrainsTheInput_ThenItIsOverTheOriginalBytes(string what, byte[] bytes)
    {
        var tracker = TrackerOver(bytes);

        _ = await ReadThroughReaderAsync(tracker);

        Assert.True(tracker.CompletedPasses == 1, what);
        Assert.Equal(ExpectedDigest(bytes), tracker.Digest);
    }

    [Fact]
    public async Task CompletedPasses_WhenTheReaderRefusesBytesThatAreNotUtf8_ThenThePassCompletesNoDigest()
    {
        var tracker = TrackerOver([.. Encoding.UTF8.GetBytes("a,b"), 0xFF, .. Encoding.UTF8.GetBytes("c\n\nd,e\n")]);

        await Assert.ThrowsAsync<DecoderFallbackException>(() => ReadThroughReaderAsync(tracker));

        Assert.Equal(0, tracker.CompletedPasses);
        Assert.Null(tracker.Digest);
        Assert.Equal(DataByteOrderMark.None, tracker.CurrentPassMark);
    }

    [Fact]
    public async Task CompletedPasses_WhenTheProviderReadsZeroMoreThanOnceAtTheEnd_ThenOnePassIsCountedOnce()
    {
        var bytes = Encoding.UTF8.GetBytes("a,b\n1,2\n");
        var zeroReads = 0;
        var tracker = new InputHashTracker(_ => new ZeroCountingStream(bytes, () => zeroReads++), "data.csv");

        _ = await ReadThroughReaderAsync(tracker);

        Assert.True(zeroReads > 1, $"the end of the input was read {zeroReads} time(s)");
        Assert.Equal(1, tracker.CompletedPasses);
        Assert.Equal(ExpectedDigest(bytes), tracker.Digest);
    }

    [Fact]
    public async Task CompletedPasses_WhenTheReaderReadsTheInputTwice_ThenTwoPassesCompleteWithTheSameRecords()
    {
        // The .cxt writer's two passes: both read the same records and both complete.
        var bytes = Encoding.UTF8.GetBytes("\nh1,h2\n\n1,\"a\r\nb\"\n  \n2,x\n");
        var tracker = TrackerOver(bytes);

        var first = await ReadThroughReaderAsync(tracker);
        var second = await ReadThroughReaderAsync(tracker);

        Assert.Equal(first, second);
        Assert.Equal(2, tracker.CompletedPasses);
        Assert.False(tracker.HasMismatch);
        Assert.Equal(ExpectedDigest(bytes), tracker.Digest);
    }

    [Fact]
    public async Task CompletedPasses_WhenASchemaReadOfALargeInputStopsEarly_ThenNoPassCompletes()
    {
        var bytes = Encoding.UTF8.GetBytes("h1,h2\n" + string.Concat(Enumerable.Range(0, 200_000).Select(i => $"{i},v\n")));
        var tracker = TrackerOver(bytes);
        var session = new FcaBedrock.Sources.WideCsvSession(tracker.OpenHashed, FcaBedrock.Core.Spec.SourceReadSettings.CreateWide());

        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["h1", "h2"], schema.Header);
        Assert.Equal(0, tracker.CompletedPasses);
        Assert.Null(tracker.Digest);
    }

    // ---- the leading bytes of a pass ------------------------------------------------------------
    //
    // Each pass keeps the first four bytes it consumed, from its own reads, so a decoding failure
    // can name the UTF-16 or UTF-32 mark the failing pass began with.

    public static TheoryData<string, byte[], string> Prefixes() => new()
    {
        { "nothing", [], nameof(DataByteOrderMark.None) },
        { "UTF-8 mark", [0xEF, 0xBB, 0xBF, 0x61], nameof(DataByteOrderMark.None) },
        { "a lone FF", [0xFF], nameof(DataByteOrderMark.None) },
        { "Latin-1 text", [0xE9, 0x74, 0xE9], nameof(DataByteOrderMark.None) },
        { "UTF-16 LE", [0xFF, 0xFE, 0x61, 0x00], nameof(DataByteOrderMark.Utf16LittleEndian) },
        { "UTF-16 LE, three bytes", [0xFF, 0xFE, 0x61], nameof(DataByteOrderMark.Utf16LittleEndian) },
        { "UTF-16 LE, character 4100", [0xFF, 0xFE, 0x00, 0x41], nameof(DataByteOrderMark.Utf16LittleEndian) },
        { "UTF-16 BE", [0xFE, 0xFF], nameof(DataByteOrderMark.Utf16BigEndian) },
        { "UTF-32 LE", [0xFF, 0xFE, 0x00, 0x00], nameof(DataByteOrderMark.Utf32LittleEndian) },
        { "UTF-32 BE", [0x00, 0x00, 0xFE, 0xFF], nameof(DataByteOrderMark.Utf32BigEndian) },
        { "part of UTF-32 BE", [0x00, 0x00, 0xFE], nameof(DataByteOrderMark.None) },
        { "FF FE alone", [0xFF, 0xFE], nameof(DataByteOrderMark.Utf16OrUtf32) },
        { "FF FE 00", [0xFF, 0xFE, 0x00], nameof(DataByteOrderMark.Utf16OrUtf32) },
    };

    [Theory]
    [MemberData(nameof(Prefixes))]
    public void Classify_WhenGivenALeadingByteSequence_ThenItNamesOnlyAMarkItCanProve(string what, byte[] prefix, string mark) =>
        Assert.True(Enum.Parse<DataByteOrderMark>(mark) == InputHashTracker.Classify(prefix), what);

    [Fact]
    public void CurrentPassMark_WhenAPassStopsAfterTwoBytes_ThenItIsJudgedFromThoseTwoAndNothingMoreIsRead()
    {
        // A pass that stops early (a failure, or a schema read) keeps the bytes it consumed, even
        // after its stream is disposed; the tracker never reads ahead to finish the mark.
        var source = new CountingStream([0xFF, 0xFE, 0x00, 0x00, 0x61, 0x00, 0x00, 0x00]);
        var tracker = new InputHashTracker(_ => source, "data.csv");

        using (var stream = tracker.OpenHashed())
        {
            Assert.Equal(2, stream.Read(new byte[2], 0, 2));
        }

        Assert.Equal(2, source.Served);
        Assert.Equal(DataByteOrderMark.Utf16OrUtf32, tracker.CurrentPassMark);
        Assert.Equal(0, tracker.CompletedPasses);
    }

    [Fact]
    public void CurrentPassMark_WhenANewPassOpens_ThenTheEarlierPassesMarkIsGone()
    {
        byte[] marked = [0xFE, 0xFF, 0x00, 0x61];
        var opens = 0;
        var tracker = new InputHashTracker(
            _ => opens++ switch
            {
                0 => new MemoryStream(marked, writable: false),
                1 => new MemoryStream(Encoding.UTF8.GetBytes("a,b\n"), writable: false),
                2 => new MemoryStream(marked, writable: false),
                _ => throw new IOException("the file is gone."),
            },
            "data.csv");

        using (var first = tracker.OpenHashed())
        {
            DrainSync(first, 3);
        }

        Assert.Equal(DataByteOrderMark.Utf16BigEndian, tracker.CurrentPassMark);

        using (var second = tracker.OpenHashed())
        {
            Assert.Equal(DataByteOrderMark.None, tracker.CurrentPassMark);
            DrainSync(second, 1);
        }

        Assert.Equal(DataByteOrderMark.None, tracker.CurrentPassMark);

        using (var third = tracker.OpenHashed())
        {
            DrainSync(third, 2);
        }

        Assert.Equal(DataByteOrderMark.Utf16BigEndian, tracker.CurrentPassMark);

        // A pass whose open fails has no mark, rather than the previous pass's.
        Assert.Throws<IOException>(tracker.OpenHashed);
        Assert.Equal(DataByteOrderMark.None, tracker.CurrentPassMark);
    }

    [Fact]
    public async Task CurrentPassMark_WhenReadAsynchronously_ThenTheMarkComesFromTheSameReads()
    {
        var tracker = TrackerOver([0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x61]);

        await using (var stream = tracker.OpenHashed())
        {
            await DrainAsync(stream, 1);
        }

        Assert.Equal(DataByteOrderMark.Utf32BigEndian, tracker.CurrentPassMark);
        Assert.Equal(1, tracker.CompletedPasses);
    }

    // ---- observed passes and their scopes (D-141) -------------------------------------------------
    //
    // An observed tracker reports each pass opened under a scope: its kind and its length when the
    // opened stream can seek, the consumed total from the hashed reads, end of input, the closure at
    // the first disposal, and the end its scope's exit decides from what reached it.

    private static (InputHashTracker Tracker, OperationProgress Progress, ProgressRecorder Recorder) Observed(
        Func<string, Stream> open)
    {
        var (progress, recorder) = ProgressRecorder.Create();
        return (new InputHashTracker(open, "data.csv", progress), progress, recorder);
    }

    [Fact]
    public void OpenHashed_WhenTheOpenedStreamIsSeekable_ThenItsLengthIsReadOnceBeforeWrapping()
    {
        var source = new LengthStream(Encoding.UTF8.GetBytes("abcdef"));
        var (tracker, progress, recorder) = Observed(_ => source);
        var scope = tracker.EnterScope(PassKind.Schema, 0, CancellationToken.None);

        using (var stream = tracker.OpenHashed())
        {
            Assert.Equal(1, source.LengthReads);
            Assert.False(stream.CanSeek);
            Assert.Equal(6, progress.Current.PassLength);
            DrainSync(stream, 4);
        }

        scope.Exit();

        Assert.Equal(1, source.LengthReads);
        Assert.Equal("PassStarted(1, Schema, 6)", recorder.Events[0].ToString());
    }

    [Fact]
    public void OpenHashed_WhenTheOpenedStreamIsNotSeekable_ThenTheLengthIsUnknown()
    {
        var source = new LengthStream(Encoding.UTF8.GetBytes("abcdef"), canSeek: false);
        var (tracker, progress, _) = Observed(_ => source);
        var scope = tracker.EnterScope(PassKind.Dat, 0, CancellationToken.None);

        using (tracker.OpenHashed())
        {
            Assert.Null(progress.Current.PassLength);
        }

        scope.Exit();

        Assert.Equal(1, source.CanSeekQueries);
        Assert.Equal(0, source.LengthReads);
    }

    [Fact]
    public void OpenHashed_WhenReadingTheLengthThrows_ThenTheLengthIsUnknownAndThePassReadsNormally()
    {
        var bytes = Encoding.UTF8.GetBytes("a,b\n1,2\n");
        var source = new LengthStream(bytes, lengthThrows: true);
        var (tracker, progress, _) = Observed(_ => source);
        var scope = tracker.EnterScope(PassKind.Statistics, 0, CancellationToken.None);

        using (var stream = tracker.OpenHashed())
        {
            Assert.Null(progress.Current.PassLength);
            DrainSync(stream, 3);
        }

        scope.Exit();

        Assert.Equal(1, source.LengthReads);
        Assert.Equal(ExpectedDigest(bytes), tracker.Digest);
        Assert.Equal(PassEnd.EndOfInput, progress.Current.PassEnd);
        Assert.Equal(bytes.Length, progress.Current.BytesConsumed);
    }

    [Fact]
    public void OpenHashed_WhenNotObserved_ThenTheLengthIsNeverRead()
    {
        var source = new LengthStream(Encoding.UTF8.GetBytes("abcdef"));
        var tracker = new InputHashTracker(_ => source, "data.csv");
        var scope = tracker.EnterScope(PassKind.Schema, 0, CancellationToken.None);

        using (var stream = tracker.OpenHashed())
        {
            DrainSync(stream, 4);
        }

        scope.Exit();

        Assert.Same(ProgressScope.None, scope);
        Assert.Null(tracker.Progress);
        Assert.Equal(0, source.CanSeekQueries);
        Assert.Equal(0, source.LengthReads);
        Assert.Equal(0, tracker.ObservedOpens);
        Assert.Equal(1, tracker.CompletedPasses);
    }

    [Fact]
    public void OpenHashed_WhenTheOpenThrows_ThenNoPassStarts()
    {
        var (tracker, progress, recorder) = Observed(_ => throw new IOException("the file is gone."));
        var scope = tracker.EnterScope(PassKind.Schema, 0, CancellationToken.None);

        Assert.Throws<IOException>(tracker.OpenHashed);
        scope.Exit();

        Assert.Equal(0, tracker.ObservedOpens);
        Assert.Empty(recorder.Passes());
        Assert.Equal(0, progress.Current.PassSequence);
        Assert.Equal(["ScopeExited(0, 0, 0)"], recorder.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void OpenHashed_WhenNoScopeIsCurrent_ThenNoPassIsRecordedAndTheOpenCountShowsIt()
    {
        // The rule is a defence, not an allowed shape: an open outside every scope records no pass, and
        // the observed open count is what lets a test see the pass that is missing from the display.
        var (tracker, progress, recorder) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abc"), writable: false));
        var scope = tracker.EnterScope(PassKind.Schema, 0, CancellationToken.None);
        scope.Exit();

        using (var stream = tracker.OpenHashed())
        {
            DrainSync(stream, 8);
        }

        Assert.Equal(1, tracker.ObservedOpens);
        Assert.Empty(recorder.Passes());
        Assert.Equal(0, progress.Current.PassSequence);
        Assert.NotNull(Record.Exception(() => recorder.AssertGrammar(completed: false, observedOpens: tracker.ObservedOpens)));
    }

    [Fact]
    public void Read_WhenBytesArrive_ThenAtMostOneUpdatePerReadCallAndNoneForAZeroLengthResult()
    {
        var (tracker, _, recorder) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdefgh"), writable: false));
        var scope = tracker.EnterScope(PassKind.Dat, 0, CancellationToken.None);

        using (var stream = tracker.OpenHashed())
        {
            Assert.Equal(0, stream.Read([], 0, 0));
            DrainSync(stream, 3);
            Assert.Equal(0, stream.Read(new byte[3], 0, 3));
        }

        scope.Exit();

        Assert.Equal(
            ["Consumed(1, 3)", "Consumed(1, 6)", "Consumed(1, 8)"],
            recorder.Events.Where(e => e.Name == ProgressEvent.Consumed).Select(e => e.ToString()));
    }

    [Fact]
    public void Dispose_WhenEndOfInputWasObserved_ThenThePassEndsAtEndOfInputAtClosure()
    {
        var (tracker, progress, recorder) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abc"), writable: false));
        var scope = tracker.EnterScope(PassKind.Calibration, WorkingStep.Calibrating, CancellationToken.None);

        var stream = tracker.OpenHashed();
        DrainSync(stream, 8);
        Assert.Equal((PassEnd)0, progress.Current.PassEnd);
        stream.Dispose();

        // At closure, while the scope still runs: the end, and the work after the input closed.
        Assert.Equal(PassEnd.EndOfInput, progress.Current.PassEnd);
        Assert.Equal(WorkingStep.Calibrating, progress.Current.Step);
        scope.Exit();
        progress.Complete();

        Assert.Equal(PassEnd.EndOfInput, recorder.EndOf(1));
        recorder.AssertGrammar(observedOpens: tracker.ObservedOpens);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dispose_WhenDisposedSynchronouslyAndAsynchronously_ThenClosureIsRecordedOnce(bool synchronousFirst)
    {
        var (tracker, _, recorder) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var scope = tracker.EnterScope(PassKind.Dat, WorkingStep.Grouping, CancellationToken.None);
        var stream = tracker.OpenHashed();
        Assert.Equal(2, stream.Read(new byte[2], 0, 2));

        if (synchronousFirst)
        {
            stream.Dispose();
            await stream.DisposeAsync();
        }
        else
        {
            await stream.DisposeAsync();
            stream.Dispose();
        }

        scope.Exit();

        Assert.Single(recorder.Events, e => e.Name == ProgressEvent.Closed);
        Assert.Equal(PassEnd.ClosedBeforeEndOfInput, recorder.EndOf(1));
    }

    [Fact]
    public void ScopeExit_WhenClosedEarlyAndNothingReachedTheScope_ThenThePassEndsClosedBeforeEndOfInput()
    {
        var (tracker, progress, recorder) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var scope = tracker.EnterScope(PassKind.Schema, 0, CancellationToken.None);

        using (var stream = tracker.OpenHashed())
        {
            Assert.Equal(2, stream.Read(new byte[2], 0, 2));
        }

        Assert.Equal((PassEnd)0, progress.Current.PassEnd);
        scope.Exit();
        progress.Complete();

        Assert.Equal(PassEnd.ClosedBeforeEndOfInput, progress.Current.PassEnd);
        recorder.AssertGrammar(observedOpens: tracker.ObservedOpens);
    }

    [Fact]
    public void ScopeExit_WhenTheCallsOwnCancellationReachedTheScope_ThenThePassEndsCancelled()
    {
        using var call = new CancellationTokenSource();
        var (tracker, progress, _) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var scope = tracker.EnterScope(PassKind.Calibration, WorkingStep.Calibrating, call.Token);

        Assert.Throws<OperationCanceledException>(() => WithinScope(scope, () =>
        {
            using var stream = tracker.OpenHashed();
            Assert.Equal(2, stream.Read(new byte[2], 0, 2));
            call.Cancel();
            call.Token.ThrowIfCancellationRequested();
        }));

        Assert.Equal(PassEnd.Cancelled, progress.Current.PassEnd);
        Assert.Equal((WorkingStep)0, progress.Current.Step);
    }

    [Fact]
    public void ScopeExit_WhenAnotherTokensCancellationReachedTheScope_ThenThePassEndsFailed()
    {
        using var call = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        var (tracker, progress, _) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var scope = tracker.EnterScope(PassKind.Dat, 0, call.Token);

        Assert.Throws<OperationCanceledException>(() => WithinScope(scope, () =>
        {
            using var stream = tracker.OpenHashed();
            Assert.Equal(2, stream.Read(new byte[2], 0, 2));
            call.Cancel();
            other.Cancel();
            other.Token.ThrowIfCancellationRequested();
        }));

        Assert.Equal(PassEnd.Failed, progress.Current.PassEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopeExit_WhenTheCallsOwnTokenReachedTheScopeWithNoCancellationRequested_ThenTheTokenDecidesTheEnd(
        bool afterEndOfInput)
    {
        // The pass end reads no request (D-141 part 3): an operation-cancelled exception that carries
        // exactly the call's own token ends the pass Cancelled, although nothing cancelled that token.
        // End of input, once the wrapper saw it, still takes precedence.
        using var call = new CancellationTokenSource();
        var expected = new OperationCanceledException("the consumer stopped.", call.Token);
        var (tracker, progress, recorder) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var scope = tracker.EnterScope(PassKind.Schema, 0, call.Token);

        var caught = Assert.Throws<OperationCanceledException>(() => WithinScope(scope, () =>
        {
            using var stream = tracker.OpenHashed();
            if (afterEndOfInput)
            {
                DrainSync(stream, 8);
            }
            else
            {
                Assert.Equal(2, stream.Read(new byte[2], 0, 2));
            }

            ThrowFromTheReader(expected);
        }));
        progress.Complete();

        Assert.Same(expected, caught);
        Assert.Equal(call.Token, caught.CancellationToken);
        Assert.Equal(afterEndOfInput ? PassEnd.EndOfInput : PassEnd.Cancelled, recorder.EndOf(1));
        Assert.Equal((WorkingStep)0, progress.Current.Step);
        recorder.AssertGrammar(observedOpens: tracker.ObservedOpens);
    }

    [Fact]
    public void ScopeExit_WhenAnyOtherExceptionReachedTheScope_ThenThePassEndsFailed()
    {
        var (tracker, progress, _) = Observed(_ => new FailingStream(Encoding.UTF8.GetBytes("abcdefgh"), 4));
        var scope = tracker.EnterScope(PassKind.Statistics, WorkingStep.Grouping, CancellationToken.None);

        Assert.Throws<IOException>(() => WithinScope(scope, () =>
        {
            using var stream = tracker.OpenHashed();
            DrainSync(stream, 4);
        }));

        Assert.Equal(PassEnd.Failed, progress.Current.PassEnd);
        Assert.Equal((WorkingStep)0, progress.Current.Step);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopeExit_WhenTheConsumerNeverDisposedTheWrapper_ThenTheExitEndsThePass(bool throws)
    {
        var (tracker, progress, recorder) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var scope = tracker.EnterScope(PassKind.Dat, WorkingStep.Grouping, CancellationToken.None);
        Stream? leaked = null;

        var thrown = Record.Exception(() => WithinScope(scope, () =>
        {
            leaked = tracker.OpenHashed();
            Assert.Equal(2, leaked.Read(new byte[2], 0, 2));
            if (throws)
            {
                throw new InvalidDataException("the consumer failed.");
            }
        }));

        Assert.Equal(throws, thrown is InvalidDataException);
        var expected = throws ? PassEnd.Failed : PassEnd.ClosedBeforeEndOfInput;
        Assert.Equal(expected, progress.Current.PassEnd);
        Assert.Equal((WorkingStep)0, progress.Current.Step);

        // The leaked wrapper's late disposal changes nothing: the pass already ended with its scope.
        leaked!.Dispose();
        progress.Complete();

        Assert.Equal(expected, progress.Current.PassEnd);
        Assert.Equal((WorkingStep)0, progress.Current.Step);
        recorder.AssertGrammar(observedOpens: tracker.ObservedOpens);
    }

    [Fact]
    public void ScopeExit_WhenTheUndisposedWrapperHadObservedEndOfInput_ThenThePassEndsAtEndOfInput()
    {
        var (tracker, progress, _) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abc"), writable: false));
        var scope = tracker.EnterScope(PassKind.Dat, 0, CancellationToken.None);
        var stream = tracker.OpenHashed();
        DrainSync(stream, 8);

        scope.Exit();

        Assert.Equal(PassEnd.EndOfInput, progress.Current.PassEnd);
        stream.Dispose();
    }

    [Fact]
    public void Dispose_WhenALeakedWrapperClosesAfterANewPassStarted_ThenTheNewPassIsUnchanged()
    {
        // Through real wrappers: a wrapper that saw end of input is never disposed by its consumer; its
        // scope ends the pass; a new scope opens pass 2; the old wrapper is disposed late. Its closure
        // neither ends pass 2 nor sets a step while pass 2's input is open.
        var (tracker, progress, recorder) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var first = tracker.EnterScope(PassKind.CxtNames, WorkingStep.Grouping, CancellationToken.None);
        var leaked = tracker.OpenHashed();
        DrainSync(leaked, 8);
        first.Exit();

        var second = tracker.EnterScope(PassKind.CxtRows, WorkingStep.Grouping, CancellationToken.None);
        using (var stream = tracker.OpenHashed())
        {
            Assert.Equal(2, stream.Read(new byte[2], 0, 2));
            leaked.Dispose();

            var current = progress.Current;
            Assert.Equal(2, current.PassSequence);
            Assert.Equal((PassEnd)0, current.PassEnd);
            Assert.Equal((WorkingStep)0, current.Step);
        }

        second.Exit();
        progress.Complete();

        Assert.Equal(PassEnd.EndOfInput, recorder.EndOf(1));
        Assert.Equal(PassEnd.ClosedBeforeEndOfInput, recorder.EndOf(2));
        recorder.AssertGrammar(observedOpens: tracker.ObservedOpens);
    }

    [Fact]
    public void Scope_WhenAnExceptionReachesIt_ThenTheSameInstanceWithItsOriginAndTokenPropagates()
    {
        // The filter catches nothing: the exception object, its type, its token and its original throw
        // origin reach the caller unchanged, nothing wraps it, and the scope still records it.
        using var call = new CancellationTokenSource();
        var expected = new OperationCanceledException("the read was cancelled.", call.Token);
        var (tracker, progress, _) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var scope = tracker.EnterScope(PassKind.Schema, 0, call.Token);
        call.Cancel();

        var caught = Assert.Throws<OperationCanceledException>(() => WithinScope(scope, () =>
        {
            using var stream = tracker.OpenHashed();
            ThrowFromTheReader(expected);
        }));

        Assert.Same(expected, caught);
        Assert.Equal(call.Token, caught.CancellationToken);
        Assert.Null(caught.InnerException);
        Assert.Contains(nameof(ThrowFromTheReader), caught.StackTrace, StringComparison.Ordinal);
        Assert.Equal(PassEnd.Cancelled, progress.Current.PassEnd);
    }

    [Fact]
    public async Task Scope_WhenAnAwaitedCallThrows_ThenTheSameInstanceWithItsOriginPropagates()
    {
        // The call scope's shape in preparation: an awaited call inside the filter, the exit in the
        // finally. The await adds propagation frames; the instance and its origin are kept.
        var expected = new IOException("the read failed.");
        var (tracker, progress, _) = Observed(_ => new MemoryStream(Encoding.UTF8.GetBytes("abcdef"), writable: false));
        var scope = tracker.EnterScope(PassKind.Calibration, WorkingStep.Calibrating, CancellationToken.None);

        async Task ReadAsync()
        {
            await using var stream = tracker.OpenHashed();
            await Task.Yield();
            ThrowFromTheReader(expected);
        }

        var caught = await Assert.ThrowsAsync<IOException>(async () =>
        {
            try
            {
                await ReadAsync();
            }
            catch (Exception exception) when (scope.Reached(exception))
            {
                throw;
            }
            finally
            {
                scope.Exit();
            }
        });

        Assert.Same(expected, caught);
        Assert.Contains(nameof(ThrowFromTheReader), caught.StackTrace, StringComparison.Ordinal);
        Assert.Equal(PassEnd.Failed, progress.Current.PassEnd);
    }

    [Fact]
    public async Task Digest_WhenObserved_ThenEqualToTheUnobservedTrackersDigestAndPassCount()
    {
        var bytes = Encoding.UTF8.GetBytes("\nh1,h2\n\n1,\"a\r\nb\"\n  \n2,x\n");
        var unobserved = TrackerOver(bytes);
        var (observed, progress, recorder) = Observed(_ => new MemoryStream(bytes, writable: false));

        var plain = await ReadThroughReaderAsync(unobserved);
        var scope = observed.EnterScope(PassKind.Statistics, 0, CancellationToken.None);
        var watched = await ReadThroughReaderAsync(observed);
        scope.Exit();
        progress.Complete();

        Assert.Equal(plain, watched);
        Assert.Equal(unobserved.Digest, observed.Digest);
        Assert.Equal(unobserved.CompletedPasses, observed.CompletedPasses);
        Assert.Equal(unobserved.HasMismatch, observed.HasMismatch);
        Assert.Equal(bytes.Length, progress.Current.BytesConsumed);
        Assert.Equal(PassEnd.EndOfInput, recorder.EndOf(1));
        recorder.AssertGrammar(observedOpens: observed.ObservedOpens);
    }

    // A call scope's shape around a synchronous body: the filter records, the finally exits.
    private static void WithinScope(ProgressScope scope, Action body)
    {
        try
        {
            body();
        }
        catch (Exception exception) when (scope.Reached(exception))
        {
            throw;
        }
        finally
        {
            scope.Exit();
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowFromTheReader(Exception exception) => throw exception;

    /// <summary>A stream that counts its seek queries and length reads, can refuse to seek, and can fail the length read.</summary>
    private sealed class LengthStream(byte[] bytes, bool canSeek = true, bool lengthThrows = false)
        : MemoryStream(bytes, writable: false)
    {
        public int CanSeekQueries { get; private set; }

        public int LengthReads { get; private set; }

        public override bool CanSeek
        {
            get
            {
                CanSeekQueries++;
                return canSeek;
            }
        }

        public override long Length
        {
            get
            {
                LengthReads++;
                return lengthThrows ? throw new IOException("the length cannot be read.") : base.Length;
            }
        }
    }

    /// <summary>An in-memory stream that reports each read that returned zero for a non-empty request.</summary>
    private sealed class ZeroCountingStream(byte[] bytes, Action onZero) : MemoryStream(bytes, writable: false)
    {
        public override int Read(byte[] buffer, int offset, int count) => Count(base.Read(buffer, offset, count), count);

        public override int Read(Span<byte> buffer) => Count(base.Read(buffer), buffer.Length);

        private int Count(int read, int requested)
        {
            if (read == 0 && requested > 0)
            {
                onZero();
            }

            return read;
        }
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

    /// <summary>A stream that counts the bytes it handed out, readable after disposal.</summary>
    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public long Served { get; private set; }

        // A derived MemoryStream's span read is routed to this array read.
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            Served += read;
            return read;
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
