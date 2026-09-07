using System.Security.Cryptography;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// A width oracle can be wrong in a way that still looks right: crossing every declared column
/// produces a large, plausible file. So the expectation is checked against a real conversion, and the
/// sparsity it claims is asserted rather than assumed.
/// </summary>
public sealed class AdsOracleTests
{
    private const long Records = 200;

    [Fact]
    public async Task DeclaredConversion_ShouldMatchTheOracleByteForByte()
    {
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("ads.csv");
        var specPath = temp.File("ads.toml");
        var outputPath = temp.File("ads.dat");

        await using (var data = File.Create(dataPath))
        {
            AdsCorpus.Write(data, Records);
        }

        await File.WriteAllTextAsync(specPath, AdsSpecs.Declared, TestContext.Current.CancellationToken);

        var conversion = await ConversionPipeline.FromSpecFileAsync(specPath, dataPath);
        Assert.Equal(AdsSpecs.FormalAttributeCount, conversion.Plan.FormalAttributes.Count);

        // The rendered names reach a .cxt header, so the oracle's list has to be the planner's list
        // - and it is spelled from the §10.7 defaults rather than borrowed from the planner.
        Assert.Equal(
            AdsSpecs.FormalAttributeNames,
            conversion.Plan.FormalAttributes.Select(attribute => attribute.RenderedName).ToList());

        var diagnostics = new List<BedrockDiagnostic>();
        await using (var output = File.Create(outputPath))
        {
            await DatWriter.WriteAsync(conversion.Emit(diagnostics), WriterOptions.Native, output);
        }

        OutputValidation.RequireCleanEmit(diagnostics, "ads-width");

        var produced = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var expected = AdsOracle.Expect(Records);

        Assert.Equal(expected.ByteLength, produced.LongLength);
        Assert.Equal(expected.Sha256, Convert.ToHexStringLower(SHA256.HashData(produced)));
    }

    [Fact]
    public void Crosses_ShouldBeSparseAndAscending()
    {
        // Three numeric bins plus a handful of flags out of 1,568 columns. An oracle that crossed
        // every declared column would produce a file of the right shape and the wrong content.
        for (var row = 0L; row < 50; row++)
        {
            var ids = AdsOracle.Crosses(row);

            Assert.Equal(ids, ids.Order().ToList());
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.InRange(ids.Count, 3, 80);
            Assert.All(ids, id => Assert.InRange(id, 0, AdsSpecs.FormalAttributeCount - 1));
        }
    }

    [Fact]
    public void Layout_ShouldPlaceTheTermsBetweenTheLocalFlagAndTheClass()
    {
        Assert.Equal(AdsSpecs.LocalId + 1, AdsSpecs.TermBase);
        Assert.Equal(AdsSpecs.TermBase + AdsCorpus.TermColumns, AdsSpecs.ClassId);
        Assert.Equal(AdsSpecs.ClassId + 1, AdsSpecs.FormalAttributeCount);
        Assert.Equal(AdsSpecs.FormalAttributeCount, AdsSpecs.FormalAttributeNames.Count);
    }
}
