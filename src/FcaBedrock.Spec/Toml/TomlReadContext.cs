using FcaBedrock.Diagnostics;
using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Shared state for one <see cref="SpecReader"/> pass: the aggregating
/// diagnostics list (EP-14), the source label for locations, and the current
/// attribute scope. Positions come from Tomlyn's zero-based spans, shifted to
/// the 1-based convention of <see cref="DiagnosticLocation"/> (§16.3).
/// </summary>
internal sealed class TomlReadContext(string? filePath)
{
    /// <summary>
    /// Every diagnostic raised during the pass, in emission order;
    /// <c>SpecReader.SortSemantic</c> later orders the semantic phase by source position.
    /// </summary>
    public List<BedrockDiagnostic> Diagnostics { get; } = [];

    /// <summary>
    /// The <c>name</c> of the <c>[[attribute]]</c> currently being read; scopes
    /// subsequent diagnostics (§16.3). Null outside attribute tables.
    /// </summary>
    public string? AttributeName { get; set; }

    /// <summary>Raises an Error-severity diagnostic anchored at <paramref name="span"/>.</summary>
    public void Error(DiagnosticCode code, string message, SourceSpan span) =>
        Add(code, DiagnosticSeverity.Error, message, span);

    /// <summary>Raises a Fatal-severity diagnostic anchored at <paramref name="span"/>.</summary>
    public void Fatal(DiagnosticCode code, string message, SourceSpan span) =>
        Add(code, DiagnosticSeverity.Fatal, message, span);

    /// <summary>Raises a Warning-severity diagnostic anchored at <paramref name="span"/>.</summary>
    public void Warning(DiagnosticCode code, string message, SourceSpan span) =>
        Add(code, DiagnosticSeverity.Warning, message, span);

    private void Add(DiagnosticCode code, DiagnosticSeverity severity, string message, SourceSpan span) =>
        Diagnostics.Add(new BedrockDiagnostic(code, severity, message, new DiagnosticLocation(
            filePath,
            span.Start.Line + 1,
            span.Start.Column + 1,
            AttributeName)));
}
