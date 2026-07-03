namespace FcaBedrock.Core.Scaling;

/// <summary>
/// Whether an ordinal threshold includes its boundary value. Spec §12.3;
/// default inclusive. Over cut bins the geometry fixes the operator by
/// direction (D-044/D-060), so the knob is meaningful for the value-bin
/// ordinal path only.
/// </summary>
public enum OrdinalBoundary
{
    /// <summary>Non-strict comparison (<c>&gt;=</c> / <c>&lt;=</c>) — the default.</summary>
    Inclusive,

    /// <summary>Strict comparison (<c>&gt;</c> / <c>&lt;</c>).</summary>
    Strict,
}
