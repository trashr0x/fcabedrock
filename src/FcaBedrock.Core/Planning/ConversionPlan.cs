using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Planning;

/// <summary>
/// The immutable, inspectable result of planning: the deterministic ordered
/// formal-attribute schema and the per-attribute emit pipelines. Pure data —
/// produced by <see cref="ConversionPlanner"/>, consumed by the emitter and the
/// writers (spec §7, decisions.md D-004/D-005).
/// </summary>
/// <param name="FormalAttributes">The ordered formal-attribute schema.</param>
/// <param name="Attributes">The per-attribute emit pipelines.</param>
/// <param name="ObjectKey">How object names are derived (§5.4).</param>
/// <param name="Execution">
/// The shape-specific execution strategy (§5 / D-082): <see cref="WideExecution"/> for
/// a wide source, <see cref="TripleExecution"/> (carrying the resolved ordering) for a
/// triple source. The emit entrypoints require the matching variant. Not a fingerprint
/// input — the execution strategy never changes output bytes.
/// </param>
public sealed record ConversionPlan(
    IReadOnlyList<FormalAttribute> FormalAttributes,
    IReadOnlyList<PlannedAttribute> Attributes,
    ObjectKey ObjectKey,
    SourceExecution Execution);
