using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Fingerprinting;

namespace FcaBedrock.Conversion;

/// <summary>
/// The exact, bounded-memory count-sensitive calibration population for one attribute
/// (§11.5, D-095/D-103): every distinct finite value and how many observations carried it,
/// under a <b>fixed-capacity fill-and-spill</b> model.
/// <para>
/// Exactness is a correctness property, not a performance target: <c>equal_frequency</c> and
/// <c>percentile_p1_p99</c> cuts must be identical whether or not the population fitted in
/// memory (§11.5 forbids approximate quantiles outright, because an approximation would break
/// both determinism (EP-7) and the D-088 auto/frozen byte-equivalence). So the spill and
/// zero-spill paths share one allocation and selection walk; only where the rows live differs.
/// </para>
/// <para>
/// This engine is <b>only</b> for count-sensitive calibration. Streaming min/max
/// (<c>equal_width</c> <c>range = "min_max"</c>, D-102) must never use it: it retains two
/// doubles and is bounded by construction, so a dictionary would buy nothing and cost a
/// budget share.
/// </para>
/// <para>
/// <b>Resident model (tier 1, byte-exact).</b> The dictionary is sized once and never grows;
/// the sort buffer a <c>Dictionary</c> cannot sort in place is preallocated at the same
/// capacity and charged. Both are retained, so both are modeled:
/// <c>Modeled(capacity) = FixedBytes + capacity · SlotBytes</c>. Over the stable
/// post-sizing graph, <c>Σ Modeled(capacity_i) ≤ max(budget, A · FloorBytes)</c>.
/// </para>
/// </summary>
internal sealed class QuantileAccumulator
{
    /// <summary>
    /// Retained bytes per accepted entry on x64: the dictionary entry struct (24 = hashCode 4 +
    /// next 4 + key 8 + value 8), its bucket int (4), and the sort-buffer element (16, the
    /// <see cref="ValueCount"/> stride). A <b>correctness</b> constant for the .NET 10 CoreCLR
    /// x64 layout, padded upward, like <see cref="ResidentModel"/>'s, and never a performance knob
    /// (the budget may be tuned; changing this requires re-validating the layout).
    /// </summary>
    public const long SlotBytes = 44;

    /// <summary>
    /// Retained bytes independent of capacity, padded upward over the real x64 layout so
    /// <c>actual ≤ modeled</c> holds (the <see cref="ResidentModel"/> posture — these are
    /// correctness constants, not knobs):
    /// <list type="bullet">
    /// <item>this accumulator object — header 16 + 128 bytes of fields (nine references, a
    /// <c>CancellationToken</c>, two ints, two longs, and a <c>SpoolRunHandle?</c>) = 144 → 160;</item>
    /// <item>the <c>Dictionary</c> object — header 16 + 64 bytes of fields = 80 → 96;</item>
    /// <item>its two array headers plus the sort buffer's — 3 × 24 → 3 × 32.</item>
    /// </list>
    /// 160 + 96 + 96 = 352, rounded up to 384.
    /// <para>
    /// Only the accumulator object's own allocation is charged here. What its <c>_catalog</c> /
    /// <c>_consolidated</c> slots <i>point to</i> is the run catalogue: <b>tier 2</b>, bounded
    /// by count and shape (bounded by the fixed catalogue ceiling K = (F-1)*L; simultaneously open
    /// readers stay bounded by the merge fan-in), never by a byte constant. The slots themselves (a
    /// reference and a nullable <see cref="SpoolRunHandle"/>) live in this object and are counted above.
    /// </para>
    /// </summary>
    public const long FixedBytes = 384;

    /// <summary>
    /// The per-attribute overshoot floor: the modeled cost of an accumulator holding a single
    /// entry. A share below this cannot be honored — an accumulator that can retain nothing
    /// cannot count — so the bound is stated as <c>max(budget, A · FloorBytes)</c> rather than
    /// pretending a tiny budget shrinks it away. Computed from the runtime's <b>actual</b>
    /// accepted capacity for one requested entry, not a guess at its prime rounding.
    /// </summary>
    public static readonly long FloorBytes = Modeled(AcceptedCapacity(1));

    private readonly string _attributeName;
    private readonly CultureInfo _culture;
    private readonly SpoolWorkspace<ValueCount> _workspace;
    private readonly GroupingOptions _options;
    private readonly ICalibrationObserver? _observer;
    private readonly CalibrationBudget _budget;
    private readonly CancellationToken _cancellationToken;
    private readonly int _capacity;

