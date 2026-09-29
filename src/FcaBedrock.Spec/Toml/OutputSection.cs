namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[output]</c> section (§8). Resolution carries it inert:
/// <c>BedrockSpec</c> never carries output config, and the writers take
/// <c>WriterOptions</c> from the caller (EP-13/EP-15). The canonical writer,
/// <c>SpecFingerprints.ComputeNative</c> and the CLI's <c>OutputSettings.Native</c> read it.
/// </summary>
/// <param name="BinLabelUnicode">Whether bin labels render Unicode operators (§8).</param>
/// <param name="Cxt">The <c>[output.cxt]</c> block (§8); null when absent.</param>
/// <param name="Dat">The <c>[output.dat]</c> block (§8); null when absent.</param>
public sealed record OutputSection(
    bool? BinLabelUnicode,
    CxtOutputSection? Cxt,
    DatOutputSection? Dat);

/// <summary>Authored <c>[output.cxt]</c> options (§8); consumed through the CLI's <c>OutputSettings.Native</c> (D-122/D-123).</summary>
/// <param name="LineEndings">Line-ending convention.</param>
/// <param name="TrailingNewline">Whether the file ends with a newline.</param>
/// <param name="SizeAdvisoryBytes">Advisory size threshold for the <c>.cxt</c> warning (§8): 0 disables it, the reader rejects a negative authored value (<c>SpecFieldInvalid</c>, D-135), and it is not a fingerprint input (D-077).</param>
public sealed record CxtOutputSection(
    LineEndings? LineEndings,
    bool? TrailingNewline,
    long? SizeAdvisoryBytes);

/// <summary>Authored <c>[output.dat]</c> options (§8); consumed through the CLI's <c>OutputSettings.Native</c> (D-122/D-123).</summary>
/// <param name="LineEndings">Line-ending convention.</param>
/// <param name="BaseIndex">First formal-attribute id (§8/§17): 1 (the default) or 0; the reader rejects any other authored value (<c>SpecFieldInvalid</c>, D-135).</param>
/// <param name="NonemptyLineTrailingSpace">Whether non-empty lines end with a space (v2 quirk, §8).</param>
/// <param name="EmptyLineTrailingSpace">Whether empty lines carry a space (v2 quirk, §8).</param>
public sealed record DatOutputSection(
    LineEndings? LineEndings,
    int? BaseIndex,
    bool? NonemptyLineTrailingSpace,
    bool? EmptyLineTrailingSpace)
{
    /// <summary>
    /// Whether the file ends with a final newline (§18.2, default true); null when
    /// absent. The symmetrical twin of <see cref="CxtOutputSection.TrailingNewline"/>
    /// (D-087). It is an init property rather than a positional parameter, so the
    /// positional constructor and deconstruction do not include it.
    /// </summary>
    public bool? TrailingNewline { get; init; }
}

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
