using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The long-text family exists so the probe's <em>text</em> guard can be reached before its value
/// guard, and so string allocation stops hiding behind per-record overhead. Both depend on the
/// values actually being long, which is what these tests hold.
/// </summary>
public sealed class LongTextCorpusTests
{
    private const long Records = 1_000;

    private static byte[] Generate(long records)
    {
        using var buffer = new MemoryStream();
        LongTextCorpus.Write(buffer, records);
        return buffer.ToArray();
    }

    [Fact]
    public void Write_WhenTheSameTierIsGeneratedTwice_ThenTheBytesAreIdentical()
    {
        Assert.Equal(SHA256.HashData(Generate(Records)), SHA256.HashData(Generate(Records)));
    }

    [Fact]
    public void BlobDomain_ShouldBeEightDistinctValuesOfExactlyTheDeclaredLength()
    {
        // Few values, each large: the guard-3 shape. Distinctness matters because a duplicated
        // domain value would quietly halve the retained text the case is pinned to.
        Assert.Equal(8, LongTextCorpus.BlobDomain.Count);
        Assert.Equal(8, LongTextCorpus.BlobDomain.Distinct(StringComparer.Ordinal).Count());
        Assert.All(LongTextCorpus.BlobDomain, blob => Assert.Equal(LongTextCorpus.BlobLength, blob.Length));
    }

    [Fact]
    public void Notes_ShouldSpanTheDeclaredLengthRangeAndBeNearlyAllDistinct()
    {
        var notes = Enumerable.Range(0, 500).Select(row => LongTextCorpus.Note(row)).ToList();

        Assert.All(notes, note => Assert.InRange(
            note.Length,
            LongTextCorpus.NoteMinimumLength,
            LongTextCorpus.NoteMinimumLength + LongTextCorpus.NoteLengthSpan - 1));

        // High value count AND high text volume at once: a note repeated across rows would make
        // this a small-domain case wearing a large-value costume.
        Assert.True(notes.Distinct(StringComparer.Ordinal).Count() > 495);
    }

    [Fact]
    public void Write_ShouldNeedNoEscaping()
    {
        var text = Encoding.UTF8.GetString(Generate(100));

        Assert.DoesNotContain('"', text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void Write_ShouldProduceTheHeaderPlusExactlyOneLinePerRecord()
    {
        var lines = Encoding.UTF8.GetString(Generate(13)).Split('\n');

        Assert.Equal(LongTextCorpus.HeaderLine, lines[0]);
        Assert.Equal(13 + 2, lines.Length);
        Assert.All(lines[..^1], line => Assert.Equal(LongTextCorpus.ColumnCount, line.Split(',').Length));
    }

    [Fact]
    public async Task DeclaredConversion_ShouldMatchTheOracleByteForByte()
    {
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("longtext.csv");
        var specPath = temp.File("longtext.toml");
        var outputPath = temp.File("longtext.dat");

        await using (var data = File.Create(dataPath))
        {
            LongTextCorpus.Write(data, Records);
        }

        await File.WriteAllTextAsync(specPath, LongTextSpecs.Declared, TestContext.Current.CancellationToken);

        var conversion = await ConversionPipeline.FromSpecFileAsync(specPath, dataPath);
        Assert.Equal(LongTextSpecs.FormalAttributeCount, conversion.Plan.FormalAttributes.Count);

        var diagnostics = new List<BedrockDiagnostic>();
        await using (var output = File.Create(outputPath))
        {
            await DatWriter.WriteAsync(conversion.Emit(diagnostics), WriterOptions.Native, output);
        }

        OutputValidation.RequireCleanEmit(diagnostics, "long text");

        var produced = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var expected = LongTextOracle.Expect(Records);

        Assert.Equal(expected.ByteLength, produced.LongLength);
        Assert.Equal(expected.Sha256, Convert.ToHexStringLower(SHA256.HashData(produced)));

        // Two crosses per object, always: the context is trivial by design, so a cost measured over
        // this family is attributable to the strings rather than to the shape of the incidence.
        Assert.Equal(Records * 2, expected.Crosses);
    }
}
