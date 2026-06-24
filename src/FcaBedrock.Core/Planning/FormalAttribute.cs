namespace FcaBedrock.Core.Planning;

/// <summary>
/// One planned formal attribute (one context column). <see cref="Id"/> is its
/// 0-based position in the deterministic global order (§17); <see cref="RenderedName"/>
/// is what the <c>.cxt</c> writer emits; <see cref="Identity"/> is its canonical,
/// style-independent identity.
/// </summary>
public sealed record FormalAttribute(
    int Id,
    string RenderedName,
    FormalAttributeIdentity Identity);
