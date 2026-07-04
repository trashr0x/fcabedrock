namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The three fingerprints computed from a resolved plan and the spec's native
/// output settings (spec §14, D-051) — the values <see cref="SpecFingerprints.VerifyStored"/>
/// compares stored fields against, and the values M7 tooling writes into a
/// frozen spec.
/// </summary>
/// <param name="SchemaFingerprint">The computed <c>schema_fingerprint</c>.</param>
/// <param name="CxtOutputFingerprint">The computed native <c>cxt_output_fingerprint</c>.</param>
/// <param name="DatOutputFingerprint">The computed native <c>dat_output_fingerprint</c>.</param>
public sealed record ComputedFingerprints(
    string SchemaFingerprint,
    string CxtOutputFingerprint,
    string DatOutputFingerprint);
