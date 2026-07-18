using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;

namespace FcaBedrock.Sources;

/// <summary>
/// The one delimited-source read path, shared by every CSV/TSV schema and record read —
/// bound sources (<see cref="WideCsvSource"/>, <see cref="TripleCsvSource"/>) and unbound
/// sessions (<see cref="WideCsvSession"/>, <see cref="TripleCsvSession"/>) alike. Sep owns
/// tokenization (D-041); this type layers the FCA semantics on top of it: header handling,
/// the §5.1 missing normalization, row-index object naming, and the triple role map.
/// <para>
/// <b>Why one owner.</b> Schema and records must never disagree about what the header is or
/// where the data starts. When those mechanics were copied across four call sites they could
/// drift; here a header is consumed in exactly one place, so schema-vs-record parity is
/// structural rather than a property four files maintain (the D-102 posture).
/// </para>
/// <para>
/// <b>Header tolerance (M5-IP-011).</b> Sep is always opened in its <em>headerless</em> mode
/// and the header, when the settings declare one, is consumed here as the first parsed
/// record. Sep's own header mode throws <see cref="ArgumentException"/> on a duplicate or
/// multiply-blank header name, which made §5.3/§10.2 — where such a header is legal, binds by
/// index, and yields <c>SourceBindingInvalid</c> only for an ambiguous <em>name</em> binding —
/// unreachable. Consuming the header ourselves realizes that already-normative behavior. It is
/// byte-neutral for every header Sep accepts today: its header cells and the same physical row
/// read as a record are identical after trim/unescape (verified across whitespace, quoting,
/// escaping, embedded delimiters/newlines, CRLF, blank cells, and Unicode).
/// </para>
/// </summary>
internal static class CsvReadPipeline
{
    /// <summary>
    /// Reads the source schema: the ordered header names when the settings declare a header,
    /// otherwise the first data record's column count.
    /// </summary>
    public static async ValueTask<SourceSchema> ReadSchemaAsync(
        Func<Stream> openStream, char delimiter, bool hasHeader, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = Open(openStream, delimiter);
        if (ConsumeHeader(reader, hasHeader) is { } header)
        {
            // Immutable storage, explicitly: a session hands this same snapshot to every caller,
            // so no castable mutable array may survive into it (D-098).
            return new SourceSchema(header.Length, header.ToImmutableArray());
        }

        // No header — either because none was declared, or because the source holds no record
        // at all. The latter keeps a *header-less* schema rather than gaining an empty header,
        // matching Sep, which likewise reports no header for empty input.
        return Advance(reader) ? new SourceSchema(ColumnCount(reader)) : new SourceSchema(0);
    }

    /// <summary>
    /// Streams cleaned wide object records in input order, naming each by its 0-based data-row
    /// index (invariant digits). A declared header is consumed as schema, never yielded, and
    /// does not advance the index: the first data record is always <c>0</c>.
    /// </summary>
    public static async IAsyncEnumerable<ObjectRecord> ReadRecordsAsync(
        Func<Stream> openStream, char delimiter, bool hasHeader, string missingToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = Open(openStream, delimiter);
        ConsumeHeader(reader, hasHeader);

        var rowIndex = 0;
        while (true)
        {
            // Checked BEFORE each advance, not after: an already-canceled read of an empty or
            // header-only source must still cancel rather than complete as an empty success, and
            // cancellation must win over a read failure on the row that would have been next.
            cancellationToken.ThrowIfCancellationRequested();
            if (!Advance(reader))
            {
                break;
            }

            // The fields are extracted by a helper because Sep's row is a ref struct: it must
            // not be a local in this iterator, whose locals outlive the yield.
            yield return new ObjectRecord(
                rowIndex.ToString(CultureInfo.InvariantCulture), ReadFields(reader, missingToken));
            rowIndex++;
        }
    }

    /// <summary>
    /// Streams cleaned triple rows in input order, reading the three roles through
    /// <paramref name="columns"/>. A role mapped past a ragged short row's end is
    /// <see langword="null"/> — absent, not an error (§5.4, D-085).
    /// </summary>
    public static async IAsyncEnumerable<TripleRow> ReadTripleRowsAsync(
        Func<Stream> openStream, char delimiter, bool hasHeader, string missingToken,
        TripleColumns columns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = Open(openStream, delimiter);
        ConsumeHeader(reader, hasHeader);

        var recordIndex = 0;
        while (true)
        {
            // Same ordering as the wide iterator, for the same reasons.
            cancellationToken.ThrowIfCancellationRequested();
            if (!Advance(reader))
            {
                break;
            }

            yield return ReadTripleRow(reader, recordIndex, missingToken, columns);
            recordIndex++;
        }
    }

