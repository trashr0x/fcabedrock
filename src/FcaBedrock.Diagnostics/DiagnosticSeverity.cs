namespace FcaBedrock.Diagnostics;

/// <summary>
/// Severity of a <see cref="BedrockDiagnostic"/>. Spec §16.2.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Informational (e.g. the count of rows merged under
    /// <c>duplicate_object_policy = "dedupe"</c>, §6.1).
    /// </summary>
    Info,

    /// <summary>Non-fatal issue (e.g. an empty column or a stale fingerprint).</summary>
    Warning,

    /// <summary>Fatal to the operation but recoverable for the next call.</summary>
    Error,

    /// <summary>Unrecoverable; processing should stop.</summary>
    Fatal,
}
