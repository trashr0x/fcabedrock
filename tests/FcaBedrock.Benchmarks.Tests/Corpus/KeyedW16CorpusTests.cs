using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The keyed dedupe family is only evidence about the dedupe path if two things hold: its keys
/// really do repeat non-contiguously, and its expectation really is the union of the plain W16 rows
/// behind each object. Both are checked here, and the second is checked against a real conversion
/// rather than against another expectation.
/// </summary>
public sealed class KeyedW16CorpusTests
{
    private const long Records = 2_000;

    private static byte[] Generate(long records)
    {
        using var buffer = new MemoryStream();
        KeyedW16Corpus.Write(buffer, records);
        return buffer.ToArray();
    }

    [Fact]
    public void Write_WhenTheSameTierIsGeneratedTwice_ThenTheBytesAreIdentical()
    {
        Assert.Equal(SHA256.HashData(Generate(Records)), SHA256.HashData(Generate(Records)));
    }

    [Fact]
    public void Write_ShouldRejectARowCountThatIsNotAWholeNumberOfObjects()
    {
        using var buffer = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => KeyedW16Corpus.Write(buffer, records: 9));
    }

    [Fact]
    public void Write_ShouldCarryTheKeyColumnAheadOfTheSixteenW16Columns()
    {
        var lines = Encoding.UTF8.GetString(Generate(20)).Split('\n');

        Assert.StartsWith("okey,", lines[0], StringComparison.Ordinal);
        Assert.Equal(KeyedW16Corpus.ColumnCount, lines[0].Split(',').Length);
        Assert.Equal(1 + W16Corpus.ColumnCount, KeyedW16Corpus.ColumnCount);
    }

    [Fact]
    public void Keys_ShouldRepeatExactlyFourTimesAndNeverContiguously()
    {
        // Contiguous key runs would let the sort-merge backend look easy. Every key here recurs
        // only after all the others have appeared, which is its honest worst case.
        var keys = new List<string>();
        for (var row = 0L; row < Records; row++)
        {
            keys.Add(KeyedW16Corpus.Key(row, Records));
        }

        var groups = keys.GroupBy(key => key, StringComparer.Ordinal).ToList();
        Assert.Equal(Records / KeyedW16Corpus.RowsPerObject, groups.Count);
        Assert.All(groups, group => Assert.Equal(KeyedW16Corpus.RowsPerObject, group.Count()));

        for (var row = 1; row < keys.Count; row++)
        {
            Assert.NotEqual(keys[row - 1], keys[row]);
        }
    }

    [Fact]
    public void RowsOf_ShouldNameExactlyTheRowsThatCarryThatKey()
    {
        var objects = KeyedW16Corpus.Objects(Records);
        for (var index = 0L; index < objects; index += 137)
        {
            var expected = KeyedW16Corpus.Key(index, Records);
            foreach (var row in KeyedW16Corpus.RowsOf(index, Records))
            {
                Assert.Equal(expected, KeyedW16Corpus.Key(row, Records));
            }
        }
    }

    [Fact]
    public void FirstOccurrenceOrder_ShouldBeAscendingObjectIndex()
    {
        // The object order a dedupe emits is first-occurrence order (§17 rule 4). The first
        // occurrence of key k{i} is row i, so objects come out in ascending index - which is what
        // lets the oracle index them by that number.
        var objects = KeyedW16Corpus.Objects(Records);
        for (var index = 0L; index < objects; index++)
        {
            Assert.Equal(index, KeyedW16Corpus.RowsOf(index, Records).First());
        }
    }

    [Fact]
    public void CleanedValue_ShouldDelegateEveryNonKeyColumnToW16()
    {
        // One value definition shared by two families is what lets a keyed expectation be built out
        // of plain W16 crosses at all.
        for (var column = 1; column < KeyedW16Corpus.ColumnCount; column++)
        {
            Assert.Equal(
                W16Corpus.CleanedValue(53, column - 1),
                KeyedW16Corpus.CleanedValue(53, column, Records));
        }
    }

    [Fact]
    public async Task DedupeConversion_ShouldMatchTheUnionOracleByteForByte()
    {
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("keyed.csv");
        var specPath = temp.File("keyed.toml");
        var outputPath = temp.File("keyed.dat");

        await using (var data = File.Create(dataPath))
        {
            KeyedW16Corpus.Write(data, Records);
        }

        await File.WriteAllTextAsync(specPath, KeyedW16Specs.Declared, TestContext.Current.CancellationToken);

        var conversion = await ConversionPipeline.FromSpecFileAsync(specPath, dataPath);

        // The load-bearing identity: the keyed spec differs from the plain one only in its column
        // offsets, its object key, and its duplicate policy, so it must plan the SAME formal
        // attributes in the same order. If that ever drifted, the keyed oracle - which is built out
        // of plain W16 crosses - would silently index into the wrong columns and still produce a
        // plausible-looking file.
        Assert.Equal(
            W16Specs.DeclaredFormalAttributeNames,
            conversion.Plan.FormalAttributes.Select(attribute => attribute.RenderedName).ToList());

        var diagnostics = new List<BedrockDiagnostic>();
        await using (var output = File.Create(outputPath))
        {
            await DatWriter.WriteAsync(conversion.Emit(diagnostics), WriterOptions.Native, output);
        }

        // The aggregated DuplicateObjectKey is the conversion saying it merged rows, which is what
        // the case asked for; nothing else is acceptable.
        OutputValidation.RequireCleanEmit(diagnostics, "keyed dedupe", [DiagnosticCode.DuplicateObjectKey]);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.DuplicateObjectKey);

        var produced = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var expected = KeyedW16Oracle.Expect(Records);

        Assert.Equal(expected.Objects, KeyedW16Corpus.Objects(Records));
        Assert.Equal(expected.ByteLength, produced.LongLength);
        Assert.Equal(expected.Sha256, Convert.ToHexStringLower(SHA256.HashData(produced)));
    }

    [Fact]
    public void Oracle_ShouldUnionRatherThanConcatenate()
    {
        // Four rows contribute up to twenty-four crosses between them, but a deduped object holds a
        // SET: the union is at most the plan's width, and in practice far smaller. A concatenating
        // oracle would still produce a plausible number, so the distinction is asserted.
        for (var index = 0L; index < 25; index++)
        {
            var union = KeyedW16Oracle.Crosses(index, Records);
            var concatenated = KeyedW16Corpus.RowsOf(index, Records)
                .SelectMany(W16DeclaredOracle.Crosses)
                .Count();

            Assert.Equal(union.Distinct().Count(), union.Count);
            Assert.True(union.Count <= concatenated);
            Assert.Equal(union, union.Order().ToList());
        }
    }
}
