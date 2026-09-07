using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The <b>W16</b> synthetic wide family: sixteen physical columns — four numeric, eight
/// categorical, four binary — over the four <see cref="CorpusTier"/> record counts.
/// <para>
/// The geometry is deliberately frozen here, in one place, because a corpus definition is a
/// measurement boundary: changing a distribution invalidates every comparison stated against it
/// (the base plan's corpus contract and the S5 invalidation rule). Every value is a pure function
/// of <c>(row, column)</c> through <see cref="Determinism"/>, so a file regenerates byte-for-byte
/// anywhere and an oracle can re-derive any single row without reading the file.
/// </para>
/// <para>
/// <b>What each column exercises.</b> <c>n_seq</c> is strictly increasing, so its distinct count
/// equals the record count — the high-cardinality numeric case. <c>n_ties</c> is a fifty-value
/// cycle, so every value sits in a large tied group, and it carries the explicit
/// <c>missing_token</c>. <c>n_skew</c> is 90% one value and 10% a long tail. <c>n_wide</c> spans
/// roughly ±1,000,000 at two decimal places, including negatives. The eight <c>c*</c> columns each
/// draw from an eight-value domain; <c>c3</c> carries deterministic <b>empty</b> cells (the other
/// missing form), and <c>c6</c>'s domain contains one value bearing the delimiter and one bearing
/// a double quote, so every generated file exercises the RFC 4180 quoting path without inflating
/// that column's cardinality. The four <c>b*</c> columns are yes/no.
/// </para>
/// </summary>
internal static class W16Corpus
{
    /// <summary>
    /// The generator revision. <b>Bump this whenever any value definition below changes</b>: it is
    /// recorded in the catalog, so a corpus prepared by an older revision is refused rather than
    /// silently compared against.
    /// </summary>
    public const int GeneratorRevision = 1;

    /// <summary>The physical column count.</summary>
    public const int ColumnCount = 16;

    /// <summary>The binding's missing token, matching the spec texts in <see cref="W16Specs"/>.</summary>
    public const string MissingToken = "?";

    /// <summary>Physical index of the strictly increasing numeric column.</summary>
    public const int ColSeq = 0;

    /// <summary>Physical index of the heavily tied numeric column.</summary>
    public const int ColTies = 1;

    /// <summary>Physical index of the skewed numeric column.</summary>
    public const int ColSkew = 2;

    /// <summary>Physical index of the wide-range signed decimal column.</summary>
    public const int ColWide = 3;

    /// <summary>Physical index of <c>c0</c>; the eight categorical columns are contiguous from here.</summary>
    public const int ColCategoricalFirst = 4;

    /// <summary>Physical index of <c>b0</c>; the four binary columns are contiguous from here.</summary>
    public const int ColBinaryFirst = 12;

    // The deterministic missing placements. Two different missing FORMS on purpose: an explicit
    // token on a numeric column, and an empty cell on a categorical one.
    private const long TiesMissingModulus = 53;
    private const long TiesMissingResidue = 2;
    private const long C3MissingModulus = 37;
    private const long C3MissingResidue = 5;

    /// <summary>The header line's column names, in physical order.</summary>
    public static IReadOnlyList<string> Columns { get; } =
    [
        "n_seq", "n_ties", "n_skew", "n_wide",
        "c0", "c1", "c2", "c3", "c4", "c5", "c6", "c7",
        "b0", "b1", "b2", "b3",
    ];

    /// <summary>The eight-value domain shared by every categorical column except <c>c6</c>.</summary>
    public static IReadOnlyList<string> StandardDomain { get; } =
        ["v0", "v1", "v2", "v3", "v4", "v5", "v6", "v7"];

    /// <summary>
    /// <c>c6</c>'s domain. <c>beta,gamma</c> contains the delimiter and <c>del"ta</c> contains the
    /// quote character, so both are always written quoted.
    /// </summary>
    public static IReadOnlyList<string> QuotedDomain { get; } =
        ["alpha", "beta,gamma", "del\"ta", "epsilon", "zeta", "eta", "theta", "iota"];

    /// <summary>The header line as written, without its terminator.</summary>
    public static string HeaderLine => string.Join(',', Columns);

    /// <summary>True when <c>n_ties</c> is the explicit missing token on this row.</summary>
    public static bool TiesIsMissing(long row) => row % TiesMissingModulus == TiesMissingResidue;

    /// <summary>True when <c>c3</c> is an empty cell on this row.</summary>
    public static bool C3IsMissing(long row) => row % C3MissingModulus == C3MissingResidue;

