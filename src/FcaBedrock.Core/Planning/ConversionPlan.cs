using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Planning;

/// <summary>
/// The immutable, inspectable result of planning: the deterministic ordered
/// formal-attribute schema and the per-attribute emit pipelines. Pure data —
/// produced by <see cref="ConversionPlanner"/>, consumed by the emitter and the
/// writers (spec §7, decisions.md D-004/D-005).
/// </summary>
public sealed record ConversionPlan(
    IReadOnlyList<FormalAttribute> FormalAttributes,
    IReadOnlyList<PlannedAttribute> Attributes,
    ObjectKey ObjectKey);
