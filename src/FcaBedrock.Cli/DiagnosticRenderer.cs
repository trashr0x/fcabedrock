using System.Globalization;
using System.Text;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Cli;

/// <summary>
/// The one place a diagnostic becomes text (D-122 part 3 / D-123 part 3). Every
/// diagnostic and every code-less host error or warning in the CLI flows through here, so the
/// committed colour/progress/machine-readable follow-ups become alternative renderer
/// implementations rather than a run-orchestration refactor.
/// <para>
/// Grammar (spec §16.4): one LF-terminated line per diagnostic, in the sparse labelled
/// form
/// </para>
/// <code>
/// file="…" line=N column=N attribute="…" record=N: severity Code: escaped-message
/// </code>
/// <para>
/// with only <b>populated</b> location fields (present = non-null, so an empty string
/// still renders as <c>file=""</c>), always in that order regardless of construction
/// order, fields separated by a single ASCII space; string fields as JSON string
/// literals and integers invariant; lowercase severity; the message without surrounding
/// quotes, through one prose transport pass. <see cref="DiagnosticEscaping"/> owns both
/// escapings. When no field is populated the whole prefix (including the <c>": "</c>) is
/// omitted and the line starts at the severity. Code-less host lines render
/// <c>error: escaped-message</c> or <c>warning: escaped-message</c>.
/// </para>
/// <para>
/// Nothing here sorts, groups, deduplicates, suppresses, or rewords: the library's
/// order and its codes, locations, and messages are reproduced exactly (D-122 declines
/// §16.4's permission to group). No culture and no platform newline is consulted.
/// </para>
/// <para>
/// The one addition is CLI-owned advice after the library message, in the same line and the
/// same transport pass (D-122 part 3): a <c>GroupingStorageFailed</c> line names
/// <c>--temp-dir</c>, chosen by the code and severity alone, and a command can add a sentence it
/// owns to one line it chooses. The library text before it is never changed, and advice is
/// never derived from a message, a location or a diagnostic's context.
/// </para>
/// </summary>
internal static class DiagnosticRenderer
{
    // The library's storage text names no host option (D-082), so the CLI maps it to its own
    // spelling. A stopping failure can be retried elsewhere; changing the option does not claim to
    // fix every cause. A cleanup failure left storage behind, and a new location cannot remove it.
    private const string StoppedStorageAdvice =
        "For a new run, --temp-dir DIR chooses the directory for temporary storage.";

    private const string CleanupStorageAdvice =
        "Choosing another --temp-dir for a new run does not remove storage that remains from this one.";

    /// <summary>The rendered line for <paramref name="diagnostic"/>, LF-terminated.</summary>
    public static string Render(BedrockDiagnostic diagnostic) => Render(diagnostic, advice: null);

    /// <summary>
    /// The rendered line for <paramref name="diagnostic"/> with <paramref name="advice"/>, a
    /// sentence the calling command owns, after the library message and any advice the CLI adds
    /// for the code; LF-terminated.
    /// </summary>
    public static string Render(BedrockDiagnostic diagnostic, string? advice)
    {
        var builder = new StringBuilder();
        AppendLocation(builder, diagnostic.Location);
        builder.Append(Spell(diagnostic.Severity));
        builder.Append(' ');
        builder.Append(diagnostic.Code.ToString());
        builder.Append(": ");

        // One transport pass over the whole line's prose, the CLI's own sentences included.
        var message = diagnostic.Message;
        if (StorageAdvice(diagnostic) is { } storage)
        {
            message += " " + storage;
        }

        if (advice is not null)
        {
            message += " " + advice;
        }

        DiagnosticEscaping.AppendProse(builder, message);
        builder.Append('\n');
        return builder.ToString();
    }

    /// <summary>
    /// The rendered line for a CLI-owned, code-less host/environment failure:
    /// a missing or unreadable input, a publication failure, an unexpected internal
    /// fault. These never join the diagnostic registry (D-122 part 2).
    /// </summary>
    public static string RenderHostError(string message) => RenderHostLine("error: ", message);

    /// <summary>
    /// The rendered line for a CLI-owned, code-less host warning: the same prose transport as
    /// <see cref="RenderHostError"/>, under <c>warning: </c>. Like a code-less error it never
    /// joins the diagnostic registry; rendering it decides no exit code.
    /// </summary>
    public static string RenderHostWarning(string message) => RenderHostLine("warning: ", message);

    /// <summary>Writes <paramref name="diagnostics"/> to <paramref name="writer"/> in the supplied order.</summary>
    public static void Write(TextWriter writer, IEnumerable<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            writer.Write(Render(diagnostic));
        }
    }

    /// <summary>True when any diagnostic is Error or worse: the exit-1 predicate.</summary>
    public static bool HasErrors(IEnumerable<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            {
                return true;
            }
        }

        return false;
    }

    // The fixed field order is a property of THIS method, not of how a DiagnosticLocation
    // happened to be constructed: the record's positional order is incidental, and a future
    // reordering of its parameters must not move these bytes.
    private static void AppendLocation(StringBuilder builder, DiagnosticLocation? location)
    {
        if (location is not { } present)
        {
            return;
        }

        var fields = 0;

        if (present.File is { } file)
        {
            Separate(builder, ref fields);
            builder.Append("file=");
            DiagnosticEscaping.AppendQuoted(builder, file);
        }

        if (present.Line is { } line)
        {
            Separate(builder, ref fields);
            builder.Append("line=");
            builder.Append(line.ToString(CultureInfo.InvariantCulture));
        }

        if (present.Column is { } column)
        {
            Separate(builder, ref fields);
            builder.Append("column=");
            builder.Append(column.ToString(CultureInfo.InvariantCulture));
        }

        if (present.AttributeName is { } attribute)
        {
            Separate(builder, ref fields);
            builder.Append("attribute=");
            DiagnosticEscaping.AppendQuoted(builder, attribute);
        }

        if (present.RecordIndex is { } record)
        {
            Separate(builder, ref fields);
            builder.Append("record=");
            builder.Append(record.ToString(CultureInfo.InvariantCulture));
        }

        if (fields > 0)
        {
            builder.Append(": ");
        }
    }

    private static string RenderHostLine(string prefix, string message)
    {
        var builder = new StringBuilder(prefix);
        DiagnosticEscaping.AppendProse(builder, message);
        builder.Append('\n');
        return builder.ToString();
    }

    private static string? StorageAdvice(BedrockDiagnostic diagnostic) => diagnostic switch
    {
        { Code: DiagnosticCode.GroupingStorageFailed, Severity: DiagnosticSeverity.Error or DiagnosticSeverity.Fatal } =>
            StoppedStorageAdvice,
        { Code: DiagnosticCode.GroupingStorageFailed, Severity: DiagnosticSeverity.Warning } => CleanupStorageAdvice,
        _ => null,
    };

    private static void Separate(StringBuilder builder, ref int fields)
    {
        if (fields > 0)
        {
            builder.Append(' ');
        }

        fields++;
    }

    // Spelled out rather than ToString().ToLowerInvariant(): these four words are a byte
    // contract, so they are written where the contract is, not derived from enum naming.
    private static string Spell(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Info => "info",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Fatal => "fatal",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown diagnostic severity."),
    };
}
