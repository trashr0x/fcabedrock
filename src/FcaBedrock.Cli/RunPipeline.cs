using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli;

/// <summary>What <see cref="RunPipeline.PrepareAsync"/> produced, with the diagnostics collected so far.</summary>
/// <param name="Diagnostics">Every diagnostic, in phase and library order — never sorted, grouped, or deduplicated.</param>
internal abstract record PipelineOutcome(IReadOnlyList<BedrockDiagnostic> Diagnostics);

/// <summary>Preparation succeeded; the run is ready to report on.</summary>
internal sealed record PipelinePrepared(PreparedRun Run, IReadOnlyList<BedrockDiagnostic> Diagnostics)
    : PipelineOutcome(Diagnostics);

/// <summary>A library phase failed; its own diagnostics explain why (exit 1).</summary>
internal sealed record PipelineDiagnosticFailure(IReadOnlyList<BedrockDiagnostic> Diagnostics)
    : PipelineOutcome(Diagnostics);

/// <summary>An ordinary host/environment failure: one sanitized code-less error, exit 1 (D-122 part 2).</summary>
internal sealed record PipelineHostFailure(string Message, IReadOnlyList<BedrockDiagnostic> Diagnostics)
    : PipelineOutcome(Diagnostics);

/// <summary>
/// The state one prepared run carries: what <c>plan</c>, <c>stats</c>, and <c>fingerprint</c>
/// read, plus the facts <c>convert</c> additionally needs to emit, fingerprint the effective
/// output, and compose a manifest.
/// <para>
/// <b>It is one run's state or nothing.</b> Every member here comes from the same preparation —
/// the same resolution token, the same calibrated state, the same bound source — and the two
/// derived plans are produced from <see cref="Calibrated"/> by <see cref="PlanEffective"/>, so a
/// document, a calibration, a plan, a source, or a fingerprint from a different run cannot be
/// paired with these (D-098).
/// </para>
/// </summary>
internal sealed class PreparedRun
{
    /// <summary>
    /// The <b>root</b> document, as authored — the owner of the stored <c>[spec]</c> fields
    /// every comparison and report reads. Deliberately not the composed document: a base
    /// file's stored fingerprints are never the root's (§13 rule 8 / D-078).
    /// </summary>
    public required SpecDocument RootDocument { get; init; }

    /// <summary>The root's canonical key — the <c>file</c> location stale warnings carry.</summary>
    public required string RootKey { get; init; }

    /// <summary>
    /// The composed document paired with its resolution token — the effective state the native
    /// fingerprints were computed over.
    /// </summary>
    public required ResolvedDocument Resolved { get; init; }

    /// <summary>
    /// The calibrated state both plans are produced from, and the owner of the retained
    /// calibration outcomes the run manifest serializes without re-derivation (D-093).
    /// </summary>
    public required CalibratedSpec Calibrated { get; init; }

    /// <summary>The resolved source shape — the D-087 v2-compat <c>.dat</c> final newline reads it.</summary>
    public required SourceShape Shape { get; init; }

    /// <summary>The native plan (<see cref="LabelStyle.Native"/>), planned once.</summary>
    public required ConversionPlan Plan { get; init; }

    /// <summary>The three native fingerprints computed from the paired resolved document and plan.</summary>
    public required ComputedFingerprints Fingerprints { get; init; }

    /// <summary>The resolved §8 <c>[output]</c> settings, before any CLI override.</summary>
    public required OutputSettings Output { get; init; }

    /// <summary>
    /// Every spec file this run loaded, in root-first-then-bases order, with its authored
    /// spelling and raw-bytes hash (§15 <c>[[run.spec_files]]</c>).
    /// </summary>
    public required IReadOnlyList<SpecChainFile> SpecChain { get; init; }

