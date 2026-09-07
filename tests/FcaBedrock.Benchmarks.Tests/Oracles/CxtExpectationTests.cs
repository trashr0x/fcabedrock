using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// The <c>.cxt</c> expectation is spelled from §18.1 rather than from the writer, which is what makes
/// it evidence — and also what makes it capable of being subtly wrong on its own. These tests check
/// it the only way that settles the question: against the real writer's bytes.
/// </summary>
public sealed class CxtExpectationTests
{
    private const long Records = 300;

    [Fact]
    public async Task Expectation_ShouldMatchTheRealWriterByteForByte()
    {
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        var specPath = temp.File("w16.toml");
        var outputPath = temp.File("w16.cxt");

        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, Records);
        }

        await File.WriteAllTextAsync(specPath, W16Specs.Declared, TestContext.Current.CancellationToken);

        var conversion = await ConversionPipeline.FromSpecFileAsync(specPath, dataPath);
        var diagnostics = new List<BedrockDiagnostic>();
        await using (var output = File.Create(outputPath))
        {
            await CxtWriter.WriteAsync(
                conversion.Plan, () => conversion.Emit(diagnostics), WriterOptions.Native, output);
        }

        OutputValidation.RequireCleanEmit(diagnostics, "w16 cxt");

        var produced = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var expected = CxtExpectation.Stream(
            Records,
            W16Specs.DeclaredFormalAttributeNames,
            index => index.ToString(CultureInfo.InvariantCulture),
            W16DeclaredOracle.Crosses);

        Assert.Equal(expected.ByteLength, produced.LongLength);
        Assert.Equal(expected.Sha256, Convert.ToHexStringLower(SHA256.HashData(produced)));
    }

    [Fact]
    public void Expectation_ShouldRenderTheDocumentedLayout()
    {
        // Header, blank, counts, blank, object names, attribute names, then one fixed-width row per
        // object. Small enough to read, so the layout is asserted directly rather than only through
        // a digest that could agree for the wrong reason.
        var expected = CxtExpectation.Stream(
            objects: 2,
            attributeNames: ["a", "b", "c"],
            objectName: index => "o" + index.ToString(CultureInfo.InvariantCulture),
            crossesOf: index => index == 0 ? [0, 2] : []);

        var rendered = "B\n\n2\n3\n\no0\no1\na\nb\nc\nX.X\n...\n";

        Assert.Equal(Encoding.UTF8.GetByteCount(rendered), expected.ByteLength);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rendered))), expected.Sha256);
        Assert.Equal(2, expected.Crosses);
    }

    [Fact]
    public void Expectation_ShouldCountCrossesRatherThanRows()
    {
        var expected = CxtExpectation.Stream(
            objects: 4,
            attributeNames: ["a", "b"],
            objectName: index => index.ToString(CultureInfo.InvariantCulture),
            crossesOf: _ => [0, 1]);

        Assert.Equal(8, expected.Crosses);
        Assert.Equal(4, expected.Objects);
    }
}
