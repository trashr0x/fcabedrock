using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// One conversion prepared up to — and stopping at — the measured boundary: the plan is resolved,
/// calibrated, and fixed, and <see cref="Emit"/> is a closure that opens a <b>fresh</b> source
/// enumeration each time it is invoked. Nothing here is open when a benchmark's timer starts.
/// </summary>
internal sealed record PreparedConversion(
    ConversionPlan Plan,
    Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> Emit);

/// <summary>
/// Builds a conversion through the real production sequence — read spec, resolve read settings,
/// open a session, read the schema, resolve against it, bind the source, calibrate, plan — so a
/// benchmark measures the shipped pipeline rather than a hand-assembled approximation of it.
/// <para>
/// Every stage runs in benchmark <em>setup</em>, outside any measured interval. A diagnostic of
/// Error or Fatal severity here is a broken corpus or a broken spec, not a data condition under
/// test, so it throws rather than being reported: the exception channel is the right one for a
/// misconfigured harness (P-14's "programmer error" side).
/// </para>
/// </summary>
internal static class ConversionPipeline
{
    /// <summary>
    /// Prepares a conversion from an authored TOML spec file over a data file.
    /// <para>
    /// <paramref name="grouping"/> reaches the production backend's own internal options through the
    /// benchmark-only friend grant D-124 records, so a budget or fan-in experiment measures the
    /// shipped grouping path rather than a re-implementation of it. Left null, the production
    /// defaults apply, which is what every case that is not an experiment uses.
    /// </para>
    /// </summary>
    public static async Task<PreparedConversion> FromSpecFileAsync(
        string specPath,
        string dataPath,
        LabelStyle labelStyle = LabelStyle.Native,
        GroupingOptions? grouping = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specPath);
        ArgumentNullException.ThrowIfNull(dataPath);

        var read = SpecReader.Read(await File.ReadAllTextAsync(specPath, cancellationToken).ConfigureAwait(false), specPath);
        return await PrepareAsync(Require(read, "spec read"), dataPath, labelStyle, grouping, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Prepares a conversion from a v2 <c>.bed</c> through the sanctioned migrate-then-resolve
    /// route — the same path the golden harness drives, so the immutable v2 outputs remain a valid
    /// byte oracle for what this produces.
    /// </summary>
    public static async Task<PreparedConversion> FromBedFileAsync(
        string bedPath,
        string dataPath,
        BindingSection binding,
        ScalingMode scalingMode,
        LabelStyle labelStyle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bedPath);
        ArgumentNullException.ThrowIfNull(dataPath);

        var read = BedReader.Read(await File.ReadAllTextAsync(bedPath, cancellationToken).ConfigureAwait(false), bedPath);
        var migrated = BedMigrator.Migrate(Require(read, "bed read"), binding, scalingMode, bedPath);
        return await PrepareAsync(Require(migrated, "bed migrate"), dataPath, labelStyle, grouping: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Opens an <b>unbound</b> source session over <paramref name="dataPath"/> under
    /// <paramref name="specPath"/>'s read settings. The session itself opens no stream: a stream is
    /// opened per read, so a drain benchmark starts from a prepared but unopened input exactly as
    /// the measured-interval contract requires.
    /// </summary>
    public static async Task<ISourceSession> OpenSessionAsync(
        string specPath, string dataPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specPath);
        ArgumentNullException.ThrowIfNull(dataPath);

        var read = SpecReader.Read(await File.ReadAllTextAsync(specPath, cancellationToken).ConfigureAwait(false), specPath);
        var settings = Require(SpecResolver.ResolveReadSettings(Require(read, "spec read")), "read settings");
        return CreateSession(settings, dataPath);
    }

    /// <summary>
    /// Runs the real read-settings/session/schema/resolve/calibrate sequence and returns the
    /// <b>calibrated state</b> rather than a plan.
    /// <para>
    /// It exists for the pure-planner benchmark, which needs exactly the input
    /// <see cref="FcaBedrock.Core.Planning.ConversionPlanner.Plan"/> takes and must not have the
    /// planning already done for it (D-093/D-098: plan consumes calibrated state, so this is the
    /// honest boundary between the two).
    /// </para>
    /// </summary>
    public static async Task<CalibratedSpec> CalibrateAsync(
        string specPath, string dataPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specPath);
        ArgumentNullException.ThrowIfNull(dataPath);

        var document = RequireDocument(
            await File.ReadAllTextAsync(specPath, cancellationToken).ConfigureAwait(false));
        var settings = RequireReadSettings(document);
        var session = CreateSession(settings, dataPath);
        var schema = await session.GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        var resolved = RequireResolved(document, schema).Resolved;

        return Require(
            session switch
            {
                TripleCsvSession triple => await Calibrator
                    .CalibrateTripleAsync(
                        resolved, triple.Bind(resolved), BenchmarkGrouping.Default, observer: null, cancellationToken)
                    .ConfigureAwait(false),
                WideCsvSession wide => await Calibrator
                    .CalibrateAsync(
                        resolved, wide.Bind(resolved), BenchmarkGrouping.Default, observer: null, cancellationToken)
                    .ConfigureAwait(false),
                _ => throw new InvalidOperationException("unrecognized session shape."),
            },
            "calibrate");
    }

