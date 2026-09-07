using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The <b>Ads-width</b> synthetic family: 1,559 physical columns, matching the geometry of the
/// internet-advertisements workload the v2 lineage used as its wide extreme — three numeric
/// columns, one local flag, 1,554 sparse term flags, and a class column.
/// <para>
/// It exists to pressure <em>width</em> rather than length. A schema of 1,559 columns is where
/// per-column costs stop being rounding error: the planner's formal-attribute list, the probe's
/// per-attribute retention accounting, and the wide reader's per-record field array all scale with
/// it, and none of those is visible in a sixteen-column corpus however many rows it has. The plan
/// therefore fixes this family at 1,000 and 10,000 rows and deliberately keeps it out of the
/// target-scale matrix.
/// </para>
/// <para>
/// <b>The content is synthetic.</b> Nothing here is derived from the original advertisements data;
/// only the shape is. The term flags are sparse (about 1.2% set), which is what makes a wide row's
/// incidence small even though its schema is enormous — the property that makes the geometry
/// interesting in the first place.
/// </para>
/// </summary>
internal static class AdsCorpus
{
    /// <summary>
    /// The generator revision. <b>Bump this whenever any value definition below changes</b>: it is
    /// recorded in the catalog, so a corpus prepared by an older revision is refused.
    /// </summary>
    public const int GeneratorRevision = 1;

    /// <summary>The number of sparse term-flag columns.</summary>
    public const int TermColumns = 1_554;

    /// <summary>The physical column count: three numeric, one local flag, the terms, and the class.</summary>
    public const int ColumnCount = 3 + 1 + TermColumns + 1;

    /// <summary>Physical index of <c>height</c>.</summary>
    public const int ColHeight = 0;

    /// <summary>Physical index of <c>width</c>.</summary>
    public const int ColWidth = 1;

    /// <summary>Physical index of <c>aratio</c>, a two-decimal ratio.</summary>
    public const int ColAspect = 2;

    /// <summary>Physical index of the <c>local</c> flag.</summary>
    public const int ColLocal = 3;

    /// <summary>Physical index of the first term flag; they are contiguous from here.</summary>
    public const int ColTermFirst = 4;

    /// <summary>Physical index of the <c>class</c> column.</summary>
    public const int ColClass = ColTermFirst + TermColumns;

    /// <summary>One term flag in a thousand-part draw below this threshold is set.</summary>
    private const ulong TermDensityPerMille = 12;

    /// <summary>The positive class label.</summary>
    public const string AdLabel = "ad.";

    /// <summary>The negative class label.</summary>
    public const string NonAdLabel = "nonad.";

    /// <summary>The header line's column names, in physical order.</summary>
    public static IReadOnlyList<string> Columns { get; } = BuildColumnNames();

    /// <summary>The header line as written, without its terminator.</summary>
    public static string HeaderLine { get; } = string.Join(',', Columns);

    /// <summary>The name of term flag <paramref name="term"/> (0-based).</summary>
    public static string TermName(int term) => "t" + term.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary><c>height</c>: 1..640, so its cut bins are populated at every row count.</summary>
    public static int Height(long row) => (int)(Determinism.Draw(row, 60) % 640) + 1;

    /// <summary><c>width</c>: 1..640.</summary>
    public static int Width(long row) => (int)(Determinism.Draw(row, 61) % 640) + 1;

    /// <summary><c>aratio</c> in hundredths: 0.10 to 20.00.</summary>
    public static long AspectHundredths(long row) => (long)(Determinism.Draw(row, 62) % 1_991UL) + 10L;

    /// <summary><c>local</c>: 1 on roughly two rows in three.</summary>
    public static bool IsLocal(long row) => Determinism.Draw(row, 63) % 3 != 0;

    /// <summary>True when term flag <paramref name="term"/> is set on <paramref name="row"/>.</summary>
    public static bool TermIsSet(long row, int term) =>
        Determinism.Draw(row, 1_000 + term) % 1_000 < TermDensityPerMille;

    /// <summary>True when the row is in the positive class — about one row in seven.</summary>
    public static bool IsAd(long row) => Determinism.Draw(row, 64) % 7 == 0;

    /// <summary>The class label for a row.</summary>
    public static string ClassLabel(long row) => IsAd(row) ? AdLabel : NonAdLabel;

    /// <summary>
    /// The <b>cleaned</b> value of one cell — what a source yields after the §5.1 trim. This family
    /// carries no missing cells: its interesting property is width, and mixing a second pressure
    /// into it would make a measurement harder to attribute, not more realistic.
    /// </summary>
    public static string CleanedValue(long row, int column) => column switch
    {
        ColHeight => Height(row).ToString(CultureInfo.InvariantCulture),
        ColWidth => Width(row).ToString(CultureInfo.InvariantCulture),
        ColAspect => Determinism.FormatHundredths(AspectHundredths(row)),
        ColLocal => IsLocal(row) ? "1" : "0",
        >= ColTermFirst and < ColClass => TermIsSet(row, column - ColTermFirst) ? "1" : "0",
        ColClass => ClassLabel(row),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Not an Ads-width column."),
    };

    /// <summary>
    /// Streams the header plus <paramref name="records"/> data rows as UTF-8 without BOM, LF line
    /// endings. No field can contain the delimiter, a quote, or a line break by construction, so no
    /// escaping is reachable here; the W16 family owns the quoting case.
    /// </summary>
    public static void Write(Stream destination, long records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(records);

        using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1 << 16,
            leaveOpen: true);

        writer.Write(HeaderLine);
        writer.Write('\n');

        var line = new StringBuilder(4 * ColumnCount);
        for (var row = 0L; row < records; row++)
        {
            if ((row & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            line.Clear();
            for (var column = 0; column < ColumnCount; column++)
            {
                if (column > 0)
                {
                    line.Append(',');
                }

                line.Append(CleanedValue(row, column));
            }

            line.Append('\n');
            writer.Write(line);
        }

        writer.Flush();
    }

    private static IReadOnlyList<string> BuildColumnNames()
    {
        var names = new List<string>(ColumnCount) { "height", "width", "aratio", "local" };
        for (var term = 0; term < TermColumns; term++)
        {
            names.Add(TermName(term));
        }

        names.Add("class");
        return names;
    }
}
