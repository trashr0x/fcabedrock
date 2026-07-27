using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The raw-bytes hashes a converted run records (§15 / D-122 part 5): the inline write hasher
/// that produces an artifact's digest while the exporter writes it, and the end-to-end property
/// that every recorded hash is the SHA-256 of exactly the bytes on disk.
/// <para>
/// Every expected digest here is computed with <see cref="SHA256"/> in this file, never with the
/// CLI's own helper.
/// </para>
/// </summary>
public sealed class ConvertHashingTests
{
    // ---- the inline write hasher --------------------------------------------------------------

    [Fact]
    public void WriteHashing_WhenBytesAreWrittenByArraySegment_ThenTheDigestCoversThem()
    {
        Assert.Equal(Expected("hello"), Write(stream => stream.Write(Bytes("hello"), 0, 5)));
    }

    [Fact]
    public void WriteHashing_WhenBytesAreWrittenBySpan_ThenTheDigestCoversThem()
    {
        Assert.Equal(Expected("hello"), Write(stream => stream.Write(Bytes("hello").AsSpan())));
    }

    [Fact]
    public void WriteHashing_WhenBytesAreWrittenOneAtATime_ThenTheDigestCoversThem()
    {
        Assert.Equal(
            Expected("hello"),
            Write(stream =>
            {
                foreach (var value in Bytes("hello"))
                {
                    stream.WriteByte(value);
                }
            }));
    }

    [Fact]
    public async Task WriteHashing_WhenBytesAreWrittenAsynchronously_ThenTheDigestCoversThem()
    {
        using var sink = new MemoryStream();
        var hashing = new HashingWriteStream(sink);

        // The array-segment overload is exercised deliberately: covering every supported write
        // entry point exactly once is the whole point of this test.
#pragma warning disable CA1835
        await hashing.WriteAsync(Bytes("hel"), 0, 3, TestContext.Current.CancellationToken);
#pragma warning restore CA1835
        await hashing.WriteAsync(Bytes("lo").AsMemory(), TestContext.Current.CancellationToken);

        Assert.Equal(Expected("hello"), hashing.Complete());
        Assert.Equal(Bytes("hello"), sink.ToArray());
    }

    [Fact]
    public void WriteHashing_WhenOnlyPartOfABufferIsWritten_ThenOnlyThatPartIsHashed()
    {
        // A short write must contribute exactly what it wrote, not the whole buffer.
        Assert.Equal(Expected("ell"), Write(stream => stream.Write(Bytes("hello"), 1, 3)));
    }

    [Fact]
    public void WriteHashing_WhenTheInnerWriteFails_ThenItIsTaggedAsAnOutputFailureAndNothingIsHashed()
    {
        // The tag is what lets the caller tell "the output could not be written" from "the source
        // could not be read" at a boundary where both are in flight (CX-M7H-006). The original
        // failure is preserved inside it, and nothing the write did not accept is hashed.
        var hashing = new HashingWriteStream(new FailingStream());

        var failure = Assert.Throws<PublicationStreamException>(() => hashing.Write(Bytes("hello"), 0, 5));

        Assert.IsType<IOException>(failure.InnerException);
        Assert.Equal(Expected(string.Empty), hashing.Complete());
    }

    [Fact]
    public void WriteHashing_WhenTheInnerSpanWriteFails_ThenItIsTaggedToo()
    {
        var hashing = new HashingWriteStream(new FailingStream());

        Assert.Throws<PublicationStreamException>(() => hashing.Write(Bytes("hello").AsSpan()));
    }

    [Fact]
    public void WriteHashing_WhenTheInnerWriteRaisesAContractDefect_ThenItIsNotTaggedAsAnOutputFailure()
    {
        // CX-M7H-015/034. At a write call, a plain ArgumentException means the writer passed an
        // invalid range — a product bug, not a full disk. Tagging it as an output failure would
        // report "cannot write the output" and send the user to check permissions for a defect in
        // this code; it is tagged as a CONTRACT fault instead, which is what carries it to the
        // sanitized unexpected-fault exit rather than into either environment-failure reading.
        var hashing = new HashingWriteStream(new FailingStream(static () => new ArgumentException("bad range")));

        var fault = Assert.Throws<PublicationFaultException>(() => hashing.Write(Bytes("hello"), 0, 5));
        Assert.IsType<ArgumentException>(fault.InnerException);
    }

