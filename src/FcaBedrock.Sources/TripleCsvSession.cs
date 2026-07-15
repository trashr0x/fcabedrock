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
/// </summary>
public sealed class TripleCsvSession
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

    /// <summary>Reads (and caches) the source schema — column count and, when present, header names.</summary>
    public async ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_schema is { } cached)
        {
            return cached;
        }

        var schema = await CsvSourceSchema.ReadAsync(
            _openStream, _settings.Delimiter, _settings.HasHeader, cancellationToken).ConfigureAwait(false);
        _schema = schema;
        return schema;
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
