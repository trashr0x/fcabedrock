using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The read decodes its stream as strict UTF-8 (spec §5.1.1): one leading UTF-8 byte order mark is
/// consumed, valid UTF-8 decodes exactly as the pinned provider's own stream route decodes it,
/// whatever the stream's read sizes, and malformed, overlong or truncated sequences and UTF-16 or
/// UTF-32 byte order marks fail the read with the decoder's own exception. The bytes it consumes
/// are the original bytes, so a hash taken over them is the file's hash.
/// </summary>
public sealed class DecoderParityTests
{
    private const string Text = "a,b\ncafé,日本\n\"q\r\nr\",z\n";

    private static readonly int[] ReadSizes = [int.MaxValue, 1, 2, 3];

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    public static TheoryData<string> AcceptedNames() => new(Accepted().Select(i => i.Name));

    public static TheoryData<string> RejectedNames() => new(Rejected().Select(i => i.Name));

    internal static (string Name, byte[] Bytes)[] Accepted() =>
    [
        ("utf8-no-bom", Utf8(Text)),
        ("utf8-bom", Concat([0xEF, 0xBB, 0xBF], Utf8(Text))),
        ("bom-only", [0xEF, 0xBB, 0xBF]),
        ("bom-then-bom", Concat([0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF], Utf8("x,y\n"))),
        ("utf8-4byte", Utf8("a," + char.ConvertFromUtf32(0x1F600) + "\n")),
        ("replacement-character", Concat(Utf8("a,"), [0xEF, 0xBF, 0xBD], Utf8("\n"))),
    ];

    internal static (string Name, byte[] Bytes)[] Rejected() =>
    [
        ("utf16le-bom", Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes(Text))),
        ("utf16be-bom", Concat([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes(Text))),
        ("utf32le-bom", Concat([0xFF, 0xFE, 0x00, 0x00], new UTF32Encoding(false, false).GetBytes(Text))),
        ("utf32be-bom", Concat([0x00, 0x00, 0xFE, 0xFF], new UTF32Encoding(true, false).GetBytes(Text))),
        ("invalid-mid", Concat(Utf8("a,b"), [0xFF], Utf8("c\nd,e\n"))),
        ("lone-continuation", Concat(Utf8("a,"), [0x80], Utf8("\n"))),
        ("truncated-at-eof", Concat(Utf8("a,caf"), [0xC3])),
        ("overlong", Concat(Utf8("a,"), [0xC0, 0xAF], Utf8("\n"))),
        ("partial-bom", Concat([0xEF, 0xBB], Utf8("x,y\n"))),
    ];

