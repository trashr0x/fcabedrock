namespace FcaBedrock.Conversion;

/// <summary>
/// A test-only observer of the count-sensitive calibration's resource use (D-095/D-103
/// resource proofs), extending the spool backend's <see cref="IGroupingObserver"/> so one
/// object can watch both tiers. No production behavior depends on it (P-6); production runs
/// leave it <see langword="null"/>.
/// <para>
/// The signals mirror the <b>two-tier</b> contract the calibration actually offers, stated
/// honestly rather than over-claimed:
/// </para>
/// <list type="bullet">
/// <item><b>Tier 1 — byte-exact.</b> <see cref="AccumulatorSized"/> and
/// <see cref="AggregateResident"/> report the accumulator state the fixed model charges
/// (the dictionary arrays and the sort buffer), which tests re-derive independently.</item>
/// <item><b>Tier 2 — structurally bounded.</b> <see cref="RunCatalog"/>, with
/// <see cref="IGroupingObserver"/>'s reader/writer and pending-deletion signals, reports
/// I/O and bookkeeping bounded by <i>count and fixed shape</i> — not by a
/// pinned byte constant, because a <c>FileStream</c>'s internal strategy/handle graph is
/// runtime-owned and any "≈ 8 KiB" claim would be unvalidatable.</item>
/// </list>
/// </summary>
internal interface ICalibrationObserver : IGroupingObserver
{
    /// <summary>
    /// One attribute's accumulator finished sizing: the <b>accepted</b> capacity
    /// (<c>Dictionary.EnsureCapacity</c>'s real prime-rounded value, not the request) and the
    /// modeled bytes charged for it. Sizing-probe transients are excluded (the D-082
    /// precedent) — this reports the stable retained graph.
    /// </summary>
    void AccumulatorSized(string attribute, int capacity, long modeledBytes);

    /// <summary>
    /// The modeled resident total across every live accumulator, reported at each spill and
    /// phase transition. Drops to zero for an accumulator once its intake state is released —
    /// which, for a spilled accumulator, happens strictly before its post-intake merge.
    /// </summary>
    void AggregateResident(long modeledBytes);

    /// <summary>One attribute's live spill-run handles after a catalog change (bounded by the merge fan-in).</summary>
    void RunCatalog(string attribute, int liveRuns);
}

/// <summary>
/// The calibration population exceeded exact <see cref="long"/> counting (§16.4
/// <c>CalibrationPopulationTooLarge</c>, D-103/G-13). Internal and thrown only from the
/// checked count sites — a per-value increment, the running total, or a merge sum.
/// <para>
/// A dedicated type rather than letting <see cref="OverflowException"/> travel: the
/// calibrator must attribute the failure to the offending attribute and must not
/// mis-report an unrelated overflow from elsewhere as a population diagnostic. Like
/// <see cref="GroupingStorageException"/> it never crosses the public seam (P-14) — the
/// calibrator converts it to a diagnostic.
/// </para>
/// </summary>
internal sealed class CalibrationPopulationOverflowException : Exception
{
    public CalibrationPopulationOverflowException(string attributeName, Exception? inner = null)
        : base($"The calibration population for attribute '{attributeName}' exceeds exact long counting.", inner) =>
        AttributeName = attributeName;

    /// <summary>The attribute whose count overflowed.</summary>
    public string AttributeName { get; }
}