    /// <summary><c>n_ties</c>'s numeric value on a row where it is present.</summary>
    public static int Ties(long row) => (int)(row % 50);

    /// <summary><c>n_skew</c>'s numeric value: 7 on 90% of rows, a long tail on the rest.</summary>
    public static int Skew(long row) =>
        row % 1000 < 900 ? 7 : (int)(Determinism.Draw(row, ColSkew) % 9973) + 8;

    /// <summary><c>n_wide</c>'s value in hundredths, spanning [-100000000, 100000000].</summary>
    public static long WideHundredths(long row) =>
        (long)(Determinism.Draw(row, ColWide) % 200_000_001UL) - 100_000_000L;

    /// <summary>The zero-based domain index of categorical column <paramref name="categorical"/> (0..7).</summary>
    public static int CategoryIndex(long row, int categorical) =>
        (int)(Determinism.Draw(row, 10 + categorical) % 8);

    /// <summary>True when binary column <paramref name="binary"/> (0..3) is <c>yes</c> on this row.</summary>
    public static bool BinaryIsYes(long row, int binary) => (Determinism.Draw(row, 20 + binary) & 1) == 0;

    /// <summary>
    /// The <b>cleaned</b> value of one cell — exactly what a source yields after the §5.1
    /// quote-aware trim and missing normalization, so an oracle compares against the same thing the
    /// pipeline sees. <see langword="null"/> is missing (an empty cell or the missing token).
    /// </summary>
    public static string? CleanedValue(long row, int column) => column switch
    {
        ColSeq => row.ToString(CultureInfo.InvariantCulture),
        ColTies => TiesIsMissing(row) ? null : Ties(row).ToString(CultureInfo.InvariantCulture),
        ColSkew => Skew(row).ToString(CultureInfo.InvariantCulture),
        ColWide => Determinism.FormatHundredths(WideHundredths(row)),
        >= ColCategoricalFirst and < ColBinaryFirst => CategoricalValue(row, column - ColCategoricalFirst),
        >= ColBinaryFirst and < ColumnCount => BinaryIsYes(row, column - ColBinaryFirst) ? "yes" : "no",
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Not a W16 column."),
    };

    /// <summary>
    /// Streams the header plus <paramref name="records"/> data rows as UTF-8 without BOM, LF line
    /// endings. Streaming is the point: the 73M tier is never materialized, and one code path
    /// writes every tier so no tier can drift from another.
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

        var line = new StringBuilder(256);
        for (var row = 0L; row < records; row++)
        {
            if ((row & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            line.Clear();
            AppendRow(line, row);
            line.Append('\n');
            writer.Write(line);
        }

        writer.Flush();
    }

    /// <summary>
    /// Appends one row's sixteen escaped fields to <paramref name="line"/>, without a terminator.
    /// <para>
    /// Separated from <see cref="Write"/> so the keyed dedupe family can prepend its object-key
    /// column and reuse this row verbatim: the two families then share one definition of what a W16
    /// row says, which is what lets an expectation derived for one hold over the other.
    /// </para>
    /// </summary>
    public static void AppendRow(StringBuilder line, long row)
    {
        ArgumentNullException.ThrowIfNull(line);

        for (var column = 0; column < ColumnCount; column++)
        {
            if (column > 0)
            {
                line.Append(',');
            }

            AppendField(line, RawField(row, column));
        }
    }

    private static string? CategoricalValue(long row, int categorical)
    {
        if (categorical == 3 && C3IsMissing(row))
        {
            return null;
        }

        var domain = categorical == 6 ? QuotedDomain : StandardDomain;
        return domain[CategoryIndex(row, categorical)];
    }

    // The RAW cell text before CSV escaping: identical to the cleaned value except that a missing
    // n_ties is written as the explicit token and a missing c3 as the empty string.
    private static string RawField(long row, int column) => column == ColTies && TiesIsMissing(row)
        ? MissingToken
        : CleanedValue(row, column) ?? string.Empty;

    // RFC 4180: quote only when the field carries the delimiter, a quote, or a line break, and
    // double an embedded quote. Nothing else is ever quoted, so the bytes stay minimal and fixed.
    private static void AppendField(StringBuilder line, string field)
    {
        if (field.AsSpan().IndexOfAny(',', '"', '\n') < 0 && !field.Contains('\r'))
        {
            line.Append(field);
            return;
        }

        line.Append('"');
        foreach (var character in field)
        {
            if (character == '"')
            {
                line.Append('"');
            }

            line.Append(character);
        }

        line.Append('"');
    }
}
