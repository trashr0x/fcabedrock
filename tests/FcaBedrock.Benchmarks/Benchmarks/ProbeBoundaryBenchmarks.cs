using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Discovery;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// The probe limits, exercised at the exact value where their behaviour changes.
/// <para>
/// Every one of these guards is specified as a <b>strictly greater</b> comparison: reaching a limit
/// exactly is not a breach, and one more is. A case set comfortably inside or comfortably outside a
/// limit proves nothing about that rule — it would pass equally against a limit implemented as
/// <c>&gt;=</c>. So each boundary below is derived from the corpus by an independent oracle and then
/// straddled: at the limit, one below it, and one above.
/// </para>
/// <para>
/// The three outcomes stay separate results, never averaged: a complete draft, a truncated draft that
/// still carries its recovery policy, and an aggregate breach that yields <b>no</b> draft at all.
/// A breach is an expected-failure case, and it is labelled as one.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Probe, BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class ProbeRetentionBoundaryBenchmark : ProbeBenchmark
{
    /// <summary>
    /// The retention limit relative to the largest domain in the corpus: <c>-1</c> forces truncation,
    /// <c>0</c> sits exactly on the limit and must <em>not</em> truncate, <c>+1</c> is comfortably
    /// complete.
    /// </summary>
    [Params(-1, 0, 1)]
    public int Offset { get; set; }

    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override ProbeOptions Options =>
        ProbeOptions.Create(valueRetentionLimit: LargestDomain + Offset);

    private static int LargestDomain { get; } =
        ProbeAccountingOracle.W16LargestDomain(CorpusTiers.Records(CorpusTier.Small));

    // One below the largest domain truncates exactly the columns that reach it — which columns
    // those are is a fact about the generator, so it is derived from the generator rather than
    // assumed to be only the one the limit was chosen from.
    private static IReadOnlyList<string> TruncatingBelow { get; } =
        ProbeOracle.W16Truncating(CorpusTiers.Records(CorpusTier.Small), LargestDomain - 1);

    private protected override void Check(Diagnosed<SpecDocument> result)
    {
        var what = $"probe retention boundary (limit {LargestDomain + Offset}, largest domain {LargestDomain})";
        if (Offset < 0)
        {
            ProbeOracle.RequireTruncated(result, W16Corpus.ColumnCount, TruncatingBelow, what);
        }
        else
        {
            // Exactly at the limit is NOT truncation. That is the whole assertion at Offset 0, and
            // an implementation using >= rather than > would fail here and nowhere else.
            ProbeOracle.RequireComplete(result, W16Corpus.ColumnCount, what);
        }
    }
}

/// <summary>
/// The aggregate retained-value guard (D-110 guard 2) at its exact threshold.
/// <para>
/// A breach yields no draft at all, which is the point of the guard: a partial draft that read as
/// complete would silently lose part of the schema. So the two cases here are genuinely different
/// outcomes rather than a fast and a slow version of one, and the breaching case is labelled as an
/// expected failure — it has a duration, and it has no draft.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Probe, BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class ProbeAggregateValueBoundaryBenchmark : ProbeBenchmark
{
    /// <summary>The guard relative to the exact total the corpus retains: <c>0</c> passes, <c>-1</c> breaches.</summary>
    [Params(-1, 0)]
    public int Offset { get; set; }

    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override ProbeOptions Options =>
        ProbeOptions.Create(maxTotalRetainedValues: RetainedValues + Offset);

    private static long RetainedValues { get; } =
        ProbeAccountingOracle.W16(CorpusTiers.Records(CorpusTier.Small)).Values;

    private protected override void Check(Diagnosed<SpecDocument> result)
    {
        var what = $"probe aggregate value guard ({RetainedValues + Offset}, corpus retains {RetainedValues})";
        if (Offset < 0)
        {
            ProbeOracle.RequireGuardBreach(result, what);
        }
        else
        {
            ProbeOracle.RequireComplete(result, W16Corpus.ColumnCount, what);
        }
    }
}

/// <summary>
/// The aggregate retained-<b>text</b> guard (D-110 guard 3) at its exact threshold, over the
/// long-text corpus.
/// <para>
/// This guard exists for the pathology the value guard cannot see: a handful of values, each
/// enormous. Only a corpus whose values are long can reach it before the value guard, which is why
/// the long-text family exists at all — and why this boundary is straddled here rather than on W16.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Probe, BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LongTextFamily, CorpusTier.Small)]
public class ProbeTextBoundaryBenchmark : ProbeBenchmark
{
    /// <summary>The guard relative to the exact text the corpus retains: <c>0</c> passes, <c>-1</c> breaches.</summary>
    [Params(-1, 0)]
    public int Offset { get; set; }

    private protected override CorpusCase Corpus => CorpusCases.LongText(CorpusTier.Small);

    private protected override ProbeOptions Options =>
        ProbeOptions.Create(maxTotalRetainedValueText: TextUnits + Offset);

    private static long TextUnits { get; } =
        ProbeAccountingOracle.LongText(CorpusTiers.Records(CorpusTier.Small)).TextUnits;

    private protected override void Check(Diagnosed<SpecDocument> result)
    {
        var what = $"probe aggregate text guard ({TextUnits + Offset} units, corpus retains {TextUnits})";
        if (Offset < 0)
        {
            ProbeOracle.RequireGuardBreach(result, what);
        }
        else
        {
            ProbeOracle.RequireComplete(result, LongTextCorpus.ColumnCount, what);
        }
    }
}
