using System.Security.Cryptography;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The lexical family's generator, specs, classification and registry entries. Nothing here reads
/// a corpus with the product reader except to show that the spec's settings reach it.
/// </summary>
public sealed class LexicalCorpusTests
{
    public static TheoryData<string> VariantNames() => new(LexicalCorpus.Variants.Select(variant => variant.Name));

    private static byte[] Generate(LexicalVariant variant, long records)
    {
        using var stream = new MemoryStream();
        LexicalCorpus.Write(variant, stream, records, TestContext.Current.CancellationToken);
        return stream.ToArray();
    }

    [Theory]
    [MemberData(nameof(VariantNames))]
    public void Write_WhenRunTwice_ThenTheBytesAreIdentical(string name)
    {
        var variant = LexicalCorpus.Variant(name);

        Assert.Equal(SHA256.HashData(Generate(variant, 200)), SHA256.HashData(Generate(variant, 200)));
    }

    [Theory]
    [MemberData(nameof(VariantNames))]
    public void Spec_WhenResolved_ThenItCarriesTheVariantsDelimiterAndAHeader(string name)
    {
        var variant = LexicalCorpus.Variant(name);
        var read = SpecReader.Read(LexicalCorpus.Spec(variant));
        Assert.True(read.TryGetValue(out var document), string.Join("; ", read.Diagnostics.Select(d => d.Message)));

        var settings = SpecResolver.ResolveReadSettings(document);
        var resolved = SpecResolver.Resolve(document, new SourceSchema(variant.Columns));

        Assert.True(settings.TryGetValue(out var readSettings));
        Assert.Equal(variant.Delimiter, readSettings.Delimiter);
        Assert.True(readSettings.HasHeader);
        Assert.True(resolved.TryGetValue(out _), string.Join("; ", resolved.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Variants_ShouldClassifyExactlyTheTwoThatExerciseTheWhitespaceAndBlankRules()
    {
        // A variant reads the same under narrower rules (only spaces removed around fields, blank
        // lines kept as records) exactly when no raw cell carries other whitespace at its edges and
        // no blank line follows a record.
        foreach (var variant in LexicalCorpus.Variants)
        {
            var otherEdgeWhitespace = false;
            for (var row = 0L; row < 20; row++)
            {
                for (var column = 0; column < variant.Columns; column++)
                {
                    var raw = variant.RawValue(row, column);
                    otherEdgeWhitespace |= raw.Length > 0
                        && ((char.IsWhiteSpace(raw[0]) && raw[0] != ' ') || (char.IsWhiteSpace(raw[^1]) && raw[^1] != ' '));
                }
            }

            Assert.True(
                variant.ExercisesWhitespaceAndBlankRules == (otherEdgeWhitespace || variant.BlankLineAfterEach),
                variant.Name);
        }

        Assert.Equal(
            ["unicode-whitespace", "blank-runs"],
            LexicalCorpus.Variants.Where(variant => variant.ExercisesWhitespaceAndBlankRules).Select(variant => variant.Name));
    }

    [Theory]
    [MemberData(nameof(VariantNames))]
    public void Registry_WhenAVariantIsRegistered_ThenItsCaseCarriesTheGeneratorRevisionAndTheVariantsSize(string name)
    {
        var variant = LexicalCorpus.Variant(name);
        var corpus = CorpusCases.Find($"lexical-{name}-small");

        Assert.NotNull(corpus);
        Assert.Equal(CorpusTier.Small, corpus.Tier);
        Assert.Equal(LexicalCorpus.GeneratorRevision, corpus.GeneratorRevision);
        Assert.Equal(variant.Records, corpus.Records);
        Assert.Equal(variant.Columns, corpus.Columns);
        Assert.Equal(CorpusOrigin.Generated, corpus.Origin);
    }
}
