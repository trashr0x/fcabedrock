namespace FcaBedrock.Core.Spec;

/// <summary>
/// What a source can prove about its preparation for the resolve → bind →
/// calibrate → emit chain (D-098/G-1). A three-state, mechanically-closed union
/// (the <see langword="private protected"/> base constructor admits no
/// out-of-assembly variant): a bound source carries a <see cref="TokenProvenance"/>
/// (full reference-identity pairing); a direct-constructed production source
/// carries a <see cref="DescriptorProvenance"/> derived from its
/// <see cref="Binding"/> (settings + triple roles); and a deliberately
/// descriptor-less adapter or test fake carries the explicit
/// <see cref="Unvalidated"/> opt-out — weaker validation is <em>named</em>, never
/// silent. Calibrate/emit guards switch exhaustively and throw on an unknown
/// variant rather than degrading to the unvalidated path.
/// </summary>
public abstract record SourceProvenance
{
    private protected SourceProvenance()
    {
    }

    /// <summary>
    /// The named opt-out: the source proves nothing about its settings/roles, so a
    /// calibrate/emit guard falls back to a schema-value check only. Used by
    /// descriptor-less adapters and test fakes.
    /// </summary>
    public static SourceProvenance Unvalidated { get; } = new UnvalidatedProvenance();

    private sealed record UnvalidatedProvenance : SourceProvenance;
}

/// <summary>
/// A bound source carries the exact <see cref="ResolvedSpec"/> token it was bound
/// against; downstream pairing is by reference identity of that instance.
/// </summary>
public sealed record TokenProvenance : SourceProvenance
{
    /// <summary>Records the bound resolution token (required).</summary>
    public TokenProvenance(ResolvedSpec resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        Resolution = resolution;
    }

    /// <summary>The resolution the source was bound against.</summary>
    public ResolvedSpec Resolution { get; }
}

/// <summary>
/// A direct-constructed production source carries the semantic settings it was
/// built from (D-098): a calibrate/emit guard checks value equality against the
/// resolution's settings, role map, and schema.
/// </summary>
public sealed record DescriptorProvenance : SourceProvenance
{
    /// <summary>
    /// Records the source's <paramref name="settings"/> and, for a triple source,
    /// its resolved role map <paramref name="roles"/>. Throws
    /// <see cref="ArgumentException"/> unless <paramref name="roles"/> is non-null
    /// exactly when the settings shape is triple.
    /// </summary>
    public DescriptorProvenance(SourceReadSettings settings, TripleColumns? roles)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if ((settings.Shape == SourceShape.Triple) != (roles is not null))
        {
            throw new ArgumentException(
                "roles must be non-null exactly when the settings shape is triple.", nameof(roles));
        }

        Settings = settings;
        Roles = roles;
    }

    /// <summary>The source's resolved read settings.</summary>
    public SourceReadSettings Settings { get; }

    /// <summary>The source's resolved triple role map; null for a wide source.</summary>
    public TripleColumns? Roles { get; }
}