    private QuantileRunCatalog? _catalog;   // built at the first spill; a zero-spill accumulator never has one
    private Dictionary<double, long>? _counts;
    private ValueCount[]? _sortBuffer;
    private int _resident;                 // used length of _sortBuffer on the zero-spill path
    private long _total;                   // the checked running total N
    private long _spilledBytes;            // this attribute's T_i: RAW spill payload only (consolidation never inflates it)
    private SpoolRunHandle? _consolidated;

    public QuantileAccumulator(
        string attributeName,
        CultureInfo culture,
        CalibrationBudget budget,
        SpoolWorkspace<ValueCount> workspace,
        GroupingOptions options,
        ICalibrationObserver? observer,
        CancellationToken cancellationToken)
    {
        _attributeName = attributeName;
        _culture = culture;
        _budget = budget;
        _workspace = workspace;
        _options = options;
        _observer = observer;
        _cancellationToken = cancellationToken;

        (_counts, _capacity) = Size(budget.Share);
        _sortBuffer = new ValueCount[_capacity];
        budget.Register(this);
        _observer?.AccumulatorSized(attributeName, _capacity, ModeledBytes);
    }

    /// <summary>The modeled retained bytes: constant for the accumulator's life, zero once released.</summary>
    public long ModeledBytes => _counts is null && _sortBuffer is null ? 0 : Modeled(_capacity);

    /// <summary>The accepted fixed capacity (the runtime's real prime-rounded value).</summary>
    public int Capacity => _capacity;

    /// <summary>The resolved culture the population was parsed under (never ambient — EP-11).</summary>
    public CultureInfo Culture => _culture;

    /// <summary>The checked total observation count <c>N</c>.</summary>
    public long Total => _total;

    /// <summary>Whether any run reached storage (false ⇒ the whole population stayed in memory).</summary>
    public bool Spilled => _catalog is { Count: > 0 } || _consolidated is not null;

    /// <summary>
    /// This attribute's <c>T_i</c>: the cumulative exact serialized bytes of its <b>successful
    /// original spills</b>, framing included. It never counts consolidation output, and never falls
    /// — deletion, consolidation, replay and finalization all leave it where it was, because it
    /// records what this attribute has written to the workspace, not what is still there.
    /// <para>
    /// Read by the owning <see cref="CalibrationBudget"/> to form the workspace-wide
    /// <see cref="CalibrationBudget.SpilledBytes"/>; see there for why the sum, not this, is the
    /// merge allowance's baseline.
    /// </para>
    /// </summary>
    public long SpilledBytes => _spilledBytes;

    /// <summary>The modeled bytes for an accumulator of <paramref name="capacity"/> accepted entries.</summary>
    public static long Modeled(int capacity) => FixedBytes + (capacity * SlotBytes);

    /// <summary>The retained failed-deletion cap for a calibration workspace (D-103).</summary>
    public static int MaxPendingDeletions(int maxMergeFanIn) => 4 * maxMergeFanIn;

    /// <summary>
    /// Adds one present observation to the population. A value that does not parse to a
    /// <b>finite</b> number under the resolved locale is excluded and tallied for this phase's
    /// own aggregated <c>SourceValueUnparseable</c> (§7/§11.5, D-100) — it never influences a
    /// cut. Existing-key increments allocate nothing.
    /// </summary>
    public void Observe(string raw, DiagnosticTally unparseable)
    {
        if (!CanonicalNumber.TryParse(raw, _culture, out var value))
        {
            unparseable.Record(raw);
            return;
        }

        // Fold ±0 HERE, not merely at cut placement. Dictionary/Equals/CompareTo all treat the two
        // zero spellings as one value, so they aggregate either way and m is right either way —
        // but WHICH spelling survives into the dictionary key, and from there into a spilled run
        // and the merged row, would depend on which arrived first. Canonicalizing at intake means
        // only +0 can ever exist downstream, so the value a cut or label is derived from is pinned
        // rather than incidental (G-6/D-096).
        Add(CanonicalNumber.CanonicalizeZero(value));
    }

    /// <summary>
    /// Ends intake: flushes any resident entries of a spilled accumulator as a final run and
    /// <b>releases both retained buffers</b>, so a post-intake merge never runs alongside the
    /// accumulator state it replaced. A zero-spill accumulator instead sorts in the buffer it
    /// already owns and drops only the dictionary — it has nothing to merge.
    /// </summary>
    public void EndIntake()
    {
        if (_catalog is { Count: > 0 })
        {
            if (_counts!.Count > 0)
            {
                SpillCurrent();
            }

            _counts = null;
            _sortBuffer = null;
            return;
        }

        _resident = FillSortBuffer();
        _counts = null;
    }

