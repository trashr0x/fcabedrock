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
    bool EmptyLineTrailingSpace)
{
    /// <summary>
    /// Whether the <c>.dat</c> file ends with a final newline (§18.2). Additive with a
    /// default of <c>true</c>, the historical behavior, so the positional constructor is
    /// unchanged and every existing construction preserves its byte-identical fingerprint
    /// (D-087). Encoded into the canonical JSON only when <c>false</c>.
    /// </summary>
    public bool TrailingNewline { get; init; } = true;
}