    [Fact]
    public void WriteHashing_WhenTheInnerWriteRaisesAnotherContractDefect_ThenItIsNotTagged()
    {
        // Writing to a disposed stream, or calling an unsupported operation, are the same class of
        // defect: the caller broke the contract, and the run must not report a disk problem — nor,
        // for the disposed case, a failure of standard output, which is what an untagged
        // ObjectDisposedException would become at the host boundary (CX-M7H-034).
        var disposed = Assert.Throws<PublicationFaultException>(
            () => new HashingWriteStream(new FailingStream(static () => new ObjectDisposedException("stage")))
                .Write(Bytes("hello"), 0, 5));
        Assert.IsType<ObjectDisposedException>(disposed.InnerException);

        var unsupported = Assert.Throws<PublicationFaultException>(
            () => new HashingWriteStream(new FailingStream(static () => new NotSupportedException()))
                .Write(Bytes("hello"), 0, 5));
        Assert.IsType<NotSupportedException>(unsupported.InnerException);
    }

    [Fact]
    public void WriteHashing_WhenTheWriteIsNeverCompleted_ThenNoDigestIsPresented()
    {
        using var sink = new MemoryStream();
        var hashing = new HashingWriteStream(sink);
        hashing.Write(Bytes("hello"), 0, 5);

        Assert.Null(hashing.Digest);

        hashing.Dispose();
        Assert.Null(hashing.Digest);
    }

    [Fact]
    public void WriteHashing_WhenCompleteIsCalledTwice_ThenTheSameDigestComesBack()
    {
        using var sink = new MemoryStream();
        var hashing = new HashingWriteStream(sink);
        hashing.Write(Bytes("hello"), 0, 5);

        Assert.Equal(hashing.Complete(), hashing.Complete());
        Assert.Equal(Expected("hello"), hashing.Complete());
    }

    [Fact]
    public void WriteHashing_WhenItIsUsed_ThenItRefusesToReadOrSeek()
    {
        // Refusing to seek is what makes "the digest covers what was written" true: a consumer
        // that repositioned the stream would leave the hash describing something else.
        using var sink = new MemoryStream();
        using var hashing = new HashingWriteStream(sink);

        Assert.False(hashing.CanRead);
        Assert.False(hashing.CanSeek);
        Assert.True(hashing.CanWrite);
        Assert.Throws<NotSupportedException>(() => hashing.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => hashing.Position);
        Assert.Throws<NotSupportedException>(() => hashing.Read(new byte[1], 0, 1));
    }

    // ---- end to end -----------------------------------------------------------------------------

