using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The synthetic corpus is a measurement boundary: if it does not regenerate identically, or if the
/// bytes on disk do not carry the values its definition claims, every comparison stated against it
/// is meaningless. These tests hold that boundary.
/// </summary>
public sealed class W16CorpusTests
{
    private static byte[] Generate(long records)
    {
        using var buffer = new MemoryStream();
        W16Corpus.Write(buffer, records);
        return buffer.ToArray();
    }

    [Fact]
    public void Write_WhenTheSameTierIsGeneratedTwice_ThenTheBytesAreIdentical()
    {
        // Determinism is the whole contract: no clock, no ambient random source, no culture.
        Assert.Equal(SHA256.HashData(Generate(2_000)), SHA256.HashData(Generate(2_000)));
    }

    [Fact]
    public void Write_ShouldProduceTheHeaderPlusExactlyOneLinePerRecord()
    {
        var text = Encoding.UTF8.GetString(Generate(37));
        var lines = text.Split('\n');

        Assert.Equal(W16Corpus.HeaderLine, lines[0]);
        Assert.Equal(37 + 2, lines.Length); // header + 37 rows + the empty tail after the last LF
        Assert.Equal(string.Empty, lines[^1]);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void Write_ShouldUseUtf8WithoutABom()
    {
        var bytes = Generate(1);

        Assert.NotEqual([0xEF, 0xBB, 0xBF], bytes.Take(3));
    }

    [Fact]
    public void Write_ShouldQuoteExactlyTheValuesThatCarryADelimiterOrAQuote()
    {
        var text = Encoding.UTF8.GetString(Generate(4_000));

        // The two c6 domain values that need escaping, in their RFC 4180 written form.
        Assert.Contains("\"beta,gamma\"", text, StringComparison.Ordinal);
        Assert.Contains("\"del\"\"ta\"", text, StringComparison.Ordinal);

        // ...and nothing else is quoted: the plain domain values never acquire quotes.
        Assert.DoesNotContain("\"v0\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"yes\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanedValue_ShouldPlaceBothMissingFormsWhereTheDefinitionSaysSo()
    {
        // n_ties carries the explicit missing token; c3 carries an empty cell. Two forms, on
        // purpose: they travel different paths through §5.1 missing detection.
        Assert.True(W16Corpus.TiesIsMissing(2));
        Assert.Null(W16Corpus.CleanedValue(2, W16Corpus.ColTies));
        Assert.False(W16Corpus.TiesIsMissing(3));
        Assert.NotNull(W16Corpus.CleanedValue(3, W16Corpus.ColTies));

        Assert.True(W16Corpus.C3IsMissing(5));
        Assert.Null(W16Corpus.CleanedValue(5, W16Corpus.ColCategoricalFirst + 3));
        Assert.False(W16Corpus.C3IsMissing(6));
        Assert.NotNull(W16Corpus.CleanedValue(6, W16Corpus.ColCategoricalFirst + 3));
    }

    [Fact]
    public void Skew_ShouldBeNinetyPercentOneValue()
    {
        var sevens = Enumerable.Range(0, 1_000).Count(row => W16Corpus.Skew(row) == 7);

        Assert.Equal(900, sevens);
    }

    [Fact]
    public void CleanedValue_ShouldSpanEveryDomainValueAndBothBinaryValues()
    {
        // A corpus whose categorical column never took some of its declared values would leave the
        // corresponding formal-attribute columns permanently empty, which is a silently weaker
        // benchmark than the geometry claims.
        for (var categorical = 0; categorical < 8; categorical++)
        {
            var column = W16Corpus.ColCategoricalFirst + categorical;
            var observed = Enumerable.Range(0, 2_000)
                .Select(row => W16Corpus.CleanedValue(row, column))
                .Where(value => value is not null)
                .Distinct(StringComparer.Ordinal)
                .Count();
            Assert.Equal(8, observed);
        }

        for (var binary = 0; binary < 4; binary++)
        {
            var column = W16Corpus.ColBinaryFirst + binary;
            var observed = Enumerable.Range(0, 200)
                .Select(row => W16Corpus.CleanedValue(row, column))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.Equal(2, observed.Count);
        }
    }

    [Fact]
    public async Task Write_WhenReadBackThroughTheProductionSource_ThenEveryCleanedValueMatchesTheDefinition()
    {
        // The cross-check that makes the definition trustworthy as an oracle: the real reader,
        // over the real bytes, must see exactly what CleanedValue says is there - including the
        // unquoted forms of the two escaped values and both missing forms.
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        var specPath = temp.File("w16.toml");
        const int records = 600;

        await File.WriteAllBytesAsync(dataPath, Generate(records), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(specPath, CorpusCases.W16(CorpusTier.Small).SpecBytes(), TestContext.Current.CancellationToken);

        var session = (IWideSourceSession)await ConversionPipeline.OpenSessionAsync(specPath, dataPath);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);
        Assert.Equal(W16Corpus.ColumnCount, schema.ColumnCount);
        Assert.Equal(W16Corpus.Columns, schema.Header);

        var row = 0L;
        await foreach (var record in session.ReadAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(W16Corpus.ColumnCount, record.FieldCount);
            for (var column = 0; column < W16Corpus.ColumnCount; column++)
            {
                Assert.Equal(W16Corpus.CleanedValue(row, column), record.Field(column));
            }

            row++;
        }

        Assert.Equal(records, row);
    }
}
