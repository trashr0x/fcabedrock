using System.Globalization;
using System.Runtime.CompilerServices;

namespace FcaBedrock.Sources;

/// <summary>The one grammar defect a raw field can carry (spec §5.1.1), in the order it is detected.</summary>
internal enum FieldFault
{
    None = 0,

    /// <summary>A quote appears after unquoted content that is not outer whitespace.</summary>
    QuoteInUnquotedField,

    /// <summary>Something other than outer whitespace follows a closing quote (a reopening quote included).</summary>
    TextAfterClosingQuote,

    /// <summary>A quoted field has no closing quote before its raw span ends (only possible at the end of the input).</summary>
    UnterminatedQuote,
}

/// <summary>The outcome of checking one raw field: its defect and where it is, and whether the field is quoted.</summary>
internal readonly record struct FieldCheck(FieldFault Fault, int FaultOffset, bool Quoted)
{
    public bool IsValid => Fault == FieldFault.None;
}

/// <summary>
/// What owned per-candidate work charges. Implemented by structs, so the reading code is generic
/// over the budget and each instantiation is specialized: with <see cref="NoCheckpoints"/> every
/// charge and every search window compiles away.
/// </summary>
internal interface IWorkBudget
{
    /// <summary>Records <paramref name="units"/> examined or copied; may check the operation's token.</summary>
    void Charge(int units);
}

/// <summary>The budget of a candidate too short to meter: charges nothing and checks nothing.</summary>
internal struct NoCheckpoints : IWorkBudget
{
    public readonly void Charge(int units)
    {
    }
}

/// <summary>
/// The cooperative cancellation checkpoint inside the owned work on one long candidate: the blank
/// test, the quote search, validation, line-break counting, decoding, the escaped copy, the
/// traversal of a triple candidate's unused columns and the diagnostic rescan.
/// <para>
/// <b>Exactly what it does.</b> A budget starts each candidate with <see cref="Quantum"/> units
/// remaining. Every grammar routine charges it: the units a search examined (charged after that
/// search window returns), one unit per whitespace unit trimmed or suffix unit checked, one per
/// field or routine entry, one per unused column passed over in a quote-free triple candidate, and,
/// before a copy into a string, the length of that copy. The escaped copy, a loop of searches and
/// segment copies run inside the string's construction callback, charges its searches like any
/// other search, so it is cancellable between its windows and among its escaped pairs. When a
/// charge brings the remaining count to zero or below, the token is checked and the count is reset
/// to <see cref="Quantum"/>: the overshoot is discarded, not carried. So the charged work from the
/// start of a candidate to its first budget checkpoint, and between two checkpoints, is at least
/// <see cref="Quantum"/> units and less than <see cref="Quantum"/> plus the largest single charge.
/// A search charges at most <see cref="Quantum"/> per window, so for searches that is under twice
/// <see cref="Quantum"/> (<c>Charge(65535)</c> then <c>Charge(65536)</c> is 131,071 units before the
/// check). A copy's charge is the copy's length, uncapped: a checkpoint can fall just before a long
/// copy. A single runtime copy (a new string, one segment's copy), a pool rental and a string
/// allocation each run as one primitive operation between checks; a loop of them in the reading
/// code is owned work and is charged.
/// </para>
/// <para>
/// <b>Which candidates are metered.</b> Only a candidate whose raw length plus column count is at
/// least <see cref="Quantum"/> (<see cref="IsLong"/>). A shorter candidate is processed with
/// <see cref="NoCheckpoints"/> and has no in-work check at all: its whole owned work runs between
/// the reader's check after the advance and its check before the record is exposed. That work is
/// several passes over the candidate (the quote search, the blank and whitespace tests, validation
/// of quoted fields, line-break counts, the copies into strings, the column traversal and, on a
/// fault, the diagnostic rescan), each at most the candidate's length, plus the allocation of its
/// strings: the size threshold bounds input size, not work. No latency bound follows from any
/// of this, and none is promised.
/// </para>
/// </summary>
internal struct CancellationBudget : IWorkBudget
{
    /// <summary>Units charged between checkpoints, the long-candidate threshold, and the search window.</summary>
    public const int Quantum = 65_536;

    private readonly CancellationToken _token;
    private int _remaining;

    public CancellationBudget(CancellationToken token)
    {
        _token = token;
        _remaining = Quantum;
    }

    /// <summary>Whether a candidate of this raw length and column count is metered.</summary>
    public static bool IsLong(int rawLength, int columnCount) => rawLength + columnCount >= Quantum;

