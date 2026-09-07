using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Converts the immutable v2 mini fixtures to <c>.dat</c> under <c>--v2-compat</c> and checks the
/// bytes against what v2 itself produced.
/// <para>
/// These are correctness cases wearing a stopwatch, and they are here on purpose. Every synthetic
/// expectation in this suite is authored, so it can only prove that the pipeline agrees with a
/// model written in the same repository; the v2 goldens are external evidence produced by a
/// different program years earlier. A harness that quietly stopped converting correctly would still
/// satisfy its own oracle — but not these bytes.
/// </para>
/// <para>
/// The fixtures are read-only (P-9). They are never copied, normalized, or written; the timed
/// interval covers the same open-emit-write-flush-close boundary as every other conversion case,
/// and validation compares the produced file against the checked-in expected bytes afterwards.
/// </para>
/// </summary>
// Deliberately no [CorpusTier]: these fixtures are a handful of rows, so they have no throughput
// denominator worth publishing, and claiming a synthetic tier's record count beside them would be
// a false denominator. The report prints "n/a" instead, which is the truthful answer.
[BenchmarkCategory(BenchmarkCategories.Mini, BenchmarkCategories.Small)]
public class MiniConvertDatBenchmark
{
    private readonly Dictionary<string, PreparedConversion> _conversions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _expected = new(StringComparer.Ordinal);
    private List<BedrockDiagnostic> _diagnostics = [];
    private byte[] _produced = [];

    /// <summary>Prepares both conversions and loads the immutable expected bytes. Outside timing.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        if (!MiniCorpus.FixturesPresent)
        {
            throw new InvalidOperationException(
                $"the immutable v2 fixtures were not found at '{BenchmarkPaths.V2FixtureRoot}'.");
        }

        foreach (var mini in MiniCorpus.All)
        {
            _conversions[mini.Variant] = await ConversionPipeline.FromBedFileAsync(
                mini.BedPath,
                mini.DataPath,
                mini.Binding,
                mini.ScalingMode,
                ConversionPipeline.LabelStyleFor(WriterOptions.V2Compat)).ConfigureAwait(false);
            _expected[mini.Variant] = await File.ReadAllBytesAsync(mini.ExpectedDatPath).ConfigureAwait(false);
        }
    }

    /// <summary>Discards the previous iteration's state. Outside timing.</summary>
    [IterationSetup]
    public void Reset()
    {
        _diagnostics = [];
        _produced = [];
    }

    /// <summary>mini-mushroom: eight categorical columns, one of which is empty in v2's own bytes.</summary>
    [Benchmark(Description = "mini-mushroom v2-compat dat")]
    public async Task<int> Mushroom() => await ConvertAsync(MiniCorpus.Mushroom).ConfigureAwait(false);

    /// <summary>mini-adult: the numeric cut-bearing mini.</summary>
    [Benchmark(Description = "mini-adult v2-compat dat")]
    public async Task<int> Adult() => await ConvertAsync(MiniCorpus.Adult).ConfigureAwait(false);

    /// <summary>Validates mini-mushroom's bytes against the immutable v2 golden.</summary>
    [IterationCleanup(Target = nameof(Mushroom))]
    public void ValidateMushroom() => Validate(MiniCorpus.Mushroom);

    /// <summary>Validates mini-adult's bytes against the immutable v2 golden.</summary>
    [IterationCleanup(Target = nameof(Adult))]
    public void ValidateAdult() => Validate(MiniCorpus.Adult);

    // The minis are tiny and their v2-compat bytes are the assertion, so the artifact is produced
    // into memory rather than onto disk: writing a 400-byte file would measure the filesystem, not
    // the conversion. Every larger case in this suite writes a real file instead.
    private async Task<int> ConvertAsync(MiniCase mini)
    {
        using var output = new MemoryStream();
        var conversion = _conversions[mini.Variant];
        await DatWriter.WriteAsync(conversion.Emit(_diagnostics), WriterOptions.V2Compat, output).ConfigureAwait(false);
        _produced = output.ToArray();
        return _produced.Length;
    }

    private void Validate(MiniCase mini)
    {
        var what = $"{mini.Variant} v2-compat dat";
        OutputValidation.RequireCleanEmit(_diagnostics, what);
        OutputValidation.RequireBytesMatch(_produced, _expected[mini.Variant], what);
    }
}
