namespace FcaBedrock.Sources;

/// <summary>
/// Why a delimited read failed, as a closed set a consumer can explain without reading any
/// exception's message (spec §5.1.1). <see cref="SourceReadException.Reason"/> carries it.
/// </summary>
public enum SourceReadFailureReason
{
    /// <summary>
    /// No closed reason is known: a failure built with a free-text constructor, or one whose cause
    /// is outside this set. A consumer explains it generically.
    /// </summary>
    Unknown = 0,

    /// <summary>A quote appears after unquoted content that is not outer whitespace, as in <c>a"b</c>.</summary>
    QuoteInUnquotedField,

    /// <summary>Something other than outer whitespace follows a closing quote, as in <c>"a"x</c>.</summary>
    TextAfterClosingQuote,

    /// <summary>The input ends inside a quoted field, as in <c>"a</c>.</summary>
    UnterminatedQuote,

    /// <summary>
    /// A record exceeded the reader's row and buffer limit of about 16 million UTF-16 code units. A
    /// quoted field that is never closed can run into this limit before the end of the input.
    /// </summary>
    ReaderLimit,
}
