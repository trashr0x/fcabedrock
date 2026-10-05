namespace FcaBedrock.Core.Scaling;

/// <summary>
/// Which way an <see cref="OrdinalScale"/> accumulates. Spec §12.3. Over half-open
/// cut bins the boundary is fixed by direction (the only whole-bin-clean pairings,
/// D-047): <see cref="Le"/> ⇒ <c>&lt;</c> at each bin's upper edge (v2's progressive
/// output), <see cref="Ge"/> ⇒ <c>&gt;=</c> at each bin's lower edge.
/// </summary>
public enum OrdinalDirection
{
    /// <summary>
    /// "At or above": over cut bins, <c>&gt;=</c> thresholds at lower edges, with the bottom
    /// bin's threshold as the tautological end (<c>all</c> for an open bottom, the first cut
    /// for a closed one). Over value bins <see cref="OrdinalBoundary"/> picks
    /// <c>&gt;=</c> or <c>&gt;</c> (§12.3).
    /// </summary>
    Ge,

    /// <summary>
    /// "Below": over cut bins, <c>&lt;</c> thresholds at upper edges, with the top bin's
    /// threshold as the tautological end (<c>all</c> for an open top, the last cut for a
    /// closed one); matches v2 progressive. Over value bins
    /// <see cref="OrdinalBoundary"/> picks <c>&lt;=</c> or <c>&lt;</c> (§12.3).
    /// </summary>
    Le,
}
