using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// The fixed scalar summary a source-drain benchmark accumulates while consuming records.
/// <para>
/// It exists for two reasons at once. It forces the drain to actually <em>read</em> every cleaned
/// field, so the measured work is the real one and cannot be optimized away; and it is small enough
/// to be re-derived independently from the corpus definition, so a completed iteration can be
/// checked for having read the right thing rather than merely for having finished.
/// </para>
/// </summary>
internal readonly record struct DrainSummary(long Records, long PresentFields, long ValueCharacters)
{
    /// <summary>Accumulates one cleaned field.</summary>
    public DrainSummary AddField(string? value) => value is null
        ? this
        : this with { PresentFields = PresentFields + 1, ValueCharacters = ValueCharacters + value.Length };

    /// <summary>Accumulates one record.</summary>
    public DrainSummary AddRecord() => this with { Records = Records + 1 };
}

/// <summary>
/// Independently derives the expected <see cref="DrainSummary"/> for a tier straight from a
/// generator's value definition, without opening the file or invoking any part of the source,
/// conversion, or export code under test.
/// <para>
/// One traversal serves every family, because a family differs only in how many columns it has and
/// what each cell cleans to — so that is all each one supplies. A family-specific copy of the loop
/// would be a second place for the summary's definition to drift.
/// </para>
/// </summary>
internal static class DrainOracle
{
    /// <summary>
    /// The summary a correct drain of <paramref name="records"/> rows of a
    /// <paramref name="columns"/>-wide family must produce, given its cleaned-value definition.
    /// </summary>
    public static DrainSummary Expected(long records, int columns, Func<long, int, string?> cleanedValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(records);
        ArgumentOutOfRangeException.ThrowIfNegative(columns);
        ArgumentNullException.ThrowIfNull(cleanedValue);

        var summary = default(DrainSummary);
        for (var row = 0L; row < records; row++)
        {
            summary = summary.AddRecord();
            for (var column = 0; column < columns; column++)
            {
                summary = summary.AddField(cleanedValue(row, column));
            }
        }

        return summary;
    }

    /// <summary>The summary a correct drain of <paramref name="records"/> W16 rows must produce.</summary>
    public static DrainSummary ExpectedW16(long records) =>
        Expected(records, W16Corpus.ColumnCount, W16Corpus.CleanedValue);

    /// <summary>The summary a correct drain of <paramref name="records"/> Ads-width rows must produce.</summary>
    public static DrainSummary ExpectedAds(long records) =>
        Expected(records, AdsCorpus.ColumnCount, AdsCorpus.CleanedValue);

    /// <summary>The summary a correct drain of <paramref name="records"/> long-text rows must produce.</summary>
    public static DrainSummary ExpectedLongText(long records) =>
        Expected(records, LongTextCorpus.ColumnCount, LongTextCorpus.CleanedValue);

    /// <summary>The summary a correct drain of <paramref name="records"/> keyed W16 rows must produce.</summary>
    public static DrainSummary ExpectedKeyed(long records) =>
        Expected(records, KeyedW16Corpus.ColumnCount, (row, column) => KeyedW16Corpus.CleanedValue(row, column, records));
}

/// <summary>
/// The expected <see cref="DrainSummary"/> for a triple tier, derived from the corpus definition.
/// <para>
/// A triple row is three role fields rather than a positional record, so it needs its own traversal:
/// the rows come out in the layout's physical order, and each contributes its subject, predicate,
/// and value. None of the three is ever missing in this family, which is a fact worth having in the
/// expectation rather than in a comment — a reader that dropped a role would change the field count.
/// </para>
/// </summary>
internal static class TripleDrainOracle
{
    /// <summary>The summary a correct drain of <paramref name="records"/> T10 rows must produce.</summary>
    public static DrainSummary Expected(long records, TripleLayout layout)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(records);

        var summary = default(DrainSummary);
        foreach (var (subject, row) in T10Corpus.PhysicalOrder(records, layout))
        {
            var (predicate, value) = T10Corpus.Row(subject, row);
            summary = summary
                .AddRecord()
                .AddField(T10Corpus.SubjectName(subject))
                .AddField(predicate)
                .AddField(value);
        }

        return summary;
    }
}
