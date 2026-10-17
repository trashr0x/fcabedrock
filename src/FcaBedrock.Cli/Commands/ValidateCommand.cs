using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
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

        var specPath = invocation.Operand(0)!;
        var dataPath = invocation.Operand(1);
        var host = new FileSpecTextSource(environment.OpenInput, FileIdentity.CreateDefault());
        var diagnostics = new List<BedrockDiagnostic>();

        string rootKey;
        string toml;
        try
        {
            rootKey = host.RegisterRoot(specPath);
            toml = host.ReadText(specPath);
        }
        catch (Exception exception) when (RunPipeline.IsSpecReadFailure(exception))
        {
            // A missing or unreadable operand is an ordinary host failure, not a pipeline
            // condition: code-less, exit 1, and the registry does not grow for it
            // (D-122 part 2). The message names the operand and the cause the failed operation
            // established, and nothing else: no exception text, no localized OS text, no
            // internal path.
            return HostFailure(environment, diagnostics, InputReadExplanation.Message(InputFile.Spec, specPath, exception));
        }

        // Phase boundaries observe the host token, so a signal delivered after dispatch stops
        // this command too. Without DATA nothing else here is cancellable; the read, parse,
        // compose and resolve stages are synchronous, so the first signal would otherwise be
        // suppressed and then ignored, and the run would report success after the user
        // cancelled it.
        var cancellation = environment.Signals.Token;
        cancellation.ThrowIfCancellationRequested();

        var read = SpecReader.Read(toml, rootKey);
        diagnostics.AddRange(read.Diagnostics);
        if (!read.TryGetValue(out var document))
        {
            return Finish(environment, diagnostics, cancellation);
        }

        cancellation.ThrowIfCancellationRequested();
        var composed = SpecComposer.Compose(document, rootKey, host);
        diagnostics.AddRange(composed.Diagnostics);
        if (!composed.TryGetValue(out var effective))
        {
            // A missing or unreadable extends base is a phase-owned condition
            // (SpecExtendsNotFound), so it keeps its registry code, unlike the root
            // operand above, which the CLI owns.
            return Finish(environment, diagnostics, cancellation);
        }

        SourceSchema? schema = null;
        if (dataPath is not null)
        {
            var settings = SpecResolver.ResolveReadSettings(effective);
            if (!settings.TryGetValue(out var readSettings))
            {
                diagnostics.AddRange(settings.Diagnostics);
                return Finish(environment, diagnostics, cancellation);
            }

            // On success this stage reports nothing at all: it fails its result on any
            // Error/Fatal and raises no warning. Its conditions are re-evaluated by the
            // full Resolve below through the same shared helpers (D-098, "no condition
            // gains a second owner"), so rendering them here could only duplicate them.
            var session = Open(readSettings, dataPath, environment);
            try
            {
                schema = await session.GetSchemaAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception exception) when (RunPipeline.IsDataReadFailure(exception))
            {
                // Validate hashes no pass, so it has no leading bytes to name a UTF-16 or UTF-32
                // mark from; a decoding failure is explained as invalid UTF-8.
                return HostFailure(
                    environment, diagnostics, DataReadExplanation.Message(dataPath, exception, DataByteOrderMark.None));
            }
        }

        cancellation.ThrowIfCancellationRequested();
        var resolved = SpecResolver.Resolve(effective, schema);
        diagnostics.AddRange(resolved.Diagnostics);
        return Finish(
            environment, diagnostics, cancellation, adviseHeader: dataPath is null && BindsByHeaderName(effective));
    }

    // Whether the composed spec binds a column by header name anywhere one can be: an attribute's
    // source, the object key or a triple role (§5.2 to §5.4). Read from the document itself, never
    // from a diagnostic's wording, so a binding failure in a spec without names gets no advice.
    private static bool BindsByHeaderName(SpecDocument document) =>
        document.Attributes.Any(attribute => attribute.Source is ColumnSourceSection { Name: not null })
        || document.Binding?.ObjectKey?.Column is NameColumnRef
        || (document.Binding?.Columns is { } roles
            && (roles.Subject is NameColumnRef || roles.Predicate is NameColumnRef || roles.Value is NameColumnRef));

    // The ONLY source call validate makes. The session is never bound and its record
    // stream is never enumerated: each row-reading path opens the stream factory again,
    // and this command opens it exactly once, for the schema read.
    private static ISourceSession Open(SourceReadSettings settings, string dataPath, CliEnvironment environment)
    {
        Stream OpenStream() => environment.OpenInput(dataPath);
        return settings.Shape == SourceShape.Triple
            ? new TripleCsvSession(OpenStream, settings)
            : new WideCsvSession(OpenStream, settings);
    }

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
