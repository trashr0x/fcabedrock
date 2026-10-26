using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Orchestration;

/// <summary>
/// The one preparation path every file-backed run shares (D-122 part 10; the shape of
/// <c>GoldenConversion.PrepareAsync</c>, at the coordinator boundary), with the message builders
/// and the failure classifiers its callers reuse.
/// <para>
/// Read root SPEC → parse → compose → resolve read settings → shape-matched session over the
/// <b>hashing</b> data-stream factory → schema → resolve against that schema → bind to that
/// exact token → calibrate <em>only</em> when the spec needs data → plan natively → compute
/// the three native fingerprints. Every phase boundary observes the host cancellation token.
/// </para>
/// <para>
/// <b>It writes nothing.</b> Diagnostics accumulate in library order and come back with the
/// outcome; the host decides what reaches which stream, and a run is returned only after
/// every required phase and the stability check succeed.
/// </para>
/// <para>
/// <b>Validation keeps its own path.</b> It deliberately stops at schema acquisition: it
/// never plans, calibrates, hashes the data, or verifies stored fingerprints.
/// </para>
/// </summary>
internal static class RunPipeline
{
    /// <summary>The message a non-usable temporary-directory value produces.</summary>
    internal const string TempDirectoryMessage =
        "the --temp-dir value must be a non-empty, non-whitespace path.";

    /// <summary>
    /// The generic code-less host error for a root SPEC open or read failure: the whole message when
    /// no closed explanation applies (<see cref="InputReadExplanation"/>).
    /// </summary>
    internal static string SpecReadMessage(string specPath) => $"cannot read the spec file '{specPath}'.";

    /// <summary>
    /// The generic code-less host error for a DATA open or read failure: the whole message when no
    /// closed explanation applies (<see cref="DataReadExplanation"/>).
    /// </summary>
    internal static string DataReadMessage(string dataPath) => $"cannot read the data file '{dataPath}'.";

    /// <summary>The code-less host error for an input that changed between passes (§17 / D-122 part 5).</summary>
    internal static string InputChangedMessage(string dataPath) =>
        $"the data file '{dataPath}' changed while it was being read.";

    /// <summary>A data read failure: <see cref="HostErrorKind.DataUnreadable"/> with <paramref name="message"/>.</summary>
    internal static HostError DataUnreadable(string dataPath, string message) =>
        new(HostErrorKind.DataUnreadable, message, subject: dataPath);

    /// <summary>An input that changed between passes: <see cref="HostErrorKind.DataChanged"/>.</summary>
    internal static HostError DataChanged(string dataPath) =>
        new(HostErrorKind.DataChanged, InputChangedMessage(dataPath), subject: dataPath);

