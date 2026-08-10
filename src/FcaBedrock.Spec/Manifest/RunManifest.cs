using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;

namespace FcaBedrock.Spec.Manifest;

/// <summary>
/// The output format one committed artifact was written in — the closed set §15's
/// <c>[[run.outputs]] format</c> field admits. A closed type rather than a free
/// string so the <c>"cxt"</c>/<c>"dat"</c> spellings and the canonical
/// CXT-before-DAT emit order are owned by the writer that serializes them
/// (D-122 part 6), not by each caller.
/// </summary>
public enum RunOutputFormat
{
    /// <summary>Burmeister <c>.cxt</c> (§18.1).</summary>
    Cxt,

    /// <summary>FIMI <c>.dat</c> (§18.2).</summary>
    Dat,
}

/// <summary>
/// One committed artifact (§15 <c>[[run.outputs]]</c>): its format, the invoked
/// output-base spelling plus the ruled extension, and its raw-bytes hash. The
/// path and hash are caller-supplied facts carried verbatim — never normalized,
/// absolutized, or recomputed.
/// </summary>
public sealed record RunOutput
{
    /// <summary>Records one committed artifact.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="hash"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined member.</exception>
    public RunOutput(RunOutputFormat format, string path, string hash)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(hash);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown run output format.");
        }

        Format = format;
        Path = path;
        Hash = hash;
    }

    /// <summary>The format this artifact was written in.</summary>
    public RunOutputFormat Format { get; }

    /// <summary>The invoked output-base spelling plus the ruled extension, verbatim.</summary>
    public string Path { get; }

    /// <summary>The committed artifact's hash, verbatim.</summary>
    public string Hash { get; }
}

/// <summary>
/// One file of an <c>extends</c> chain (§15 <c>[[run.spec_files]]</c>): the
/// authored spelling — the root operand, or a referrer-relative <c>extends</c>
/// reference — and that file's raw-bytes hash. Canonical filesystem identity keys
/// (§13) never enter the manifest, so the spelling is carried verbatim.
/// </summary>
public sealed record SpecFileEntry
{
    /// <summary>Records one chain file.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="hash"/> is null.</exception>
    public SpecFileEntry(string path, string hash)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(hash);

        Path = path;
        Hash = hash;
    }

    /// <summary>The authored path spelling, verbatim.</summary>
    public string Path { get; }

    /// <summary>The file's raw-bytes hash, verbatim.</summary>
    public string Hash { get; }
}

/// <summary>
/// One <c>[[run.calibrations]]</c> entry: a <b>retained</b>
/// <see cref="AttributeCalibration"/> outcome plus, for
/// <see cref="CalibratedCuts"/> only, the caller-supplied authored discretizer
/// kind spelling.
/// <para>
/// The outcome is held by reference and read directly (D-093): the attribute
/// name, the cuts, and the values are the calibrator's retained facts, never
/// re-derived, re-resolved, or copied into a second union. An empty
/// <see cref="CalibratedCuts"/> is refused — it is not a successful outcome, so
/// no §15 entry describes it — while an empty <see cref="ObservedDomain"/>,
/// <see cref="IncludeAdditions"/>, or <see cref="PassthroughBins"/> is a
/// legitimate zero-discovery result and serializes as an explicit empty array.
/// The one extra fact is
/// <see cref="Discretizer"/> — <c>equal_frequency</c> or <c>equal_width</c> —
/// which the outcome cannot carry and which must never be guessed from the cuts,
/// the effective manual-cut state, the attribute name, or any other document
/// state.
/// </para>
/// </summary>
public sealed record RunCalibration
{
    /// <summary>Associates a retained outcome with its authored discretizer spelling, when it has one.</summary>
    /// <param name="outcome">The retained calibration outcome.</param>
    /// <param name="discretizer">
    /// The authored discretizer kind spelling — required for
    /// <see cref="CalibratedCuts"/>, and <see langword="null"/> for every other
    /// kind, whose §15 entry carries no <c>discretizer</c> field.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="outcome"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="outcome"/> is an empty <see cref="CalibratedCuts"/> — not a
    /// successful calibration outcome, so it has no manifest entry — or
    /// <paramref name="discretizer"/> is absent for a cuts outcome or supplied for
    /// a non-cut outcome that has nowhere to serialize it.
    /// </exception>
    public RunCalibration(AttributeCalibration outcome, string? discretizer)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        // §15 records one entry per *successfully* calibrated attribute. An empty
        // calibrated-cut list is not one: every auto-cut configuration has at least
        // two bins, and the calibrated-state boundary rejects a cut count other than
        // bins - 1 (D-102). Admitting it here would mint an audit entry for a run
        // that cannot exist and would blur the line between a
        // legitimate zero-discovery non-cut outcome and an unsuccessful cut
        // calibration — so the state is unrepresentable rather than serialized.
        if (outcome is CalibratedCuts { Cuts.Count: 0 })
        {
            throw new ArgumentException(
                "an empty calibrated-cut list is not a successful calibration outcome (D-102), so it has " +
                "no §15 manifest entry; the explicit empty-array form belongs to the zero-discovery " +
                "observed-domain, include-additions, and passthrough-bins outcomes only.",
                nameof(outcome));
        }