    public void Charge(int units)
    {
        _remaining -= units;
        if (_remaining <= 0)
        {
            Checkpoint();
        }
    }

    // Kept out of line: it is the cold path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Checkpoint()
    {
        _token.ThrowIfCancellationRequested();
        _remaining = Quantum; // the overshoot below zero is discarded
    }
}

/// <summary>
/// The Bedrock delimited-text grammar (spec §5.1.1) for one raw field: the untouched span Sep
/// 0.15.0 returns with <c>Unescape = false</c> and <c>Trim = SepTrim.None</c>. Sep has already
/// split the candidate, so nothing here searches across fields or records; within a candidate it
/// searches for quotes, and for line breaks only inside quoted content.
/// <para>
/// Every routine takes the candidate's work budget and charges what it examines; with a
/// <see cref="CancellationBudget"/> every search runs window by window, so one long field is
/// cancellable between windows (<see cref="CancellationBudget"/> states exactly when the token is
/// checked). Decoding rents nothing; any future pool rental here must be returned in a
/// <c>finally</c> block, because a checkpoint can throw between the rental and its return.
/// </para>
/// </summary>
internal static class DelimitedFieldGrammar
{
    public const char Quote = '"';

    /// <summary>
    /// Outer whitespace O at a lexical position: a member of W (spec §5.1, exactly
    /// <see cref="char.IsWhiteSpace(char)"/>) that is not the selected delimiter, CR or LF. A
    /// structural character is never whitespace.
    /// </summary>
    public static bool IsOuterWhitespace(char c, char delimiter) =>
        c != delimiter && c != '\r' && c != '\n' && char.IsWhiteSpace(c);

    /// <summary>
    /// Whether <paramref name="text"/> (a whole candidate or one field) holds no quote. A quote-free
    /// field is a valid unquoted field and holds no delimiter, CR or LF (Sep treats those as literal
    /// only inside quotes), so its decoded value is its text without W at either end.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsQuoteFree<TBudget>(ReadOnlySpan<char> text, ref TBudget budget)
        where TBudget : struct, IWorkBudget =>
        IndexOfQuote(text, 0, ref budget) < 0;

    /// <summary>The index of the first quote in one field, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FirstQuote<TBudget>(ReadOnlySpan<char> raw, ref TBudget budget)
        where TBudget : struct, IWorkBudget =>
        IndexOfQuote(raw, 0, ref budget);

    /// <summary>
    /// The decoded value of a quote-free field: W removed at both ends (in a quote-free field the
    /// delimiter, CR and LF cannot occur, so W and O coincide there), then one copy into the string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string MaterializeUnquoted<TBudget>(ReadOnlySpan<char> raw, char delimiter, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        if (raw.Length == 0)
        {
            budget.Charge(1);
            return string.Empty;
        }

        if (!char.IsWhiteSpace(raw[0]) && !char.IsWhiteSpace(raw[^1]))
        {
            budget.Charge(raw.Length + 1); // the copy below
            return new string(raw);
        }

