namespace FcaBedrock.Core.Scaling;

/// <summary>
/// The structural form of a formal attribute's canonical bin/threshold key —
/// what the schema fingerprint encodes as its <c>"bin"</c> field (spec §14,
/// decisions.md D-069/D-077). The string
/// <see cref="FcaBedrock.Core.Planning.FormalAttributeIdentity.BinKey"/> remains
/// the identity/collision unit; this is its machine-readable twin, produced by
/// the same planner walk so the two cannot drift. A closed hierarchy: external
/// assemblies construct the leaves but cannot derive new ones.
/// </summary>
public abstract record CanonicalBin;

/// <summary>
/// A non-interval bin key, encoded as its plain string: a value bin's label, an
/// ordinal threshold's canonical key (including the open-end <c>all</c>, D-047),
/// the dichotomic empty key, or the <c>missing</c> column key (§10.5).
/// </summary>
/// <param name="Label">The canonical key string (equal to the identity's bin key).</param>
public sealed record ValueBin(string Label) : CanonicalBin;

/// <summary>
/// A numeric cut bin (<c>manual_cuts</c>, §11.2): <c>[Lo, Hi)</c>, where a
/// <see langword="null"/> end means <i>unbounded</i> (runs to ±∞). The D-069
/// <c>lo_open</c>/<c>hi_open</c> fingerprint flags derive from that nullness and
/// mean "unbounded end", never interval inclusivity — every bounded cut bin is
/// uniformly closed-open (§11.2), so inclusivity carries no information (D-077).
/// </summary>
/// <param name="Lo">The finite lower cut, or <see langword="null"/> for −∞.</param>
/// <param name="Hi">The finite upper cut, or <see langword="null"/> for +∞.</param>
public sealed record NumericCutBin(double? Lo, double? Hi) : CanonicalBin;

/// <summary>
/// A categorical cut bin (<c>ordered_cuts</c>, §11.8): the same shape as
/// <see cref="NumericCutBin"/> with the cut category strings as bounds.
/// </summary>
/// <param name="Lo">The lower cut category, or <see langword="null"/> for the open low end.</param>
/// <param name="Hi">The upper cut category, or <see langword="null"/> for the open high end.</param>
public sealed record TextCutBin(string? Lo, string? Hi) : CanonicalBin;