    /// <summary>
    /// Binds a plan to the shape-bound source. The shape was decided where the source was built,
    /// so a wide plan can never be handed to the triple entrypoint (the
    /// <c>GoldenConversion.PreparedConversion</c> shape).
    /// </summary>
    public required Func<ConversionPlan, Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>>> EmitWith
    {
        get;
        init;
    }

    /// <summary>The emit delegate for the native plan — what the report commands enumerate.</summary>
    public Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> Emit => EmitWith(Plan);

    /// <summary>The input-stability tracker, re-checked after any further pass.</summary>
    public required InputHashTracker Input { get; init; }

    /// <summary>The SPEC operand, verbatim — §15 records it as authored.</summary>
    public required string SpecPath { get; init; }

    /// <summary>The DATA operand, verbatim — the host-failure messages name it, and §15 records it.</summary>
    public required string DataPath { get; init; }

    /// <summary>
    /// Plans this run's <em>same</em> calibrated state under <paramref name="style"/> — the only
    /// way a second plan is produced, which is what keeps the native/effective pair exact
    /// (D-044/D-077). Planning is pure, so the second plan costs no data pass.
    /// </summary>
    public Diagnosed<ConversionPlan> PlanEffective(LabelStyle style) => ConversionPlanner.Plan(Calibrated, style);
}

/// <summary>
/// The one preparation path <c>plan</c>, <c>stats</c>, and <c>fingerprint</c> share (D-122
/// part 10; the shape of <c>GoldenConversion.PrepareAsync</c>, at the CLI boundary).
/// <para>
/// Read root SPEC → parse → compose → resolve read settings → shape-matched session over the
/// <b>hashing</b> data-stream factory → schema → resolve against that schema → bind to that
/// exact token → calibrate <em>only</em> when the spec needs data → plan natively → compute
/// the three native fingerprints. Every phase boundary observes the host cancellation token.
/// </para>
/// <para>
/// <b>It writes nothing.</b> Diagnostics accumulate in library order and come back with the
/// outcome; the handler decides what reaches which stream, and a report is built only after
/// every required phase and the stability check succeed.
/// </para>
/// <para>
/// <b>Not a validate refactor.</b> <c>validate</c> deliberately stops at schema acquisition —
/// it never plans, calibrates, hashes, or verifies stored fingerprints — so it keeps its own
/// path and its bytes are untouched.
/// </para>
/// </summary>
internal static class RunPipeline
{
    /// <summary>The message a non-usable <c>--temp-dir</c> value produces.</summary>
    internal const string TempDirectoryMessage =
        "the --temp-dir value must be a non-empty, non-whitespace path.";

    /// <summary>The code-less host error for an unusable <c>--temp-dir</c>, or a spec/data read failure.</summary>
    internal static string SpecReadMessage(string specPath) => $"cannot read the spec file '{specPath}'.";

    /// <summary>The code-less host error for a DATA open or read failure.</summary>
    internal static string DataReadMessage(string dataPath) => $"cannot read the data file '{dataPath}'.";

    /// <summary>The code-less host error for an input that changed between passes (§17 / D-122 part 5).</summary>
    internal static string InputChangedMessage(string dataPath) =>
        $"the data file '{dataPath}' changed while it was being read.";

    /// <summary>Prepares one read-only data run from a parsed invocation.</summary>
    public static async Task<PipelineOutcome> PrepareAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var specPath = invocation.Operand(0)!;
        var dataPath = invocation.Operand(1)!;
        var cancellation = environment.Signals.Token;
        var diagnostics = new List<BedrockDiagnostic>();

        // Runtime options first: an unusable value is a caller mistake, and failing it here
        // means no file is opened for a run that cannot proceed. The parser accepts any text
        // for --temp-dir, so the empty case lands on the library's validating boundary — whose
        // ArgumentException is caught HERE, because an ordinary environment/option failure must
        // not reach the unexpected-fault exit (D-122 part 8).
        ConversionRuntimeOptions runtimeOptions;
        try
        {
            runtimeOptions = invocation.Value("--temp-dir") is { } tempDirectory
                ? new ConversionRuntimeOptions { TempDirectory = tempDirectory }
                : new ConversionRuntimeOptions();
        }
        catch (ArgumentException)
        {
            return new PipelineHostFailure(TempDirectoryMessage, diagnostics);
        }

