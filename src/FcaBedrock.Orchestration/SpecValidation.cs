using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Orchestration;

/// <summary>A validation that reached resolve: the composed document it resolved.</summary>
public sealed record ValidatedSpec
{
    internal ValidatedSpec(SpecDocument composed)
    {
        ArgumentNullException.ThrowIfNull(composed);
        Composed = composed;
    }

    /// <summary>The composed document; an owned snapshot that callers must not mutate.</summary>
    public SpecDocument Composed { get; }
}

/// <summary>
/// Schema validation of a spec (D-122 part 10), the CLI's <c>validate</c> moved.
/// <para>
/// Without DATA the existing no-schema resolution applies verbatim, so a binding by header name
/// fails. With DATA the source schema (the header, or the first non-blank record when headerless)
/// is acquired and the spec is resolved against it, so bindings by header name are checked.
/// </para>
/// <para>
/// <b>Validation is schema validation, not a dry run of conversion.</b> It examines no record
/// after the one that gives the schema, and it does not plan, calibrate, freeze, verify stored
/// fingerprints, hash the data, compute fingerprints, emit, publish, or mutate anything. A file
/// whose schema is valid but whose later records are malformed validates successfully: that is
/// the boundary, not an oversight. Bytes that are not UTF-8 differ only in where they are found:
/// the schema read decodes ahead of its record, so such bytes near the start of the file fail
/// validation, and ones further on are not seen (D-122 part 10).
/// </para>
/// </summary>
public static class SpecValidation
{
    /// <summary>
    /// Validates <paramref name="specPath"/>, against <paramref name="dataPath"/>'s schema when one is
    /// given. Completed: every step ran up to resolve; the diagnostics are the verdict and may hold
    /// Errors, so completion is not admission to a run. Diagnostic failure: parse, composition or
    /// (with data) read settings failed. Host failure: <see cref="HostErrorKind.SpecUnreadable"/>
    /// for the root, <see cref="HostErrorKind.DataUnreadable"/> for the data. Every phase boundary
    /// observes <paramref name="cancellation"/>.
    /// <para>
    /// The chain loads with its own <see cref="FileIdentity.CreateDefault"/> service, not the host's
    /// identity seam, as the CLI's validation always has (D-140).
    /// </para>
    /// </summary>
    public static async Task<Result<Diagnosed<ValidatedSpec>, HostFailure>> ValidateAsync(
        string specPath, string? dataPath, OrchestrationHost host, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(specPath);
        ArgumentNullException.ThrowIfNull(host);

        var source = new FileSpecTextSource(host.OpenInput, FileIdentity.CreateDefault());
        var diagnostics = new List<BedrockDiagnostic>();

        string rootKey;
        string toml;
        try
        {
            rootKey = source.RegisterRoot(specPath);
            toml = source.ReadText(specPath);
        }
        catch (Exception exception) when (RunPipeline.IsSpecReadFailure(exception))
        {
            // A missing or unreadable operand is an ordinary host failure, not a pipeline
            // condition: code-less, and the registry does not grow for it (D-122 part 2). The
            // message names the operand and the cause the failed operation established, and
            // nothing else: no exception text, no localized OS text, no internal path.
            return MixedOutcome<ValidatedSpec>.HostFailed(
                new HostError(
                    HostErrorKind.SpecUnreadable,
                    InputReadExplanation.Message(InputFile.Spec, specPath, exception),
                    subject: specPath),
                diagnostics);
        }

        // Phase boundaries observe the host token, so a signal delivered after dispatch stops
        // validation too. Without DATA nothing else here is cancellable; the read, parse,
        // compose and resolve stages are synchronous, so the first signal would otherwise be
        // suppressed and then ignored, and the run would report success after the user
        // cancelled it.
        cancellation.ThrowIfCancellationRequested();

        var read = SpecReader.Read(toml, rootKey);
        diagnostics.AddRange(read.Diagnostics);
        if (!read.TryGetValue(out var document))
        {
            return MixedOutcome<ValidatedSpec>.Failed(diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();
        var composed = SpecComposer.Compose(document, rootKey, source);
        diagnostics.AddRange(composed.Diagnostics);
        if (!composed.TryGetValue(out var effective))
        {
            // A missing or unreadable extends base is a phase-owned condition
            // (SpecExtendsNotFound), so it keeps its registry code, unlike the root
            // operand above, which the host owns.
            return MixedOutcome<ValidatedSpec>.Failed(diagnostics);
        }

        SourceSchema? schema = null;
        if (dataPath is not null)
        {
            var settings = SpecResolver.ResolveReadSettings(effective);
            if (!settings.TryGetValue(out var readSettings))
            {
                diagnostics.AddRange(settings.Diagnostics);
                return MixedOutcome<ValidatedSpec>.Failed(diagnostics);
            }

            // On success this stage reports nothing at all: it fails its result on any
            // Error/Fatal and raises no warning. Its conditions are re-evaluated by the
            // full Resolve below through the same shared helpers (D-098, "no condition
            // gains a second owner"), so reporting them here could only duplicate them.
            var session = Open(readSettings, dataPath, host);
            try
            {
                schema = await session.GetSchemaAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception exception) when (RunPipeline.IsDataReadFailure(exception))
            {
                // Validation hashes no pass, so it has no leading bytes to name a UTF-16 or UTF-32
                // mark from; a decoding failure is explained as invalid UTF-8.
                return MixedOutcome<ValidatedSpec>.HostFailed(
                    RunPipeline.DataUnreadable(
                        dataPath, DataReadExplanation.Message(dataPath, exception, DataByteOrderMark.None)),
                    diagnostics);
            }
        }

        cancellation.ThrowIfCancellationRequested();
        var resolved = SpecResolver.Resolve(effective, schema);
        diagnostics.AddRange(resolved.Diagnostics);
        return MixedOutcome<ValidatedSpec>.Completed(new ValidatedSpec(effective), diagnostics);
    }

    // The ONLY source call validation makes. The session is never bound and its record
    // stream is never enumerated: each row-reading path opens the stream factory again,
    // and validation opens it exactly once, for the schema read.
    private static ISourceSession Open(SourceReadSettings settings, string dataPath, OrchestrationHost host)
    {
        Stream OpenStream() => host.OpenInput(dataPath);
        return settings.Shape == SourceShape.Triple
            ? new TripleCsvSession(OpenStream, settings)
            : new WideCsvSession(OpenStream, settings);
    }
}
