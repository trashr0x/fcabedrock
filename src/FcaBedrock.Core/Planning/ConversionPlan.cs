using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Planning;

/// <summary>
/// The immutable, inspectable result of planning: the deterministic ordered
/// formal-attribute schema, the per-attribute emit pipelines, and the executable
/// restrictions. Pure data — produced by <see cref="ConversionPlanner"/>, consumed
/// by the emitter and the writers (spec §7, decisions.md D-004/D-005).
/// <para>
/// A sealed class with an <b>internal</b> constructor (planner-owned), so a plan
/// pairing incompatible attributes/pipelines/execution/spec/style is
/// unrepresentable outside Core (D-098): its lists are planner-built immutable
/// arrays, and it carries the <see cref="CalibratedSpec"/> it was planned from so
/// the plan ↔ spec/schema pairing is structural, and the
/// <see cref="LabelStyle"/> its rendered names were baked with.
/// </para>
/// </summary>
public sealed class ConversionPlan
{
    internal ConversionPlan(
        ImmutableArray<FormalAttribute> formalAttributes,
        ImmutableArray<PlannedAttribute> attributes,
        ImmutableArray<PlannedRestriction> restrictions,
        ObjectKey objectKey,
        SourceExecution execution,
        CalibratedSpec calibrated,
        LabelStyle labelStyle)
    {
        FormalAttributes = formalAttributes;
        Attributes = attributes;
        Restrictions = restrictions;
        ObjectKey = objectKey;
        Execution = execution;
        Calibrated = calibrated;
        LabelStyle = labelStyle;
    }

    /// <summary>The ordered formal-attribute schema.</summary>
    public IReadOnlyList<FormalAttribute> FormalAttributes { get; }

    /// <summary>The per-attribute emit pipelines.</summary>
    public IReadOnlyList<PlannedAttribute> Attributes { get; }

    /// <summary>
    /// The executable object-level restrictions, in spec-attribute order — one per
    /// attribute with a non-empty <c>restrict_to</c>, included or filter-only; empty
    /// when the spec restricts nothing (§10.4/D-091).
    /// </summary>
    public IReadOnlyList<PlannedRestriction> Restrictions { get; }

    /// <summary>How object names are derived (§5.4).</summary>
    public ObjectKey ObjectKey { get; }

    /// <summary>
    /// The shape-specific execution strategy (§5 / D-082): <see cref="WideExecution"/> for a
    /// wide source, <see cref="TripleExecution"/> for a triple source. The emit entrypoints
    /// require the matching variant. Not a fingerprint input.
    /// </summary>
    public SourceExecution Execution { get; }

    /// <summary>The calibrated state this plan was produced from — the plan ↔ spec/schema pairing (D-098).</summary>
    public CalibratedSpec Calibrated { get; }

    /// <summary>The label style the rendered names were baked with (D-044); pairs with the cxt output fingerprint (§14).</summary>
    public LabelStyle LabelStyle { get; }
}
