namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The rewrite behind <c>fingerprint --write</c> (D-140): one reading of when fingerprints can be
/// written, the §14 fully frozen rule, and a rewrite that changes only the three stored fields of
/// the root as authored.
/// </summary>
public sealed class FingerprintWritingTests
{
    [Fact]
    public async Task Corrected_WhenTheRunIsNotFullyFrozen_ThenItThrows()
    {
        // An observed domain needs data, so the spec is not fully frozen.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            """);
        var data = temp.Write("data.csv", PublicationRun.Data);
        var run = OutcomeAssert.Completed(await RunPreparation.PrepareAsync(
            new FileRunRequest { SpecPath = spec, DataPath = data }, new OrchestrationHost(File.OpenRead), CancellationToken.None)).Value;

        Assert.False(run.IsFullyFrozen);
        Assert.Throws<ArgumentException>(() => FingerprintWriting.Corrected(run));
    }

    [Fact]
    public async Task Corrected_WhenTheRunIsFullyFrozen_ThenOnlyTheThreeStoredFieldsChange()
    {
        using var run = await PublicationRun.CreateAsync(PublicationRun.SpecFile);
        Assert.True(run.Run.IsFullyFrozen);

        var corrected = FingerprintWriting.Corrected(run.Run);

        var expected = run.Run.RootDocument with
        {
            Spec = run.Run.RootDocument.Spec! with
            {
                SchemaFingerprint = run.Run.Fingerprints.SchemaFingerprint,
                CxtOutputFingerprint = run.Run.Fingerprints.CxtOutputFingerprint,
                DatOutputFingerprint = run.Run.Fingerprints.DatOutputFingerprint,
            },
        };
        Assert.Equal(expected, corrected);
        Assert.Null(run.Run.RootDocument.Spec!.SchemaFingerprint);
        Assert.Equal(run.Run.RootDocument.Spec.Extends, corrected.Spec!.Extends);
    }
}