    /// <summary>Prepares one read-only data run whose root is the file <paramref name="request"/> names.</summary>
    public static async Task<OperationOutcome<PreparedFileRun>> PrepareAsync(
        FileRunRequest request, OrchestrationHost host, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(host);

        var specPath = request.SpecPath;
        var dataPath = request.DataPath;
        var tempDirectory = request.TempDirectory;
        var diagnostics = new List<BedrockDiagnostic>();

        // Runtime options first: an unusable value is a caller mistake, and failing it here
        // means no file is opened for a run that cannot proceed. The CLI's parser already refuses
        // a blank --temp-dir as usage, with the same whitespace test, so this catch is the backstop
        // for the library's validating boundary: its ArgumentException must not reach the
        // unexpected-fault exit (D-122 part 8).
        ConversionRuntimeOptions runtimeOptions;
        try
        {
            runtimeOptions = tempDirectory is not null
                ? new ConversionRuntimeOptions { TempDirectory = tempDirectory }
                : new ConversionRuntimeOptions();
        }
        catch (ArgumentException)
        {
            return new OperationHostFailure<PreparedFileRun>(
                new HostError(HostErrorKind.TemporaryDirectoryInvalid, TempDirectoryMessage), diagnostics);
        }

        var source = new FileSpecTextSource(host.OpenInput, host.SpecIdentities());

        string rootKey;
        string toml;
        try
        {
            rootKey = source.RegisterRoot(specPath);
            toml = source.ReadText(specPath);
        }
        catch (Exception exception) when (IsSpecReadFailure(exception))
        {
            return new OperationHostFailure<PreparedFileRun>(
                new HostError(
                    HostErrorKind.SpecUnreadable,
                    InputReadExplanation.Message(InputFile.Spec, specPath, exception),
                    subject: specPath),
                diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        var read = SpecReader.Read(toml, rootKey);
        diagnostics.AddRange(read.Diagnostics);
        if (!read.TryGetValue(out var rootDocument))
        {
            return new OperationDiagnosticFailure<PreparedFileRun>(diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        // Root and bases share one file-identity service, so an alias of an already-loaded file
        // is a cycle rather than a fresh load, and references stay referrer-relative.
        var composed = SpecComposer.Compose(rootDocument, rootKey, source);
        diagnostics.AddRange(composed.Diagnostics);
        if (!composed.TryGetValue(out var effective))
        {
            // A missing or unreadable base keeps its phase-owned SpecExtendsNotFound; only the
            // ROOT operand is the host's own code-less failure.
            return new OperationDiagnosticFailure<PreparedFileRun>(diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        // Schema-independent settings only. On success this stage reports nothing: the full
        // resolve below re-evaluates the same conditions through the same helpers, so anything
        // reported here could only be a duplicate (D-098).
        var settings = SpecResolver.ResolveReadSettings(effective);
        if (!settings.TryGetValue(out var readSettings))
        {
            diagnostics.AddRange(settings.Diagnostics);
            return new OperationDiagnosticFailure<PreparedFileRun>(diagnostics);
        }

        // Every data pass goes through the tracker, so hashing is inline with the bytes the
        // source was reading anyway, never a separate hash-only read (D-122 part 5).
        var input = new InputHashTracker(host.OpenInput, dataPath);
        var triple = readSettings.Shape == SourceShape.Triple;
        ISourceSession session = triple
            ? new TripleCsvSession(input.OpenHashed, readSettings)
            : new WideCsvSession(input.OpenHashed, readSettings);

        SourceSchema schema;
        try
        {
            schema = await session.GetSchemaAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDataReadFailure(exception))
        {
            return new OperationHostFailure<PreparedFileRun>(
                DataUnreadable(dataPath, DataReadExplanation.Message(dataPath, exception, input.CurrentPassMark)),
                diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        var resolved = SpecResolver.Resolve(effective, schema);
        diagnostics.AddRange(resolved.Diagnostics);
        if (!resolved.TryGetValue(out var resolvedDocument))
        {
            return new OperationDiagnosticFailure<PreparedFileRun>(diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        // Bound to THIS resolution token, so calibrate and emit pair by reference identity
        // before any row is read (D-098). Binding reads nothing.
        var wideSource = triple ? null : ((WideCsvSession)session).Bind(resolvedDocument.Resolved);
        var tripleSource = triple ? ((TripleCsvSession)session).Bind(resolvedDocument.Resolved) : null;

        CalibratedSpec calibratedSpec;
        if (CalibratedSpec.RequiresData(resolvedDocument.Resolved.Spec))
        {
            Diagnosed<CalibratedSpec> calibrated;
            try
            {
                calibrated = triple
                    ? await Calibrator.CalibrateTripleAsync(
                        resolvedDocument.Resolved, tripleSource!, runtimeOptions, cancellation).ConfigureAwait(false)
                    : await Calibrator.CalibrateAsync(
                        resolvedDocument.Resolved, wideSource!, runtimeOptions, cancellation).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsDataReadFailure(exception))
            {
                return new OperationHostFailure<PreparedFileRun>(
                    DataUnreadable(dataPath, DataReadExplanation.Message(dataPath, exception, input.CurrentPassMark)),
                    diagnostics);
            }

            diagnostics.AddRange(calibrated.Diagnostics);
            if (!calibrated.TryGetValue(out var value))
            {
                return new OperationDiagnosticFailure<PreparedFileRun>(diagnostics);
            }

            calibratedSpec = value;
        }
        else
        {
            // Fully declared: the same Plan input, reached with no data pass at all (§7/D-093).
            calibratedSpec = CalibratedSpec.FromFullyDeclared(resolvedDocument.Resolved);
        }

        cancellation.ThrowIfCancellationRequested();

        // Native only. v2-compatible labels are a convert-only override (D-011), and effective
        // override fingerprints are a manifest fact, never a report one (§14). The presentation is
        // the document's own bin_label_unicode, the flag the native fingerprints hash.
        var output = OutputSettings.Native(resolvedDocument.Document);
        var planned = ConversionPlanner.Plan(calibratedSpec, LabelStyle.Native, output.BinLabelUnicode);
        diagnostics.AddRange(planned.Diagnostics);
        if (!planned.TryGetValue(out var plan))
        {
            return new OperationDiagnosticFailure<PreparedFileRun>(diagnostics);
        }

        var fingerprints = SpecFingerprints.ComputeNative(resolvedDocument, plan);

        if (input.HasMismatch)
        {
            return new OperationHostFailure<PreparedFileRun>(DataChanged(dataPath), diagnostics);
        }

        // The emit binder captures the bound source and the runtime options, never a token: each
        // operation that enumerates the run passes the token it was given (D-140).
        PreparedRunEmitter emit = triple
            ? (effectivePlan, sink, token) =>
                Emitter.EmitTripleAsync(effectivePlan, tripleSource!, sink, runtimeOptions, token)
            : (effectivePlan, sink, token) =>
                Emitter.EmitAsync(effectivePlan, wideSource!, sink, runtimeOptions, token);

        var prepared = new PreparedFileRun(
            rootDocument,
            rootKey,
            resolvedDocument,
            calibratedSpec,
            readSettings.Shape,
            plan,
            fingerprints,
            output,
            source.Chain,
            emit,
            input,
            specPath,
            dataPath);

        return new OperationCompleted<PreparedFileRun>(prepared, diagnostics);
    }

    /// <summary>
    /// Expected open/read failures for the <b>root SPEC</b> operand, applied by every operation
    /// that reads one, validation included, so all of them classify one unreadable spec
    /// identically. <see cref="InvalidDataException"/> (a non-UTF-8 byte-order mark) derives from
    /// <see cref="SystemException"/> rather than <see cref="IOException"/>, so it is named;
    /// <see cref="DecoderFallbackException"/> (malformed UTF-8) arrives as an
    /// <see cref="ArgumentException"/>; and <see cref="ObjectDisposedException"/> is the read
    /// family <see cref="IsDataReadFailure"/> also names. Cancellation is absent on purpose: it is
    /// not an input error, so the host classifies it: in the CLI, a silent exit 3 when it is the
    /// host's own requested cancellation, and the internal fault (exit 4) otherwise.
    /// </summary>
    internal static bool IsSpecReadFailure(Exception exception) =>
        exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or SourceReadException
            or ArgumentException
            or NotSupportedException
            or ObjectDisposedException;

    /// <summary>
    /// Expected open/read failures for the <b>DATA</b> operand, applied identically at every
    /// real data pass: schema acquisition (the only pass validation makes), calibration, the
    /// statistics counting enumeration and the convert's emission.
    /// An unreadable source must not change its public classification merely because the
    /// failure landed after the schema was acquired.
    /// <para>
    /// These are the established source-neutral provider/read families the existing
    /// <c>Prober</c> boundary already recognizes, listed <b>by their specific types</b>. That
    /// is the whole point: <see cref="DecoderFallbackException"/> derives from
    /// <see cref="ArgumentException"/> and <see cref="ObjectDisposedException"/> from
    /// <see cref="InvalidOperationException"/>, so naming each one keeps a real read failure on
    /// the code-less host-failure path <em>without</em> admitting its broad base; those bases are
    /// the documented call-contract channel of the calibrator, the emitter, and the
    /// calibrated-state factories (D-093/D-098), and absorbing them would disguise a defect as
    /// a broken data file (EP-14).
    /// </para>
    /// </summary>
    internal static bool IsDataReadFailure(Exception exception) =>
        exception is SourceReadException
            or IOException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or DecoderFallbackException
            or InvalidDataException;
}
