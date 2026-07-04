using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Fingerprinting;

/// <summary>
/// The <c>.cxt</c>-only fingerprint inputs (spec §8/§14, D-051/D-069): the
/// bin-label style, the Unicode-operator flag, and the <c>.cxt</c> writer
/// settings. <c>size_advisory_bytes</c> is deliberately absent — it changes a
/// warning, never output bytes (D-077).
/// </summary>
/// <param name="LabelStyle">
/// The bin-label render style. Must match the style the plan was produced with —
/// see <see cref="FingerprintCalculator.ComputeCxtOutputFingerprint"/>.
/// </param>
/// <param name="BinLabelUnicode">Whether bin labels render Unicode operators (§8).</param>
/// <param name="LineEnding">The <c>.cxt</c> line-ending convention (§8).</param>
/// <param name="TrailingNewline">Whether the file ends with a newline (§8).</param>
public sealed record CxtFingerprintInputs(
    LabelStyle LabelStyle,
    bool BinLabelUnicode,
    LineEnding LineEnding,
    bool TrailingNewline);