    [Theory]
    [MemberData(nameof(AcceptedNames))]
    public async Task Read_WhenTheStreamIsHashedAsItIsRead_ThenTheHashIsTheFilesHash(string name)
    {
        var bytes = Accepted().Single(i => i.Name == name).Bytes;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        _ = await DrainAsync(WideSession(() => new HashingStream(new ScriptedStream(bytes), hash), missingToken: string.Empty).ReadAsync());

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToHexString(hash.GetHashAndReset()));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Read_WhenAQuotedCarriageReturnPairSitsBetweenMultibyteCharacters_ThenEveryDecodedUnitKeepsItsOrder(int readSize)
    {
        var content = "é\r\r日" + char.ConvertFromUtf32(0x1F600);
        var bytes = Utf8("x,\"" + content + "\",z\na,b\n");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var records = await DrainAsync(
            WideSession(() => new HashingStream(new ScriptedStream(bytes) { MaxChunk = readSize }, hash)).ReadAsync());

        Assert.Equal([Render(["x", content, "z"]), Render(["a", "b"])], records.Select(r => Render(Fields(r))));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToHexString(hash.GetHashAndReset()));
        Assert.Equal(
            [Render(["x", "\"" + content + "\"", "z"]), Render(["a", "b"])],
            DelimitedSourceReaderTests.RawCandidates(() => new ScriptedStream(bytes) { MaxChunk = readSize }).Select(Render));
    }

    [Theory]
    [MemberData(nameof(AcceptedNames))]
    public void RawCandidates_WhenValidUtf8IsDecodedByTheReadOrByTheProvidersOwnStreamRoute_ThenTheyAreIdenticalAtEveryReadSize(string name)
    {
        // The provider's own route builds its decoder from the stream itself; the read builds a
        // strict decoder and hands it to the provider through the whole-span reader. For valid
        // UTF-8, with or without a leading byte order mark, both must see the same candidates, at
        // each read size.
        var bytes = Accepted().Single(i => i.Name == name).Bytes;
        foreach (var size in ReadSizes)
        {
            var owned = DelimitedSourceReaderTests.RawCandidates(() => new ScriptedStream(bytes) { MaxChunk = size }).Select(Render);

            Assert.Equal(ProviderRoute(new ScriptedStream(bytes) { MaxChunk = size }), owned);
        }
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Read_WhenTheInputIsValidUtf8_ThenOnlyOneLeadingByteOrderMarkIsConsumedAndEveryOtherCharacterIsKept(int readSize)
    {
        // A read of one to three bytes splits the leading mark and every multibyte character.
        // The first mark is consumed, a second one is the content U+FEFF, and a genuine U+FFFD is
        // data like any other character.
        var plain = await WideRecords(Utf8(Text), readSize);
        var marked = await WideRecords(Accepted().Single(i => i.Name == "utf8-bom").Bytes, readSize);
        var twice = await WideRecords(Accepted().Single(i => i.Name == "bom-then-bom").Bytes, readSize);
        var replacement = await WideRecords(Accepted().Single(i => i.Name == "replacement-character").Bytes, readSize);
        var markOnly = await WideRecords(Accepted().Single(i => i.Name == "bom-only").Bytes, readSize);

        Assert.Equal(
            [Render(["a", "b"]), Render(["café", "日本"]), Render(["q\r\nr", "z"])],
            plain);
        Assert.Equal(plain, marked);
        Assert.Equal([Render([U(0xFEFF) + "x", "y"])], twice);
        Assert.Equal([Render(["a", U(0xFFFD)])], replacement);
        Assert.Empty(markOnly);
    }

    [Theory]
    [MemberData(nameof(RejectedNames))]
    public async Task Read_WhenTheBytesAreNotValidUtf8_ThenEveryReadFailsWithTheDecoderException(string name)
    {
        // Schema, unbound and bound record reads in both shapes, at each read size: the decoder's
        // own exception, unwrapped, so every read-failure boundary already classifies it.
        var bytes = Rejected().Single(i => i.Name == name).Bytes;
        foreach (var size in ReadSizes)
        {
            Func<Stream> open = () => new ScriptedStream(bytes) { MaxChunk = size };

            await Assert.ThrowsAsync<DecoderFallbackException>(async () => await WideSession(open).GetSchemaAsync());
            await Assert.ThrowsAsync<DecoderFallbackException>(() => DrainAsync(WideSession(open).ReadAsync()));
            await Assert.ThrowsAsync<DecoderFallbackException>(() => DrainAsync(new WideCsvSource(open, WideBinding()).ReadAsync()));
            await Assert.ThrowsAsync<DecoderFallbackException>(async () => await TripleSession(open).GetSchemaAsync());
            await Assert.ThrowsAsync<DecoderFallbackException>(
                () => DrainAsync(TripleSession(open).ReadRowsAsync(new TripleColumns(0, 1, 0))));
            await Assert.ThrowsAsync<DecoderFallbackException>(
                () => DrainAsync(new TripleCsvSource(open, TripleBinding(columns: new TripleColumns(0, 1, 0))).ReadRowsAsync()));
        }
    }

    [Fact]
    public async Task Read_WhenTheDecoderFails_ThenTheStreamIsClosedOnceAndNothingWraps()
    {
        var stream = new ScriptedStream(Rejected().Single(i => i.Name == "invalid-mid").Bytes);

        var failure = await Assert.ThrowsAsync<DecoderFallbackException>(() => DrainAsync(WideSession(() => stream).ReadAsync()));

        Assert.Null(failure.InnerException);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenTheDecoderFailsAndClosingFailsToo_ThenTheDecoderFailureWinsAndCarriesTheCloseFailure()
    {
        var close = new IOException("close");
        var stream = new ScriptedStream(Rejected().Single(i => i.Name == "invalid-mid").Bytes) { DisposeFailure = close };

        var failure = await Assert.ThrowsAsync<DecoderFallbackException>(async () => await WideSession(() => stream).GetSchemaAsync());

        Assert.Equal<Exception>([close], Assert.IsAssignableFrom<IReadOnlyList<Exception>>(failure.Data[DelimitedSourceReader.SecondaryCleanupFailuresKey]));
    }

    [Fact]
    public async Task Read_WhenCancellationIsObservedBeforeTheInvalidBytesAreRead_ThenTheReadIsCancelled()
    {
        // The first read serves only valid bytes and requests cancellation; the invalid byte would
        // arrive with the next read, which never happens.
        using var cancellation = new CancellationTokenSource();
        byte[] bytes = [.. Utf8("a,b\n"), 0xFF, .. Utf8("\n")];
        var stream = new CancellingStream(bytes, firstRead: 4, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await WideSession(() => stream).GetSchemaAsync(cancellation.Token));
        Assert.Equal(4, stream.Served);
    }

    // Serves `firstRead` bytes on its first read and then requests cancellation.
    private sealed class CancellingStream(byte[] bytes, int firstRead, CancellationTokenSource cancellation)
        : MemoryStream(bytes, writable: false)
    {
        private bool _first = true;

        public long Served { get; private set; }

        // A derived MemoryStream's span read is routed to this array read.
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, _first ? Math.Min(count, firstRead) : count);
            Served += read;
            if (_first)
            {
                _first = false;
                cancellation.Cancel();
            }

            return read;
        }
    }

    private static async Task<List<string>> WideRecords(byte[] bytes, int readSize) =>
        (await DrainAsync(WideSession(() => new ScriptedStream(bytes) { MaxChunk = readSize }, missingToken: string.Empty).ReadAsync()))
            .Select(r => Render(Fields(r)))
            .ToList();

    private static List<string> ProviderRoute(Stream stream)
    {
        var candidates = new List<string>();
        using var sep = DelimitedSourceReader.ProviderOptions(',').From(stream);
        while (sep.MoveNext())
        {
            var row = sep.Current;
            var fields = new string[row.ColCount];
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i] = new string(row[i].Span);
            }

            candidates.Add(Render(fields));
        }

        return candidates;
    }

    /// <summary>A pass-through stream hashing every byte read, as the CLI's input hashing does.</summary>
    internal sealed class HashingStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            hash.AppendData(buffer[..n]);
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
