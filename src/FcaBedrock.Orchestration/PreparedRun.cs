using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Orchestration;

/// <summary>Emits a prepared run's objects for <c>plan</c>, routing diagnostics to the sink, observing the token.</summary>
internal delegate IAsyncEnumerable<EmittedObject> PreparedRunEmitter(
    ConversionPlan plan, ICollection<BedrockDiagnostic> sink, CancellationToken cancellation);

/// <summary>
/// The state one prepared run carries: what the reports read, plus the facts a conversion also
/// needs to emit, fingerprint the effective output, and compose a manifest.
/// <para>
/// <b>It is one run's state or nothing</b> (D-098). Every member comes from the same preparation
/// (the same resolution token, the same calibrated state, the same bound source), and every
/// further plan is produced from that calibrated state inside Orchestration, so a document, a
/// calibration, a plan, a source, or a fingerprint from a different run cannot be paired with
/// these. It holds no open stream between calls. Each operation that reads data observes the token
/// passed to that operation, and each further data pass is hashed and compared with the first
/// complete pass under the existing stability rule.
/// </para>
/// <para>
/// A closed family: the only constructor is <see langword="private protected"/>, so no assembly
/// but this one can add a kind of prepared run. The documents it returns are owned snapshots that
/// Orchestration never mutates after returning them; callers must not mutate them either. A
/// prepared run belongs to one operation and is never used concurrently.
/// </para>
/// </summary>
public abstract class PreparedRun
{
    private readonly PreparedRunEmitter _emit;

    private protected PreparedRun(
        SpecDocument rootDocument,
        string rootKey,
        ResolvedDocument resolved,
        CalibratedSpec calibrated,
        SourceShape shape,
        ConversionPlan plan,
        ComputedFingerprints fingerprints,
        OutputSettings output,
        IReadOnlyList<SpecChainFile> specChain,
        PreparedRunEmitter emit,
        InputHashTracker input,
        string specPath,
        string dataPath)
    {
        RootDocument = rootDocument;
        RootKey = rootKey;
        Resolved = resolved;
        Calibrated = calibrated;
        Shape = shape;
        Plan = plan;
        Fingerprints = fingerprints;
        Output = output;

        // A copy, never the text source's live list, so a later read through that source cannot
        // change what this run reports (D-140).
        SpecChain = specChain.ToImmutableArray();
        _emit = emit;
        Input = input;
        SpecPath = specPath;
        DataPath = dataPath;
        IsFullyFrozen = !CalibratedSpec.RequiresData(resolved.Resolved.Spec);
    }

    /// <summary>
    /// The <b>root</b> document, as authored: the owner of the stored <c>[spec]</c> fields every
    /// comparison and report reads. Deliberately not the composed document: a base file's stored
    /// fingerprints are never the root's (§13 rule 8 / D-078).
    /// </summary>
    public SpecDocument RootDocument { get; }

    /// <summary>The root's canonical key: the <c>file</c> location stale warnings carry.</summary>
    public string RootKey { get; }

    /// <summary>
    /// The native plan (<see cref="LabelStyle.Native"/>, with the document's own
    /// <c>bin_label_unicode</c>), planned once.
    /// </summary>
    public ConversionPlan Plan { get; }

    /// <summary>The three native fingerprints computed from the paired resolved document and plan.</summary>
    public ComputedFingerprints Fingerprints { get; }

    /// <summary>True when no part of the spec needs data: §14's definition of fully frozen.</summary>
    public bool IsFullyFrozen { get; }

    /// <summary>
    /// Every spec file this run loaded, in root-first-then-bases order, with its authored
    /// spelling, raw-bytes hash and load-time identity (§15 <c>[[run.spec_files]]</c>). An
    /// immutable snapshot.
    /// </summary>
    public IReadOnlyList<SpecChainFile> SpecChain { get; }

    /// <summary>
    /// The composed document paired with its resolution token: the effective state the native
    /// fingerprints were computed over.
    /// </summary>
    internal ResolvedDocument Resolved { get; }

    /// <summary>
    /// The calibrated state every plan is produced from, and the owner of the retained calibration
    /// outcomes the run manifest serializes without re-derivation (D-093).
    /// </summary>
    internal CalibratedSpec Calibrated { get; }

    /// <summary>The resolved source shape; the D-087 v2-compat <c>.dat</c> final newline reads it.</summary>
    internal SourceShape Shape { get; }

    /// <summary>The resolved §8 <c>[output]</c> settings, before any host override.</summary>
    internal OutputSettings Output { get; }

    /// <summary>The input-stability tracker, re-checked after any further pass.</summary>
    internal InputHashTracker Input { get; }

    /// <summary>The SPEC path as the request spelled it; §15 records it as authored.</summary>
    internal string SpecPath { get; }

    /// <summary>The DATA path as the request spelled it; the host-failure messages name it, and §15 records it.</summary>
    internal string DataPath { get; }

    /// <summary>
    /// Emits this run's objects under <paramref name="plan"/> from the shape-bound source. The shape
    /// was decided where the source was built, so a wide plan can never be handed to the triple
    /// entrypoint (the <c>GoldenConversion.PreparedConversion</c> shape). The token is the calling
    /// operation's, never the preparation's.
    /// </summary>
    internal IAsyncEnumerable<EmittedObject> EmitWith(
        ConversionPlan plan, ICollection<BedrockDiagnostic> sink, CancellationToken cancellation) =>
        _emit(plan, sink, cancellation);

    /// <summary>
    /// Plans this run's <em>same</em> calibrated state under the effective <paramref name="style"/>
    /// and <paramref name="settings"/>: the only way a second plan is produced, which is what keeps
    /// the native/effective pair exact (D-044/D-077). The plan takes the settings' own
    /// <c>bin_label_unicode</c>, the flag their <c>.cxt</c> fingerprint inputs hash. Planning is
    /// pure, so the second plan costs no data pass.
    /// </summary>
    internal Diagnosed<ConversionPlan> PlanEffective(LabelStyle style, OutputSettings settings) =>
        ConversionPlanner.Plan(Calibrated, style, settings.BinLabelUnicode);
}

/// <summary>
/// A prepared run whose root was read from a file: the only kind the convert sequence and the
/// manifest composer accept, so an export of an in-memory root cannot be expressed (D-139 part 2).
/// </summary>
public sealed class PreparedFileRun : PreparedRun
{
    internal PreparedFileRun(
        SpecDocument rootDocument,
        string rootKey,
        ResolvedDocument resolved,
        CalibratedSpec calibrated,
        SourceShape shape,
        ConversionPlan plan,
        ComputedFingerprints fingerprints,
        OutputSettings output,
        IReadOnlyList<SpecChainFile> specChain,
        PreparedRunEmitter emit,
        InputHashTracker input,
        string specPath,
        string dataPath)
        : base(
            rootDocument, rootKey, resolved, calibrated, shape, plan, fingerprints, output, specChain, emit,
            input, specPath, dataPath)
    {
    }
}
