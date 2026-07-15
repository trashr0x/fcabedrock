using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources;

/// <summary>
/// The stage-2 wide source session (D-098/G-1): constructible from
/// <see cref="SourceReadSettings"/> alone — no resolved binding, no attribute
/// indexes — and able to read the schema so the resolver can bind by header name.
/// <para>
/// <b>Lifecycle.</b> The first successful <see cref="GetSchemaAsync"/> caches an
/// immutable schema snapshot; subsequent calls return it without reopening the
/// stream. A canceled or failed read (including an <c>openStream</c> factory
/// exception) caches nothing and propagates, so the next call retries.
/// <see cref="Bind"/> before a successful schema read throws
/// <see cref="InvalidOperationException"/>; it may be called more than once, each
/// call re-validating and returning a fresh bound source over the same replayable
/// stream factory.
/// </para>
/// </summary>
public sealed class WideCsvSession
{
    private readonly Func<Stream> _openStream;
    private readonly SourceReadSettings _settings;
    private SourceSchema? _schema;

    /// <summary>
    /// Creates a session over <paramref name="openStream"/> with wide
    /// <paramref name="settings"/>. Throws <see cref="ArgumentException"/> when the
    /// settings are not wide, or <see cref="NotSupportedException"/> for a non-standard
    /// quote (the existing source-constructor postures).
    /// </summary>
    public WideCsvSession(Func<Stream> openStream, SourceReadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Shape != SourceShape.Wide)
        {
            throw new ArgumentException("WideCsvSession requires wide read settings.", nameof(settings));
        }

        if (settings.QuoteChar != '"')
        {
            throw new NotSupportedException("WideCsvSession supports only the '\"' quote character (RFC 4180).");
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
    /// Binds the session to <paramref name="resolved"/>, validating that its settings
    /// value-equal this session's and that its schema value-equals the cached schema
    /// this session read; returns the bound source carrying the resolution token.
    /// Throws <see cref="InvalidOperationException"/> before a successful schema read or
    /// on any mismatch (the D-082 call-contract posture).
    /// </summary>
    public WideCsvSource Bind(ResolvedSpec resolved)
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

        return new WideCsvSource(_openStream, resolved.Spec.Binding, resolved);
    }
}
