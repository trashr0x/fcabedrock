namespace FcaBedrock.Conversion;

/// <summary>
/// Merges sorted <see cref="ValueCount"/> runs into <b>one</b> ascending, count-aggregated
/// run with bounded fan-in (D-095/D-103) — the count-sensitive twin of
/// <see cref="RunMerger{TRow}"/>, which orders by <c>(Rank, Seq)</c> and never folds rows.
/// Merging here is not just interleaving: rows carrying the <b>same</b> value are summed
/// into one output row, so the consolidated run is a true aggregated population — exactly
/// the distinct ascending <c>(value, count)</c> sequence the §11.5 quantile walk needs.
/// <para>
/// It ports the grouping backend's 3T storage model verbatim: before <b>every</b> output —
/// each intermediate batch <i>and</i> the final consolidated run — pending deletions are
/// retried, the output is projected conservatively as the sum of its inputs (aggregation
/// only shrinks), and the batch halts in-path <b>without opening the output</b> if live
/// bytes plus that projection would exceed <c>3·T</c>. Merge output coexists with its
/// inputs until they delete, so the final write is no safer than an intermediate one and
/// gets the same gate.
/// </para>
/// <para>
/// <b>Both sides of that gate are the workspace's.</b> Live bytes come from the
/// <see cref="SpoolWorkspace{TRow}"/>, which a whole calibration shares across its
/// count-sensitive attributes, so the <c>baselineT</c> a caller supplies must be that same
/// workspace's cumulative original-spill payload — every accumulator's, not the calling
/// attribute's alone. Passing one attribute's total while several spill into the workspace
/// compares a set against a fraction of its own baseline and refuses valid populations
/// (<see cref="CalibrationBudget.SpilledBytes"/> is where the calibration path forms it).
/// </para>
/// </summary>
internal sealed class ValueCountMerger
{
    private readonly SpoolWorkspace<ValueCount> _workspace;
    private readonly GroupingOptions _options;
    private readonly string _attributeName;

    public ValueCountMerger(SpoolWorkspace<ValueCount> workspace, GroupingOptions options, string attributeName)
    {
        _workspace = workspace;
        _options = options;
        _attributeName = attributeName;
    }

    /// <summary>
    /// Reduces <paramref name="runs"/> to exactly one ascending count-aggregated run,
    /// multi-stage when the count exceeds the fan-in, deleting each consumed input.
    /// <paramref name="baselineT"/> is the cumulative <b>raw spill</b> payload of the whole
    /// workspace this merger writes into — every accumulator sharing it, at this boundary
    /// (consolidation output never inflates it, and it never falls). A single input needs no
    /// merge and is returned untouched — nothing is opened, so no gate applies.
    /// </summary>
    /// <exception cref="GroupingStorageException">An in-path storage failure, or the 3T escalation.</exception>
    /// <exception cref="CalibrationPopulationOverflowException">A merged count sum exceeds <see cref="long"/>.</exception>
    public SpoolRunHandle Consolidate(List<SpoolRunHandle> runs, long baselineT, CancellationToken cancellationToken)
    {
        var bound = baselineT > long.MaxValue / 3 ? long.MaxValue : 3 * baselineT;

        while (runs.Count > 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new List<SpoolRunHandle>();
            foreach (var batch in runs.Chunk(_options.MaxMergeFanIn))
            {
                // A lone tail run carries forward unchanged: merging it with itself would
                // copy bytes and burn a 3T allowance for no aggregation.
                next.Add(batch.Length == 1 ? batch[0] : MergeBatch(batch, bound, baselineT, cancellationToken));
            }

            runs = next;
        }

        return runs[0];
    }