        return MaterializeTrimmed(raw, delimiter, ref budget);
    }

    private static string MaterializeTrimmed<TBudget>(ReadOnlySpan<char> raw, char delimiter, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        var start = 0;
        var end = raw.Length;
        while (start < end && IsOuterWhitespace(raw[start], delimiter))
        {
            start++;
            budget.Charge(1);
        }

        while (end > start && IsOuterWhitespace(raw[end - 1], delimiter))
        {
            end--;
            budget.Charge(1);
        }

        budget.Charge(end - start + 1); // the copy below
        return end == start ? string.Empty : new string(raw[start..end]);
    }

    /// <summary>
    /// Validates one raw field without decoding it (an unused triple column, the header a record
    /// read consumes, the record that gives a headerless width, the diagnostic rescan). Parity alone
    /// is not validity: the opening position, every inner quote, the closing suffix and the end
    /// state are all checked. Never allocates.
    /// </summary>
    public static FieldCheck Check<TBudget>(ReadOnlySpan<char> raw, char delimiter, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        budget.Charge(1);
        var firstQuote = IndexOfQuote(raw, 0, ref budget);
        if (firstQuote < 0)
        {
            // Unquoted. A quote-free span cannot contain the delimiter, CR or LF: Sep treats those
            // as literal only while a column's quote count is odd, which needs a quote.
            return new FieldCheck(FieldFault.None, -1, Quoted: false);
        }

        // A quote may open a field only after outer whitespace.
        for (var i = 0; i < firstQuote; i++)
        {
            if (!IsOuterWhitespace(raw[i], delimiter))
            {
                return new FieldCheck(FieldFault.QuoteInUnquotedField, firstQuote, Quoted: false);
            }

            budget.Charge(1);
        }

        var position = firstQuote + 1;
        while (true)
        {
            var quoteAt = IndexOfQuote(raw, position, ref budget);
            if (quoteAt < 0)
            {
                return new FieldCheck(FieldFault.UnterminatedQuote, firstQuote, Quoted: true);
            }

            if (quoteAt + 1 < raw.Length && raw[quoteAt + 1] == Quote)
            {
                // A doubled quote inside quoted content is one literal quote.
                position = quoteAt + 2;
                continue;
            }

            // Closing quote: only outer whitespace may follow before the field boundary.
            for (var k = quoteAt + 1; k < raw.Length; k++)
            {
                if (!IsOuterWhitespace(raw[k], delimiter))
                {
                    return new FieldCheck(FieldFault.TextAfterClosingQuote, k, Quoted: true);
                }

                budget.Charge(1);
            }

            return new FieldCheck(FieldFault.None, -1, Quoted: true);
        }
    }

    /// <summary>
    /// Validates and decodes a field that holds a quote, the first one at
    /// <paramref name="firstQuote"/>. One pass over the quotes validates the field (the defects and
    /// offsets <see cref="Check"/> reports) and counts the escaped pairs; a separate count finds the
    /// line breaks inside the quoted content; the decoded length is charged; then the value is
    /// copied once, straight into the new string: content without an escaped pair by one runtime
    /// copy, escaped content by <see cref="CopyUnescaped"/> inside the string's construction
    /// callback, which charges the same budget as it searches. Returns null on a defect.
    /// </summary>
    public static string? DecodeField<TBudget>(
        ReadOnlySpan<char> raw, int firstQuote, char delimiter, ref TBudget budget, out FieldCheck fault, out int lineBreaks)
        where TBudget : struct, IWorkBudget
    {
        lineBreaks = 0;
        budget.Charge(1);

        // A quote may open a field only after outer whitespace.
        for (var i = 0; i < firstQuote; i++)
        {
            if (!IsOuterWhitespace(raw[i], delimiter))
            {
                fault = new FieldCheck(FieldFault.QuoteInUnquotedField, firstQuote, Quoted: false);
                return null;
            }

            budget.Charge(1);
        }

        var contentStart = firstQuote + 1;
        var position = contentStart;
        var escapes = 0;
        int closing;
        while (true)
        {
            var at = IndexOfQuote(raw, position, ref budget);
            if (at < 0)
            {
                fault = new FieldCheck(FieldFault.UnterminatedQuote, firstQuote, Quoted: true);
                return null;
            }

            if (at + 1 < raw.Length && raw[at + 1] == Quote)
            {
                escapes++;
                position = at + 2;
                continue;
            }

            closing = at;
            break;
        }

        for (var k = closing + 1; k < raw.Length; k++)
        {
            if (!IsOuterWhitespace(raw[k], delimiter))
            {
                fault = new FieldCheck(FieldFault.TextAfterClosingQuote, k, Quoted: true);
                return null;
            }

            budget.Charge(1);
        }

        fault = default;
        var content = raw[contentStart..closing];
        lineBreaks = CountLineBreaks(content, ref budget);
        var length = content.Length - escapes;
        budget.Charge(length + 1); // the copy's charge: a checkpoint can fall between validation and the copy
        if (length == 0)
        {
            return string.Empty;
        }

        if (escapes == 0)
        {
            return new string(content); // one runtime copy: a primitive operation between checks
        }

        // The escaped copy is owned work, not one primitive: its loop searches the content again and
        // copies it segment by segment, so it charges the same budget as it goes. The budget travels
        // by reference in the callback's state, and the static callback allocates nothing per call.
        // String.Create has no exception handler around the callback, so a cancellation thrown inside
        // it propagates and the partly built string is never exposed.
        return string.Create(
            length,
            new EscapedCopy<TBudget>(content, ref budget),
            static (destination, state) => CopyUnescaped(state.Content, destination, ref state.Budget));
    }

    /// <summary>
    /// Copies valid quoted <paramref name="content"/> into <paramref name="destination"/>, keeping
    /// one quote of each doubled pair (every quote in valid quoted content is the first of a pair:
    /// copy up to and including it, skip its twin). Its searches are the windowed, charged searches
    /// of every other routine, so on a metered candidate a checkpoint can fall inside a long run
    /// before the next pair and among many consecutive pairs (each pair's search charges at least
    /// one unit). Each segment's copy is one primitive operation between checks.
    /// </summary>
    public static void CopyUnescaped<TBudget>(ReadOnlySpan<char> content, Span<char> destination, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        var written = 0;
        var position = 0;
        while (true)
        {
            var at = IndexOfQuote(content, position, ref budget);
            if (at < 0)
            {
                content[position..].CopyTo(destination[written..]);
                return;
            }

            content[position..(at + 1)].CopyTo(destination[written..]);
            written += at + 1 - position;
            position = at + 2;
        }
    }

    /// <summary>The escaped copy's callback state: the content, and the candidate's budget by reference.</summary>
    private readonly ref struct EscapedCopy<TBudget>
        where TBudget : struct, IWorkBudget
    {
        public readonly ReadOnlySpan<char> Content;
        public readonly ref TBudget Budget;

        public EscapedCopy(ReadOnlySpan<char> content, ref TBudget budget)
        {
            Content = content;
            Budget = ref budget;
        }
    }

    /// <summary>
    /// Whether the only column of a one-column candidate makes the candidate blank: every unit is
    /// outer whitespace, the empty candidate included. A candidate with a delimiter has two or more
    /// columns and is never blank, and a quote is never whitespace, so the caller tests one-column
    /// candidates only.
    /// </summary>
    public static bool IsBlankCandidate<TBudget>(ReadOnlySpan<char> onlyColumn, char delimiter, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        budget.Charge(1);
        foreach (var c in onlyColumn)
        {
            if (!IsOuterWhitespace(c, delimiter))
            {
                return false;
            }

            budget.Charge(1);
        }

        return true;
    }

    /// <summary>Physical line breaks inside a span: CRLF, a lone CR and a lone LF each count once.</summary>
    public static int CountLineBreaks<TBudget>(ReadOnlySpan<char> text, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        var count = 0;
        var position = 0;
        while (true)
        {
            var at = IndexOfLineBreak(text, position, ref budget);
            if (at < 0)
            {
                return count;
            }

            count++;
            position = at + (text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n' ? 2 : 1);
        }
    }

    /// <summary>
    /// The read failure for a malformed field: invariant, with zero-based record and column indices
    /// and one-based physical lines, and no raw data.
    /// </summary>
    public static string FaultMessage(FieldFault fault, int? recordIndex, long startLine, int column, long faultLine)
    {
        var record = recordIndex is { } index
            ? string.Create(CultureInfo.InvariantCulture, $"data record {index}")
            : "the header record";
        var defect = fault switch
        {
            FieldFault.QuoteInUnquotedField => "a quote appears inside an unquoted field",
            FieldFault.TextAfterClosingQuote => "text follows the closing quote of a quoted field",
            FieldFault.UnterminatedQuote => "a quoted field is not closed before the end of the input",
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"The source could not be read: {record}, column {column}, starting on line {startLine} (the defect is on line {faultLine}): {defect}.");
    }

    // The windowed searches. Each window is one vectorized search; the budget is charged the units
    // it examined, after that window's search, so a checkpoint can fall between two windows of one
    // long field. With NoCheckpoints the type test is a JIT-time constant and the search is one
    // plain IndexOf.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOfQuote<TBudget>(ReadOnlySpan<char> text, int from, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        if (typeof(TBudget) == typeof(NoCheckpoints))
        {
            var found = text[from..].IndexOf(Quote);
            return found < 0 ? -1 : from + found;
        }

        return IndexOfQuoteWindowed(text, from, ref budget);
    }

    private static int IndexOfQuoteWindowed<TBudget>(ReadOnlySpan<char> text, int from, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        var position = from;
        while (position < text.Length)
        {
            var length = Math.Min(CancellationBudget.Quantum, text.Length - position);
            var relative = text.Slice(position, length).IndexOf(Quote);
            if (relative >= 0)
            {
                budget.Charge(relative + 1);
                return position + relative;
            }

            budget.Charge(length);
            position += length;
        }

        return -1;
    }

    private static int IndexOfLineBreak<TBudget>(ReadOnlySpan<char> text, int from, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        if (typeof(TBudget) == typeof(NoCheckpoints))
        {
            var found = text[from..].IndexOfAny('\r', '\n');
            return found < 0 ? -1 : from + found;
        }

        var position = from;
        while (position < text.Length)
        {
            var length = Math.Min(CancellationBudget.Quantum, text.Length - position);
            var relative = text.Slice(position, length).IndexOfAny('\r', '\n');
            if (relative >= 0)
            {
                budget.Charge(relative + 1);
                return position + relative;
            }

            budget.Charge(length);
            position += length;
        }

        return -1;
    }
}
