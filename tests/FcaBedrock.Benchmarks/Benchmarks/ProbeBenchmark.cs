using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Discovery;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Discovery: one set-based observation pass over an unbound source, producing a draft spec.
/// <para>
/// <b>The measured interval</b> starts with the corpus prepared and nothing open. It covers opening
/// the session, reading the schema, observing every cleaned record once, and building the draft
/// document. Serializing the draft to canonical TOML is deliberately outside it — that is the
/// caller's job (D-109), not probe's — as is checking the draft afterwards.
/// </para>
/// <para>
/// The three <b>outcomes</b> below are measured as separate cases because they are separate
/// results, not degrees of the same one. A successful probe retains every distinct value and
/// authors complete domains. A <b>truncated</b> probe hits the per-attribute retention limit and
/// authors a prefix plus <c>unknown_value_policy = "include"</c>, so converting its draft still
/// recovers the full schema — it is a usable draft that says so. A <b>guard breach</b> exceeds an
/// aggregate limit and yields <em>no draft at all</em>, because a partial draft that read as
/// complete would be worse than none. Timing them together would average three different things.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Probe)]
public abstract class ProbeBenchmark
{
    private PreparedCorpus _corpus = null!;
    private SourceReadSettings _settings = null!;
    private ProbeOptions _options = null!;
    private Diagnosed<SpecDocument> _result;

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>The probe options this case runs under.</summary>
    private protected abstract ProbeOptions Options { get; }

    /// <summary>Checks the outcome this case is supposed to produce.</summary>
    private protected abstract void Check(Diagnosed<SpecDocument> result);

    /// <summary>Resolves the read settings and the options. Outside every measured interval.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _corpus = CorpusPreparer.Require(Corpus);
        _settings = ConversionPipeline.RequireReadSettings(
            ConversionPipeline.RequireDocument(File.ReadAllText(_corpus.SpecPath)));
        _options = Options;
    }

    /// <summary>Opens a fresh session and runs the whole observation pass.</summary>
    [Benchmark(Description = "probe")]
    public async Task<int> Probe()
    {
        var session = ConversionPipeline.CreateSession(_settings, _corpus.DataPath);
        _result = session switch
        {
            ITripleSourceSession triple => await Prober
                .ProbeTripleAsync(triple, _settings, columns: null, _options).ConfigureAwait(false),
            IWideSourceSession wide => await Prober.ProbeAsync(wide, _settings, _options).ConfigureAwait(false),
            _ => throw new InvalidOperationException("unrecognized session shape."),
        };

        return _result.Diagnostics.Count;
    }

    /// <summary>Checks the completed outcome after the pass. Outside timing.</summary>
    [IterationCleanup]
    public void Validate() => Check(_result);
}

/// <summary>
/// A successful wide probe at 10,000 records: every column's domain fits well inside the retention
/// limit, so the draft is complete and nothing is truncated.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class ProbeWideSmall : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override ProbeOptions Options => ProbeOptions.Default;

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireComplete(result, W16Corpus.ColumnCount, "probe (w16-small)");
}

/// <summary>
/// The same source under a deliberately small retention limit: <c>n_seq</c> has one distinct value
/// per record, so it must truncate, and the draft must say so rather than silently shrink.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class ProbeWideTruncatedSmall : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override ProbeOptions Options => ProbeOptions.Create(valueRetentionLimit: 64);

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireTruncated(result, W16Corpus.ColumnCount, "probe (w16-small, truncated)");
}

/// <summary>
/// The same source against an aggregate guard it cannot satisfy: the outcome is <b>no draft</b>,
/// and the cost of reaching that conclusion is worth knowing because it is the cost a user pays
/// before being told to re-probe.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class ProbeWideGuardBreachSmall : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override ProbeOptions Options => ProbeOptions.Create(maxTotalRetainedValues: 128);

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireGuardBreach(result, "probe (w16-small, guard breach)");
}

