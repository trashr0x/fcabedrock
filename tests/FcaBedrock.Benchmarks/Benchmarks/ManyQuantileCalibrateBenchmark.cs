using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Core.Calibration;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Sixteen exact count-sensitive attributes in one calibration pass.
/// <para>
/// Exact equal-frequency calibration retains one bounded accumulator per attribute, and the buffer
/// bound is <c>max(budget, attributeCount x FloorBytes)</c> (D-095/D-103). The floor arm only starts
/// to matter when there are enough attributes for it to exceed the budget, so a four-attribute spec
/// never reaches it — which is why this case exists beside the ordinary calibration one. Four bin
/// counts over each of the four numeric columns give sixteen genuinely independent accumulators over
/// the same corpus, with no change to the data at all.
/// </para>
/// <para>
/// It crosses the two hard populations on purpose: <c>n_seq</c> is maximally high-cardinality, so
/// its accumulator holds a distinct entry per row, while <c>n_ties</c> and <c>n_skew</c> are heavily
/// tied, so the boundary-feasibility path runs in the same pass.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Calibrate, BenchmarkCategories.Grouping)]
public abstract class ManyQuantileCalibrateBenchmark : CalibrateBenchmark
{
    private protected override string SpecText => W16PressureSpecs.ManyQuantiles;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireManyQuantiles(calibrated, records, $"many-quantile calibrate ({Corpus.Id})");
}

/// <summary>Sixteen count-sensitive attributes at 10,000 records.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class ManyQuantileCalibrateSmall : ManyQuantileCalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);
}

/// <summary>Sixteen count-sensitive attributes at 730,000 records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class ManyQuantileCalibrateWorking : ManyQuantileCalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);
}

/// <summary>
/// Sixteen count-sensitive attributes at 7.3M records: the case where sixteen simultaneous
/// accumulators over a high-cardinality population actually meet the memory budget. Opt-in.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class ManyQuantileCalibrateScale7M : ManyQuantileCalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);
}