        var isCuts = outcome is CalibratedCuts;
        if (isCuts && discretizer is null)
        {
            throw new ArgumentException(
                "a cuts calibration entry requires the authored discretizer kind spelling; it cannot be " +
                "reconstructed from the cuts (§15).",
                nameof(discretizer));
        }

        if (!isCuts && discretizer is not null)
        {
            throw new ArgumentException(
                "only a cuts calibration entry carries a discretizer field (§15); pass null for an " +
                "observed-domain, include-additions, or passthrough-bins outcome.",
                nameof(discretizer));
        }

        Outcome = outcome;
        Discretizer = discretizer;
    }

    /// <summary>The retained calibration outcome, consumed as-is.</summary>
    public AttributeCalibration Outcome { get; }

    /// <summary>
    /// The authored discretizer kind spelling for a cuts outcome; null for every
    /// other kind.
    /// </summary>
    public string? Discretizer { get; }
}

/// <summary>
/// The §15 <c>[run]</c> table: the run's audit and reproduction facts, in the
/// order they are emitted. Every value is a caller-supplied fact carried
/// verbatim — this type acquires no clock, version, argv, path, hash, or
/// fingerprint of its own.
/// </summary>
public sealed record RunSection
{
    /// <summary>Records the <c>[run]</c> facts.</summary>
    /// <param name="toolVersion">The single version string <c>--version</c> prints.</param>
    /// <param name="timestamp">
    /// The run timestamp: a <b>whole-second UTC</b> instant. §15 defines the field
    /// as whole-second RFC 3339 UTC from an injected clock, and names it an audit
    /// field — so a fractional or offset value is rejected here rather than
    /// silently rewritten; producing a whole-second UTC instant is the clock
    /// seam's obligation.
    /// </param>
    /// <param name="commandLine">
    /// The complete process command-line array <b>including its actual
    /// argv[0]</b>, preserved verbatim (D-123 point 4).
    /// </param>
    /// <param name="specPath">The verbatim SPEC command operand.</param>
    /// <param name="specFileHash">The root spec's raw TOML bytes hash.</param>
    /// <param name="schemaFingerprint">The run's schema fingerprint.</param>
    /// <param name="cxtOutputFingerprint">The effective <c>.cxt</c> output fingerprint; null when no <c>.cxt</c> was written.</param>
    /// <param name="datOutputFingerprint">The effective <c>.dat</c> output fingerprint; null when no <c>.dat</c> was written.</param>
    /// <param name="inputPath">The verbatim DATA command operand.</param>
    /// <param name="inputHash">The raw input bytes hash.</param>
    /// <exception cref="ArgumentNullException">A required argument, or an element of <paramref name="commandLine"/>, is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="timestamp"/> is not a whole-second UTC instant.</exception>
    public RunSection(
        string toolVersion,
        DateTimeOffset timestamp,
        IReadOnlyList<string> commandLine,
        string specPath,
        string specFileHash,
        string schemaFingerprint,
        string? cxtOutputFingerprint,
        string? datOutputFingerprint,
        string inputPath,
        string inputHash)
    {
        ArgumentNullException.ThrowIfNull(toolVersion);
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(specPath);
        ArgumentNullException.ThrowIfNull(specFileHash);
        ArgumentNullException.ThrowIfNull(schemaFingerprint);
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(inputHash);

        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "the run timestamp must be UTC (§15 emits RFC 3339 with a 'Z' offset); normalizing an " +
                "audit value here would silently rewrite it.",
                nameof(timestamp));
        }

        // The same predicate TomlLiteral.FormatDateTime uses to decide whether to
        // emit a fractional part, so a constructed RunSection can never serialize
        // one.
        if (timestamp.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException(
                "the run timestamp must be whole-second (§15 emits no fractional seconds); truncating an " +
                "audit value here would silently rewrite it.",
                nameof(timestamp));
        }

        ToolVersion = toolVersion;
        Timestamp = timestamp;
        CommandLine = ManifestCollections.Snapshot(commandLine, nameof(commandLine));
        SpecPath = specPath;
        SpecFileHash = specFileHash;
        SchemaFingerprint = schemaFingerprint;
        CxtOutputFingerprint = cxtOutputFingerprint;
        DatOutputFingerprint = datOutputFingerprint;
        InputPath = inputPath;
        InputHash = inputHash;
    }

    /// <summary>The tool version string.</summary>
    public string ToolVersion { get; }

    /// <summary>The whole-second UTC run timestamp.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>The complete audit argv, argv[0] included, in order.</summary>
    public IReadOnlyList<string> CommandLine { get; }

    /// <summary>The verbatim SPEC operand.</summary>
    public string SpecPath { get; }

    /// <summary>The root spec's raw-bytes hash.</summary>
    public string SpecFileHash { get; }

    /// <summary>The run's schema fingerprint.</summary>
    public string SchemaFingerprint { get; }

    /// <summary>The effective <c>.cxt</c> output fingerprint, or null.</summary>
    public string? CxtOutputFingerprint { get; }

    /// <summary>The effective <c>.dat</c> output fingerprint, or null.</summary>
    public string? DatOutputFingerprint { get; }

    /// <summary>The verbatim DATA operand.</summary>
    public string InputPath { get; }

    /// <summary>The raw input bytes hash.</summary>
    public string InputHash { get; }
}

