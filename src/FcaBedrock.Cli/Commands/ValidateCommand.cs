using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>validate SPEC [DATA]</c> (D-122 part 10).
/// <para>
/// Without DATA the existing no-schema resolution applies verbatim, so a binding by header name
/// fails; when the spec binds by header name, the command then adds one conditional sentence naming
/// <c>validate SPEC DATA</c> to the first binding failure, because only it knows DATA was not
/// given. With DATA the source
/// schema (the header, or the first non-blank record when headerless) is acquired and the spec is
/// resolved against it, so bindings by header name are checked.
/// </para>
/// <para>
/// <b>Validate is schema validation, not a dry run of conversion.</b> It examines no record
/// after the one that gives the schema, and it does not plan, calibrate, freeze, verify stored
/// fingerprints, hash the data, compute fingerprints, emit, publish, or mutate anything. A file
/// whose schema is valid but whose later records are malformed validates successfully: that is
/// the boundary, not an oversight. Bytes that are not UTF-8 differ only in where they are found:
/// the schema read decodes ahead of its record, so such bytes near the start of the file fail
/// validate, and ones further on are not seen (D-122 part 10).
/// </para>
/// </summary>
internal static class ValidateCommand
{
    // Without DATA there is no header, so a binding by header name cannot resolve. The sentence is
    // conditional because a binding failure can have another cause that DATA would not fix, and the
    // library diagnostics stay as they are (D-122 part 10).
    internal const string HeaderAdvice =
        "If a binding names a header column, give DATA so validate can read the header: fcabedrock validate SPEC DATA.";

    /// <summary>Runs the command; 0 when no Error/Fatal diagnostic was produced, otherwise 1.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var dataPath = invocation.Operand(1);
        var cancellation = environment.Signals.Token;

        var outcome = await SpecValidation.ValidateAsync(
            invocation.Operand(0)!, dataPath, environment.Orchestration, cancellation).ConfigureAwait(false);

        // A missing or unreadable operand is an ordinary host failure: code-less, exit 1, and
        // rendered without a cancellation check (D-122 part 2).
        if (outcome.IsError)
        {
            var failure = outcome.Error;
            return HostFailure(environment, [.. failure.Diagnostics], failure.Error.Message);
        }

        // Resolve was reached: the diagnostics are the verdict, with the header advice when DATA
        // was not given and the composed spec binds by header name.
        if (outcome.Value.Value is { } validated)
        {
            return Finish(
                environment, [.. outcome.Value.Diagnostics], cancellation,
                adviseHeader: dataPath is null && BindsByHeaderName(validated.Composed));
        }

        // Parse, composition or read settings failed; a missing or unreadable extends base is a
        // phase-owned condition (SpecExtendsNotFound) and keeps its registry code.
        return Finish(environment, [.. outcome.Value.Diagnostics], cancellation);
    }

    // Whether the composed spec binds a column by header name anywhere one can be: an attribute's
    // source, the object key or a triple role (§5.2 to §5.4). Read from the document itself, never
    // from a diagnostic's wording, so a binding failure in a spec without names gets no advice.
    private static bool BindsByHeaderName(SpecDocument document) =>
        document.Attributes.Any(attribute => attribute.Source is ColumnSourceSection { Name: not null })
        || document.Binding?.ObjectKey?.Column is NameColumnRef
        || (document.Binding?.Columns is { } roles
            && (roles.Subject is NameColumnRef || roles.Predicate is NameColumnRef || roles.Value is NameColumnRef));

    private static int HostFailure(CliEnvironment environment, List<BedrockDiagnostic> diagnostics, string message)
    {
        DiagnosticRenderer.Write(environment.Error, diagnostics);
        environment.Error.Write(DiagnosticRenderer.RenderHostError(message));
        return 1;
    }

    private static int Finish(
        CliEnvironment environment, List<BedrockDiagnostic> diagnostics, CancellationToken cancellation,
        bool adviseHeader = false)
    {
        // The last boundary before anything is rendered: a cancelled run reports nothing at
        // all, so the check has to precede the write rather than follow it.
        cancellation.ThrowIfCancellationRequested();

        // Library order, verbatim: no sorting, grouping, deduplication, or rewording. The one
        // addition is the header advice on the first binding failure of a run without DATA whose
        // spec binds by header name.
        var advised = adviseHeader ? diagnostics.FindIndex(IsBindingFailure) : -1;
        for (var i = 0; i < diagnostics.Count; i++)
        {
            environment.Error.Write(i == advised
                ? DiagnosticRenderer.Render(diagnostics[i], HeaderAdvice)
                : DiagnosticRenderer.Render(diagnostics[i]));
        }

        return DiagnosticRenderer.HasErrors(diagnostics) ? 1 : 0;
    }

    // The two codes a binding by header name reports when no schema exists (§5.2 to §5.4).
    private static bool IsBindingFailure(BedrockDiagnostic diagnostic) =>
        diagnostic.Code is DiagnosticCode.SourceBindingInvalid or DiagnosticCode.ObjectKeyBindingInvalid;
}
