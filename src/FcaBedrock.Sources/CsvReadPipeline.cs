using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;

namespace FcaBedrock.Sources;

/// <summary>
/// The one delimited-source read path, shared by every CSV/TSV schema and record read: bound
/// sources (<see cref="WideCsvSource"/>, <see cref="TripleCsvSource"/>) and unbound sessions
/// (<see cref="WideCsvSession"/>, <see cref="TripleCsvSession"/>) alike. It reads by spec §5.1.1:
/// Sep splits candidate records (D-041), and this type validates and decodes every raw field of
/// every candidate before the record is exposed, skips blank records, consumes the header, numbers
/// the data records, applies the §5.1 missing normalization and maps the triple roles.
/// <para>
/// <b>Why one owner.</b> Schema and records must never disagree about what the header is or
/// where the data starts. A header is consumed in exactly one place, so schema-vs-record parity
/// is structural rather than a property several call sites must maintain (the D-102 posture).
/// </para>
/// <para>
/// <b>Header tolerance.</b> Sep is always opened in its <em>headerless</em> mode, and the header,
/// when the settings declare one, is the first non-blank record, decoded here by the field
/// grammar. Sep's own header mode throws <see cref="ArgumentException"/> on a duplicate or
/// multiply-blank header name, which would make §5.3/§10.2 unreachable: there such a header is
/// legal, binds by index, and yields <c>SourceBindingInvalid</c> only for an ambiguous
/// <em>name</em> binding.
/// </para>
/// <para>
/// <b>Each candidate.</b> A one-column candidate is tested for blankness (a candidate with a
/// delimiter is never blank). Then one quote search covers the whole candidate: a candidate with no
/// quote is valid as a whole, because a quote-free field is a valid unquoted field that holds no
/// delimiter, CR or LF, so its fields, unused ones included, need no further validation, and each
/// decoded value is the field without W at either end. A candidate with a quote is examined field
/// by field with the full grammar (<see cref="DelimitedFieldGrammar"/>). Wide reads decode every
/// field; triple reads decode the three roles and validate every other column without decoding it.
/// Line numbers are counted here, one per candidate plus the line breaks inside quoted fields; Sep's
/// own line numbers are not used.
/// </para>
/// <para>
/// <b>Cancellation.</b> The operation token is checked before the stream is acquired, after Sep's
/// initialization, before and after every advance (blank candidates included), inside owned work
/// on a long candidate (<see cref="CancellationBudget"/>), and before a schema or record is
/// exposed. The whole-span reader's checks bound how much a cancelled read still reads; these
/// checks keep a cancelled read from exposing anything, because Sep serves candidates it already
/// holds without reading. No raw Sep span or row is held across a yield or an await.
/// </para>
/// </summary>
internal static class CsvReadPipeline
{
    /// <summary>
    /// Reads the source schema: the ordered header names when the settings declare a header,
    /// otherwise the first non-blank record's column count. A source with no non-blank record has
    /// no header and no columns.
    /// </summary>
    public static async ValueTask<SourceSchema> ReadSchemaAsync(
        Func<Stream> openStream, char delimiter, bool hasHeader, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested(); // before acquisition

        var owner = DelimitedSourceReader.Open(openStream, delimiter, cancellationToken);
        try
        {
            var lines = new LineState();
            while (true)
            {
                if (!owner.MoveNext())
                {
                    return new SourceSchema(0);
                }

                var row = owner.Reader.Current;
                lines.StartCandidate();
                var count = row.ColCount;
                var span = row.Span;
                SourceSchema? schema;
                if (CancellationBudget.IsLong(span.Length, count))
                {
                    var budget = new CancellationBudget(owner.CancellationToken);
                    schema = SchemaCandidate(row, count, span, delimiter, hasHeader, ref lines, ref budget);
                }
                else
                {
                    var none = default(NoCheckpoints);
                    schema = SchemaCandidate(row, count, span, delimiter, hasHeader, ref lines, ref none);
                }

                if (schema is null)
                {
                    continue; // a blank candidate
                }

                owner.ThrowIfCancellationRequested(); // before the schema is exposed
                return schema;
            }
        }
        catch (Exception ex)
        {
            owner.Fail(ex);
            throw;
        }
        finally
        {
            owner.Close(); // a sole cleanup failure fails the schema read, so a session caches nothing
        }
    }

