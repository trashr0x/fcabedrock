using System.Globalization;
using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Exports to Burmeister <c>.cxt</c> — the format that costs <b>two</b> complete passes.
/// <para>
/// A <c>.cxt</c> header carries the object and attribute counts before any row, and its object names
/// come before the matrix, so a conforming writer takes a bounded object-name pass and then replays
/// the emission for the rows (§18.1). It must never materialize the matrix. That makes it the
/// opposite shape to <c>.dat</c>, which streams once and needs no header count — and the difference
/// between the two on the same corpus is the price of the format, which is a number a user choosing
/// an output format actually wants.
/// </para>
/// <para>
/// The measured interval is the same contract as every other conversion case, so the <em>whole</em>
/// two-pass write is inside it. Bounded coverage on purpose: the matrix asks for the minis, real
/// Adult, the working W16 tier, and the small Ads-width geometry. A dense 73M-record <c>.cxt</c> is
/// explicitly not an M8 product.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert)]
public abstract class CxtExportBenchmark
{
    private ConversionRun _run = null!;

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>The expected artifact, derived independently of the writer.</summary>
    private protected abstract ContextExpectation Expected(PreparedCorpus prepared);

    /// <summary>The formal-attribute count the expectation's frozen layout assumes.</summary>
    private protected abstract int FormalAttributeCount { get; }

    /// <summary>Prepares the plan and the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun($"cxt export ({Corpus.Id})", Corpus, ExportFormat.Cxt);
        await _run.SetupAsync(Expected, FormalAttributeCount).ConfigureAwait(false);
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Runs the name pass, replays the emission, and writes the real <c>.cxt</c> artifact.</summary>
    [Benchmark(Description = "emit + cxt export")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>Validates the artifact after disposal, then reclaims its space.</summary>
    [IterationCleanup]
    public void Validate() => _run.Validate();

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>
/// The W16 <c>.cxt</c> at 730,000 objects: 33 formal attributes, so the matrix is narrow and the
/// object-name pass is the part that grows. Opt-in.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class WideCxtExportWorking : CxtExportBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override int FormalAttributeCount => W16Specs.DeclaredFormalAttributeCount;

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        CxtExpectation.Stream(
            prepared.Records,
            W16Specs.DeclaredFormalAttributeNames,
            // Wide object keys default to row_index, so an object's name is its 0-based data-row
            // index in invariant digits (§5.4).
            index => index.ToString(CultureInfo.InvariantCulture),
            W16DeclaredOracle.Crosses);
}

/// <summary>
/// The Ads-width <c>.cxt</c> at 10,000 objects: 1,568 formal attributes, so each matrix row is
/// 1,568 characters and the file is dominated by the '.' of a sparse context. That asymmetry — a
/// <c>.dat</c> of a few hundred kilobytes against a <c>.cxt</c> of fifteen megabytes over the same
/// data — is exactly what the format choice costs on a wide schema.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdsFamily, CorpusTier.Small)]
public class AdsCxtExportSmall : CxtExportBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Ads(CorpusTier.Small);

    private protected override int FormalAttributeCount => AdsSpecs.FormalAttributeCount;

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        CxtExpectation.Stream(
            prepared.Records,
            AdsSpecs.FormalAttributeNames,
            index => index.ToString(CultureInfo.InvariantCulture),
            AdsOracle.Crosses);
}

/// <summary>
/// Converts the immutable v2 mini fixtures to <c>.cxt</c> under <c>--v2-compat</c> and checks the
/// bytes against what v2 itself produced.
/// <para>
/// The <c>.cxt</c> counterpart of the mini <c>.dat</c> case, and it carries more of v2's format than
/// the <c>.dat</c> does: object names, formal-attribute names, CRLF line endings, and v2's trailing
/// space on non-empty lines. A rendered attribute name is not something a synthetic oracle written
/// in this repository can independently confirm — these bytes were produced by a different program,
/// years earlier, and they can.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Mini, BenchmarkCategories.Small)]
public class MiniConvertCxtBenchmark
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
            _expected[mini.Variant] = await File.ReadAllBytesAsync(mini.ExpectedCxtPath).ConfigureAwait(false);
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
    [Benchmark(Description = "mini-mushroom v2-compat cxt")]
    public async Task<int> Mushroom() => await ConvertAsync(MiniCorpus.Mushroom).ConfigureAwait(false);

    /// <summary>mini-adult: the numeric cut-bearing mini, whose bin labels reach the header.</summary>
    [Benchmark(Description = "mini-adult v2-compat cxt")]
    public async Task<int> Adult() => await ConvertAsync(MiniCorpus.Adult).ConfigureAwait(false);

    /// <summary>Validates mini-mushroom's bytes against the immutable v2 golden.</summary>
    [IterationCleanup(Target = nameof(Mushroom))]
    public void ValidateMushroom() => Validate(MiniCorpus.Mushroom);

    /// <summary>Validates mini-adult's bytes against the immutable v2 golden.</summary>
    [IterationCleanup(Target = nameof(Adult))]
    public void ValidateAdult() => Validate(MiniCorpus.Adult);

    // As with the mini .dat case, the artifact is produced into memory: these fixtures are a few
    // hundred bytes, and writing them to disk would measure the filesystem rather than the
    // conversion. Every larger case in this suite writes a real file instead.
    private async Task<int> ConvertAsync(MiniCase mini)
    {
        using var output = new MemoryStream();
        var conversion = _conversions[mini.Variant];
        await CxtWriter.WriteAsync(
            conversion.Plan, () => conversion.Emit(_diagnostics), WriterOptions.V2Compat, output).ConfigureAwait(false);
        _produced = output.ToArray();
        return _produced.Length;
    }

    private void Validate(MiniCase mini)
    {
        var what = $"{mini.Variant} v2-compat cxt";
        OutputValidation.RequireCleanEmit(_diagnostics, what);
        OutputValidation.RequireBytesMatch(_produced, _expected[mini.Variant], what);
    }
}
