namespace FcaBedrock.Core.Scaling;

/// <summary>
/// Which way an <see cref="OrdinalScale"/> accumulates. Spec §12.3. Over half-open
/// cut bins the boundary is fixed by direction (the only whole-bin-clean pairings,
/// D-044): <see cref="Le"/> ⇒ <c>&lt;</c> at each bin's upper edge (v2's progressive
/// output), <see cref="Ge"/> ⇒ <c>&gt;=</c> at each bin's lower edge.
/// </summary>
public enum OrdinalDirection
{
    /// <summary>
    /// "At or above": over cut bins, <c>&gt;=</c> thresholds at lower edges, with the open
    /// bottom as the tautological end. Over value bins <see cref="OrdinalBoundary"/> picks
    /// <c>&gt;=</c> or <c>&gt;</c> (§12.3).
    /// </summary>
    Ge,

    /// <summary>
    /// "Below": over cut bins, <c>&lt;</c> thresholds at upper edges, with the open top as
    /// the tautological end; matches v2 progressive. Over value bins
    /// <see cref="OrdinalBoundary"/> picks <c>&lt;=</c> or <c>&lt;</c> (§12.3).
    /// </summary>
    Le,
}
