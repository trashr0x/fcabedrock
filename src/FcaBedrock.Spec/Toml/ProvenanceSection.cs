namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[provenance]</c> section (§4). Documentation-only: carried
/// inert through resolution — nothing in Core reads it.
/// </summary>
/// <param name="Author">Who authored the spec.</param>
/// <param name="CreatedAt">When the spec was created.</param>
/// <param name="SourceUrl">Where the source data came from.</param>
/// <param name="SourceHash">A recorded hash of the source data.</param>
/// <param name="DerivedFrom">What the spec was derived from (e.g. a migrated <c>.bed</c>).</param>
/// <param name="Notes">Free-text notes.</param>
public sealed record ProvenanceSection(
    string? Author,
    DateTimeOffset? CreatedAt,
    string? SourceUrl,
    string? SourceHash,
    string? DerivedFrom,
    string? Notes);
