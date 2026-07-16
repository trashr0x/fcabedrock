using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;

namespace FcaBedrock.Conversion;

/// <summary>
/// The pinned <c>equal_frequency</c> / <c>percentile_p1_p99</c> numeric rules (spec §11.5,
/// D-088/D-103, the G-5 formulas). One place each, so no production path carries a second
/// copy: rank selection, gap allocation, and cut placement.
/// <para>
/// The split is deliberate and load-bearing (P-11): <b>rank selection is exact integer
/// arithmetic</b> (no floating target is ever formed), while <b>cut placement is
/// binary64</b> (the cut is a data value, so it lives in the data's own type). Mixing the
/// two is the classic quantile bug — a rank target rounded through a <c>double</c> silently
/// picks the wrong order statistic once <c>N</c> approaches 2^53.
/// </para>
/// <para>
/// Vocabulary, fixed by §11.5 and used throughout: the aggregated population is distinct
/// ascending finite values <c>v_1 … v_m</c> with counts <c>c_i</c>, cumulative
/// <c>C_i</c> (<c>C_0 = 0</c>), total <c>N = C_m</c>. A <b>gap</b> <c>g ∈ 1 … m-1</c> is the
/// interval between <c>v_g</c> and <c>v_{g+1}</c>; a cut is placed in a gap, so
/// <c>bins - 1</c> cuts need <c>bins - 1</c> distinct gaps and therefore <c>m ≥ bins</c>.
/// </para>
/// </summary>
internal static class QuantileSelection
{
    /// <summary>
    /// Whether boundary <paramref name="k"/>'s target has been reached by the group whose
    /// cumulative count is <paramref name="cumulative"/>: the exact rational comparison
    /// <c>C_i·bins ≥ N·k</c>.
    /// <para>
    /// The target <c>N·k / bins</c> is <b>never materialized</b> — neither as a
    /// <see cref="double"/> nor a <see cref="decimal"/>. Both sides are cross-multiplied into
    /// <see cref="UInt128"/> instead, which is exact by construction: counts are non-negative
    /// checked <see cref="long"/> (≤ 2^63) and <c>bins</c>/<c>k</c> are positive
    /// <see cref="int"/> (&lt; 2^31), so each product is below 2^94 and cannot overflow 128
    /// bits. This is what makes selection correct at 7.3M–73M records and beyond (D-007):
    /// a <c>double</c> rank target loses integer precision above 2^53 and would select the
    /// group one too early.
    /// </para>
    /// </summary>
    public static bool TargetReached(long cumulative, long total, int bins, int k) =>
        Scale(cumulative, bins) >= Scale(total, k);

    /// <summary>
    /// Whether boundary <paramref name="k"/>'s target lands <b>exactly</b> on this group's
    /// upper edge: <c>N·k == C_i·bins</c>. An exact edge already separates whole groups, so
    /// there is no tie to resolve and <see cref="TiePolicy"/> does not apply (§11.5:
    /// <c>tie_policy</c> governs a boundary landing <i>inside</i> a run of equal values).
    /// </summary>
    public static bool TargetIsEdge(long cumulative, long total, int bins, int k) =>
        Scale(cumulative, bins) == Scale(total, k);

    /// <summary>
    /// The desired gap <c>d(k)</c> for a boundary whose target falls at
    /// <paramref name="groupIndex"/> (1-based), before feasibility is considered.
    /// <list type="bullet">
    /// <item>Exactly on the group's edge (<paramref name="isEdge"/>) → <c>d = i</c>,
    /// regardless of policy: the boundary already sits between two whole groups.</item>
    /// <item>Strictly inside the group → <see cref="TiePolicy.Left"/> puts the whole group
    /// below the cut (<c>d = i</c>), <see cref="TiePolicy.Right"/> above it
    /// (<c>d = i - 1</c>).</item>
    /// </list>
    /// The result is a <i>preference</i>: it may be <c>0</c> or <c>m</c> — not a gap at all —
    /// which <see cref="AllocateGap"/> resolves. That is normal, not an error (G-5).
    /// </summary>
    public static int DesiredGap(int groupIndex, bool isEdge, TiePolicy tiePolicy) =>
        isEdge || tiePolicy == TiePolicy.Left ? groupIndex : groupIndex - 1;

