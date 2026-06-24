namespace FcaBedrock.Diagnostics;

/// <summary>
/// Optional location context for a <see cref="BedrockDiagnostic"/>. Any subset of
/// fields may be populated. Spec §16.3.
/// </summary>
/// <param name="File">Path to the spec/data file, if applicable.</param>
/// <param name="Line">1-based line, for spec files.</param>
/// <param name="Column">1-based column, for spec files.</param>
/// <param name="AttributeName">For attribute-scoped issues.</param>
/// <param name="RecordIndex">For data issues, 0-based.</param>
public readonly record struct DiagnosticLocation(
    string? File = null,
    int? Line = null,
    int? Column = null,
    string? AttributeName = null,
    long? RecordIndex = null);
