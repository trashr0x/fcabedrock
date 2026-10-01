using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The <b>lexical</b> family: small wide corpora that each stress one part of the delimited-text
/// grammar (spec §5.1.1) rather than a scale. Every variant has a header line of <c>c0</c>,
/// <c>c1</c>, ... and its values are a pinned function of the record and column index.
/// <list type="bullet">
/// <item><c>quoted-multiline</c>: six quoted fields per record, each holding doubled quotes and a
/// line break (CRLF or LF), with CRLF record terminators.</item>
/// <item><c>tab</c> and <c>space</c>: sixteen plain fields separated by TAB or by a space.</item>
/// <item><c>many-fields</c>: 2,000 records of 200 short fields: per-field cost.</item>
/// <item><c>long-fields</c>: 500 records of four 5,000-character fields: long-field scanning.</item>
/// <item><c>unicode-whitespace</c>: sixteen fields, each padded with a no-break space before it and
/// an ideographic space after it, which the reader removes.</item>
/// <item><c>blank-runs</c>: sixteen plain fields per record and a blank line after every record,
/// which the reader skips.</item>
/// </list>
/// The first five read the same under any reader that follows the quoting rules and keeps blank
/// lines out of the data; the last two exist to measure the Unicode-whitespace and blank-record
/// rules themselves (<see cref="LexicalVariant.ExercisesWhitespaceAndBlankRules"/>).
/// </summary>
internal static class LexicalCorpus
{
    /// <summary>
    /// The generator revision. <b>Bump this whenever any value definition below changes.</b>
    /// </summary>
    public const int GeneratorRevision = 1;

    /// <summary>The variants, in registry order.</summary>
    public static IReadOnlyList<LexicalVariant> Variants { get; } =
    [
        new("quoted-multiline", ',', Records: 10_000, Columns: 6, "\r\n", BlankLineAfterEach: false, ExercisesWhitespaceAndBlankRules: false,
            QuotedMultiline, QuotedMultilineValue),
        new("tab", '\t', Records: 10_000, Columns: 16, "\n", BlankLineAfterEach: false, ExercisesWhitespaceAndBlankRules: false,
            (r, c) => Plain('t', r, c), (r, c) => Plain('t', r, c)),
        new("space", ' ', Records: 10_000, Columns: 16, "\n", BlankLineAfterEach: false, ExercisesWhitespaceAndBlankRules: false,
            (r, c) => Plain('s', r, c), (r, c) => Plain('s', r, c)),
        new("many-fields", ',', Records: 2_000, Columns: 200, "\n", BlankLineAfterEach: false, ExercisesWhitespaceAndBlankRules: false,
            ManyFieldsValue, ManyFieldsValue),
        new("long-fields", ',', Records: 500, Columns: 4, "\n", BlankLineAfterEach: false, ExercisesWhitespaceAndBlankRules: false,
            LongFieldsValue, LongFieldsValue),
        new("unicode-whitespace", ',', Records: 10_000, Columns: 16, "\n", BlankLineAfterEach: false, ExercisesWhitespaceAndBlankRules: true,
            (r, c) => (char)0x00A0 + UnicodeWhitespaceValue(r, c) + (char)0x3000, UnicodeWhitespaceValue),
        new("blank-runs", ',', Records: 10_000, Columns: 16, "\n", BlankLineAfterEach: true, ExercisesWhitespaceAndBlankRules: true,
            (r, c) => Plain('b', r, c), (r, c) => Plain('b', r, c)),
    ];

    /// <summary>The variant with the given name.</summary>
    public static LexicalVariant Variant(string name) =>
        Variants.SingleOrDefault(variant => string.Equals(variant.Name, name, StringComparison.Ordinal))
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "no such lexical variant.");

    /// <summary>
    /// Writes <paramref name="variant"/>'s data file: the header line, then
    /// <paramref name="records"/> records, each followed by its terminator (and a blank line for
    /// <c>blank-runs</c>). UTF-8 without a byte order mark.
    /// </summary>
    public static void Write(LexicalVariant variant, Stream stream, long records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(records);

        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1 << 16, leaveOpen: true);
        var line = new StringBuilder();
        for (var column = 0; column < variant.Columns; column++)
        {
            if (column > 0)
            {
                line.Append(variant.Delimiter);
            }

            line.Append('c').Append(column.ToString(CultureInfo.InvariantCulture));
        }

        writer.Write(line);
        writer.Write(variant.Terminator);
        for (var row = 0L; row < records; row++)
        {
            if (row % 1_000 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            line.Clear();
            for (var column = 0; column < variant.Columns; column++)
            {
                if (column > 0)
                {
                    line.Append(variant.Delimiter);
                }

                line.Append(variant.RawValue(row, column));
            }

            writer.Write(line);
            writer.Write(variant.Terminator);
            if (variant.BlankLineAfterEach)
            {
                writer.Write(variant.Terminator);
            }
        }
    }

    /// <summary>
    /// The spec a variant's drain reads its settings from: the variant's delimiter, a header, and
    /// one excluded attribute, because a drain analyzes nothing.
    /// </summary>
    public static string Spec(LexicalVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var delimiter = variant.Delimiter switch
        {
            '\t' => "\\t",
            _ => variant.Delimiter.ToString(),
        };
        return $$"""
            # A lexical drain case: only the read settings matter.
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true
            delimiter = "{{delimiter}}"

            [[attribute]]
            name = "c0"
            source = { kind = "column", index = 0 }
            include = false

            """;
    }

    private static string Plain(char prefix, long row, int column) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}{row}x{column}");

    // The decoded value: doubled quotes decode to one quote, and the line break is kept.
    private static string QuotedMultilineValue(long row, int column) =>
        string.Create(CultureInfo.InvariantCulture, $"v{row}_{column} \"q\" a{(column % 2 == 0 ? "\r\n" : "\n")}b");

    private static string QuotedMultiline(long row, int column) =>
        "\"" + QuotedMultilineValue(row, column).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string ManyFieldsValue(long row, int column) =>
        string.Create(CultureInfo.InvariantCulture, $"m{(row + column) % 97}");

    private static string LongFieldsValue(long row, int column) =>
        new((char)('a' + ((row + column) % 26)), 5_000);

    private static string UnicodeWhitespaceValue(long row, int column) =>
        string.Create(CultureInfo.InvariantCulture, $"u{row}_{column}");
}

/// <summary>
/// One lexical variant: its separator, size and terminator, the raw text of each cell, and the value
/// a correct reader decodes from it (spec §5.1.1).
/// </summary>
/// <param name="Name">The variant name; the case id is <c>lexical-{Name}-small</c>.</param>
/// <param name="Delimiter">The field delimiter.</param>
/// <param name="Records">The data record count (the header and blank lines are not records).</param>
/// <param name="Columns">The field count of every record.</param>
/// <param name="Terminator">The record terminator.</param>
/// <param name="BlankLineAfterEach">Whether a blank line follows every record.</param>
/// <param name="ExercisesWhitespaceAndBlankRules">
/// Whether the data reads differently under a reader that removes only spaces around fields and
/// treats blank lines as records: true for the two variants that measure those rules.
/// </param>
/// <param name="RawValue">The cell text as written.</param>
/// <param name="DecodedValue">The cell value a correct reader decodes.</param>
internal sealed record LexicalVariant(
    string Name,
    char Delimiter,
    long Records,
    int Columns,
    string Terminator,
    bool BlankLineAfterEach,
    bool ExercisesWhitespaceAndBlankRules,
    Func<long, int, string> RawValue,
    Func<long, int, string> DecodedValue);
