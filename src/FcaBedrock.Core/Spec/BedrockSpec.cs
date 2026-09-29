namespace FcaBedrock.Core.Spec;

/// <summary>
/// A fully-resolved Bedrock spec: the binding plus the ordered attributes. The
/// in-memory form the planner consumes, whether it came from a TOML file or a
/// migrated v2 <c>.bed</c> file.
/// </summary>
public sealed record BedrockSpec(
    Binding Binding,
    IReadOnlyList<AttributeSpec> Attributes);