    /// <summary>
    /// Reduces a spilled accumulator's runs to the one consolidated ascending, count-aggregated
    /// run the two-pass walk replays. No-op for a zero-spill accumulator (no storage is touched).
    /// Called after every accumulator has ended intake, one attribute at a time.
    /// </summary>
    public void PrepareReplay()
    {
        if (_catalog is not { Count: > 0 } catalog)
        {
            return;
        }

        // Generation ascending, creation order within a generation: a fixed order, never chosen by
        // size, filesystem order, hash iteration or timing. A catalogue already holding exactly one
        // run (S = F^g) is returned untouched by the merger, so no duplicate final rewrite occurs.
        var merger = new ValueCountMerger(_workspace, _options, _attributeName);
        var consolidated = merger.Consolidate(catalog.Snapshot(), _budget.SpilledBytes, _cancellationToken);
        catalog.ReplaceWithFinal(consolidated);
        _consolidated = consolidated;
        _observer?.RunCatalog(_attributeName, catalog.Count);
    }

    /// <summary>
    /// The <c>equal_frequency</c> cuts (§11.5), or <see langword="null"/> when the population has
    /// fewer than <c>bins</c> distinct values — the §11.5 distinct-value guard, which stops rather
    /// than silently producing fewer bins. <paramref name="distinctCount"/> reports <c>m</c> either
    /// way, for the diagnostic.
    /// <para>
    /// The two passes are what make a spilled population as exact as an in-memory one: the
    /// feasibility window needs the <b>global</b> <c>m</c> before it can allocate the first
    /// boundary, and cut placement needs the values adjoining each <b>selected</b> gap after it —
    /// neither is knowable from a single forward walk. Between the passes, allocation is pure
    /// arithmetic over <c>bins - 1</c> values, so nothing population-sized is ever retained.
    /// </para>
    /// </summary>
    public double[]? TryExtractEqualFrequencyCuts(PendingEqualFrequency config, out int distinctCount)
    {
        var bins = config.Bins;
        var boundaries = bins - 1;
        var desired = new int[boundaries];
        var assigned = 0;
        var m = 0;

        // Pass 1: m, and each boundary's desired gap. N is the intake total, so the exact
        // rational target N·k/bins is known from the first group onward.
        Replay((index, _, cumulative) =>
        {
            m = index;
            while (assigned < boundaries && QuantileSelection.TargetReached(cumulative, _total, bins, assigned + 1))
            {
                var k = assigned + 1;
                desired[assigned] = QuantileSelection.DesiredGap(
                    index, QuantileSelection.TargetIsEdge(cumulative, _total, bins, k), config.TiePolicy);
                assigned++;
            }
        });

        distinctCount = m;
        if (m < bins)
        {
            return null;
        }

        // Allocation: each boundary takes the nearest feasible gap in its window, ascending.
        var gaps = new int[boundaries];
        var previous = 0;
        for (var k = 1; k <= boundaries; k++)
        {
            previous = QuantileSelection.AllocateGap(desired[k - 1], previous, m, bins, k);
            gaps[k - 1] = previous;
        }

        // Pass 2: the values adjoining each selected gap. Gaps ascend and are distinct, but two
        // may be adjacent (g and g+1), so one group can be both one gap's lower value and the
        // previous gap's upper — each gap is therefore tested independently.
        var lower = new double[boundaries];
        var upper = new double[boundaries];
        Replay((index, row, _) =>
        {
            for (var j = 0; j < boundaries; j++)
            {
                if (index == gaps[j])
                {
                    lower[j] = row.Value;
                }
                else if (index == gaps[j] + 1)
                {
                    upper[j] = row.Value;
                }
            }
        });

        var cuts = new double[boundaries];
        for (var j = 0; j < boundaries; j++)
        {
            cuts[j] = QuantileSelection.PlaceCut(lower[j], upper[j], config.CutPlacement);
        }

        return cuts;
    }