    [Fact]
    public async Task Convert_WhenTheRunCommits_ThenEveryRecordedHashIsTheSha256OfTheBytesOnDisk()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var manifest = run.Text(".manifest.toml");
        Assert.Contains($"spec_file_hash = \"{OfFile(run.Spec)}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"input_hash = \"{OfFile(run.Data)}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"hash = \"{OfFile(run.Target(".cxt"))}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"hash = \"{OfFile(run.Target(".dat"))}\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenTheDataCarriesAwkwardRawBytes_ThenTheInputHashCoversThemVerbatim()
    {
        // A byte-order mark, CRLF endings, and non-ASCII values: the digest is over the raw
        // bytes, with nothing decoded, normalized, or re-encoded first.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var dataPath = Path.Combine(temp.Path, "data.csv");
        var raw = new List<byte> { 0xEF, 0xBB, 0xBF };
        raw.AddRange(Encoding.UTF8.GetBytes("colour\r\nrédd\r\ngreen\r\n"));
        await File.WriteAllBytesAsync(dataPath, raw.ToArray(), TestContext.Current.CancellationToken);

        var spec = temp.Write("spec.toml", ObservedDomainSpec);
        var basePath = temp.Resolve("out");

        var exit = await harness.RunAsync("convert", spec, dataPath, "--out", basePath, "--format", "cxt");

        Assert.Equal(0, exit);
        Assert.Contains(
            $"input_hash = \"{OfFile(dataPath)}\"",
            await File.ReadAllTextAsync(basePath + ".manifest.toml", TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    // ---- input stability ---------------------------------------------------------------------------

    [Fact]
    public async Task Convert_WhenASingleDatPassReadsStableBytes_ThenThatPassesDigestIsRecorded()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Resolve("data.csv");
        var basePath = temp.Resolve("out");
        harness.OpenInput = PassIndexed(spec, CliFixtures.IndexBoundSpec, CliFixtures.WideData);

        var exit = await harness.RunAsync("convert", spec, data, "--out", basePath, "--format", "dat");

        Assert.Equal(0, exit);
        Assert.Contains(
            $"input_hash = \"{Of(Encoding.UTF8.GetBytes(CliFixtures.WideData))}\"",
            await File.ReadAllTextAsync(basePath + ".manifest.toml", TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenTheCxtReplayReadsStableBytes_ThenTheRunCommits()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Resolve("data.csv");
        var basePath = temp.Resolve("out");
        harness.OpenInput = PassIndexed(spec, CliFixtures.IndexBoundSpec, CliFixtures.WideData);

        var exit = await harness.RunAsync("convert", spec, data, "--out", basePath, "--format", "both");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(basePath + ".cxt"));
        Assert.True(File.Exists(basePath + ".dat"));
    }

    [Fact]
    public async Task Convert_WhenTheCxtReplayPassSeesDifferentBytes_ThenTheRunFailsBeforeAnyCommit()
    {
        // Opens: schema, .cxt pass 1, .cxt pass 2. The third one differs, so the replay's
        // completed digest disagrees and the run stops before the commit — exactly one code-less
        // DATA-changed error, and nothing public.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Resolve("data.csv");
        var basePath = temp.Resolve("out");
        harness.OpenInput = PassIndexed(
            spec,
            CliFixtures.IndexBoundSpec,

            CliFixtures.WideData,
            CliFixtures.WideData,
            "colour,size\nred,1\nred,2\n");

        var exit = await harness.RunAsync("convert", spec, data, "--out", basePath, "--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(RunPipeline.InputChangedMessage(data)), harness.StdErr);
        Assert.Empty(Directory.GetFiles(temp.Path, "out*"));
    }

    [Fact]
    public async Task Convert_WhenALaterDatPassSeesDifferentBytes_ThenTheRunFailsBeforeAnyCommit()
    {
        // `both` runs .cxt twice and then .dat once, so the mismatch lands on the fourth open —
        // a later pass, after the .cxt replay already agreed.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Resolve("data.csv");
        var basePath = temp.Resolve("out");
        harness.OpenInput = PassIndexed(
            spec,
            CliFixtures.IndexBoundSpec,

            CliFixtures.WideData,
            CliFixtures.WideData,
            CliFixtures.WideData,
            "colour,size\nred,1\nred,2\n");

        var exit = await harness.RunAsync("convert", spec, data, "--out", basePath, "--format", "both");

        Assert.Equal(1, exit);
        Assert.EndsWith(
            DiagnosticRenderer.RenderHostError(RunPipeline.InputChangedMessage(data)),
            harness.StdErr,
            StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(temp.Path, "out*"));
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private const string ObservedDomainSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        """;

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Of(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string OfFile(string path) => Of(File.ReadAllBytes(path));

    private static string Expected(string text) => Of(Bytes(text));

    private static string Write(Action<HashingWriteStream> write)
    {
        using var sink = new MemoryStream();
        using var hashing = new HashingWriteStream(sink);
        write(hashing);
        return hashing.Complete();
    }

    // Serves the spec from memory and the DATA from a per-open script, so successive passes can
    // differ deterministically — no file mutation, no timing race (CX-M7P-008).
    private static Func<string, Stream> PassIndexed(
        string specPath, string specText, params string[] dataPasses)
    {
        var index = 0;
        return path =>
        {
            if (string.Equals(path, specPath, StringComparison.Ordinal))
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(specText), writable: false);
            }

            var pass = dataPasses[Math.Min(index++, dataPasses.Length - 1)];
            return new MemoryStream(Encoding.UTF8.GetBytes(pass), writable: false);
        };
    }

    /// <summary>A sink that refuses every write, with a caller-chosen failure.</summary>
    private sealed class FailingStream(Func<Exception>? failure = null) : Stream
    {
        private readonly Func<Exception> _failure = failure ?? (static () => new IOException("no space"));

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw _failure();

        public override void Write(ReadOnlySpan<byte> buffer) => throw _failure();
    }
}
