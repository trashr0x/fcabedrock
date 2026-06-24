namespace FcaBedrock.Core.Spec;

/// <summary>
/// A fully-resolved Bedrock spec: the binding plus the ordered attributes. The
/// in-memory form the planner consumes, regardless of whether it came from a v2
/// <c>.bed</c> migration (slice 1) or a TOML file (M2).
/// </summary>
public sealed record BedrockSpec(
    Binding Binding,
    IReadOnlyList<AttributeSpec> Attributes);
