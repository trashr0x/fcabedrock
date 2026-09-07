using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The <b>long-string</b> pressure family: four columns whose values are long rather than numerous.
/// <para>
/// It exists for one pathology the other families cannot express. Probe's boundedness guards count
/// two different things — retained <em>values</em> and retained <em>text</em> (D-110 guards 2 and 3)
/// — and the second exists precisely because a few enormous strings can exhaust memory while the
/// value count stays trivial. A corpus of short tokens can never reach that guard first, so it
/// cannot show whether the accounting works; this one can.
/// </para>
/// <para>
/// It also puts string allocation in the foreground. Every cleaned field is a fresh string, so a
/// family whose average field is hundreds of characters rather than a handful is where the reader's
/// per-field allocation cost stops hiding behind everything else.
/// </para>
/// <list type="bullet">
/// <item><c>id</c> — the row index; strictly increasing, so object identity is trivially checkable.</item>
/// <item><c>tag</c> — an eight-value short domain; the cheap control column.</item>
/// <item><c>blob</c> — one of eight <b>fixed</b> 512-character values: few distinct values, each
/// large, which is the guard-3 shape.</item>
/// <item><c>note</c> — a per-row string of 64 to 1,024 characters, distinct on almost every row:
/// high value count <em>and</em> high text volume at once.</item>
/// </list>
/// </summary>
internal static class LongTextCorpus
{
    /// <summary>
    /// The generator revision. <b>Bump this whenever any value definition below changes.</b>
    /// </summary>
    public const int GeneratorRevision = 1;

    /// <summary>The physical column count.</summary>
    public const int ColumnCount = 4;

    /// <summary>Physical index of <c>id</c>.</summary>
    public const int ColId = 0;

    /// <summary>Physical index of <c>tag</c>.</summary>
    public const int ColTag = 1;

    /// <summary>Physical index of <c>blob</c>.</summary>
    public const int ColBlob = 2;

    /// <summary>Physical index of <c>note</c>.</summary>
    public const int ColNote = 3;

    /// <summary>The exact length, in UTF-16 code units, of every <c>blob</c> value.</summary>
    public const int BlobLength = 512;

    /// <summary>The shortest <c>note</c>.</summary>
    public const int NoteMinimumLength = 64;

    /// <summary>The number of distinct <c>note</c> lengths.</summary>
    public const int NoteLengthSpan = 961;

    /// <summary>The header line's column names, in physical order.</summary>
    public static IReadOnlyList<string> Columns { get; } = ["id", "tag", "blob", "note"];

    /// <summary>The header line as written, without its terminator.</summary>
    public static string HeaderLine { get; } = string.Join(',', Columns);

    /// <summary>The eight-value short domain <c>tag</c> draws from.</summary>
    public static IReadOnlyList<string> TagDomain { get; } =
        ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];

    /// <summary>The eight fixed 512-character values <c>blob</c> draws from.</summary>
    public static IReadOnlyList<string> BlobDomain { get; } = BuildBlobDomain();

    /// <summary>The zero-based <c>tag</c> domain index for a row.</summary>
    public static int TagIndex(long row) => (int)(Determinism.Draw(row, 70) % 8);

    /// <summary>The zero-based <c>blob</c> domain index for a row.</summary>
    public static int BlobIndex(long row) => (int)(Determinism.Draw(row, 71) % 8);

    /// <summary>The length, in characters, of a row's <c>note</c>.</summary>
    public static int NoteLength(long row) =>
        NoteMinimumLength + (int)(Determinism.Draw(row, 72) % NoteLengthSpan);

    /// <summary>
    /// A row's <c>note</c>: a deterministic run of lowercase letters and digits, so no field can
    /// contain the delimiter, a quote, or a line break and no escaping is reachable.
    /// </summary>
    public static string Note(long row)
    {
        var length = NoteLength(row);
        var text = new StringBuilder(length);
        var state = Determinism.Draw(row, 73);
        for (var i = 0; i < length; i++)
        {
            state = Determinism.Mix(state);
            text.Append(Alphabet[(int)(state % (ulong)Alphabet.Length)]);
        }

        return text.ToString();
    }

    /// <summary>
    /// The <b>cleaned</b> value of one cell. This family carries no missing cells: it is a text-size
    /// case, and a second pressure would only make a measurement harder to attribute.
    /// </summary>
    public static string CleanedValue(long row, int column) => column switch
    {
        ColId => row.ToString(CultureInfo.InvariantCulture),
        ColTag => TagDomain[TagIndex(row)],
        ColBlob => BlobDomain[BlobIndex(row)],
        ColNote => Note(row),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Not a long-text column."),
    };

    /// <summary>
    /// Streams the header plus <paramref name="records"/> data rows as UTF-8 without BOM, LF line
    /// endings.
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

        var line = new StringBuilder(2048);
        for (var row = 0L; row < records; row++)
        {
            if ((row & 0x3FFF) == 0)
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

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    // Eight fixed values, each exactly BlobLength characters, differing from the first character on
    // so no two share a prefix and a truncated comparison cannot pass by accident.
    private static IReadOnlyList<string> BuildBlobDomain()
    {
        var domain = new List<string>(8);
        for (var index = 0; index < 8; index++)
        {
            var text = new StringBuilder(BlobLength);
            var state = Determinism.Draw(index, 74);
            for (var i = 0; i < BlobLength; i++)
            {
                state = Determinism.Mix(state);
                text.Append(Alphabet[(int)(state % (ulong)Alphabet.Length)]);
            }

            domain.Add(text.ToString());
        }

        return domain;
    }
}