    /// <summary>
    /// The exact <c>p1</c>/<c>p99</c> order statistics (§11.4 <c>percentile_p1_p99</c>), or
    /// <see langword="false"/> when the population is empty or the two coincide — a span with no
    /// spread cannot bound equal-width bins. Same two-pass shape as
    /// <see cref="TryExtractEqualFrequencyCuts"/>: pass 1 fixes the positions, pass 2 reads their
    /// values.
    /// </summary>
    public bool TryExtractPercentileSpan(out double p1, out double p99)
    {
        var p1Index = 0;
        var p99Index = 0;
        Replay((index, _, cumulative) =>
        {
            if (p1Index == 0 && QuantileSelection.PercentileReached(cumulative, _total, 1))
            {
                p1Index = index;
            }

            if (p99Index == 0 && QuantileSelection.PercentileReached(cumulative, _total, 99))
            {
                p99Index = index;
            }
        });

        p1 = 0.0;
        p99 = 0.0;
        if (p1Index == 0 || p99Index == 0)
        {
            return false; // no usable observation at all
        }

        var foundP1 = 0.0;
        var foundP99 = 0.0;
        Replay((index, row, _) =>
        {
            if (index == p1Index)
            {
                foundP1 = row.Value;
            }

            if (index == p99Index)
            {
                foundP99 = row.Value;
            }
        });

        p1 = foundP1;
        p99 = foundP99;
        return p1 < p99;
    }

    /// <summary>
    /// Releases every retained buffer and deletes the consolidated run through the established
    /// cleanup channel. Called once cut extraction has completed (or been abandoned).
    /// </summary>
    public void Release()
    {
        _counts = null;
        _sortBuffer = null;
        _resident = 0;

        if (_consolidated is { } handle)
        {
            _consolidated = null;
            _catalog?.Clear();
            _workspace.DeleteRun(handle);
            _observer?.RunCatalog(_attributeName, 0);
        }
    }

    // Sizes the fixed-capacity dictionary for this accumulator's byte share. EnsureCapacity
    // reports the runtime's REAL (prime-rounded) capacity, which is what the model must charge —
    // the request would under-count. Because the runtime rounds UP to a prime, the accepted
    // capacity can overshoot the share; when it does, the request is reduced and retried until it
    // fits. The discarded probe is a sizing transient, excluded from the tier-1 bound exactly as
    // D-082 excludes the List resize-copy transient (the guarantee is over the stable post-sizing
    // graph). At a request of one the floor applies and the overshoot is FloorBytes — bounded and
    // per-attribute.
    private static (Dictionary<double, long> Counts, int Capacity) Size(long share)
    {
        var requested = (int)Math.Clamp((share - FixedBytes) / SlotBytes, 1, int.MaxValue);
        while (true)
        {
            var counts = new Dictionary<double, long>(requested);
            var capacity = counts.EnsureCapacity(requested);
            if (requested <= 1 || Modeled(capacity) <= share)
            {
                return (counts, capacity);
            }

            // Reduce MULTIPLICATIVELY, not by one. Decrementing would re-round to the very same
            // prime on the next probe and crawl one integer at a time across the gap below it —
            // ~130k probes at a 64 MiB share, each allocating a multi-megabyte dictionary. The
            // ~0.8 factor clears a typical prime gap in one step (correctness does not depend on
            // that: any factor below 1 terminates, it only costs extra probes), and the
            // min(requested-1, capacity-1) floor keeps the sequence strictly decreasing at small
            // sizes where the multiply would round back to itself.
            requested = Math.Max(1, Math.Min(requested - 1, capacity - 1) * 4 / 5);
        }
    }

    private static int AcceptedCapacity(int requested)
    {
        var probe = new Dictionary<double, long>(requested);
        return probe.EnsureCapacity(requested);
    }

    private void Add(double value)
    {
        var counts = _counts!;
        if (counts.TryGetValue(value, out var existing))
        {
            counts[value] = AddChecked(existing, 1); // in-place update: no allocation, no growth
        }
        else
        {
            // A new key at capacity spills first, so the dictionary never grows past its share.
            if (counts.Count == _capacity)
            {
                SpillCurrent();
            }

            _counts![value] = 1;
        }

        _total = AddChecked(_total, 1);
    }

