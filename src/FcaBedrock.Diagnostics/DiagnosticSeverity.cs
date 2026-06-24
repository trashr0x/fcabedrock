namespace FcaBedrock.Diagnostics;

/// <summary>
/// Severity of a <see cref="BedrockDiagnostic"/>. Spec §16.2.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational (e.g. "auto-discretizer calibrated to cuts X").</summary>
    Info,

    /// <summary>Non-fatal issue (e.g. empty extent, deprecated field).</summary>
    Warning,

    /// <summary>Fatal to the operation but recoverable for the next call.</summary>
    Error,

    /// <summary>Unrecoverable; processing should stop.</summary>
    Fatal,
}