/// <summary>A successful triple probe: predicates are discovered in first-appearance order.</summary>
[BenchmarkCategory(BenchmarkCategories.Small, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Small)]
public class ProbeTripleSmall : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Interleaved);

    private protected override ProbeOptions Options => ProbeOptions.Default;

    // Four predicates appear in the corpus, including the one no conversion spec binds: probe
    // discovers what the DATA has, which is not the same question as what a spec asked for.
    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireComplete(result, expectedAttributes: 4, "probe (t10-unordered-small)");
}

/// <summary>
/// A wide probe at 730,000 records. <c>n_seq</c> has 730,000 distinct values, so the default
/// per-attribute limit truncates it: that is the honest working-tier outcome, and setting a limit
/// large enough to avoid it would measure a configuration no user would choose. Opt-in.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class ProbeWideWorking : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override ProbeOptions Options => ProbeOptions.Default;

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireTruncated(result, W16Corpus.ColumnCount, "probe (w16-working)");
}

/// <summary>
/// A triple probe at 730,000 rows: the high predicate-cardinality case, and it truncates.
/// <para>
/// The arithmetic is worth stating, because it is not the obvious one. The tier has 73,000 subjects,
/// so <c>Stage</c> takes at most 73,000 distinct <em>numeric</em> values — comfortably inside the
/// 100,000 default. But probe retains distinct <b>raw</b> values, and every subject writes its stage
/// twice, once as <c>N</c> and once as <c>N.0</c>. That is up to 146,000 distinct retained strings
/// for one predicate, and the retention limit is reached.
/// </para>
/// <para>
/// It is exactly the distinction §5.3.1 turns on — one numeric value, two raw observations — arriving
/// here as a probe outcome rather than a calibration one, and it is the honest working-tier result.
/// Opt-in.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Working)]
public class ProbeTripleWorking : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Interleaved);

    private protected override ProbeOptions Options => ProbeOptions.Default;

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireTruncated(result, expectedAttributes: 4, "probe (t10-unordered-working)");
}

/// <summary>A successful wide probe at 7.3M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class ProbeWideScale7M : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);

    // n_seq has 7.3M distinct values, so the default per-attribute limit truncates it; that is the
    // realistic scale outcome, and pretending otherwise would need a limit no user would set.
    private protected override ProbeOptions Options => ProbeOptions.Default;

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireTruncated(result, W16Corpus.ColumnCount, "probe (w16-scale7m)");
}

/// <summary>
/// A wide probe at 73M records. <c>n_seq</c> and <c>n_wide</c> both far exceed the default
/// per-attribute retention limit, so the draft truncates — and that is the honest target-scale
/// outcome, not a configuration to tune around.
/// <para>
/// Truncation is what makes this case measurable at all. Retention is bounded at 100,000 values per
/// attribute, so the probe's memory does not grow with the tier: seventy-three million records cost
/// a full ordered pass and a bounded set, which is the property D-110 exists to guarantee and this
/// is where it is exercised at scale. Opt-in.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale73M)]
public class ProbeWideScale73M : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale73M);

    private protected override ProbeOptions Options => ProbeOptions.Default;

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireTruncated(result, W16Corpus.ColumnCount, "probe (w16-scale73m)");
}

/// <summary>
/// A triple probe at 73M rows: 7.3 million subjects, so <c>Stage</c> reaches roughly fourteen
/// million distinct raw values across its two spellings and truncates, while <c>Tissue</c>,
/// <c>Signal</c>, and the unmatched predicate stay tiny.
/// <para>
/// The mixed shape is the interesting one — one enormous predicate beside three bounded ones — and it
/// is what a real triple store looks like. Opt-in.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Scale, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale73M)]
public class ProbeTripleScale73M : ProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale73M, TripleLayout.Interleaved);

    private protected override ProbeOptions Options => ProbeOptions.Default;

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireTruncated(result, expectedAttributes: 4, "probe (t10-unordered-scale73m)");
}
