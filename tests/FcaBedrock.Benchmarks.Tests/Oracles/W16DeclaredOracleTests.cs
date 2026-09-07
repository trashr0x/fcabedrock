using System.Security.Cryptography;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// The oracle is what turns a timing into evidence, so it is held to two things at once: it must
/// agree with hand-derived facts, and it must agree with what the production pipeline actually
/// produces — while being derived from the corpus definition and the spec's documented semantics
/// rather than from the conversion code.
/// </summary>
public sealed class W16DeclaredOracleTests
{
    [Theory]
    // value, cuts, expected zero-based open-ended bin index
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(39, 3)]
    [InlineData(40, 4)]
    [InlineData(49, 4)]
    public void BinIndex_ShouldPlaceAValueInItsHalfOpenBin(int value, int expected)
    {
        // §11.2 geometry: `<c0`, `[c0, c1)`, ..., `>=cn`. A cut is the INCLUSIVE lower edge of the
        // bin above it.
        Assert.Equal(expected, W16DeclaredOracle.BinIndex(value, W16Specs.TiesCuts));
    }

    [Fact]
    public void Crosses_ShouldSkipTheTiesColumnEntirelyWhenItIsMissing()
    {
        // n_ties has the DEFAULT missing policy: a missing value produces no cross at all.
        var missing = W16DeclaredOracle.Crosses(2);
        var present = W16DeclaredOracle.Crosses(3);

        Assert.True(W16Corpus.TiesIsMissing(2));
        Assert.DoesNotContain(missing, id => id < W16Specs.C0Base);
        Assert.Contains(present, id => id < W16Specs.C0Base);
    }

    [Fact]
    public void Crosses_ShouldCrossTheMissingColumnWhenTheAsAttributeColumnIsMissing()
    {
        // c3 carries missing_policy = "as_attribute": a missing value crosses its own column,
        // which sits AFTER that attribute's value bins (§10.5 / D-074).
        Assert.True(W16Corpus.C3IsMissing(5));
        Assert.Contains(W16Specs.C3MissingId, W16DeclaredOracle.Crosses(5));

        Assert.False(W16Corpus.C3IsMissing(6));
        Assert.DoesNotContain(W16Specs.C3MissingId, W16DeclaredOracle.Crosses(6));
    }

    [Fact]
    public void Crosses_ShouldCrossTheDichotomicColumnOnlyForItsTrueValue()
    {
        for (var row = 0L; row < 40; row++)
        {
            Assert.Equal(W16Corpus.BinaryIsYes(row, 0), W16DeclaredOracle.Crosses(row).Contains(W16Specs.B0Id));
        }
    }

    [Fact]
    public void Crosses_ShouldBeAscendingAndCoverEveryIncludedAttribute()
    {
        for (var row = 0L; row < 200; row++)
        {
            var ids = W16DeclaredOracle.Crosses(row);

            Assert.Equal(ids.Order(), ids);
            Assert.Equal(ids.Distinct(), ids);

            // Four attributes always cross exactly once (c0, c3-or-its-missing-column, n_skew, c6);
            // n_ties and b0 are conditional, so the row is never empty and never over-full.
            var expected = 4 + (W16Corpus.TiesIsMissing(row) ? 0 : 1) + (W16Corpus.BinaryIsYes(row, 0) ? 1 : 0);
            Assert.Equal(expected, ids.Count);
        }
    }

    [Fact]
    public void Expect_ShouldReportTheSameCrossTotalTheRowsCarry()
    {
        const int records = 500;
        var expectation = W16DeclaredOracle.Expect(records);
        var counted = Enumerable.Range(0, records).Sum(row => W16DeclaredOracle.Crosses(row).Count);

        Assert.Equal(records, expectation.Objects);
        Assert.Equal(counted, expectation.Crosses);
    }

    [Fact]
    public async Task Expect_ShouldMatchWhatTheProductionPipelineActuallyWrites()
    {
        // The load-bearing test. Two independent derivations of the same artifact - one from the
        // corpus definition plus the documented spec semantics, one from the shipped
        // reader/planner/emitter/writer - must agree byte for byte. If they ever disagree, either
        // the pipeline changed behaviour or the oracle is wrong, and both are worth stopping for.
        using var temp = TempDirectory.Create();
        var (dataPath, specPath) = await WriteCorpusAsync(temp, records: 400);

        var produced = await ConvertAsync(specPath, dataPath, temp.File("out.dat"));
        var expected = W16DeclaredOracle.Expect(400);

        Assert.Equal(expected.ByteLength, produced.LongLength);
        Assert.Equal(expected.Sha256, Convert.ToHexStringLower(SHA256.HashData(produced)));
    }

    [Fact]
    public async Task Expect_WhenTheDataDiffersFromTheDefinition_ThenTheOracleRejectsTheOutput()
    {
        // The deliberate wrong-output probe: one altered cell must be enough for the oracle to
        // refuse the artifact. An oracle that could not fail here would validate nothing.
        using var temp = TempDirectory.Create();
        var (dataPath, specPath) = await WriteCorpusAsync(temp, records: 400);

        var lines = await File.ReadAllLinesAsync(dataPath, TestContext.Current.CancellationToken);
        var fields = lines[1].Split(',');
        fields[W16Corpus.ColCategoricalFirst] = fields[W16Corpus.ColCategoricalFirst] == "v7" ? "v0" : "v7";
        lines[1] = string.Join(',', fields);
        await File.WriteAllTextAsync(
            dataPath, string.Join('\n', lines) + "\n", TestContext.Current.CancellationToken);

        var produced = await ConvertAsync(specPath, dataPath, temp.File("out.dat"));
        var expected = W16DeclaredOracle.Expect(400);

        Assert.NotEqual(expected.Sha256, Convert.ToHexStringLower(SHA256.HashData(produced)));
        Assert.Throws<InvalidOperationException>(() => OutputValidation.RequireFileMatches(
            temp.File("out.dat"), expected.ByteLength, expected.Sha256, "probe"));
    }

    [Fact]
    public async Task DeclaredSpec_ShouldPlanExactlyTheFrozenColumnLayoutTheOracleIndexesInto()
    {
        // The oracle addresses formal attributes by fixed id. That is only sound while the plan
        // really has those columns in that order, so the frozen layout is checked against a real
        // plan rather than assumed.
        using var temp = TempDirectory.Create();
        var (dataPath, specPath) = await WriteCorpusAsync(temp, records: 200);

        var conversion = await ConversionPipeline.FromSpecFileAsync(specPath, dataPath);
        var rendered = conversion.Plan.FormalAttributes.Select(attribute => attribute.RenderedName).ToList();

        Assert.Equal(W16Specs.DeclaredFormalAttributeNames, rendered);
        Assert.Equal("n_ties-<10", rendered[W16Specs.TiesBase]);
        Assert.Equal("c0-v0", rendered[W16Specs.C0Base]);
        Assert.Equal("b0", rendered[W16Specs.B0Id]);
        Assert.Equal("c3-v0", rendered[W16Specs.C3Base]);
        Assert.Equal("c3-missing", rendered[W16Specs.C3MissingId]);
        Assert.Equal("n_skew-<8", rendered[W16Specs.SkewBase]);
        Assert.Equal("c6-alpha", rendered[W16Specs.C6Base]);
        Assert.Equal(W16Specs.C6Base + 8, rendered.Count);
    }

    private static async Task<(string DataPath, string SpecPath)> WriteCorpusAsync(TempDirectory temp, long records)
    {
        var dataPath = temp.File("w16.csv");
        var specPath = temp.File("w16.toml");

        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, records);
        }

        await File.WriteAllBytesAsync(specPath, CorpusCases.W16(CorpusTier.Small).SpecBytes(), TestContext.Current.CancellationToken);
        return (dataPath, specPath);
    }

    private static async Task<byte[]> ConvertAsync(string specPath, string dataPath, string outputPath)
    {
        var conversion = await ConversionPipeline.FromSpecFileAsync(specPath, dataPath);
        var diagnostics = new List<Diagnostics.BedrockDiagnostic>();

        await using (var output = File.Create(outputPath))
        {
            await DatWriter.WriteAsync(conversion.Emit(diagnostics), WriterOptions.Native, output);
        }

        OutputValidation.RequireCleanEmit(diagnostics, "oracle cross-check");
        return await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
    }
}