    /// <summary>Reads an authored spec, or throws with its diagnostics.</summary>
    public static SpecDocument RequireDocument(string specText)
    {
        ArgumentNullException.ThrowIfNull(specText);
        return Require(SpecReader.Read(specText), "spec read");
    }

    /// <summary>Resolves the schema-independent read settings, or throws with their diagnostics.</summary>
    public static SourceReadSettings RequireReadSettings(SpecDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Require(SpecResolver.ResolveReadSettings(document), "read settings");
    }

    /// <summary>Resolves a document against a schema, or throws with its diagnostics.</summary>
    public static ResolvedDocument RequireResolved(SpecDocument document, SourceSchema schema)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Require(SpecResolver.Resolve(document, schema), "resolve");
    }

    /// <summary>Creates the shape-matched session for <paramref name="settings"/>.</summary>
    public static ISourceSession CreateSession(SourceReadSettings settings, string dataPath)
    {
        ArgumentNullException.ThrowIfNull(dataPath);
        return CreateSession(settings, () => File.OpenRead(dataPath));
    }

    /// <summary>
    /// Creates the shape-matched session over an arbitrary stream opener.
    /// <para>
    /// A session opens lazily, per read, so the opener is the seam a hashing or counting wrapper
    /// belongs in — which is exactly how the CLI supplies its input-stability tracker. The
    /// hash-pair benchmarks use it for the same reason: the wrapper has to sit where production puts
    /// it, or the measured chunking is not the production chunking.
    /// </para>
    /// </summary>
    public static ISourceSession CreateSession(SourceReadSettings settings, Func<Stream> open)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(open);

        return settings.Shape == SourceShape.Triple
            ? new TripleCsvSession(open, settings)
            : new WideCsvSession(open, settings);
    }

    private static async Task<PreparedConversion> PrepareAsync(
        SpecDocument document,
        string dataPath,
        LabelStyle labelStyle,
        GroupingOptions? grouping,
        CancellationToken cancellationToken)
    {
        // Never GroupingOptions.Default: that spools to the OS temporary directory, which is a
        // different volume from everything else this run touches.
        var options = grouping ?? BenchmarkGrouping.Default;
        var settings = Require(SpecResolver.ResolveReadSettings(document), "read settings");

        // The two-stage bootstrap (D-098): a source cannot exist before resolution, and name-bound
        // resolution needs the schema, so read settings -> session -> schema -> resolve -> bind.
        if (settings.Shape == SourceShape.Triple)
        {
            var session = (TripleCsvSession)CreateSession(settings, dataPath);
            var schema = await session.GetSchemaAsync(cancellationToken).ConfigureAwait(false);
            var resolved = Require(SpecResolver.Resolve(document, schema), "resolve");
            var source = session.Bind(resolved.Resolved);
            var calibrated = Require(
                await Calibrator.CalibrateTripleAsync(resolved.Resolved, source, options, observer: null, cancellationToken)
                    .ConfigureAwait(false),
                "calibrate");
            var plan = Require(ConversionPlanner.Plan(calibrated, labelStyle), "plan");
            return new PreparedConversion(
                plan, sink => Emitter.EmitTripleAsync(plan, source, sink, options, cancellationToken));
        }
        else
        {
            var session = (WideCsvSession)CreateSession(settings, dataPath);
            var schema = await session.GetSchemaAsync(cancellationToken).ConfigureAwait(false);
            var resolved = Require(SpecResolver.Resolve(document, schema), "resolve");
            var source = session.Bind(resolved.Resolved);
            var calibrated = Require(
                await Calibrator.CalibrateAsync(resolved.Resolved, source, options, observer: null, cancellationToken)
                    .ConfigureAwait(false),
                "calibrate");
            var plan = Require(ConversionPlanner.Plan(calibrated, labelStyle), "plan");
            return new PreparedConversion(plan, sink => Emitter.EmitAsync(plan, source, sink, options, cancellationToken));
        }
    }

    private static T Require<T>(Diagnosed<T> result, string stage)
    {
        if (!result.TryGetValue(out var value))
        {
            throw new InvalidOperationException($"benchmark {stage} failed: {Describe(result.Diagnostics)}");
        }

        // A Warning is legitimate on some prepared cases, but nothing at Error or above may be
        // carried past setup: a benchmark whose plan is invalid measures nothing.
        var blocking = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            .ToList();
        return blocking.Count == 0
            ? value
            : throw new InvalidOperationException($"benchmark {stage} reported errors: {Describe(blocking)}");
    }

    /// <summary>Renders diagnostics for a setup-failure message.</summary>
    public static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return string.Join(
            Environment.NewLine,
            diagnostics.Select(diagnostic => $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}"));
    }

    /// <summary>The label style implied by a writer preset: v2-compat bytes imply v2-compat labels (D-044).</summary>
    public static LabelStyle LabelStyleFor(WriterOptions options) =>
        options == WriterOptions.V2Compat ? LabelStyle.V2Compat : LabelStyle.Native;
}