    // Writes the resident entries as one sorted run and clears the dictionary, keeping its
    // capacity: here the capacity IS the budget, so replacing the dictionary would buy no bound
    // and cost a re-sizing. The run then enters generation 0 and any carry it completes cascades,
    // so live run handles per accumulator never exceed the fixed catalogue ceiling K = (F-1)*L;
    // simultaneously open readers stay bounded by the merge fan-in.
    //
    // The original run is written and its raw bytes registered in T BEFORE any carry, so the
    // workspace-wide 3T allowance a carry is measured against already includes this spill.
    private void SpillCurrent()
    {
        var catalog = _catalog ??= new QuantileRunCatalog(_options.MaxMergeFanIn);

        var used = FillSortBuffer();
        var handle = _workspace.WriteRun(RankedRows(used), GroupingOperation.Spill);
        _spilledBytes = ResidentModel.SaturatingAdd(_spilledBytes, handle.SizeBytes);
        catalog.Insert(handle);
        _observer?.RunCatalog(_attributeName, catalog.Count);
        _observer?.BufferSpilled(ModeledBytes);
        _budget.ReportAggregate();
        _counts!.Clear();

        Carry(catalog);
    }

    // Completes the carry chain the new leaf may have started: while a generation holds exactly the
    // fan-in, those F runs — oldest first — merge into one run of the next generation, and the
    // cascade continues upward. Promotion is by original-leaf count, never by byte size, and a
    // higher-generation run is never used to fill a lower batch.
    //
    // This is the one case where a merge runs DURING intake, so this accumulator's dictionary and
    // sort buffer are co-resident with the merge's readers/writer. Tier 2 accounts for that
    // co-residence (D-103), which is why the release-before-merge rule is stated for the
    // POST-intake phase only.
    //
    // The baseline is the workspace's T_so_far at THIS boundary — every accumulator's original
    // spills up to now, not a prediction of their final payload. T only grows, so a later merge is
    // never measured against a smaller allowance than an earlier one.
    //
    // A level is committed only after its merge RETURNS: on a storage failure or cancellation the
    // catalogue is untouched, the calibration aborts, and the workspace — which tracks every
    // undeleted path — owns the cleanup. A partial carry is never resumed.
    private void Carry(QuantileRunCatalog catalog)
    {
        var merger = new ValueCountMerger(_workspace, _options, _attributeName);
        for (var generation = 0; catalog.IsFull(generation); generation++)
        {
            var carried = merger.Consolidate(catalog.Batch(generation), _budget.SpilledBytes, _cancellationToken);
            catalog.Carry(generation, carried);
            _observer?.RunCatalog(_attributeName, catalog.Count);
        }
    }

    private int FillSortBuffer()
    {
        var buffer = _sortBuffer!;
        var used = 0;
        foreach (var (value, count) in _counts!)
        {
            buffer[used++] = new ValueCount(value, count);
        }

        // Only the used segment: no population-sized secondary list is ever allocated.
        Array.Sort(buffer, 0, used, ValueCountComparer.Instance);
        return used;
    }

    private IEnumerable<RankedRow<ValueCount>> RankedRows(int used)
    {
        // Rank is unused for ordering here (rows are already value-sorted and merged by value);
        // Seq is the run-local index, keeping the framing deterministic.
        for (var i = 0; i < used; i++)
        {
            yield return new RankedRow<ValueCount>(Rank: 0, Seq: i, _sortBuffer![i]);
        }
    }

    // Walks the aggregated ascending (value, count) sequence, handing each group its 1-based
    // index, its row, and the checked running cumulative. One method for both paths, so the
    // spilled and in-memory walks cannot drift.
    private void Replay(Action<int, ValueCount, long> onGroup)
    {
        long cumulative = 0;
        var index = 0;
        foreach (var row in Rows())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            cumulative = AddChecked(cumulative, row.Count);
            onGroup(++index, row, cumulative);
        }
    }

    private IEnumerable<ValueCount> Rows()
    {
        if (_consolidated is not { } handle)
        {
            for (var i = 0; i < _resident; i++)
            {
                yield return _sortBuffer![i];
            }

            yield break;
        }

        var reader = _workspace.OpenRun(handle); // records its own MergeRead failure at source
        _observer?.RunOpenedForRead(handle.Path);
        try
        {
            while (true)
            {
                RankedRow<ValueCount> entry;
                try
                {
                    if (!reader.TryRead(out entry))
                    {
                        break;
                    }
                }
                catch (GroupingStorageException ex)
                {
                    // Record at source, before the finally close its unwinding triggers, so the
                    // ledger stays in first-occurrence order (D-082).
                    _workspace.RecordInPathFailure(ex.Operation, ex.Kind, ex.PathSample);
                    throw;
                }

                yield return entry.Row;
            }
        }
        finally
        {
            _workspace.CloseRun(reader);
            _observer?.RunClosed(handle.Path);
        }
    }

    // Every count total is checked (G-13): a per-value increment, the running N, and the replay's
    // cumulative. Overflow is a distinct condition — too much data to count exactly — and must
    // reach the caller as CalibrationPopulationTooLarge, never as CalibrationDataInsufficient (its
    // opposite), a storage failure, or a bare OverflowException.
    private long AddChecked(long a, long b)
    {
        try
        {
            return checked(a + b);
        }
        catch (OverflowException ex)
        {
            throw new CalibrationPopulationOverflowException(_attributeName, ex);
        }
    }

    /// <summary>Seeds the running total for the overflow-path test seam (never a production path).</summary>
    internal void SeedTotalForTest(long total) => _total = total;
}

