namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[output]</c> section (§8). Carried inert by design:
/// <c>BedrockSpec</c> never carries output config — the writers take
/// <c>WriterOptions</c> from the caller (P-13/P-15). Wired to the reader/writer
/// and fingerprint slices (C/E).
/// </summary>
/// <param name="BinLabelUnicode">Whether bin labels render Unicode operators (§8).</param>
/// <param name="Cxt">The <c>[output.cxt]</c> block (§8); null when absent.</param>
/// <param name="Dat">The <c>[output.dat]</c> block (§8); null when absent.</param>
public sealed record OutputSection(
    bool? BinLabelUnicode,
    CxtOutputSection? Cxt,
    DatOutputSection? Dat);

/// <summary>Authored <c>[output.cxt]</c> options (§8); carried inert in this slice.</summary>
/// <param name="LineEndings">Line-ending convention.</param>
/// <param name="TrailingNewline">Whether the file ends with a newline.</param>
/// <param name="SizeAdvisoryBytes">Advisory size threshold for the <c>.cxt</c> warning (§8).</param>
public sealed record CxtOutputSection(
    LineEndings? LineEndings,
    bool? TrailingNewline,
    long? SizeAdvisoryBytes);

/// <summary>Authored <c>[output.dat]</c> options (§8); carried inert in this slice.</summary>
/// <param name="LineEndings">Line-ending convention.</param>
/// <param name="BaseIndex">First formal-attribute id (§8/§17).</param>
/// <param name="NonemptyLineTrailingSpace">Whether non-empty lines end with a space (v2 quirk, §8).</param>
/// <param name="EmptyLineTrailingSpace">Whether empty lines carry a space (v2 quirk, §8).</param>
public sealed record DatOutputSection(
    LineEndings? LineEndings,
    int? BaseIndex,
    bool? NonemptyLineTrailingSpace,
    bool? EmptyLineTrailingSpace);

/// <summary>
/// Line-ending conventions for output files (§8). A document-model twin:
/// Export's own options type is not referenceable from Spec (dependency rule).
/// </summary>
public enum LineEndings
{
    /// <summary>Unix <c>\n</c>.</summary>
    Lf,

    /// <summary>Windows <c>\r\n</c>.</summary>
    Crlf,
}
