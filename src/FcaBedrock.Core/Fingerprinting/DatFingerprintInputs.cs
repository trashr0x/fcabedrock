namespace FcaBedrock.Core.Fingerprinting;

/// <summary>
/// The <c>.dat</c>-only fingerprint inputs (spec §8/§14, D-051/D-069): the
/// <c>.dat</c> writer settings alone. Rendered names never enter — <c>.dat</c>
/// carries numeric ids, not names (D-051).
/// </summary>
/// <param name="BaseIndex">First formal-attribute id (§8/§17).</param>
/// <param name="LineEnding">The <c>.dat</c> line-ending convention (§8).</param>
/// <param name="NonemptyLineTrailingSpace">Whether non-empty lines end with a space (v2 quirk, §8).</param>
/// <param name="EmptyLineTrailingSpace">Whether empty lines carry a space (v2 quirk, §8).</param>
public sealed record DatFingerprintInputs(
    int BaseIndex,
    LineEnding LineEnding,
    bool NonemptyLineTrailingSpace,
    bool EmptyLineTrailingSpace);
