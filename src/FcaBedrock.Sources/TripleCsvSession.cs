using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources;

/// <summary>
/// The stage-2 triple source session (D-098/G-1) — the triple twin of
/// <see cref="WideCsvSession"/>. Constructible from triple
/// <see cref="SourceReadSettings"/> alone, reads the schema so the resolver can
/// bind roles by header name, and binds to a resolution carrying the token. The
/// bound source pulls its resolved role→column map from
/// <c>resolved.Spec.Binding.TripleColumns</c>. Same caching/retry/bind lifecycle
/// as <see cref="WideCsvSession"/>.
/// <para>
/// <b>Unbound reads.</b> As an <see cref="ITripleSourceSession"/> the session also streams
/// rows without a spec (D-109), taking the role map <em>per read</em>. Roles therefore never
/// enter session identity: reading under one map neither constrains nor is constrained by a
/// later <see cref="Bind"/> under another, which keeps the D-098 bound flow — where a session
/// is constructed before its authored role map is even resolvable — exactly as it was.
/// </para>
/// </summary>
public sealed class TripleCsvSession : ITripleSourceSession
{
    private readonly Func<Stream> _openStream;
    private readonly SourceReadSettings _settings;
    private SourceSchema? _schema;

    /// <summary>
    /// Creates a session over <paramref name="openStream"/> with triple
    /// <paramref name="settings"/>. Throws <see cref="ArgumentException"/> when the
    /// settings are not triple, or <see cref="NotSupportedException"/> for a
    /// non-standard quote.
    /// </summary>
    public TripleCsvSession(Func<Stream> openStream, SourceReadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Shape != SourceShape.Triple)
        {
            throw new ArgumentException("TripleCsvSession requires triple read settings.", nameof(settings));
        }

        if (settings.QuoteChar != '"')
        {
            throw new NotSupportedException("TripleCsvSession supports only the '\"' quote character (RFC 4180).");
        }

        _openStream = openStream;
        _settings = settings;
    }

    /// <inheritdoc/>
    public SourceShape Shape => _settings.Shape;

    /// <summary>
    /// The immutable read settings this session tokenizes with — delimited-source detail, kept
    /// off the source-neutral seam.
    /// </summary>
    public SourceReadSettings ReadSettings => _settings;

    /// <summary>Reads (and caches) the source schema — column count and, when present, header names.</summary>
    public async ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_schema is { } cached)
        {
            return cached;
        }

        var schema = await CsvReadPipeline.ReadSchemaAsync(
            _openStream, _settings.Delimiter, _settings.HasHeader, cancellationToken).ConfigureAwait(false);
        _schema = schema;
        return schema;
    }

    /// <summary>
    /// Streams the cleaned triple rows without any spec (D-109), reading the three roles
    /// through <paramref name="columns"/> — resolved indexes into the ordered schema, supplied
    /// per read. Independent of <see cref="GetSchemaAsync"/> and <see cref="Bind"/>, caches
    /// nothing, and reopens the stream factory on each call, so the sequence is replayable.
    /// <para>
    /// This session neither resolves role <em>names</em> nor defaults the map: a caller passes
    /// an already-resolved one (the settled default is <c>TripleColumns(0, 1, 2)</c>, passed
    /// explicitly). Throws <see cref="ArgumentNullException"/> for a null map and
    /// <see cref="ArgumentOutOfRangeException"/> for a negative role — programmer errors, not
    /// data problems. Role <em>distinctness</em> is deliberately not checked here: it is a
    /// spec-validate concern (<c>TripleColumnsNotDistinct</c>) with a single owner, and reading
    /// a repeated role is mechanically well-defined.
    /// </para>
    /// </summary>
    public IAsyncEnumerable<TripleRow> ReadRowsAsync(
        TripleColumns columns, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentOutOfRangeException.ThrowIfNegative(columns.Subject, nameof(columns));
        ArgumentOutOfRangeException.ThrowIfNegative(columns.Predicate, nameof(columns));
        ArgumentOutOfRangeException.ThrowIfNegative(columns.Value, nameof(columns));

        return CsvReadPipeline.ReadTripleRowsAsync(
            _openStream, _settings.Delimiter, _settings.HasHeader, _settings.MissingToken,
            columns, cancellationToken);
    }

    /// <summary>
    /// Binds the session to <paramref name="resolved"/> (settings + schema value
    /// equality), returning the bound source carrying the resolution token. Throws
    /// <see cref="InvalidOperationException"/> before a successful schema read or on any
    /// mismatch.
    /// </summary>
    public TripleCsvSource Bind(ResolvedSpec resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (_schema is not { } schema)
        {
            throw new InvalidOperationException("Bind requires a successful GetSchemaAsync first.");
        }

        if (!resolved.Settings.Equals(_settings))
        {
            throw new InvalidOperationException("The resolution's read settings do not match this session's.");
        }

        if (resolved.Schema is not { } resolvedSchema || !CsvSourceSchema.Equal(resolvedSchema, schema))
        {
            throw new InvalidOperationException("The resolution's schema does not match the schema this session read.");
        }

        return new TripleCsvSource(_openStream, resolved.Spec.Binding, resolved);
    }
}