    /// <summary>
    /// Streams cleaned wide object records in input order, naming each by its 0-based data record
    /// index (invariant digits). A declared header is consumed, never yielded, and blank records
    /// are skipped; neither advances the index, so the first data record is always <c>0</c>.
    /// </summary>
    public static async IAsyncEnumerable<ObjectRecord> ReadRecordsAsync(
        Func<Stream> openStream, char delimiter, bool hasHeader, string missingToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested(); // before acquisition

        var owner = DelimitedSourceReader.Open(openStream, delimiter, cancellationToken);
        try
        {
            var lines = new LineState();
            var needHeader = hasHeader;
            var index = 0;
            while (true)
            {
                // The candidate loop runs inside a try that records the primary failure; the yield
                // stays outside it, because C# forbids a yield inside a try with a catch.
                ObjectRecord? record = null;
                try
                {
                    while (owner.MoveNext())
                    {
                        var row = owner.Reader.Current;
                        lines.StartCandidate();
                        var count = row.ColCount;
                        var span = row.Span;
                        string?[]? fields;
                        if (CancellationBudget.IsLong(span.Length, count))
                        {
                            var budget = new CancellationBudget(owner.CancellationToken);
                            fields = RecordCandidate(row, count, span, delimiter, ref needHeader, missingToken, index, ref lines, ref budget);
                        }
                        else
                        {
                            var none = default(NoCheckpoints);
                            fields = RecordCandidate(row, count, span, delimiter, ref needHeader, missingToken, index, ref lines, ref none);
                        }

                        if (fields is null)
                        {
                            continue; // a blank candidate, or the consumed header
                        }

                        owner.ThrowIfCancellationRequested(); // before the record is exposed
                        record = new ObjectRecord(index.ToString(CultureInfo.InvariantCulture), fields);
                        index++;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    owner.Fail(ex);
                    throw;
                }

                if (record is null)
                {
                    break;
                }

                yield return record;
            }
        }
        finally
        {
            owner.Close();
        }
    }

    /// <summary>
    /// Streams cleaned triple rows in input order, reading the three roles through
    /// <paramref name="columns"/> and numbering the rows by their 0-based data record index. A role
    /// mapped past a ragged short row's end is <see langword="null"/>: absent, not an error (§5.4,
    /// D-085).
    /// </summary>
    public static async IAsyncEnumerable<TripleRow> ReadTripleRowsAsync(
        Func<Stream> openStream, char delimiter, bool hasHeader, string missingToken,
        TripleColumns columns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested(); // before acquisition

        var owner = DelimitedSourceReader.Open(openStream, delimiter, cancellationToken);
        try
        {
            var lines = new LineState();
            var needHeader = hasHeader;
            var index = 0;
            while (true)
            {
                TripleRow? result = null;
                try
                {
                    while (owner.MoveNext())
                    {
                        var row = owner.Reader.Current;
                        lines.StartCandidate();
                        var count = row.ColCount;
                        var span = row.Span;
                        bool retained;
                        string? subject, predicate, value;
                        if (CancellationBudget.IsLong(span.Length, count))
                        {
                            var budget = new CancellationBudget(owner.CancellationToken);
                            retained = TripleCandidate(row, count, span, delimiter, ref needHeader, missingToken, columns, index, ref lines, ref budget, out subject, out predicate, out value);
                        }
                        else
                        {
                            var none = default(NoCheckpoints);
                            retained = TripleCandidate(row, count, span, delimiter, ref needHeader, missingToken, columns, index, ref lines, ref none, out subject, out predicate, out value);
                        }

                        if (!retained)
                        {
                            continue; // a blank candidate, or the consumed header
                        }

                        owner.ThrowIfCancellationRequested(); // before the row is exposed
                        result = new TripleRow(index, subject, predicate, value);
                        index++;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    owner.Fail(ex);
                    throw;
                }

                if (result is not { } emitted)
                {
                    break;
                }

                yield return emitted;
            }
        }
        finally
        {
            owner.Close();
        }
    }

    /// <summary>
    /// One candidate of a schema read: null for a blank candidate, else the header (decoded, never
    /// missing-normalized) or, headerless, the candidate's width after validating it.
    /// </summary>
    internal static SourceSchema? SchemaCandidate<TBudget>(
        SepReader.Row row, int count, ReadOnlySpan<char> span, char delimiter, bool hasHeader,
        ref LineState lines, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        if (count == 1 && DelimitedFieldGrammar.IsBlankCandidate(row[0].Span, delimiter, ref budget))
        {
            return null;
        }

        var quoteFree = DelimitedFieldGrammar.IsQuoteFree(span, ref budget);
        if (!hasHeader)
        {
            if (!quoteFree)
            {
                ValidateRow(row, count, delimiter, 0, ref lines, ref budget);
            }

            return new SourceSchema(count);
        }

        var cells = new string[count];
        for (var i = 0; i < cells.Length; i++)
        {
            var raw = row[i].Span;
            int quote;
            if (quoteFree || (quote = DelimitedFieldGrammar.FirstQuote(raw, ref budget)) < 0)
            {
                cells[i] = DelimitedFieldGrammar.MaterializeUnquoted(raw, delimiter, ref budget);
            }
            else
            {
                cells[i] = DelimitedFieldGrammar.DecodeField(raw, quote, delimiter, ref budget, out var fault, out var breaks)
                    ?? throw Fault(row, delimiter, null, i, fault, ref lines, ref budget);
                lines.AddBreakCount(breaks);
            }
        }

        // Immutable storage, explicitly: a session hands this same snapshot to every caller, so no
        // castable mutable array may survive into it (D-098).
        return new SourceSchema(cells.Length, cells.ToImmutableArray());
    }

    /// <summary>
    /// One candidate of a wide record read: null for a blank candidate or the consumed header, else
    /// every field decoded and missing-normalized.
    /// </summary>
    internal static string?[]? RecordCandidate<TBudget>(
        SepReader.Row row, int count, ReadOnlySpan<char> span, char delimiter, ref bool needHeader, string missingToken,
        int index, ref LineState lines, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        if (count == 1 && DelimitedFieldGrammar.IsBlankCandidate(row[0].Span, delimiter, ref budget))
        {
            return null;
        }

        var quoteFree = DelimitedFieldGrammar.IsQuoteFree(span, ref budget);
        if (needHeader)
        {
            needHeader = false;
            if (!quoteFree)
            {
                ValidateRow(row, count, delimiter, null, ref lines, ref budget);
            }

            return null;
        }

        var fields = new string?[count];
        for (var i = 0; i < count; i++)
        {
            var raw = row[i].Span;
            string value;
            int quote;
            if (quoteFree || (quote = DelimitedFieldGrammar.FirstQuote(raw, ref budget)) < 0)
            {
                value = DelimitedFieldGrammar.MaterializeUnquoted(raw, delimiter, ref budget);
            }
            else
            {
                value = DelimitedFieldGrammar.DecodeField(raw, quote, delimiter, ref budget, out var fault, out var breaks)
                    ?? throw Fault(row, delimiter, index, i, fault, ref lines, ref budget);
                lines.AddBreakCount(breaks);
            }

            fields[i] = Normalize(value, missingToken);
        }

        return fields;
    }

    /// <summary>
    /// One candidate of a triple read: false for a blank candidate or the consumed header, else the
    /// three roles decoded and missing-normalized, with every other column validated and none of
    /// them decoded.
    /// </summary>
    internal static bool TripleCandidate<TBudget>(
        SepReader.Row row, int count, ReadOnlySpan<char> span, char delimiter, ref bool needHeader, string missingToken,
        TripleColumns columns, int index, ref LineState lines, ref TBudget budget,
        out string? subject, out string? predicate, out string? value)
        where TBudget : struct, IWorkBudget
    {
        subject = null;
        predicate = null;
        value = null;
        if (count == 1 && DelimitedFieldGrammar.IsBlankCandidate(row[0].Span, delimiter, ref budget))
        {
            return false;
        }

        var quoteFree = DelimitedFieldGrammar.IsQuoteFree(span, ref budget);
        if (needHeader)
        {
            needHeader = false;
            if (!quoteFree)
            {
                ValidateRow(row, count, delimiter, null, ref lines, ref budget);
            }

            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (i != columns.Subject && i != columns.Predicate && i != columns.Value)
            {
                // An unused column is validated, never decoded. In a quote-free candidate the
                // candidate-wide search has already established that it is a valid unquoted field,
                // so nothing in it is examined again; passing over it is still owned work, charged
                // one unit, so a long run of unused columns is cancellable like any other owned work.
                if (quoteFree)
                {
                    budget.Charge(1);
                    continue;
                }

                var unused = row[i].Span;
                var check = DelimitedFieldGrammar.Check(unused, delimiter, ref budget);
                if (!check.IsValid)
                {
                    throw Fault(row, delimiter, index, i, check, ref lines, ref budget);
                }

                lines.AddBreaks(unused, check, ref budget);
                continue;
            }

            var raw = row[i].Span;
            string decoded;
            int quote;
            if (quoteFree || (quote = DelimitedFieldGrammar.FirstQuote(raw, ref budget)) < 0)
            {
                decoded = DelimitedFieldGrammar.MaterializeUnquoted(raw, delimiter, ref budget);
            }
            else
            {
                decoded = DelimitedFieldGrammar.DecodeField(raw, quote, delimiter, ref budget, out var fault, out var breaks)
                    ?? throw Fault(row, delimiter, index, i, fault, ref lines, ref budget);
                lines.AddBreakCount(breaks);
            }

            var normalized = Normalize(decoded, missingToken);
            if (i == columns.Subject)
            {
                subject = normalized;
            }

            if (i == columns.Predicate)
            {
                predicate = normalized;
            }

            if (i == columns.Value)
            {
                value = normalized;
            }
        }

        return true;
    }

    // A candidate with a quote somewhere, validated field by field without decoding: a consumed
    // header, or the record that gives a headerless schema its width.
    private static void ValidateRow<TBudget>(
        SepReader.Row row, int count, char delimiter, int? index, ref LineState lines, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        for (var i = 0; i < count; i++)
        {
            var raw = row[i].Span;
            var check = DelimitedFieldGrammar.Check(raw, delimiter, ref budget);
            if (!check.IsValid)
            {
                throw Fault(row, delimiter, index, i, check, ref lines, ref budget);
            }

            lines.AddBreaks(raw, check, ref budget);
        }
    }

    // The read failure for a malformed field. The physical line of the defect is recomputed only
    // here, on the failure path; the rescan charges the same budget, so a cancellation observed
    // during it wins over the defect that has been found but not yet raised.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SourceReadException Fault<TBudget>(
        SepReader.Row row, char delimiter, int? index, int column, in FieldCheck check, ref LineState lines, ref TBudget budget)
        where TBudget : struct, IWorkBudget
    {
        long before = 0;
        for (var i = 0; i < column; i++)
        {
            var raw = row[i].Span;
            var earlier = DelimitedFieldGrammar.Check(raw, delimiter, ref budget);
            before += earlier.Quoted ? DelimitedFieldGrammar.CountLineBreaks(raw, ref budget) : 0;
        }

        var faultLine = lines.Start + before + DelimitedFieldGrammar.CountLineBreaks(row[column].Span[..check.FaultOffset], ref budget);
        return new SourceReadException(DelimitedFieldGrammar.FaultMessage(check.Fault, index, lines.Start, column, faultLine));
    }

    // Missing detection over the decoded value (§5.1): an empty value, or one equal to the verbatim
    // missing_token. A quoted value with deliberate whitespace is present unless it equals the token
    // exactly. An empty missing_token disables token matching only; an empty value stays missing.
    private static string? Normalize(string value, string missingToken) =>
        value.Length == 0 || string.Equals(value, missingToken, StringComparison.Ordinal)
            ? null
            : value;

    /// <summary>
    /// The physical-line counter of one read: one line per candidate, blank ones included, plus the
    /// line breaks inside quoted fields. <see cref="Start"/> is the current candidate's first line.
    /// </summary>
    internal struct LineState
    {
        private long _next;

        public long Start { get; private set; }

        public void StartCandidate()
        {
            if (_next == 0)
            {
                _next = 1;
            }

            Start = _next;
            _next++;
        }

        public void AddBreakCount(int breaks) => _next += breaks;

        public void AddBreaks<TBudget>(ReadOnlySpan<char> raw, in FieldCheck check, ref TBudget budget)
            where TBudget : struct, IWorkBudget
        {
            if (check.Quoted)
            {
                _next += DelimitedFieldGrammar.CountLineBreaks(raw, ref budget);
            }
        }
    }
}
