using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources;

/// <summary>
/// A triple (subject–predicate–value) CSV/TSV <see cref="ITripleRowSource"/>. Sibling to
/// <see cref="WideCsvSource"/>: Sep owns DSV tokenization (D-041); this type layers the
/// declared delimiter, header handling, the resolved role→column map, and missing
/// normalization. It is deliberately dumb — no grouping, no subject/contiguity validation,
/// no object-key semantics (D-082); those belong to the Conversion layer. Constructed from a
/// replayable stream factory so it can be re-read for the <c>.cxt</c> two-pass and the
/// <c>unordered</c> first-appearance grouping (D-082) without temp files.
/// </summary>
public sealed class TripleCsvSource : ITripleRowSource
{
    private readonly Func<Stream> _openStream;
    private readonly Binding _binding;
    private readonly char _delimiter;
    private readonly bool _hasHeader;
    private readonly string _missingToken;
    private readonly TripleColumns _columns;
    private SourceProvenance? _provenance;

    /// <summary>
    /// Constructs a direct production source over <paramref name="binding"/>; its
    /// <see cref="Provenance"/> is a <see cref="DescriptorProvenance"/> derived from the
    /// binding (settings + role map, D-098).
    /// </summary>
    public TripleCsvSource(Func<Stream> openStream, Binding binding)
        : this(openStream, binding, provenance: null)
    {
    }

    // Session-bound source (D-098 stage 4): carries the resolution token. Same-assembly-only.
    internal TripleCsvSource(Func<Stream> openStream, Binding binding, ResolvedSpec token)
        : this(openStream, binding, new TokenProvenance(token))
    {
    }

    private TripleCsvSource(Func<Stream> openStream, Binding binding, SourceProvenance? provenance)
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
        _binding = binding;
        _delimiter = binding.Delimiter;
        _hasHeader = binding.HasHeader;
        _missingToken = binding.MissingToken;
        _provenance = provenance;
    }

    /// <inheritdoc/>
    public SourceProvenance Provenance =>
        _provenance ??= new DescriptorProvenance(
            SourceReadSettings.Create(
                _binding.Shape, _binding.Encoding, _binding.Delimiter, _binding.QuoteChar,
                _binding.HasHeader, _binding.MissingToken, _binding.Ordering),
            _binding.TripleColumns);

    public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        CsvReadPipeline.ReadSchemaAsync(_openStream, _delimiter, _hasHeader, cancellationToken);

    public IAsyncEnumerable<TripleRow> ReadRowsAsync(CancellationToken cancellationToken = default) =>
        CsvReadPipeline.ReadTripleRowsAsync(
            _openStream, _delimiter, _hasHeader, _missingToken, _columns, cancellationToken);
}
