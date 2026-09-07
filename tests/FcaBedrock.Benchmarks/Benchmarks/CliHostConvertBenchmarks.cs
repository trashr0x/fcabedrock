using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// One complete <c>fcabedrock convert</c> at the argv boundary: parse, resolve, calibrate, plan,
/// emit, export, hash the input and the staged output inline, publish through the real staged
/// transaction, and write the run manifest.
/// <para>
/// The library benchmarks measure phases; this measures the <b>product</b>. It is the only case in
/// the suite where the publication transaction and the manifest are inside the clock, and the
/// difference between it and the corresponding emit-and-export case is what M7's input-stability and
/// publication guarantees actually cost a user.
/// </para>
/// <para>
/// Labelled <b>CLI-host throughput</b>, never installed-command latency: process start, host
/// resolution, the tool shim, runtime startup, and console attachment are outside it. See
/// <see cref="CliHostRun"/>.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Cli, BenchmarkCategories.Convert)]
public abstract class CliHostConvertBenchmark
{
    private CliHostRun _run = null!;
    private ContextExpectation _expected = null!;

    /// <summary>The corpus this class converts.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>The output format this class requests.</summary>
    private protected virtual string Format => "dat";

    /// <summary>Whether this class suppresses the manifest sidecar.</summary>
    private protected virtual bool NoManifest => false;

    /// <summary>The expected artifact, derived independently of the pipeline.</summary>
    private protected abstract ContextExpectation Expected(PreparedCorpus prepared);

    /// <summary>Composes the argv and derives the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _run = new CliHostRun($"cli convert ({Corpus.Id}{(NoManifest ? ", no manifest" : string.Empty)})", Corpus)
        {
            Format = Format,
            NoManifest = NoManifest,
        };

        _run.Setup();
        _expected = Expected(_run.Prepared);
    }

    /// <summary>Removes the previous iteration's published artifacts and takes fresh sinks.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Runs one complete command.</summary>
    [Benchmark(Description = "cli-host convert")]
    public Task<int> Convert() => _run.RunAsync();

    /// <summary>Validates exit status, diagnostics, published bytes, and manifest state.</summary>
    [IterationCleanup]
    public void Validate() => _run.Validate(_expected);

    /// <summary>Removes every artifact this case published.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>The declared W16 command at 10,000 records: the default, byte-validated case.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class CliHostConvertWideSmall : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        W16DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The declared W16 command at 730,000 records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class CliHostConvertWideWorking : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        W16DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The declared W16 command at 7.3M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class CliHostConvertWideScale7M : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        W16DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The declared W16 command at 73M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale73M)]
public class CliHostConvertWideScale73M : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale73M);

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        W16DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The unordered triple command at 10,000 rows: the grouping backend under the real host.</summary>
[BenchmarkCategory(BenchmarkCategories.Small, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Small)]
public class CliHostConvertTripleSmall : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Interleaved);

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        T10DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The contiguous triple command at 730,000 rows: the single-pass path. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Working)]
public class CliHostConvertTripleGroupedWorking : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Grouped);

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        T10DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The unordered triple command at 7.3M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale7M)]
public class CliHostConvertTripleScale7M : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Interleaved);

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        T10DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The unordered triple command at 73M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale73M)]
public class CliHostConvertTripleScale73M : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale73M, TripleLayout.Interleaved);

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        T10DeclaredOracle.Expect(prepared.Records);
}

/// <summary>
/// The same command with the manifest sidecar suppressed.
/// <para>
/// Paired with the manifest-bearing case above, and the pair is what makes the number mean anything.
/// <c>--no-manifest</c> suppresses <b>only</b> the sidecar: the complete input pass is still hashed
/// inline, the staged output is still hashed, and the publication transaction still runs. So the
/// difference between the two cases is the sidecar's own cost — composing it, hashing it, and
/// committing it last — and it is emphatically <em>not</em> "the cost of hashing".
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class CliHostConvertNoManifestWorking : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override bool NoManifest => true;

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        W16DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The manifest-suppressed command at 7.3M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class CliHostConvertNoManifestScale7M : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);

    private protected override bool NoManifest => true;

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        W16DeclaredOracle.Expect(prepared.Records);
}

/// <summary>
/// The <c>.cxt</c> command at the working tier: the two-pass export inside a full publication.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class CliHostConvertCxtWorking : CliHostConvertBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override string Format => "cxt";

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        CxtExpectation.Stream(
            prepared.Records,
            W16Specs.DeclaredFormalAttributeNames,
            index => index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            W16DeclaredOracle.Crosses);
}

