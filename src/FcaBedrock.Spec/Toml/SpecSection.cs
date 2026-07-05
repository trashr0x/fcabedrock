namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[spec]</c> section (§3). The three fingerprints are the
/// stored values §14 verification reads (D-051/D-069). <c>extends</c> is
/// consumed by <see cref="SpecComposer"/> (§13, D-027/D-078); a composed
/// document always carries it as null.
/// </summary>
/// <param name="Version">Spec format version; must be <c>1</c> to resolve (§2/§3).</param>
/// <param name="SchemaFingerprint">Recorded schema fingerprint (§9); carried inert.</param>
/// <param name="CxtOutputFingerprint">Recorded <c>.cxt</c> output fingerprint (§9); carried inert.</param>
/// <param name="DatOutputFingerprint">Recorded <c>.dat</c> output fingerprint (§9); carried inert.</param>
/// <param name="Extends">Reference to a base spec to compose with (§13); null when the spec stands alone or is composed.</param>
/// <param name="Description">Free-text description (§3).</param>
public sealed record SpecSection(
    long? Version,
    string? SchemaFingerprint,
    string? CxtOutputFingerprint,
    string? DatOutputFingerprint,
    string? Extends,
    string? Description);
