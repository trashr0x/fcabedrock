using System.Globalization;
using System.Text;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Cli;

/// <summary>
/// The one place a diagnostic becomes text (D-122 part 3 / D-123 part 3). Every
/// diagnostic and every code-less host error in the CLI flows through here, so the
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
/// literals and integers invariant; lowercase severity; the message JSON-escaped
/// without surrounding quotes. When no field is populated the whole prefix — including
/// the <c>": "</c> — is omitted and the line starts at the severity. Code-less host
/// errors render <c>error: escaped-message</c>.
/// </para>
/// <para>
/// Nothing here sorts, groups, deduplicates, suppresses, or rewords: the library's
/// order and its codes, locations, and messages are reproduced exactly (D-122 declines
/// §16.4's permission to group). No culture and no platform newline is consulted.
/// </para>
/// </summary>
internal static class DiagnosticRenderer
{
    /// <summary>The rendered line for <paramref name="diagnostic"/>, LF-terminated.</summary>
    public static string Render(BedrockDiagnostic diagnostic)
    {
        var builder = new StringBuilder();
        AppendLocation(builder, diagnostic.Location);
        builder.Append(Spell(diagnostic.Severity));
        builder.Append(' ');
        builder.Append(diagnostic.Code.ToString());
        builder.Append(": ");
        JsonStringEscaping.AppendEscaped(builder, diagnostic.Message);
        builder.Append('\n');
        return builder.ToString();
    }

    /// <summary>
    /// The rendered line for a CLI-owned, code-less host/environment failure —
    /// a missing or unreadable input, a publication failure, an unexpected internal
    /// fault. These never join the diagnostic registry (D-122 part 2).
    /// </summary>
    public static string RenderHostError(string message)
    {
        var builder = new StringBuilder("error: ");
        JsonStringEscaping.AppendEscaped(builder, message);
        builder.Append('\n');
        return builder.ToString();
    }

    /// <summary>Writes <paramref name="diagnostics"/> to <paramref name="writer"/> in the supplied order.</summary>
    public static void Write(TextWriter writer, IEnumerable<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            writer.Write(Render(diagnostic));
        }
    }

    /// <summary>True when any diagnostic is Error or worse — the exit-1 predicate.</summary>
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
            JsonStringEscaping.AppendLiteral(builder, file);
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
            JsonStringEscaping.AppendLiteral(builder, attribute);
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
