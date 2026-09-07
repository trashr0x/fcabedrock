using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Planning;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// The pure planner: resolved, calibrated state in; a <see cref="ConversionPlan"/> out; <b>no data
/// I/O at all</b>.
/// <para>
/// It is the one phase that touches no file, so it is the one phase whose cost is a pure function of
/// the <em>spec</em> rather than of the corpus. That makes it the right place to see what a wide
/// schema costs before a single record is read: the narrow case plans 33 formal attributes and the
/// Ads-width case plans 1,568, over the same code, so the difference between them is width and
/// nothing else.
/// </para>
/// <para>
/// This is useful phase context, deliberately <b>not</b> a substitute for a scale throughput number.
/// Planning happens once per conversion; a conversion that reads 73 million records pays this cost
/// exactly once, and reporting it beside a per-record rate would invite exactly the wrong comparison.
/// </para>
/// <para>
/// <b>The measured interval</b> is the planner call and the consumption of its result. Reading the
/// spec, opening a session, acquiring the schema, resolving, and calibrating all happen once in
/// setup — a plan cannot exist without them, and including them would make this a conversion
/// benchmark with an unusual name.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Plan, BenchmarkCategories.Small)]
public abstract class PlanBenchmark
{
    private CalibratedSpec _calibrated = null!;
    private int _formalAttributes;

    /// <summary>The corpus whose spec and schema this class plans.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>The formal-attribute count a correct plan of that spec produces.</summary>
    private protected abstract int ExpectedFormalAttributes { get; }

    /// <summary>Resolves and calibrates once, so the measured call has state to plan. Outside timing.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        var prepared = CorpusPreparer.Require(Corpus);
        _calibrated = await ConversionPipeline.CalibrateAsync(prepared.SpecPath, prepared.DataPath)
            .ConfigureAwait(false);
    }

    /// <summary>Plans the calibrated spec and consumes the result.</summary>
    [Benchmark(Description = "plan")]
    public int Plan()
    {
        var planned = ConversionPlanner.Plan(_calibrated);
        if (!planned.TryGetValue(out var plan))
        {
            throw new InvalidOperationException(
                $"plan ({Corpus.Id}) produced no plan: {ConversionPipeline.Describe(planned.Diagnostics)}");
        }

        _formalAttributes = plan.FormalAttributes.Count;
        return _formalAttributes;
    }

    /// <summary>Checks the completed plan's shape. Outside timing.</summary>
    [IterationCleanup]
    public void Validate()
    {
        if (_formalAttributes != ExpectedFormalAttributes)
        {
            throw new InvalidOperationException(
                $"plan ({Corpus.Id}) produced {_formalAttributes} formal attributes, "
                + $"expected {ExpectedFormalAttributes}.");
        }
    }
}

/// <summary>The narrow plan: six spec attributes, 33 formal attributes.</summary>
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class PlanNarrow : PlanBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override int ExpectedFormalAttributes => W16Specs.DeclaredFormalAttributeCount;
}

/// <summary>The wide plan: 1,559 spec attributes, 1,568 formal attributes.</summary>
[BenchmarkCorpus(CorpusCases.AdsFamily, CorpusTier.Micro)]
public class PlanAdsWidth : PlanBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Ads(CorpusTier.Micro);

    private protected override int ExpectedFormalAttributes => AdsSpecs.FormalAttributeCount;
}
