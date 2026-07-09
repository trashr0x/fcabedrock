using System.Runtime.CompilerServices;
using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;

namespace FcaBedrock.Sources;

/// <summary>
/// A triple (subject–predicate–value) CSV/TSV <see cref="ITripleRowSource"/>. Sibling to
/// <see cref="WideCsvSource"/>: Sep owns DSV tokenization (D-041); this type layers the
/// declared delimiter, header handling, the resolved role→column map, and missing
/// normalization. It is deliberately dumb — no grouping, no subject/contiguity validation,
/// no object-key semantics (D-082); those belong to the Conversion layer. Constructed from a
/// replayable stream factory so it can be re-read for the <c>.cxt</c> two-pass and the
/// Slice D grouping/spool without temp files.
/// </summary>
public sealed class TripleCsvSource : ITripleRowSource
{
    private readonly Func<Stream> _openStream;
    private readonly char _delimiter;
    private readonly bool _hasHeader;
    private readonly string _missingToken;
    private readonly TripleColumns _columns;

    public TripleCsvSource(Func<Stream> openStream, Binding binding)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Shape != SourceShape.Triple)
        {
            throw new ArgumentException("TripleCsvSource requires a triple binding.", nameof(binding));
        }

        if (binding.QuoteChar != '"')
        {
            throw new NotSupportedException("TripleCsvSource supports only the '\"' quote character (RFC 4180).");
        }

        _columns = binding.TripleColumns
            ?? throw new ArgumentException("A triple binding must carry a resolved role→column map.", nameof(binding));

        _openStream = openStream;
        _delimiter = binding.Delimiter;
        _hasHeader = binding.HasHeader;
        _missingToken = binding.MissingToken;
    }

    public async ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = OpenReader();
        if (reader.HasHeader)
        {
            var names = reader.Header.ColNames;
            return new SourceSchema(names.Count, [.. names]);
        }

        foreach (var row in reader)
        {
            return new SourceSchema(row.ColCount);
        }

        return new SourceSchema(0);
    }

    public async IAsyncEnumerable<TripleRow> ReadRowsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        using var reader = OpenReader();
        var recordIndex = 0;
        foreach (var row in reader)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Extract the three role fields into strings before yielding: Sep's row is a ref
            // struct and must not be captured across the yield (mirrors WideCsvSource).
            var count = row.ColCount;
            var subject = _columns.Subject < count ? Normalize(row[_columns.Subject].ToString()) : null;
            var predicate = _columns.Predicate < count ? Normalize(row[_columns.Predicate].ToString()) : null;
            var value = _columns.Value < count ? Normalize(row[_columns.Value].ToString()) : null;

            yield return new TripleRow(recordIndex, subject, predicate, value);
            recordIndex++;
        }
    }

    private SepReader OpenReader() =>
        Sep.New(_delimiter)
            // SepTrim.Outer trims an UNQUOTED field's surrounding whitespace before unescape while
            // preserving whitespace INSIDE a quoted field — spec §5.1, applied uniformly to all
            // three roles. DisableColCountCheck lets a short/ragged row through so an absent role
            // degrades to null (a data problem the Conversion layer diagnoses — D-082), rather than
            // Sep throwing across the seam. Sep still owns tokenization/unescape (the D-041 contract).
            .Reader(o => o with { HasHeader = _hasHeader, Unescape = true, Trim = SepTrim.Outer, DisableColCountCheck = true })
            .From(_openStream());

    // Already quote-aware-trimmed by Sep (§5.1): an empty cell (unquoted blank or quoted "") or
    // one equal to the verbatim missing_token normalizes to null, uniformly across subject,
    // predicate, and value (D-082). A short row's absent role is handled by the caller (→ null),
    // so this is a value-shaped normalization only. Quoted interior whitespace is preserved and
    // is not missing unless it equals the token exactly.
    private string? Normalize(string value) =>
        value.Length == 0 || string.Equals(value, _missingToken, StringComparison.Ordinal)
            ? null
            : value;
}
