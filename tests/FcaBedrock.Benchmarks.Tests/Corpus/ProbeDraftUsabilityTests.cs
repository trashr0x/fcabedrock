using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Discovery;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// A probe draft is only worth measuring if it is <b>usable</b>: D-107 says a draft must re-read,
/// resolve, and convert the very source it was discovered from.
/// <para>
/// The Discovery suite already proves the round trip on its own small fixtures. What is added here
/// is the end of the sentence — <em>and convert</em> — over the benchmark corpora, which are the ones
/// the probe timings are quoted against. A draft that serialized cleanly but could not convert its
/// own source would make every probe measurement a measurement of something nobody can use.
/// </para>
/// </summary>
public sealed class ProbeDraftUsabilityTests
{
    private static async Task<(string Toml, int Attributes)> DraftAsync(
        string dataPath, string specText, ProbeOptions options)
    {
        var settings = ConversionPipeline.RequireReadSettings(ConversionPipeline.RequireDocument(specText));
        var session = (IWideSourceSession)ConversionPipeline.CreateSession(settings, dataPath);
        var result = await Prober.ProbeAsync(session, settings, options);

        Assert.True(result.TryGetValue(out var draft), "the probe produced no draft.");
        return (SpecWriter.Write(draft), draft.Attributes.Count);
    }

    private static async Task<long> ConvertAsync(TempDirectory temp, string name, string specText, string dataPath)
    {
        var specPath = temp.File(name + ".toml");
        var outputPath = temp.File(name + ".dat");
        await File.WriteAllTextAsync(specPath, specText, TestContext.Current.CancellationToken);

        var conversion = await ConversionPipeline.FromSpecFileAsync(specPath, dataPath);
        var diagnostics = new List<BedrockDiagnostic>();
        await using (var output = File.Create(outputPath))
        {
            await DatWriter.WriteAsync(conversion.Emit(diagnostics), WriterOptions.Native, output);
        }

        OutputValidation.RequireCleanEmit(diagnostics, name);
        return new FileInfo(outputPath).Length;
    }

    [Fact]
    public async Task AW16Draft_ShouldRereadStablyAndConvertItsOwnSource()
    {
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, 3_000);
        }

        var (toml, attributes) = await DraftAsync(dataPath, W16Specs.Declared, ProbeOptions.Default);
        Assert.Equal(W16Corpus.ColumnCount, attributes);

        // Re-read stability: the draft parses, and writing it again produces the same bytes. A draft
        // that changed on a round trip could not be committed and reused.
        var reread = ConversionPipeline.RequireDocument(toml);
        Assert.Equal(toml, SpecWriter.Write(reread));

        // ...and it converts the source it came from, cleanly.
        Assert.True(await ConvertAsync(temp, "w16-draft", toml, dataPath) > 0);
    }

    [Fact]
    public async Task AnAdsWidthDraft_ShouldRereadStablyAndConvertItsOwnSource()
    {
        // 1,559 discovered attributes: the width case, where a draft is large enough that a
        // serialization defect would be easy to miss by inspection.
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("ads.csv");
        await using (var data = File.Create(dataPath))
        {
            AdsCorpus.Write(data, 500);
        }

        var (toml, attributes) = await DraftAsync(dataPath, AdsSpecs.Declared, ProbeOptions.Default);
        Assert.Equal(AdsCorpus.ColumnCount, attributes);

        var reread = ConversionPipeline.RequireDocument(toml);
        Assert.Equal(toml, SpecWriter.Write(reread));

        Assert.True(await ConvertAsync(temp, "ads-draft", toml, dataPath) > 0);
    }

    [Fact]
    public async Task ATruncatedDraft_ShouldStillConvertItsOwnSource()
    {
        // The case that matters most, and the one D-108's recovery policy exists for: a truncated
        // draft carries a partial domain, so converting the same source would meet values the draft
        // never recorded. It stays usable because the truncated attribute carries `include`, which
        // recovers the tail at calibration. A truncated draft that could not convert its own source
        // would be a draft in name only.
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, 3_000);
        }

        var (toml, _) = await DraftAsync(
            dataPath, W16Specs.Declared, ProbeOptions.Create(valueRetentionLimit: 64));

        Assert.Contains("include", toml, StringComparison.Ordinal);

        var reread = ConversionPipeline.RequireDocument(toml);
        Assert.Equal(toml, SpecWriter.Write(reread));

        Assert.True(await ConvertAsync(temp, "w16-truncated-draft", toml, dataPath) > 0);
    }
}
