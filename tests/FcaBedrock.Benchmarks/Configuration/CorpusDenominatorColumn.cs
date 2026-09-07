using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Configuration;

/// <summary>
/// What a timing has to be divided by before it means anything: the exact input-record count and
/// input byte length of the corpus the case read.
/// <para>
/// BenchmarkDotNet reports time and allocations; it is not domain-aware, so records/second and
/// MiB/second are ours to supply. Publishing the <em>denominators</em> rather than a precomputed
/// rate is deliberate — the raw measurement stays BenchmarkDotNet's, and any derived figure in the
/// report can be rechecked against the corpus catalog that produced it.
/// </para>
/// </summary>
internal sealed class CorpusDenominatorColumn(CorpusDenominatorColumn.Denominator denominator) : IColumn
{
    /// <summary>Which denominator a column instance reports.</summary>
    internal enum Denominator
    {
        /// <summary>The exact number of input records the case's corpus carries.</summary>
        Records,

        /// <summary>The exact input byte length, when the corpus is prepared.</summary>
        InputBytes,
    }

    /// <summary>The input-record count column.</summary>
    public static IColumn Records { get; } = new CorpusDenominatorColumn(Denominator.Records);

    /// <summary>The input-byte column.</summary>
    public static IColumn InputBytes { get; } = new CorpusDenominatorColumn(Denominator.InputBytes);

    /// <inheritdoc/>
    public string Id => "Corpus" + denominator;

    /// <inheritdoc/>
    public string ColumnName => denominator == Denominator.Records ? "Records" : "InputMiB";

    /// <inheritdoc/>
    public bool AlwaysShow => true;

    /// <inheritdoc/>
    public ColumnCategory Category => ColumnCategory.Custom;

    /// <inheritdoc/>
    public int PriorityInCategory => denominator == Denominator.Records ? 0 : 1;

    /// <inheritdoc/>
    public bool IsNumeric => true;

    /// <inheritdoc/>
    public UnitType UnitType => UnitType.Dimensionless;

    /// <inheritdoc/>
    public string Legend => denominator == Denominator.Records
        ? "Input records in the corpus this case read (the throughput denominator)"
        : "Input size in MiB, from the prepared corpus catalog";

    /// <inheritdoc/>
    public bool IsAvailable(Summary summary) => true;

    /// <inheritdoc/>
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    /// <inheritdoc/>
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        ArgumentNullException.ThrowIfNull(benchmarkCase);

        if (benchmarkCase.Descriptor.Type.GetCustomAttribute<BenchmarkCorpusAttribute>()?.Resolve() is not { } corpus)
        {
            return "n/a";
        }

        // A column must never throw and must never claim a fact it does not have: an unprepared
        // corpus has no recorded byte length yet, and an externally acquired one has no record
        // count either, because its size is measured at preparation rather than chosen.
        var prepared = CorpusPreparer.TryLoad(CorpusPreparer.Describe(corpus));

        if (denominator == Denominator.Records)
        {
            var records = prepared?.Records ?? (corpus.RecordsDeclared ? corpus.Records : 0L);
            return records > 0 ? records.ToString("N0", CultureInfo.InvariantCulture) : "?";
        }

        return prepared is null
            ? "?"
            : (prepared.InputBytes / (double)(1024 * 1024)).ToString("N1", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
        GetValue(summary, benchmarkCase);

    /// <inheritdoc/>
    public override string ToString() => ColumnName;
}
