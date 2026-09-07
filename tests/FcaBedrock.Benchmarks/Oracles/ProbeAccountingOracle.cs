using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// Independently derives what a probe's boundedness guards will have counted over a corpus, so a
/// benchmark can sit <b>exactly</b> on a threshold rather than somewhere near it.
/// <para>
/// The accounting is logical and deterministic (D-110): distinct cleaned non-missing values retained
/// per attribute, counted once per <em>retaining attribute</em> with no cross-attribute
/// deduplication, and text summed as each retained string's UTF-16 code-unit length. Those two
/// sentences are the whole model, so it can be re-derived from a generator's value definition —
/// which is what makes a case at <c>limit</c>, <c>limit - 1</c>, and <c>limit + 1</c> a test of the
/// strictly-greater boundary rather than an approximation of it.
/// </para>
/// <para>
/// Nothing here calls the prober. If a boundary case fails, either this model or the accounting is
/// wrong — and that disagreement is exactly the finding such a case exists to produce.
/// </para>
/// </summary>
internal static class ProbeAccountingOracle
{
    /// <summary>
    /// The number of retained values a probe over <paramref name="records"/> rows of a
    /// <paramref name="columns"/>-wide family accounts for, given its cleaned-value definition.
    /// </summary>
    public static long RetainedValues(long records, int columns, Func<long, int, string?> cleanedValue) =>
        Accumulate(records, columns, cleanedValue).Values;

    /// <summary>The retained value text, in UTF-16 code units, over the same corpus.</summary>
    public static long RetainedTextUnits(long records, int columns, Func<long, int, string?> cleanedValue) =>
        Accumulate(records, columns, cleanedValue).TextUnits;

    /// <summary>Both totals in one traversal, for a case that pins both guards at once.</summary>
    public static (long Values, long TextUnits) Accumulate(
        long records, int columns, Func<long, int, string?> cleanedValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(records);
        ArgumentOutOfRangeException.ThrowIfNegative(columns);
        ArgumentNullException.ThrowIfNull(cleanedValue);

        var values = 0L;
        var textUnits = 0L;

        // Per attribute, deliberately: the guards count a value once for every attribute that
        // retains it, so a value shared by two columns is two retained values and its text is
        // counted twice. Deduplicating across columns here would model a different guard.
        for (var column = 0; column < columns; column++)
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            for (var row = 0L; row < records; row++)
            {
                if (cleanedValue(row, column) is { } value && distinct.Add(value))
                {
                    values++;
                    textUnits += value.Length;
                }
            }
        }

        return (values, textUnits);
    }

    /// <summary>The totals a probe over <paramref name="records"/> W16 rows accounts for.</summary>
    public static (long Values, long TextUnits) W16(long records) =>
        Accumulate(records, W16Corpus.ColumnCount, W16Corpus.CleanedValue);

    /// <summary>The totals a probe over <paramref name="records"/> long-text rows accounts for.</summary>
    public static (long Values, long TextUnits) LongText(long records) =>
        Accumulate(records, LongTextCorpus.ColumnCount, LongTextCorpus.CleanedValue);

    /// <summary>
    /// The largest number of distinct values any single W16 column holds over
    /// <paramref name="records"/> rows — the per-attribute retention boundary a case must straddle.
    /// </summary>
    public static int W16LargestDomain(long records)
    {
        var largest = 0;
        for (var column = 0; column < W16Corpus.ColumnCount; column++)
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            for (var row = 0L; row < records; row++)
            {
                if (W16Corpus.CleanedValue(row, column) is { } value)
                {
                    distinct.Add(value);
                }
            }

            largest = Math.Max(largest, distinct.Count);
        }

        return largest;
    }
}