    // One batch (≤ fan-in inputs) → one aggregated run, gated by the 3T preflight.
    private SpoolRunHandle MergeBatch(
        SpoolRunHandle[] batch, long bound, long baselineT, CancellationToken cancellationToken)
    {
        // Retry failed deletions before projecting, so a transient failure is cleared before it
        // can force an avoidable escalation (D-082).
        _workspace.RetryPendingDeletions();

        var liveBytes = _workspace.LiveBytes;
        _options.Observer?.LiveBytes(liveBytes);

        long projected = 0;
        foreach (var handle in batch)
        {
            projected += handle.SizeBytes;
        }

        if (liveBytes + projected > bound)
        {
            _workspace.RecordInPathFailure(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, null); // record at source, before the throw
            throw new GroupingStorageException(
                GroupingOperation.CleanupDelete,
                SpoolFailureKind.DeleteFailed,
                null,
                $"Spool cleanup fell behind while calibrating attribute '{_attributeName}': retained runs ({liveBytes} bytes) plus the next merge output ({projected} bytes) would exceed the degraded maximum of {bound} bytes (3T, T = {baselineT}).");
        }

        var output = _workspace.WriteRun(Aggregate(batch, cancellationToken), GroupingOperation.MergeWrite);
        foreach (var handle in batch)
        {
            _workspace.DeleteRun(handle);
        }

        return output;
    }

    // A streaming k-way merge over one batch (≤ fan-in readers, one writer, a PriorityQueue of
    // ≤ fan-in entries), folding equal values into a single row. Within a run values are
    // strictly ascending and distinct — every run is either a spilled dictionary snapshot or a
    // prior aggregation — so once a reader advances past a value it can never return to it, and
    // the fold below is exhaustive for that value.
    private IEnumerable<RankedRow<ValueCount>> Aggregate(
        IReadOnlyList<SpoolRunHandle> batch, CancellationToken cancellationToken)
    {
        var readers = new SpoolRunReader<ValueCount>[batch.Count];
        var current = new ValueCount[batch.Count];
        var heap = new PriorityQueue<int, double>();
        var opened = 0;
        long seq = 0;

        // A reader read-fault is an in-path failure: record it at source (before the finally
        // reader-close its unwinding triggers) so the ledger stays in first-occurrence order.
        bool Advance(int i)
        {
            try
            {
                if (!readers[i].TryRead(out var entry))
                {
                    return false;
                }

                current[i] = entry.Row;
                heap.Enqueue(i, entry.Row.Value);
                return true;
            }
            catch (GroupingStorageException ex)
            {
                _workspace.RecordInPathFailure(ex.Operation, ex.Kind, ex.PathSample);
                throw;
            }
        }

        try
        {
            for (var i = 0; i < batch.Count; i++)
            {
                readers[i] = _workspace.OpenRun(batch[i]); // OpenRun records its own MergeRead failure at source
                _options.Observer?.RunOpenedForRead(batch[i].Path);
                opened++;
                Advance(i);
            }

            while (heap.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var index = heap.Dequeue();
                var value = current[index].Value;
                var count = current[index].Count;
                Advance(index);

                // Fold every other reader sitting on the identical value. Exact double equality
                // is the intended identity here: these are the same numeric bin, and ±0 was
                // folded at intake so no two spellings of one value can reach a run.
                while (heap.TryPeek(out var other, out var otherValue) && otherValue.Equals(value))
                {
                    heap.Dequeue();
                    count = AddChecked(count, current[other].Count);
                    Advance(other);
                }

                yield return new RankedRow<ValueCount>(Rank: 0, Seq: seq++, new ValueCount(value, count));
            }
        }
        finally
        {
            // Reader-close failures route to the non-throwing cleanup channel (CleanupClose
            // Warning), so a failing Dispose never replaces the primary result or failure.
            for (var i = 0; i < opened; i++)
            {
                _workspace.CloseRun(readers[i]);
                _options.Observer?.RunClosed(batch[i].Path);
            }
        }
    }

    // Merged counts are checked (G-13): summing two runs' counts for one value is exactly where
    // a population beyond long can first appear, and it must surface as
    // CalibrationPopulationTooLarge — never as a storage failure, and never as an
    // OverflowException escaping the seam.
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
}
