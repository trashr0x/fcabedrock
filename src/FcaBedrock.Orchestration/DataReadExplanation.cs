using System.Globalization;
using System.Text;
using FcaBedrock.Sources;

namespace FcaBedrock.Orchestration;

/// <summary>
/// The code-less host error for DATA that could not be read (D-122 part 2), built at every DATA
/// read catch. It says why the read failed and, where the reader knows it, where, then what to do:
/// <code>
/// cannot read the data file 'bad.csv': a quoted field is not closed before the end of the input
/// (data record 1, field 0, counted from 0; record starts on line 3, defect on line 4). Add the
/// missing closing quote, or double each quote inside the field.
/// </code>
/// <para>
/// <b>Only closed facts are disclosed.</b> The reason and position come from
/// <see cref="SourceReadException.Reason"/> and <see cref="SourceReadException.Position"/>, and a
/// decoding failure from its type and the failing pass's <see cref="DataByteOrderMark"/>. A refused
/// open is explained by <see cref="InputReadExplanation"/>, as for every input file. No exception
/// message is copied: a free-text message or an inner exception can hold a path, an
/// operating-system text or source data. Every other failure, and a read failure with no closed
/// reason, keeps the generic sentence. The message is plain text; the renderer escapes it once.
/// </para>
/// </summary>
internal static class DataReadExplanation
{
    /// <summary>The message for a DATA read failure caused by <paramref name="cause"/>.</summary>
    /// <param name="dataPath">The DATA operand, as given.</param>
    /// <param name="cause">The read failure.</param>
    /// <param name="mark">
    /// The byte order mark the failing pass began with, or <see cref="DataByteOrderMark.None"/> when
    /// it is not tracked.
    /// </param>
    public static string Message(string dataPath, Exception cause, DataByteOrderMark mark)
    {
        ArgumentNullException.ThrowIfNull(dataPath);
        ArgumentNullException.ThrowIfNull(cause);

        if (cause is SourceReadException { Reason: not SourceReadFailureReason.Unknown } read)
        {
            var where = read.Position is { } position ? Where(position) : string.Empty;
            var detail = where.Length == 0 ? Reason(read.Reason) : $"{Reason(read.Reason)} ({where})";
            return $"cannot read the data file '{dataPath}': {detail}. {Advice(read.Reason)}";
        }

        if (cause is DecoderFallbackException)
        {
            return mark == DataByteOrderMark.None
                ? $"cannot read the data file '{dataPath}': it is not valid UTF-8. Save it as UTF-8."
                : $"cannot read the data file '{dataPath}': it starts with a {MarkName(mark)} byte order mark, but data "
                  + "must be UTF-8. Save it as UTF-8.";
        }

        // An open the operating system refused, explained as every input's is; anything else keeps
        // the generic sentence.
        return InputReadExplanation.Message(InputFile.Data, dataPath, cause);
    }

    private static string Reason(SourceReadFailureReason reason) => reason switch
    {
        SourceReadFailureReason.QuoteInUnquotedField => "a quote appears inside an unquoted field",
        SourceReadFailureReason.TextAfterClosingQuote => "text follows the closing quote of a quoted field",
        SourceReadFailureReason.UnterminatedQuote => "a quoted field is not closed before the end of the input",
        SourceReadFailureReason.ReaderLimit =>
            "a record exceeded the reader's row and buffer limit of about 16 million UTF-16 code units",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The read failure reason has no explanation."),
    };

    private static string Advice(SourceReadFailureReason reason) => reason switch
    {
        SourceReadFailureReason.QuoteInUnquotedField => "Quote the whole field and double each quote inside it.",
        SourceReadFailureReason.TextAfterClosingQuote => "End the field right after its closing quote, or double each quote inside it.",
        SourceReadFailureReason.UnterminatedQuote => "Add the missing closing quote, or double each quote inside the field.",
        SourceReadFailureReason.ReaderLimit => "Look for a quoted field that is never closed.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The read failure reason has no advice."),
    };

    private static string MarkName(DataByteOrderMark mark) => mark switch
    {
        DataByteOrderMark.Utf16LittleEndian => "UTF-16 little-endian",
        DataByteOrderMark.Utf16BigEndian => "UTF-16 big-endian",
        DataByteOrderMark.Utf32LittleEndian => "UTF-32 little-endian",
        DataByteOrderMark.Utf32BigEndian => "UTF-32 big-endian",
        DataByteOrderMark.Utf16OrUtf32 => "UTF-16 or UTF-32",
        _ => throw new ArgumentOutOfRangeException(nameof(mark), mark, "The mark has no name."),
    };

    // "data record 1, field 0, counted from 0; record starts on line 3, defect on line 4", naming
    // only the coordinates the reader knows. Indices count from 0 and lines from 1.
    private static string Where(SourceReadPosition position)
    {
        var indices = new List<string>(2);
        if (position.RecordKind == SourceReadRecordKind.Header)
        {
            indices.Add("the header record");
        }
        else if (position.DataRecordIndex is { } record)
        {
            indices.Add(string.Create(CultureInfo.InvariantCulture, $"data record {record}"));
        }

        if (position.FieldIndex is { } field)
        {
            indices.Add(string.Create(CultureInfo.InvariantCulture, $"field {field}"));
        }

        var parts = new List<string>(2);
        if (indices.Count > 0)
        {
            var counted = position.DataRecordIndex is not null || position.FieldIndex is not null;
            parts.Add(string.Join(", ", indices) + (counted ? ", counted from 0" : string.Empty));
        }

        var lines = (position.RecordStartLine, position.DefectLine) switch
        {
            ({ } start, { } defect) when start == defect => string.Create(CultureInfo.InvariantCulture, $"line {start}"),
            ({ } start, { } defect) => string.Create(CultureInfo.InvariantCulture, $"record starts on line {start}, defect on line {defect}"),
            ({ } start, null) => string.Create(CultureInfo.InvariantCulture, $"record starts on line {start}"),
            (null, { } defect) => string.Create(CultureInfo.InvariantCulture, $"defect on line {defect}"),
            _ => null,
        };
        if (lines is not null)
        {
            parts.Add(lines);
        }

        return string.Join("; ", parts);
    }
}
