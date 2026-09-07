using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The triple family carries the properties the triple path has to get right, so these check that
/// the corpus really has them — a "multi-valued" column that happened to be single-valued, or an
/// "interleaved" file whose subjects were contiguous after all, would quietly turn the benchmark
/// into a weaker one than it claims to be.
/// </summary>
public sealed class T10CorpusTests
{
    private const int Records = 2_000; // 200 subjects: several interleaving blocks

    private static byte[] Generate(long records, TripleLayout layout)
    {
        using var buffer = new MemoryStream();
        T10Corpus.Write(buffer, records, layout);
        return buffer.ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Write_WhenGeneratedTwice_ThenTheBytesAreIdentical(bool grouped)
    {
        // A bool rather than the layout enum: the enum is internal to the benchmark assembly, and a
        // public test signature cannot name it.
        var layout = grouped ? TripleLayout.Grouped : TripleLayout.Interleaved;

        Assert.Equal(SHA256.HashData(Generate(Records, layout)), SHA256.HashData(Generate(Records, layout)));
    }

    [Fact]
    public void Write_ShouldCarryTheSameRowsInBothLayoutsAndOnlyReorderThem()
    {
        var grouped = Lines(Generate(Records, TripleLayout.Grouped));
        var interleaved = Lines(Generate(Records, TripleLayout.Interleaved));

        Assert.Equal(Records, grouped.Length);
        Assert.NotEqual(grouped, interleaved);                       // genuinely a different order
        Assert.Equal(grouped.Order(StringComparer.Ordinal), interleaved.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Write_ShouldMakeSubjectsContiguousOnlyInTheGroupedLayout()
    {
        // The distinction the two orderings turn on: `subject_grouped` requires contiguity and
        // `unordered` exists because real data does not have it.
        Assert.True(IsContiguous(Lines(Generate(Records, TripleLayout.Grouped))));
        Assert.False(IsContiguous(Lines(Generate(Records, TripleLayout.Interleaved))));
    }

    [Fact]
    public void Write_ShouldPreserveFirstAppearanceSubjectOrderAcrossBothLayouts()
    {
        // This is what makes the two layouts comparable at all: object order is first-appearance
        // order, so if the layouts disagreed here their outputs could not be byte-identical.
        Assert.Equal(
            FirstAppearanceSubjects(Lines(Generate(Records, TripleLayout.Grouped))),
            FirstAppearanceSubjects(Lines(Generate(Records, TripleLayout.Interleaved))));
    }

    [Fact]
    public void Rows_ShouldCarryTheMultiValuedDuplicateEquivalentSpellingAndUnmatchedCases()
    {
        var multiValued = 0;
        var equalPair = 0;

        for (var subject = 0L; subject < 200; subject++)
        {
            var rows = Enumerable.Range(0, T10Corpus.RowsPerSubject).Select(row => T10Corpus.Row(subject, row)).ToList();

            // An exact duplicate of an earlier row, which union must treat as idempotent.
            Assert.Equal(rows[0], rows[2]);
            Assert.Equal(rows[3], rows[9]);

            // Two raw spellings of one numeric value: distinct observations, one bin.
            Assert.Equal(rows[5].Value + ".0", rows[6].Value);
            Assert.Equal(rows[5], rows[7]);

            // A predicate nothing binds.
            Assert.Equal(T10Corpus.UnmatchedPredicate, rows[8].Predicate);

            if (T10Corpus.DistinctTissues(subject).Count == 2)
            {
                multiValued++;
            }
            else
            {
                equalPair++;
            }
        }

        // Both shapes must actually occur, or the multi-valued union is never exercised - and the
        // idempotent-duplicate case never is either.
        Assert.True(multiValued > 0, "no subject carried two distinct Tissue values.");
        Assert.True(equalPair > 0, "no subject repeated one Tissue value.");
    }

    [Fact]
    public void Stage_ShouldBeHighCardinalityAcrossSubjects()
    {
        // The count-sensitive calibration is only interesting against a wide population; a Stage
        // that collapsed onto a few values would make the quantile path trivial.
        var distinct = Enumerable.Range(0, 2_000).Select(s => T10Corpus.Stage(s)).Distinct().Count();

        Assert.True(distinct > 1_900, $"only {distinct} distinct stages across 2,000 subjects.");
    }

    [Fact]
    public async Task DeclaredConversion_ShouldProduceIdenticalBytesFromBothLayouts()
    {
        // The load-bearing equivalence: the same observations under a data-independent spec must
        // convert to the same context whether they arrived contiguously or interleaved - one
        // streaming single-pass, the other through the grouping backend.
        using var temp = TempDirectory.Create();

        var grouped = await ConvertAsync(temp, TripleLayout.Grouped);
        var interleaved = await ConvertAsync(temp, TripleLayout.Interleaved);
        var expected = T10DeclaredOracle.Expect(Records);

        Assert.Equal(grouped, interleaved);
        Assert.Equal(expected.ByteLength, grouped.LongLength);
        Assert.Equal(expected.Sha256, Convert.ToHexStringLower(SHA256.HashData(grouped)));
    }

    private static async Task<byte[]> ConvertAsync(TempDirectory temp, TripleLayout layout)
    {
        var name = layout == TripleLayout.Grouped ? "grouped" : "unordered";
        var dataPath = temp.File(name + ".csv");
        var specPath = temp.File(name + ".toml");
        var outputPath = temp.File(name + ".dat");

        await using (var data = File.Create(dataPath))
        {
            T10Corpus.Write(data, Records, layout);
        }

        var spec = layout == TripleLayout.Grouped ? T10Specs.AsGrouped(T10Specs.Declared) : T10Specs.Declared;
        await File.WriteAllTextAsync(specPath, spec, TestContext.Current.CancellationToken);

        var conversion = await ConversionPipeline.FromSpecFileAsync(specPath, dataPath);
        var diagnostics = new List<Diagnostics.BedrockDiagnostic>();
        await using (var output = File.Create(outputPath))
        {
            await DatWriter.WriteAsync(conversion.Emit(diagnostics), WriterOptions.Native, output);
        }

        OutputValidation.RequireCleanEmit(diagnostics, $"t10 {name}");
        return await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
    }

    private static string[] Lines(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsContiguous(string[] lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = string.Empty;
        foreach (var subject in lines.Select(line => line.Split(',')[0]))
        {
            if (string.Equals(subject, current, StringComparison.Ordinal))
            {
                continue;
            }

            if (!seen.Add(subject))
            {
                return false; // a subject recurred after an intervening one
            }

            current = subject;
        }

        return true;
    }

    private static IReadOnlyList<string> FirstAppearanceSubjects(string[] lines)
    {
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subject in lines.Select(line => line.Split(',')[0]))
        {
            if (seen.Add(subject))
            {
                order.Add(subject);
            }
        }

        return order;
    }
}