/// <summary>
/// Snapshots the caller-owned collections the manifest model accepts, so no
/// public property is backed by a list the caller still holds.
/// </summary>
internal static class ManifestCollections
{
    internal static ImmutableArray<T> Snapshot<T>(IReadOnlyList<T> items, string parameterName)
        where T : class
    {
        var builder = ImmutableArray.CreateBuilder<T>(items.Count);
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentNullException(parameterName, "the collection contains a null element.");
            }

            builder.Add(item);
        }

        return builder.MoveToImmutable();
    }
}

/// <summary>
/// One §15 run manifest, as immutable facts: the <c>[run]</c> table, the
/// committed <c>[[run.outputs]]</c>, the <c>extends</c>-chain
/// <c>[[run.spec_files]]</c> (empty when the spec is a single file), and the
/// retained <c>[[run.calibrations]]</c> (empty when no outcome was retained).
/// <para>
/// The model carries every §15 field without loss and decides nothing:
/// serialization is <c>RunManifestWriter.Write</c>'s job, and acquisition — clock,
/// version, argv, paths, hashes, fingerprints — is the composing host's. Every
/// caller-owned collection is snapshotted at construction, so a later mutation of
/// the caller's list cannot reach the emitted bytes, and element order and
/// explicit emptiness are preserved exactly.
/// </para>
/// </summary>
public sealed record RunManifest
{
    /// <summary>Assembles one run manifest.</summary>
    /// <param name="run">The <c>[run]</c> facts.</param>
    /// <param name="outputs">The committed artifacts — one or two, at most one per format.</param>
    /// <param name="specFiles">The <c>extends</c> chain root-first then bases; empty when there is no chain.</param>
    /// <param name="calibrations">The retained outcomes in <c>CalibratedSpec.Calibrations</c> order; empty when none.</param>
    /// <exception cref="ArgumentNullException">An argument, or a collection element, is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="outputs"/> is empty or repeats a format, or a per-format
    /// output fingerprint on <paramref name="run"/> is present without its format
    /// (or absent with it) — §15 makes each fingerprint present <b>iff</b> that
    /// format was written.
    /// </exception>
    public RunManifest(
        RunSection run,
        IReadOnlyList<RunOutput> outputs,
        IReadOnlyList<SpecFileEntry> specFiles,
        IReadOnlyList<RunCalibration> calibrations)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(specFiles);
        ArgumentNullException.ThrowIfNull(calibrations);

        Run = run;
        Outputs = ManifestCollections.Snapshot(outputs, nameof(outputs));
        SpecFiles = ManifestCollections.Snapshot(specFiles, nameof(specFiles));
        Calibrations = ManifestCollections.Snapshot(calibrations, nameof(calibrations));

        RequireCoherentOutputs(run, Outputs);
    }

    /// <summary>The <c>[run]</c> table.</summary>
    public RunSection Run { get; }

    /// <summary>The committed artifacts; the writer emits them in canonical format order.</summary>
    public IReadOnlyList<RunOutput> Outputs { get; }

    /// <summary>The <c>extends</c> chain files, root-first then bases; empty when there is no chain.</summary>
    public IReadOnlyList<SpecFileEntry> SpecFiles { get; }

    /// <summary>The retained calibration outcomes, in spec-attribute order; empty when none.</summary>
    public IReadOnlyList<RunCalibration> Calibrations { get; }

    // §15: a manifest-bearing run committed the artifacts --format selected, so it
    // records one or two, never two of a kind; and the per-format fingerprint
    // fields are present iff that format was written. Checking both here — the one
    // place that sees the [run] table and the output set together — keeps a
    // structurally impossible manifest unrepresentable (P-10) rather than letting
    // the writer emit an incoherent audit record.
    private static void RequireCoherentOutputs(RunSection run, IReadOnlyList<RunOutput> outputs)
    {
        if (outputs.Count == 0)
        {
            throw new ArgumentException(
                "a run manifest records the committed artifacts of its run; there is at least one (§15).",
                nameof(outputs));
        }

        var cxt = 0;
        var dat = 0;
        foreach (var output in outputs)
        {
            if (output.Format == RunOutputFormat.Cxt)
            {
                cxt++;
            }
            else
            {
                dat++;
            }
        }

        if (cxt > 1 || dat > 1)
        {
            throw new ArgumentException(
                "a run commits at most one artifact per format; the outputs repeat a format (§15).",
                nameof(outputs));
        }

        if ((run.CxtOutputFingerprint is not null) != (cxt == 1))
        {
            throw new ArgumentException(
                "cxt_output_fingerprint is present iff a .cxt artifact was written (§15).",
                nameof(run));
        }

        if ((run.DatOutputFingerprint is not null) != (dat == 1))
        {
            throw new ArgumentException(
                "dat_output_fingerprint is present iff a .dat artifact was written (§15).",
                nameof(run));
        }
    }
}