    /// <summary>
    /// Allocates boundary <paramref name="k"/>'s gap: the desired gap clamped into the
    /// feasibility window <c>[lo, hi]</c>, where <c>lo = p + 1</c>
    /// (<paramref name="previousGap"/>, initially 0) and
    /// <c>hi = m - 1 - (bins - 1 - k)</c>.
    /// <para>
    /// The window is what makes the §11.5 distinct-gap obligation <b>total</b> rather than
    /// best-effort, and it is authoritative over tie-side preference (the G-5 §11.5
    /// precedence amendment): <c>lo</c> forces gaps to strictly ascend, and <c>hi</c>
    /// reserves one free gap for every later boundary, so <c>bins - 1</c> distinct ascending
    /// gaps always exist once <c>m ≥ bins</c>. Cuts are therefore strictly ascending
    /// <b>by construction</b> — a descending allocation is unrepresentable, and the
    /// post-hoc validity check is defense in depth, not the guarantee.
    /// </para>
    /// <para>
    /// A clamp is not a failure and is never diagnosed: it is exactly how §11.5's own
    /// examples resolve — a colliding boundary, a saturated tied run, and both domain edges
    /// (<c>d = 0</c> from <see cref="TiePolicy.Right"/> on the first tied group, <c>d = m</c>
    /// from <see cref="TiePolicy.Left"/> on the last) all land on the opposite side of their
    /// preference here.
    /// </para>
    /// </summary>
    public static int AllocateGap(int desired, int previousGap, int distinctCount, int bins, int k)
    {
        var lo = previousGap + 1;
        var hi = distinctCount - 1 - (bins - 1 - k);
        return Math.Min(Math.Max(desired, lo), hi);
    }

    /// <summary>
    /// Places the cut within the selected gap between <paramref name="lower"/>
    /// (<c>v_g</c>) and <paramref name="upper"/> (<c>v_{g+1}</c>), which are distinct finite
    /// values with <c>lower &lt; upper</c>.
    /// <list type="bullet">
    /// <item><see cref="CutPlacement.RightValue"/> → the gap's upper value (§11.5).</item>
    /// <item><see cref="CutPlacement.Midpoint"/> → the midpoint, by the pinned sign-aware
    /// expression order (G-5): a same-sign gap (or one with a zero bound) uses
    /// <c>a + (b - a) / 2</c>; a gap crossing zero uses <c>(a + b) / 2</c>. Each form is
    /// overflow-safe for its own case — the subtraction would overflow an opposite-sign
    /// extreme gap, and the sum would overflow a same-sign extreme one — so neither form
    /// alone is correct and they must not be folded together or reordered.</item>
    /// </list>
    /// <para>
    /// If the midpoint cannot land strictly above <paramref name="lower"/> — the values are
    /// adjacent representable doubles, so no double lies between them — it falls back to
    /// <paramref name="upper"/>. Under §11.2 half-open <c>[lo, hi)</c> geometry that is
    /// membership-identical to the midpoint's intent, and it keeps the cut inside its gap so
    /// the strict ascent of the whole cut list survives. A midpoint landing exactly on
    /// <paramref name="upper"/> is already membership-correct and is left alone.
    /// </para>
    /// <para>
    /// The result is positive-zero canonicalized (G-6): a computed <c>-0</c> must never reach
    /// a bin identity, label, or hash.
    /// </para>
    /// </summary>
    public static double PlaceCut(double lower, double upper, CutPlacement placement)
    {
        if (placement == CutPlacement.RightValue)
        {
            return CanonicalNumber.CanonicalizeZero(upper);
        }

        // The pinned expression order (G-5); do not reorder, fold, or route through decimal.
        var midpoint = lower >= 0.0 || upper <= 0.0
            ? lower + ((upper - lower) / 2.0)
            : (lower + upper) / 2.0;

        // Adjacent representable doubles leave no midpoint strictly above `lower`; the upper
        // value is the membership-correct cut there.
        if (!(midpoint > lower))
        {
            midpoint = upper;
        }

        return CanonicalNumber.CanonicalizeZero(midpoint);
    }

    /// <summary>
    /// Whether the <paramref name="percent"/>-th percentile position has been reached by the
    /// group whose cumulative count is <paramref name="cumulative"/>: the exact comparison
    /// <c>C_i·100 ≥ percent·N</c> (§11.4 <c>percentile_p1_p99</c>, D-089/D-103).
    /// <para>
    /// The same <see cref="UInt128"/> cross-multiplication as
    /// <see cref="TargetReached"/> — <c>p1</c>/<c>p99</c> are <b>exact order statistics</b>,
    /// never interpolated between neighbours and never taken from a machine-dependent
    /// percentile library, both of which would break the D-088 auto/frozen byte-equivalence.
    /// </para>
    /// </summary>
    public static bool PercentileReached(long cumulative, long total, int percent) =>
        Scale(cumulative, 100) >= Scale(total, percent);

    // Counts are non-negative checked longs and the multipliers positive ints, so the cast to
    // ulong is lossless and the product is far below UInt128's range.
    private static UInt128 Scale(long count, int multiplier) =>
        (UInt128)(ulong)count * (UInt128)(ulong)(uint)multiplier;
}
