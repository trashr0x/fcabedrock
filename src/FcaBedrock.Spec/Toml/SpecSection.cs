namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[spec]</c> section (§3). <c>extends</c> joins at the
/// extends slice (D-066/Slice F). The three fingerprints are carried inert
/// until the fingerprint slice (D-051/D-069).
/// </summary>
/// <param name="Version">Spec format version; must be <c>1</c> to resolve (§2/§3).</param>
/// <param name="SchemaFingerprint">Recorded schema fingerprint (§9); carried inert.</param>
/// <param name="CxtOutputFingerprint">Recorded <c>.cxt</c> output fingerprint (§9); carried inert.</param>
/// <param name="DatOutputFingerprint">Recorded <c>.dat</c> output fingerprint (§9); carried inert.</param>
/// <param name="Description">Free-text description (§3).</param>
public sealed record SpecSection(
    long? Version,
    string? SchemaFingerprint,
    string? CxtOutputFingerprint,
    string? DatOutputFingerprint,
    string? Description);
