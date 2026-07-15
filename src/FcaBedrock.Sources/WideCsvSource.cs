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
    private readonly Binding _binding;
    private readonly char _delimiter;
    private readonly bool _hasHeader;
    private readonly string _missingToken;
    private SourceProvenance? _provenance;

    /// <summary>
    /// Constructs a direct production source over <paramref name="binding"/>; its
    /// <see cref="Provenance"/> is a <see cref="DescriptorProvenance"/> derived from the
    /// binding (D-098).
    /// </summary>
    public WideCsvSource(Func<Stream> openStream, Binding binding)
        : this(openStream, binding, provenance: null)
    {
    }

    // Session-bound source (D-098 stage 4): carries the resolution token so calibrate/
    // emit pair by reference identity. Same-assembly-only (WideCsvSession.Bind).
    internal WideCsvSource(Func<Stream> openStream, Binding binding, ResolvedSpec token)
        : this(openStream, binding, new TokenProvenance(token))
    {
    }

    private WideCsvSource(Func<Stream> openStream, Binding binding, SourceProvenance? provenance)
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

        // The source is object-key-agnostic: it always names records by row index, and the emitter
        // derives column-key names + duplicate policy from the plan (§5.4/§6.1, P-15) — including
        // dedupe, which now executes. Only a composite object key is a planner reject (Fatal), reached
        // because the pipeline builds the source before it plans.
        _openStream = openStream;
        _binding = binding;
        _delimiter = binding.Delimiter;
        _hasHeader = binding.HasHeader;
        _missingToken = binding.MissingToken;
        _provenance = provenance;
    }

    /// <inheritdoc/>
    // A bound source's token is fixed at construction; a direct source derives its
    // descriptor lazily from the binding (once) so a fake with an unusual binding never
    // trips validation unless the guard actually reads Provenance.
    public SourceProvenance Provenance =>
        _provenance ??= new DescriptorProvenance(
            SourceReadSettings.Create(
                _binding.Shape, _binding.Encoding, _binding.Delimiter, _binding.QuoteChar,
                _binding.HasHeader, _binding.MissingToken, _binding.Ordering),
            _binding.TripleColumns);

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
            // DisableColCountCheck lets a short/ragged row through (like TripleCsvSource) rather than
            // throwing across the Sources/Conversion seam: an absent mapped cell then surfaces as data
            // — an absent key column is ObjectKeyValueInvalid at emit, an absent attribute cell is
            // missing (§5.4/§16.4, D-085). This is narrow raggedness tolerance, not a parsing redesign.
            .Reader(o => o with { HasHeader = _hasHeader, Unescape = true, Trim = SepTrim.Outer, DisableColCountCheck = true })
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