/// <summary>
/// The <c>--format both</c> command at the working tier: one transaction publishing two artifacts.
/// <para>
/// The publication transaction's job is that both artifacts and the manifest appear together or not
/// at all, so a case that publishes two of them is where that costs something — a second export over
/// a replayed emission, a second staged file, a second inline hash, and a commit that has to order
/// three files rather than two. Both artifacts are validated against their own independent
/// expectations, because a transaction that committed one correct file and one wrong one would
/// otherwise pass.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Cli, BenchmarkCategories.Convert, BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class CliHostConvertBothWorking
{
    private CliHostRun _run = null!;
    private ContextExpectation _dat = null!;
    private ContextExpectation _cxt = null!;

    /// <summary>Composes the argv and derives both expectations. Outside every measured interval.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var corpus = CorpusCases.W16(CorpusTier.Working);
        _run = new CliHostRun("cli convert both (w16-working)", corpus) { Format = "both" };
        _run.Setup();

        _dat = W16DeclaredOracle.Expect(_run.Prepared.Records);
        _cxt = CxtExpectation.Stream(
            _run.Prepared.Records,
            W16Specs.DeclaredFormalAttributeNames,
            index => index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            W16DeclaredOracle.Crosses);
    }

    /// <summary>Removes the previous iteration's published artifacts and takes fresh sinks.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Runs one complete two-artifact command.</summary>
    [Benchmark(Description = "cli-host convert (both formats)")]
    public Task<int> Convert() => _run.RunAsync();

    /// <summary>Validates exit status, both artifacts, and the manifest.</summary>
    [IterationCleanup]
    public void Validate() => _run.ValidateBoth(_dat, _cxt);

    /// <summary>Removes every artifact this case published.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>
/// An <b>auto-calibrated</b> convert of interleaved triple input: the command that makes
/// <b>two</b> complete input passes.
/// <para>
/// Every other CLI case here converts under a fully declared spec, so the command opens the data
/// once. This one's cuts come from the population, so it calibrates and then emits — two complete
/// passes over the same file, each hashed inline and each required to agree with the first (D-122
/// part 5 / §17). That replay is the input-stability guarantee at its real cost, and it is the only
/// shape in the suite that exercises it.
/// </para>
/// <para>
/// Its output is <b>not derivable</b> ahead of the run: equal-frequency cuts over a deliberately
/// tie-heavy population are the D-103 feasibility algorithm's business, and an oracle that
/// re-implemented it would be the code under test. So this case asserts what it honestly can — a
/// successful exit, no diagnostics, an object count equal to the corpus's subject count, the
/// manifest present, and byte-identical output across every iteration of the run.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Cli, BenchmarkCategories.Convert, BenchmarkCategories.Triple)]
public abstract class CliHostConvertAutoBenchmark
{
    private CliHostRun _run = null!;
    private long _expectedObjects;

    /// <summary>The corpus this class converts.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Composes the argv and writes the auto spec. Outside every measured interval.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _run = new CliHostRun($"cli convert auto ({Corpus.Id})", Corpus)
        {
            Format = "dat",
            SpecOverride = T10Specs.Auto,

            // The auto spec leaves Tissue's domain undeclared, so the command says so on every run:
            // the column set came from this input, which is what the mode was asked to do. Named
            // here rather than filtered globally, so any OTHER diagnostic still fails the case.
            ExpectedDiagnosticCodes = ["ObservedDomainUsed"],
        };

        _run.Setup();
        _expectedObjects = T10Corpus.Subjects(_run.Prepared.Records);
    }

    /// <summary>Removes the previous iteration's published artifacts and takes fresh sinks.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Runs one complete two-pass command.</summary>
    [Benchmark(Description = "cli-host convert (auto-calibrated, two input passes)")]
    public Task<int> Convert() => _run.RunAsync();

    /// <summary>Validates exit status, object count, manifest state, and intra-run determinism.</summary>
    [IterationCleanup]
    public void Validate() => _run.ValidateBaseline(_expectedObjects);

    /// <summary>Removes every artifact this case published.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>The auto-calibrated triple command at 7.3M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale7M)]
public class CliHostConvertAutoScale7M : CliHostConvertAutoBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Interleaved);
}

/// <summary>The auto-calibrated triple command at 73M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale73M)]
public class CliHostConvertAutoScale73M : CliHostConvertAutoBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale73M, TripleLayout.Interleaved);
}

/// <summary>The auto-calibrated triple command at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Working)]
public class CliHostConvertAutoWorking : CliHostConvertAutoBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Interleaved);
}