        var host = new FileSpecTextSource(environment.OpenInput, FileIdentity.CreateDefault());

        string rootKey;
        string toml;
        try
        {
            rootKey = host.RegisterRoot(specPath);
            toml = host.ReadText(specPath);
        }
        catch (Exception exception) when (IsSpecReadFailure(exception))
        {
            return new PipelineHostFailure(SpecReadMessage(specPath), diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        var read = SpecReader.Read(toml, rootKey);
        diagnostics.AddRange(read.Diagnostics);
        if (!read.TryGetValue(out var rootDocument))
        {
            return new PipelineDiagnosticFailure(diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        // Root and bases share one file-identity service, so an alias of an already-loaded file
        // is a cycle rather than a fresh load, and references stay referrer-relative.
        var composed = SpecComposer.Compose(rootDocument, rootKey, host);
        diagnostics.AddRange(composed.Diagnostics);
        if (!composed.TryGetValue(out var effective))
        {
            // A missing or unreadable base keeps its phase-owned SpecExtendsNotFound; only the
            // ROOT operand is the CLI's own code-less failure.
            return new PipelineDiagnosticFailure(diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        // Schema-independent settings only. On success this stage reports nothing: the full
        // resolve below re-evaluates the same conditions through the same helpers, so anything
        // rendered here could only be a duplicate (D-098).
        var settings = SpecResolver.ResolveReadSettings(effective);
        if (!settings.TryGetValue(out var readSettings))
        {
            diagnostics.AddRange(settings.Diagnostics);
            return new PipelineDiagnosticFailure(diagnostics);
        }

        // Every data pass goes through the tracker, so hashing is inline with the bytes the
        // source was reading anyway — never a separate hash-only read (D-122 part 5).
        var input = new InputHashTracker(environment.OpenInput, dataPath);
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
            return new PipelineHostFailure(DataReadMessage(dataPath), diagnostics);
        }

        cancellation.ThrowIfCancellationRequested();

        var resolved = SpecResolver.Resolve(effective, schema);
        diagnostics.AddRange(resolved.Diagnostics);
        if (!resolved.TryGetValue(out var resolvedDocument))
        {
            return new PipelineDiagnosticFailure(diagnostics);
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
                return new PipelineHostFailure(DataReadMessage(dataPath), diagnostics);
            }

            diagnostics.AddRange(calibrated.Diagnostics);
            if (!calibrated.TryGetValue(out var value))
            {
                return new PipelineDiagnosticFailure(diagnostics);
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
        // override fingerprints are a manifest fact, never a report one (§14).
        var planned = ConversionPlanner.Plan(calibratedSpec, LabelStyle.Native);
        diagnostics.AddRange(planned.Diagnostics);
        if (!planned.TryGetValue(out var plan))
        {
            return new PipelineDiagnosticFailure(diagnostics);
        }

        var fingerprints = SpecFingerprints.ComputeNative(resolvedDocument, plan);

        if (input.HasMismatch)
        {
            return new PipelineHostFailure(InputChangedMessage(dataPath), diagnostics);
        }

        var prepared = new PreparedRun
        {
            RootDocument = rootDocument,
            RootKey = rootKey,
            Resolved = resolvedDocument,
            Calibrated = calibratedSpec,
            Shape = readSettings.Shape,
            Plan = plan,
            Fingerprints = fingerprints,
            Output = OutputSettings.Native(resolvedDocument.Document),
            SpecChain = host.Chain,
            EmitWith = triple
                ? effective => sink =>
                    Emitter.EmitTripleAsync(effective, tripleSource!, sink, runtimeOptions, cancellation)
                : effective => sink =>
                    Emitter.EmitAsync(effective, wideSource!, sink, runtimeOptions, cancellation),
            Input = input,
            SpecPath = specPath,
            DataPath = dataPath,
        };

        return new PipelinePrepared(prepared, diagnostics);
    }

    /// <summary>
    /// Renders a failed preparation: accumulated diagnostics first, then — for a host failure —
    /// the one sanitized code-less line. Always exit 1; a failed <c>Diagnosed</c> carries an
    /// Error or Fatal by construction, and a host failure is exit 1 by contract.
    /// </summary>
    public static int Fail(CliEnvironment environment, PipelineOutcome outcome, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome is PipelineHostFailure host)
        {
            return HostFailure(environment, host.Diagnostics, host.Message, cancellation);
        }

        // A cancelled run reports nothing at all — the same rule the success path follows, so a
        // signal arriving as a phase failed cannot turn a cancellation into a reported failure.
        cancellation.ThrowIfCancellationRequested();

        // Library order, verbatim: no sorting, grouping, deduplication, or rewording.
        DiagnosticRenderer.Write(environment.Error, outcome.Diagnostics);
        return 1;
    }

    /// <summary>Renders <paramref name="diagnostics"/>, then one code-less host error; exit 1.</summary>
    public static int HostFailure(
        CliEnvironment environment,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        string message,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(diagnostics);

        cancellation.ThrowIfCancellationRequested();

        DiagnosticRenderer.Write(environment.Error, diagnostics);
        environment.Error.Write(DiagnosticRenderer.RenderHostError(message));
        return 1;
    }

    /// <summary>
    /// The single exit point of a successful report command: observe cancellation one last time,
    /// render the diagnostics in library order, and write the <b>already complete</b> report in
    /// one logical write — but only when nothing failed. An Error or Fatal produces exit 1 and
    /// no report bytes at all; Warnings and Info leave the report and exit 0 intact.
    /// </summary>
    public static int Complete(
        CliEnvironment environment,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        string report,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(report);

        // Before anything is rendered: a cancelled run reports nothing at all, so this has to
        // precede the writes rather than follow them.
        cancellation.ThrowIfCancellationRequested();

        DiagnosticRenderer.Write(environment.Error, diagnostics);
        if (DiagnosticRenderer.HasErrors(diagnostics))
        {
            return 1;
        }

        environment.Out.Write(report);
        return 0;
    }

    /// <summary>
    /// Expected open/read failures for the <b>root SPEC</b> operand, matching
    /// <c>validate</c>'s set exactly so the two commands classify one unreadable spec
    /// identically. Cancellation is absent on purpose: it is exit 3, not an input error.
    /// </summary>
    internal static bool IsSpecReadFailure(Exception exception) =>
        exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or SourceReadException
            or ArgumentException
            or NotSupportedException;

    /// <summary>
    /// Expected open/read failures for the <b>DATA</b> operand, applied identically at every
    /// real data pass — schema acquisition, calibration, and the stats counting enumeration.
    /// An unreadable source must not change its public exit classification merely because the
    /// failure landed after the schema was acquired.
    /// <para>
    /// These are the established source-neutral provider/read families the existing
    /// <c>Prober</c> boundary already recognizes, listed <b>by their specific types</b>. That
    /// is the whole point: <see cref="DecoderFallbackException"/> derives from
    /// <see cref="ArgumentException"/> and <see cref="ObjectDisposedException"/> from
    /// <see cref="InvalidOperationException"/>, so naming each one keeps a real read failure on
    /// the code-less exit-1 path <em>without</em> admitting its broad base — those bases are
    /// the documented call-contract channel of the calibrator, the emitter, and the
    /// calibrated-state factories (D-093/D-098), and absorbing them would disguise a defect as
    /// a broken data file (P-14).
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
