using System.Globalization;
using System.Runtime.CompilerServices;
using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;

namespace FcaBedrock.Sources;

/// <summary>
/// A wide-CSV/TSV <see cref="IRecordSource"/>. DSV tokenization is delegated to
/// Sep (decisions.md D-041); this type layers the FCA semantics: the declared
/// delimiter, header handling, missing detection, and row-index object naming.
/// Constructed from a replayable stream factory so it can be re-read for the
/// <c>.cxt</c> two-pass and tested without temp files.
/// </summary>
public sealed class WideCsvSource : IRecordSource
{
    private readonly Func<Stream> _openStream;
    private readonly char _delimiter;
    private readonly bool _hasHeader;
    private readonly string _missingToken;

    public WideCsvSource(Func<Stream> openStream, Binding binding)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Shape != SourceShape.Wide)
        {
            throw new ArgumentException("WideCsvSource requires a wide binding.", nameof(binding));
        }

        if (binding.QuoteChar != '"')
        {
            throw new NotSupportedException("WideCsvSource supports only the '\"' quote character (RFC 4180).");
        }

        if (binding.ObjectKey is not RowIndexObjectKey)
        {
            throw new NotSupportedException("WideCsvSource supports only row-index object keys in this slice.");
        }

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

    public async IAsyncEnumerable<ObjectRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        using var reader = OpenReader();
        var rowIndex = 0;
        foreach (var row in reader)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fields = new string?[row.ColCount];
            for (var i = 0; i < row.ColCount; i++)
            {
                fields[i] = Normalize(row[i].ToString());
            }

            yield return new ObjectRecord(rowIndex.ToString(CultureInfo.InvariantCulture), fields);
            rowIndex++;
        }
    }

    private SepReader OpenReader() =>
        Sep.New(_delimiter)
            // Trim = Outer trims an UNQUOTED field's surrounding whitespace before unescape, while
            // preserving whitespace INSIDE a quoted field — exactly spec §5.1. Sep still owns
            // tokenization/unescape, so the D-041 integration contract is unchanged.
            .Reader(o => o with { HasHeader = _hasHeader, Unescape = true, Trim = SepTrim.Outer })
            .From(_openStream());

    // The value is already quote-aware-trimmed by Sep (§5.1), so missing detection is a direct
    // comparison: an empty value (unquoted blank or quoted "") or one equal to the verbatim
    // missing_token. A quoted value with deliberate interior whitespace is preserved and is not
    // missing unless it equals the token exactly.
    private string? Normalize(string value) =>
        value.Length == 0 || string.Equals(value, _missingToken, StringComparison.Ordinal)
            ? null
            : value;
}
