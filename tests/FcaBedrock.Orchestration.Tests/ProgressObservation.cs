using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// One preparation, plan, preflight and publish of a convert, observed or not, rendered as text: the
/// outcome of each call, the complete diagnostic list in order, any exception that escaped, the attempt
/// record, the bytes of each output, the publication filesystem's operations and the residue left. Two
/// renderings of the same case, one observed and one not, must be equal (EP-7, D-141). The outputs are
/// removed afterwards, so the next rendering meets the same location.
/// </summary>
internal static class ProgressObservation
{
    /// <summary>What one rendering found.</summary>
    internal sealed record Result(string Text, ProgressRecorder? Recorder, int DataOpens);

    /// <summary>The arrangement a case may make before the operations run.</summary>
    internal sealed record Arrangement(RecordingPublicationFileSystem Files, ScriptedInput Input, CancellationTokenSource Signals);

    /// <summary>Runs the whole convert chain in <paramref name="directory"/> and renders what happened.</summary>
    public static async Task<Result> ConvertAsync(
        string directory,
        string specPath,
        string dataPath,
        bool observed,
        bool cxt = true,
        bool dat = true,
        bool manifest = true,
        bool v2 = false,
        Action<Arrangement>? arrange = null)
    {
        var input = new ScriptedInput(dataPath);
        var files = new RecordingPublicationFileSystem();
        using var signals = new CancellationTokenSource();
        var host = input.Host(files);
        OperationProgress? progress = null;
        ProgressRecorder? recorder = null;
        if (observed)
        {
            (progress, recorder) = ProgressRecorder.Create();
        }

        arrange?.Invoke(new Arrangement(files, input, signals));
        var text = new StringBuilder();
        var outputBase = Path.Combine(directory, "out");
        PreflightedConvertOutput? handle = null;
        PreparedFileRun? run = null;

        try
        {
            var prepared = await RunPreparation.PrepareAsync(
                new FileRunRequest { SpecPath = specPath, DataPath = dataPath }, host, progress, signals.Token);
            Render(text, "prepare", prepared);
            if (prepared.IsError || prepared.Value.Value is not { } preparedRun)
            {
                return Finish(text, recorder, progress, input, run);
            }

            run = preparedRun;
            var planned = ConvertPublication.PlanConversion(run, v2, prepared.Value.Diagnostics);
            Render(text, "plan", planned);
            if (planned.Value is not { } conversion)
            {
                return Finish(text, recorder, progress, input, run);
            }

            var preflight = ConvertPublication.Preflight(
                new ConvertOutput { Base = outputBase, WritesCxt = cxt, WritesDat = dat, WritesManifest = manifest, ReplaceExisting = false },
                run,
                host,
                signals.Token);
            Render(text, "preflight", preflight);
            if (preflight.IsError)
            {
                return Finish(text, recorder, progress, input, run);
            }

            handle = preflight.Value;
            var facts = new ManifestHostFacts
            {
                ToolVersion = "fcabedrock-vnext test",
                CommandLine = ["fcabedrock", "convert"],
                UtcNow = static () => new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
            };
            var published = await ConvertPublication.PublishAsync(conversion, handle, facts, planned.Diagnostics, signals.Token);
            Render(text, "publish", published);
        }
        catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
        {
            text.Append("threw ").Append(exception.GetType().FullName).Append(": ").Append(exception.Message);
            if (exception is OperationCanceledException cancelled)
            {
                text.Append(" (token ").Append(cancelled.CancellationToken == signals.Token ? "host" : "other").Append(')');
            }

            text.Append('\n');
        }
        finally
        {
            if (handle is not null)
            {
                RenderAttempt(text, handle.Attempt);
                handle.Dispose();
            }
        }

        foreach (var extension in new[] { ".cxt", ".dat", ".manifest.toml" })
        {
            var path = outputBase + extension;
            text.Append("file out").Append(extension).Append(": ");
            if (File.Exists(path))
            {
                text.Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))).Append('\n');
                File.Delete(path);
            }
            else
            {
                text.Append("absent\n");
            }
        }

        foreach (var operation in files.Operations)
        {
            text.Append("op ").Append(RecordingPublicationFileSystem.Fold(operation)).Append('\n');
        }

        foreach (var residue in Directory.EnumerateFiles(directory, "out.fcabedrock-*").Order(StringComparer.Ordinal))
        {
            text.Append("residue ").Append(RecordingPublicationFileSystem.Fold(Path.GetFileName(residue))).Append('\n');
        }

        return Finish(text, recorder, progress, input, run);
    }

    /// <summary>
    /// Renders a mixed result and its diagnostics under the test-only labels <c>HostFailure</c> with
    /// its kind and message, <c>Completed</c> and <c>DiagnosticFailure</c>.
    /// </summary>
    public static void Render<T>(StringBuilder text, string call, Result<Diagnosed<T>, HostFailure> outcome)
    {
        if (!outcome.IsError)
        {
            Render(text, call, outcome.Value);
            return;
        }

        var failure = outcome.Error;
        text.Append(call).Append(": HostFailure ").Append(failure.Error.Kind).Append(' ').Append(failure.Error.Message).Append('\n');
        AppendDiagnostics(text, failure.Diagnostics);
    }

    /// <summary>Renders a pure result and its diagnostics: <c>Completed</c> with a value, otherwise <c>DiagnosticFailure</c>.</summary>
    public static void Render<T>(StringBuilder text, string call, Diagnosed<T> outcome)
    {
        text.Append(call).Append(": ").Append(outcome.Value is null ? "DiagnosticFailure" : "Completed").Append('\n');
        AppendDiagnostics(text, outcome.Diagnostics);

        if (outcome.Value is ContextStatistics counts)
        {
            text.Append("  counts ").Append(counts.Objects).Append(' ').Append(counts.FormalAttributes).Append(' ')
                .Append(counts.Crosses).Append(' ').Append(counts.CrosslessObjects).Append(' ').Append(counts.EmptyAttributes).Append('\n');
        }
    }

    /// <summary>Renders a host-only result, which carries no list: <c>Completed</c>, or <c>HostFailure</c> with its kind and message.</summary>
    public static void Render<T>(StringBuilder text, string call, Result<T, HostError> outcome)
    {
        text.Append(call).Append(": ");
        if (outcome.IsError)
        {
            text.Append("HostFailure ").Append(outcome.Error.Kind).Append(' ').Append(outcome.Error.Message);
        }
        else
        {
            text.Append("Completed");
        }

        text.Append('\n');
    }

    /// <summary>Renders a publication attempt record: every fact but the committed objects' identities, which are fresh objects per run.</summary>
    public static void RenderAttempt(StringBuilder text, PublicationAttempt attempt)
    {
        text.Append("attempt ").Append(attempt.State).Append(" rollback ").Append(attempt.Rollback);
        if (attempt.RollbackBlocker is { } blocker)
        {
            text.Append(" blocker ").Append(blocker.Step).Append(' ').Append(RecordingPublicationFileSystem.Fold(blocker.EntryName));
        }

        text.Append('\n');
        if (attempt.Committed is { } committed)
        {
            text.Append("  cleanup ").Append(committed.Cleanup).Append(' ').Append(committed.CleanupWarning ?? "-").Append('\n');
            foreach (var file in committed.Files)
            {
                text.Append("  committed ").Append(file.Kind).Append(' ').Append(file.Spelling).Append(' ').Append(file.FullPath)
                    .Append(' ').Append(file.Hash).Append(file.Identity == default ? " no-identity" : " identity").Append('\n');
            }
        }
    }

    /// <summary>One diagnostic, every field that a host renders.</summary>
    public static string Describe(BedrockDiagnostic diagnostic) =>
        $"{diagnostic.Severity} {diagnostic.Code} {diagnostic.Message} @ {diagnostic.Location}";

    private static void AppendDiagnostics(StringBuilder text, IReadOnlyList<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            text.Append("  ").Append(Describe(diagnostic)).Append('\n');
        }
    }

    private static Result Finish(
        StringBuilder text, ProgressRecorder? recorder, OperationProgress? progress, ScriptedInput input, PreparedFileRun? run)
    {
        if (progress is not null)
        {
            progress.Complete();
            if (run is not null)
            {
                Assert.Equal(input.DataOpens.Count, run.Input.ObservedOpens);
            }

            recorder!.AssertGrammar(observedOpens: input.DataOpens.Count);
        }

        return new Result(text.ToString(), recorder, input.DataOpens.Count);
    }
}
