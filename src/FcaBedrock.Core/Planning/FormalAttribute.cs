using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Planning;

/// <summary>
/// One planned formal attribute (one context column). <see cref="Id"/> is its
/// 0-based position in the deterministic global order (§17); <see cref="RenderedName"/>
/// is what the <c>.cxt</c> writer emits; <see cref="Identity"/> is its canonical,
/// style-independent identity; <see cref="Bin"/> is the structural twin of
/// <see cref="FormalAttributeIdentity.BinKey"/> that the fingerprint encoder
/// hashes as the <c>"bin"</c> field (§14, D-069/D-077) — same planner walk, so
/// key and structure cannot drift.
/// </summary>
public sealed record FormalAttribute(
    int Id,
    string RenderedName,
    FormalAttributeIdentity Identity,
    CanonicalBin Bin);