    // Sep is opened headerless unconditionally (see the type remarks); the remaining tokenizer
    // options are exactly the pinned ones. Trim = Outer trims an UNQUOTED field's surrounding
    // whitespace before unescape while preserving whitespace INSIDE a quoted field — spec §5.1.
    // DisableColCountCheck lets a short/ragged row through rather than throwing across the
    // Sources seam, so an absent mapped cell surfaces as data the Conversion layer diagnoses.
    private static SepReader Open(Func<Stream> openStream, char delimiter)
    {
        var stream = openStream();
        try
        {
            return Sep.New(delimiter)
                .Reader(o => o with
                {
                    HasHeader = false, Unescape = true, Trim = SepTrim.Outer, DisableColCountCheck = true,
                })
                .From(stream);
        }
        catch (NotSupportedException ex) when (IsTokenizerFailure(ex))
        {
            // Only the path this method introduces is handled here; every other exception keeps
            // its existing identity and disposal behavior (P-1).
            stream.Dispose();
            throw new SourceReadException("The source could not be opened for reading.", ex);
        }
    }

    // Sep signals its row/buffer ceiling ("Buffer or row has reached maximum supported length of
    // 16777216", also raised for an unterminated quote) as a NotSupportedException. That is an
    // expected provider read failure, normalized HERE so no consumer needs to know Sep exists
    // (M5-IP-008). Nothing else is caught: cancellation, argument/state errors, and every other
    // framework exception propagate as themselves (P-14).
    private static bool Advance(SepReader reader)
    {
        try
        {
            return reader.MoveNext();
        }
        catch (NotSupportedException ex) when (IsTokenizerFailure(ex))
        {
            throw new SourceReadException(
                "The source could not be read: a row exceeded the maximum supported length "
                + "(an unterminated quote will also produce this).", ex);
        }
    }

    // "Did the TOKENIZER refuse, or did the stream we were handed misbehave?" Being inside a Sep
    // call is not enough to answer that: Sep reads through the caller's stream, so a stream whose
    // Read throws NotSupportedException (a non-readable stream, say) surfaces through the very
    // same call. Wrapping that would disguise a programmer/contract error as an expected read
    // failure — exactly what M5-IP-008 forbids — and would later mistranslate into
    // ProbeSourceReadFailed. So normalize only failures thrown from within Sep itself.
    //
    // Verified against pinned Sep 0.15.0: the limit failure's TargetSite is
    // SepThrow.NotSupportedException_BufferOrRowLengthExceedsMaximumSupported (assembly "Sep"),
    // while a throwing stream's TargetSite is its own Read. Origin is preferred over matching
    // Sep's message text or throw-helper name, which would couple us to its internals; and an
    // unrecognized failure fails OPEN — it propagates unwrapped rather than being absorbed.
    private static bool IsTokenizerFailure(Exception ex) =>
        ex.TargetSite?.DeclaringType?.Assembly == typeof(Sep).Assembly;

    // Consumes the header record exactly once when one is declared, returning its cells
    // VERBATIM after Sep's trim/unescape. Header cells are never missing-normalized: a header
    // that happens to equal missing_token (e.g. "?") is that literal name, and binds by name
    // when unique (§10.2). Returns null when no header is declared, or when the source holds
    // no record to take one from.
    private static string[]? ConsumeHeader(SepReader reader, bool hasHeader)
    {
        if (!hasHeader || !Advance(reader))
        {
            return null;
        }

        var row = reader.Current;
        var cells = new string[row.ColCount];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = row[i].ToString();
        }

        return cells;
    }

    private static int ColumnCount(SepReader reader) => reader.Current.ColCount;

    private static string?[] ReadFields(SepReader reader, string missingToken)
    {
        var row = reader.Current;
        var fields = new string?[row.ColCount];
        for (var i = 0; i < fields.Length; i++)
        {
            fields[i] = Normalize(row[i].ToString(), missingToken);
        }

        return fields;
    }

    private static TripleRow ReadTripleRow(
        SepReader reader, int recordIndex, string missingToken, TripleColumns columns)
    {
        var row = reader.Current;
        var count = row.ColCount;
        var subject = columns.Subject < count ? Normalize(row[columns.Subject].ToString(), missingToken) : null;
        var predicate = columns.Predicate < count ? Normalize(row[columns.Predicate].ToString(), missingToken) : null;
        var value = columns.Value < count ? Normalize(row[columns.Value].ToString(), missingToken) : null;
        return new TripleRow(recordIndex, subject, predicate, value);
    }

    // The value is already quote-aware-trimmed by Sep (§5.1), so missing detection is a direct
    // comparison: an empty value (unquoted blank or quoted "") or one equal to the verbatim
    // missing_token. A quoted value with deliberate interior whitespace is preserved and is not
    // missing unless it equals the token exactly. An empty missing_token disables token matching
    // only — an empty cell stays missing.
    private static string? Normalize(string value, string missingToken) =>
        value.Length == 0 || string.Equals(value, missingToken, StringComparison.Ordinal)
            ? null
            : value;
}