/// <summary>
/// Divides the calibration memory budget across the attributes that need an exact
/// count-sensitive population, and reports the modeled aggregate (D-095/D-103).
/// <para>
/// The share is floor-clamped — <c>share = max(FloorBytes, budget / A)</c> — so a pathologically
/// small budget still yields a usable accumulator per attribute rather than a zero or negative
/// share. The honest consequence is stated rather than hidden: the guaranteed bound is
/// <c>max(budget, A · FloorBytes)</c>, not the budget alone.
/// </para>
/// <para>
/// It also carries the registry of those accumulators, which is what makes the workspace-wide
/// spill payload (<see cref="SpilledBytes"/>) available to each of them. One
/// <c>CalibrationRun</c> creates exactly one budget and one <see cref="SpoolWorkspace{TRow}"/> and
/// hands both to every accumulator it builds, so "registered here" and "spilling into that
/// workspace" name the same set.
/// </para>
/// </summary>
internal sealed class CalibrationBudget
{
    private readonly List<QuantileAccumulator> _accumulators = [];
    private readonly ICalibrationObserver? _observer;

    public CalibrationBudget(long budget, int countSensitiveAttributes, ICalibrationObserver? observer)
    {
        _observer = observer;
        Share = countSensitiveAttributes <= 0
            ? 0
            : Math.Max(QuantileAccumulator.FloorBytes, budget / countSensitiveAttributes);
    }

    /// <summary>The per-accumulator byte share (floor-clamped); zero when nothing is count-sensitive.</summary>
    public long Share { get; }

    /// <summary>The live accumulators, in spec-attribute order.</summary>
    public IReadOnlyList<QuantileAccumulator> Accumulators => _accumulators;

    /// <summary>
    /// The shared workspace's <c>T</c>: the cumulative original-spill payload of <b>every</b>
    /// accumulator registered here, which is the baseline the D-082 <c>L + P ≤ 3T</c> merge
    /// allowance must be measured against.
    /// <para>
    /// Both sides of that inequality have to describe the same set of files, and the retained side
    /// — <see cref="SpoolWorkspace{TRow}.LiveBytes"/> — is the whole workspace's, because one
    /// workspace serves the whole calibration. Using a single attribute's payload as the baseline
    /// instead would divide the allowance by the number of attributes spilling into it: with A
    /// comparable accumulators the retained bytes grow with A while the allowance does not, so a
    /// perfectly ordinary population is refused with a degraded-cleanup diagnostic on healthy
    /// storage. Summing here gives one consistently scoped guarantee: <b>not</b> a per-attribute
    /// multiplier, and not a larger allowance than D-082 states.
    /// </para>
    /// <para>
    /// It grows only with original spills, so it is monotone: deletion, consolidation, replay and an
    /// attribute finishing all leave it alone, and an accumulator that never spilled contributes
    /// nothing. Saturating, so the running total cannot wrap a merge projection into permission.
    /// </para>
    /// </summary>
    public long SpilledBytes
    {
        get
        {
            long total = 0;
            foreach (var accumulator in _accumulators)
            {
                total = ResidentModel.SaturatingAdd(total, accumulator.SpilledBytes);
            }

            return total;
        }
    }

    public void Register(QuantileAccumulator accumulator) => _accumulators.Add(accumulator);

    /// <summary>Reports the modeled resident total across every registered accumulator.</summary>
    public void ReportAggregate()
    {
        if (_observer is null)
        {
            return;
        }

        long total = 0;
        foreach (var accumulator in _accumulators)
        {
            total = ResidentModel.SaturatingAdd(total, accumulator.ModeledBytes);
        }

        _observer.AggregateResident(total);
    }
}
