using System.Globalization;

namespace FcaBedrock.Sources;

/// <summary>
/// An <b>expected</b> failure to read a source, normalized by the adapter into one
/// source-neutral type. It is the unbound seam's failure channel: the seam
/// hands back records, so a provider-specific read failure has nowhere else to surface,
/// and a consumer must be able to recognize one without knowing which adapter produced it:
/// Discovery must never catch a Sep type to learn that a CSV row was unreadable.
/// <para>
/// <b>Scope.</b> Adapter/provider read failures only. When the adapter normalizes a
/// provider's own failure, that exception is retained as <see cref="Exception.InnerException"/>;
/// a failure the adapter detects itself, such as malformed quoting (spec §5.1.1), has none.
/// This type is deliberately <em>not</em>
/// a catch-all: <see cref="OperationCanceledException"/> always propagates unwrapped and is
/// never a read failure (D-111/D-112), and programmer errors (<see cref="ArgumentException"/>,
/// <see cref="InvalidOperationException"/>, <see cref="NullReferenceException"/>, violated
/// invariants) propagate as themselves rather than being disguised as an infrastructure
/// problem (EP-14). Invalid UTF-8 is not wrapped either: the decoder's own
/// <see cref="System.Text.DecoderFallbackException"/> reaches the caller unchanged.
/// </para>
/// <para>
/// <b>Closed detail.</b> <see cref="Reason"/> and <see cref="Position"/> say why and where the
/// read failed, so a consumer can explain the failure without reading any message text. The
/// free-text constructors leave them <see cref="SourceReadFailureReason.Unknown"/> and
/// <see langword="null"/>. A free-text message, and any inner exception's message, can hold a path
/// or source data and is not meant to be shown as an explanation.
/// </para>
/// </summary>
public sealed class SourceReadException : Exception
{
    /// <summary>Creates a read failure with no further detail.</summary>
    public SourceReadException()
    {
    }

    /// <summary>Creates a read failure described by <paramref name="message"/>.</summary>
    public SourceReadException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a read failure described by <paramref name="message"/>, retaining the
    /// provider's own <paramref name="innerException"/> as the cause.
    /// </summary>
    public SourceReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a read failure from its closed detail. The message is built from
    /// <paramref name="reason"/> and <paramref name="position"/> alone, invariantly, and holds no
    /// source data. Every argument is required, so no call of the free-text constructors can bind
    /// here.
    /// </summary>
    /// <param name="reason">Why the read failed.</param>
    /// <param name="position">Where it failed, or <see langword="null"/> when that is not known.</param>
    /// <param name="innerException">The provider's own failure, or <see langword="null"/> when the reader found the defect itself.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not a defined value.</exception>
    public SourceReadException(SourceReadFailureReason reason, SourceReadPosition? position, Exception? innerException)
        : base(Describe(reason, position), innerException)
    {
        Reason = reason;
        Position = position;
    }

    /// <summary>Why the read failed; <see cref="SourceReadFailureReason.Unknown"/> for a free-text failure.</summary>
    public SourceReadFailureReason Reason { get; }

    /// <summary>Where the read failed, or <see langword="null"/> when that is not known.</summary>
    public SourceReadPosition? Position { get; }

    // The controlled message: zero-based record and field indices, one-based physical lines, and
    // no source data. For a fully known grammar defect it reads "The source could not be read:
    // data record 1, column 0, starting on line 3 (the defect is on line 4): <defect>."
    private static string Describe(SourceReadFailureReason reason, SourceReadPosition? position)
    {
        var cause = reason switch
        {
            SourceReadFailureReason.Unknown => null,
            SourceReadFailureReason.QuoteInUnquotedField => "a quote appears inside an unquoted field",
            SourceReadFailureReason.TextAfterClosingQuote => "text follows the closing quote of a quoted field",
            SourceReadFailureReason.UnterminatedQuote => "a quoted field is not closed before the end of the input",
            SourceReadFailureReason.ReaderLimit =>
                "a record exceeded the reader's row and buffer limit of about 16 million UTF-16 code units. A quoted "
                + "field that is never closed can run into this limit",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The read failure reason is not a defined value."),
        };

        var message = "The source could not be read";
        if (position is { } known && Where(known) is { Length: > 0 } where)
        {
            message += ": " + where;
        }

        if (cause is not null)
        {
            message += ": " + cause;
        }

        return message + ".";
    }

    private static string Where(SourceReadPosition position)
    {
        var parts = new List<string>(3);
        switch (position.RecordKind)
        {
            case SourceReadRecordKind.Data:
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"data record {position.DataRecordIndex}"));
                break;
            case SourceReadRecordKind.Header:
                parts.Add("the header record");
                break;
        }

        if (position.FieldIndex is { } field)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"column {field}"));
        }

        if (position.RecordStartLine is { } start)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"starting on line {start}"));
        }

        var where = string.Join(", ", parts);
        if (position.DefectLine is { } defect)
        {
            where = where.Length == 0
                ? string.Create(CultureInfo.InvariantCulture, $"the defect is on line {defect}")
                : string.Create(CultureInfo.InvariantCulture, $"{where} (the defect is on line {defect})");
        }

        return where;
    }
}
