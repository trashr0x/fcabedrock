using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// The lexical drain oracle derives its summary from the variant's decoded-value definition alone,
/// never from a reader. These tests pin what it counts and show that the product reader, given the
/// variant's own spec, drains the generated file to exactly that summary.
/// </summary>
public sealed class LexicalDrainOracleTests
{
    public static TheoryData<string> VariantNames() => new(LexicalCorpus.Variants.Select(variant => variant.Name));

    [Fact]
    public void Expected_WhenTheVariantHasBlankLines_ThenTheyAreNotRecords()
    {
        var expected = LexicalDrainOracle.Expected(LexicalCorpus.Variant("blank-runs"), 3);

        Assert.Equal(3, expected.Records);
        Assert.Equal(48, expected.PresentFields);
    }

    [Fact]
    public void Expected_WhenValuesArePaddedWithUnicodeWhitespace_ThenOnlyTheDecodedCharactersCount()
    {
        // u0_0 .. u0_15: 4 characters for columns 0-9 and 5 for columns 10-15, with no padding.
        var expected = LexicalDrainOracle.Expected(LexicalCorpus.Variant("unicode-whitespace"), 1);

        Assert.Equal((10 * 4) + (6 * 5), expected.ValueCharacters);
    }

    [Fact]
    public void Expected_WhenComputedTwice_ThenTheSummariesAreEqual()
    {
        foreach (var variant in LexicalCorpus.Variants)
        {
            Assert.Equal(LexicalDrainOracle.Expected(variant, 100), LexicalDrainOracle.Expected(variant, 100));
        }
    }

    [Theory]
    [MemberData(nameof(VariantNames))]
    public async Task Expected_WhenTheProductReaderDrainsTheGeneratedFile_ThenTheSummariesAgree(string name)
    {
        var variant = LexicalCorpus.Variant(name);
        const long Records = 300;
        using var temp = TempDirectory.Create();
        var path = temp.File("data.csv");
        await using (var stream = File.Create(path))
        {
            LexicalCorpus.Write(variant, stream, Records, TestContext.Current.CancellationToken);
        }

        var read = SpecReader.Read(LexicalCorpus.Spec(variant));
        Assert.True(read.TryGetValue(out var document));
        Assert.True(SpecResolver.ResolveReadSettings(document).TryGetValue(out var settings));
        var session = new WideCsvSession(() => File.OpenRead(path), settings);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);

        var actual = default(DrainSummary);
        await foreach (var record in session.ReadAsync(TestContext.Current.CancellationToken))
        {
            actual = actual.AddRecord();
            for (var field = 0; field < record.FieldCount; field++)
            {
                actual = actual.AddField(record.Field(field));
            }
        }

        Assert.Equal(variant.Columns, schema.ColumnCount);
        Assert.Equal(LexicalDrainOracle.Expected(variant, Records), actual);
    }
}
