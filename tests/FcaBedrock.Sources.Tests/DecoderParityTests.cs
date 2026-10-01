using System.Security.Cryptography;
using System.Text;
using nietras.SeparatedValues;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The read decodes its stream exactly as the pinned provider's own stream route does (UTF-8, byte
/// order mark detection, the default buffer, replacement of invalid bytes), whatever the stream's
/// read sizes, and the bytes it consumes are the original bytes, so a hash taken over them is the
/// file's hash. Decoded-character preservation is asserted here; the separately owned dependence
/// of non-UTF-8 byte order mark detection on the first read's size is preserved, not corrected.
/// </summary>
public sealed class DecoderParityTests
{
    private const string Text = "a,b\ncafé,日本\n\"q\r\nr\",z\n";

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    public static TheoryData<string> InputNames() => new(Inputs().Select(i => i.Name));

    internal static (string Name, byte[] Bytes)[] Inputs() =>
    [
        ("utf8-no-bom", Utf8(Text)),
        ("utf8-bom", Concat([0xEF, 0xBB, 0xBF], Utf8(Text))),
        ("utf16le-bom", Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes(Text))),
        ("utf16be-bom", Concat([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes(Text))),
        ("utf32le-bom", Concat([0xFF, 0xFE, 0x00, 0x00], new UTF32Encoding(false, false).GetBytes(Text))),
        ("utf32be-bom", Concat([0x00, 0x00, 0xFE, 0xFF], new UTF32Encoding(true, false).GetBytes(Text))),
        ("invalid-mid", Concat(Utf8("a,b"), [0xFF], Utf8("c\nd,e\n"))),
        ("lone-continuation", Concat(Utf8("a,"), [0x80], Utf8("\n"))),
        ("truncated-at-eof", Concat(Utf8("a,caf"), [0xC3])),
        ("overlong", Concat(Utf8("a,"), [0xC0, 0xAF], Utf8("\n"))),
        ("bom-only", [0xEF, 0xBB, 0xBF]),
        ("bom-then-bom", Concat([0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF], Utf8("x,y\n"))),
        ("utf8-4byte", Utf8("a," + char.ConvertFromUtf32(0x1F600) + "\n")),
    ];

    [Theory]
    [MemberData(nameof(InputNames))]
    public async Task Read_WhenTheStreamIsHashedAsItIsRead_ThenTheHashIsTheFilesHash(string name)
    {
        var bytes = Inputs().Single(i => i.Name == name).Bytes;
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
    [MemberData(nameof(InputNames))]
    public void RawCandidates_WhenDecodedByTheReadOrByTheProvidersOwnStreamRoute_ThenTheyAreIdenticalAtEveryReadSize(string name)
    {
        // The provider's own route builds its decoder from the stream itself; the read builds the
        // same decoder and hands it to the provider through the whole-span reader. Whatever the
        // decoder makes of these bytes (a byte order mark, an invalid byte, a truncated character),
        // both routes must see the same candidates, at each read size.
        var bytes = Inputs().Single(i => i.Name == name).Bytes;
        foreach (var size in new[] { int.MaxValue, 1, 2, 3 })
        {
            var owned = DelimitedSourceReaderTests.RawCandidates(() => new ScriptedStream(bytes) { MaxChunk = size }).Select(Render);

            Assert.Equal(ProviderRoute(new ScriptedStream(bytes) { MaxChunk = size }), owned);
        }
    }

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
