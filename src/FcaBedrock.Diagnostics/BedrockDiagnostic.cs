namespace FcaBedrock.Diagnostics;

/// <summary>
/// A single problem detected by parsing, validation, planning, calibration, or
/// conversion. Spec §16.1.
/// </summary>
/// <param name="Code">The stable condition identifier.</param>
/// <param name="Severity">How serious the condition is.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="Location">Optional file/attribute/record context.</param>
/// <param name="Context">Optional structured payload for tooling.</param>
public readonly record struct BedrockDiagnostic(
    DiagnosticCode Code,
    DiagnosticSeverity Severity,
    string Message,
    DiagnosticLocation? Location = null,
    object? Context = null);
