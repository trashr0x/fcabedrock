namespace FcaBedrock.Core.Scaling;

/// <summary>
/// Which way an <see cref="OrdinalScale"/> accumulates. Spec §12.3. Over half-open
/// cut bins the boundary is fixed by direction (the only whole-bin-clean pairings,
/// D-044): <see cref="Le"/> ⇒ <c>&lt;</c> at each bin's upper edge (v2's progressive
/// output), <see cref="Ge"/> ⇒ <c>&gt;=</c> at each bin's lower edge.
/// </summary>
public enum OrdinalDirection
{
    /// <summary>"At or above" — <c>&gt;=</c> thresholds at lower edges; tautological end is the open bottom.</summary>
    Ge,

    /// <summary>"Below" — <c>&lt;</c> thresholds at upper edges; tautological end is the open top. Matches v2 progressive.</summary>
    Le,
}
