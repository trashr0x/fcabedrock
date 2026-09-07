using System.Text;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// The post-iteration gate. Each of these is a way a benchmark could otherwise publish a throughput
/// figure for work it did not correctly do.
/// </summary>
public sealed class OutputValidationTests
{
    private static readonly byte[] Content = "1 2 3\n4 5\n"u8.ToArray();

    private static string Digest => CorpusCatalog.HashBytes(Content);

    [Fact]
    public void RequireFileMatches_WhenTheArtifactIsExactlyRight_ThenItPasses()
    {
        using var temp = TempDirectory.Create();
        var path = temp.File("out.dat");
        File.WriteAllBytes(path, Content);

        OutputValidation.RequireFileMatches(path, Content.Length, Digest, "case");
    }

    [Fact]
    public void RequireFileMatches_WhenNoArtifactWasProduced_ThenItThrows()
    {
        using var temp = TempDirectory.Create();

        var failure = Assert.Throws<InvalidOperationException>(
            () => OutputValidation.RequireFileMatches(temp.File("absent.dat"), 10, Digest, "case"));
        Assert.Contains("no output was produced", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireFileMatches_WhenTheLengthIsWrong_ThenItThrows()
    {
        using var temp = TempDirectory.Create();
        var path = temp.File("out.dat");
        File.WriteAllBytes(path, Content);

        var failure = Assert.Throws<InvalidOperationException>(
            () => OutputValidation.RequireFileMatches(path, Content.Length + 1, Digest, "case"));
        Assert.Contains("expected 11 bytes", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireFileMatches_WhenTheContentIsWrongButTheLengthIsRight_ThenItThrows()
    {
        // The case a length check alone would miss: a wrong cross id is the same number of bytes.
        using var temp = TempDirectory.Create();
        var path = temp.File("out.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("1 2 9\n4 5\n"));

        var failure = Assert.Throws<InvalidOperationException>(
            () => OutputValidation.RequireFileMatches(path, Content.Length, Digest, "case"));
        Assert.Contains("has digest", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireBytesMatch_ShouldNameTheFirstDifferingByte()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => OutputValidation.RequireBytesMatch("1 2 9\n"u8, "1 2 3\n"u8, "case"));

        Assert.Contains("byte 4", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireBytesMatch_ShouldReportALengthDifferenceWhenThePrefixAgrees()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => OutputValidation.RequireBytesMatch("1 2 3\n1\n"u8, "1 2 3\n"u8, "case"));

        Assert.Contains("8 bytes produced, 6 expected", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireSummary_WhenTheDrainReadTheWrongThing_ThenItThrows()
    {
        var expected = new DrainSummary(10, 160, 400);

        OutputValidation.RequireSummary(expected, expected, "case");
        Assert.Throws<InvalidOperationException>(
            () => OutputValidation.RequireSummary(expected with { Records = 9 }, expected, "case"));
        Assert.Throws<InvalidOperationException>(
            () => OutputValidation.RequireSummary(expected with { PresentFields = 159 }, expected, "case"));
    }

    [Fact]
    public void RequireCleanEmit_ShouldPermitTheDegenerateShapeWarningsAndNothingElse()
    {
        // An empty column, an empty row, and an empty context are outcomes the spec blesses; they
        // cannot hide a wrong result, because byte equality is the real gate.
        OutputValidation.RequireCleanEmit(
            [
                Warning(DiagnosticCode.AttributeHasNoCrosses),
                Warning(DiagnosticCode.ObjectHasNoCrosses),
                Warning(DiagnosticCode.NoObjectsEmitted),
            ],
            "case");

        // Anything else means the run produced an artifact its own caller would have to discard.
        Assert.Throws<InvalidOperationException>(() => OutputValidation.RequireCleanEmit(
            [Warning(DiagnosticCode.UnknownValueObserved)], "case"));
        Assert.Throws<InvalidOperationException>(() => OutputValidation.RequireCleanEmit(
            [Error(DiagnosticCode.GroupingStorageFailed)], "case"));
    }

    private static BedrockDiagnostic Warning(DiagnosticCode code) =>
        new(code, DiagnosticSeverity.Warning, code.ToString(), null);

    private static BedrockDiagnostic Error(DiagnosticCode code) =>
        new(code, DiagnosticSeverity.Error, code.ToString(), null);
}
