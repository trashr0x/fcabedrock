using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The <b>keyed-wide dedupe</b> pressure family: the W16 columns with a leading object-key column
/// whose values repeat, converted under <c>duplicate_object_policy = "dedupe"</c>.
/// <para>
/// It is a <em>separately named</em> case rather than a variant spec over the plain W16 file,
/// exactly as the corpus contract requires: a keyed corpus is different data, and quietly reading
/// W16 under a keyed spec would make two measurements share a name while measuring different work.
/// </para>
/// <para>
/// <b>Why the keys interleave.</b> Wide <c>dedupe</c> shares the grouping/sort-merge/spool backend
/// with triple <c>unordered</c>, because non-contiguous keys cannot be merged in one naive pass
/// without holding every cross (§6.1, P-16). Contiguous key runs would let that backend look easy;
/// here every key recurs only after all the others have appeared, so each of the four rows behind an
/// object is separated by the full object count. That is the honest worst case for the path this
/// family exists to measure.
/// </para>
/// <para>
/// Object names are the cleaned key values, and their first occurrences run in ascending row order,
/// so the emitted object order is simply <c>k0000000000</c>, <c>k0000000001</c>, … — hand-checkable
/// despite the interleaving.
/// </para>
/// </summary>
internal static class KeyedW16Corpus
{
    /// <summary>
    /// The generator revision. <b>Bump this whenever any value definition below changes.</b>
    /// </summary>
    public const int GeneratorRevision = 1;

    /// <summary>Rows per object: every key appears exactly this many times.</summary>
    public const int RowsPerObject = 4;

    /// <summary>The physical column count — the object key plus the sixteen W16 columns.</summary>
    public const int ColumnCount = 1 + W16Corpus.ColumnCount;

    /// <summary>Physical index of the object-key column.</summary>
    public const int ColKey = 0;

    /// <summary>The number of formal objects a tier of <paramref name="records"/> rows collapses to.</summary>
    public static long Objects(long records) => records / RowsPerObject;

    /// <summary>The object-key value on <paramref name="row"/>, given the tier's row count.</summary>
    public static string Key(long row, long records) =>
        "k" + (row % Objects(records)).ToString("D10", CultureInfo.InvariantCulture);

    /// <summary>
    /// The rows that collapse onto object <paramref name="objectIndex"/>, ascending. The first of
    /// them is the object's first occurrence, which fixes its position (§17 rule 4).
    /// </summary>
    public static IEnumerable<long> RowsOf(long objectIndex, long records)
    {
        var objects = Objects(records);
        for (var repeat = 0; repeat < RowsPerObject; repeat++)
        {
            yield return objectIndex + (repeat * objects);
        }
    }

    /// <summary>
    /// The <b>cleaned</b> value of one cell: the key column, then the sixteen W16 columns shifted
    /// one place right. Reusing <see cref="W16Corpus.CleanedValue"/> is deliberate — the two
    /// families then share one value definition, so a dedupe expectation and a plain W16
    /// expectation cannot drift apart over the same row.
    /// </summary>
    public static string? CleanedValue(long row, int column, long records) => column == ColKey
        ? Key(row, records)
        : W16Corpus.CleanedValue(row, column - 1);

    /// <summary>The header line as written, without its terminator.</summary>
    public static string HeaderLine { get; } = "okey," + W16Corpus.HeaderLine;

    /// <summary>
    /// Streams the header plus <paramref name="records"/> data rows as UTF-8 without BOM, LF line
    /// endings. The row count must be a whole number of <see cref="RowsPerObject"/>-row objects.
    /// </summary>
    public static void Write(Stream destination, long records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(records);

        if (records % RowsPerObject != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(records), records, $"a keyed W16 tier must be a whole number of {RowsPerObject}-row objects.");
        }

        using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1 << 16,
            leaveOpen: true);

        writer.Write(HeaderLine);
        writer.Write('\n');

        var line = new StringBuilder(288);
        for (var row = 0L; row < records; row++)
        {
            if ((row & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            line.Clear();
            line.Append(Key(row, records)).Append(',');
            W16Corpus.AppendRow(line, row);
            line.Append('\n');
            writer.Write(line);
        }

        writer.Flush();
    }
}
