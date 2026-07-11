namespace FcaBedrock.Conversion;

/// <summary>
/// Merges sorted spool runs into one <c>(Rank, Seq)</c>-ordered stream with <b>bounded fan-in</b>
/// (D-082). While more than <see cref="GroupingOptions.MaxMergeFanIn"/> runs remain, batches of at most
/// the fan-in are merged into intermediate runs (their inputs deleted as consumed) until the count
/// falls to the fan-in, then a single streaming k-way merge yields the result — so a merge holds at
/// most fan-in readers plus one writer open at once. Before each intermediate batch, the
/// <b>degraded-cleanup escalation</b> enforces the pinned peak: if retained live bytes plus the batch's
/// worst-case output would exceed <c>3T</c> (T = the initial spill payload), it halts (Error) before
/// creating any output — so cleanup falling permanently behind can never grow disk past 3T.
/// </summary>
internal sealed class RunMerger<TRow>
{
    private readonly SpoolWorkspace<TRow> _workspace;
    private readonly GroupingOptions _options;

    public RunMerger(SpoolWorkspace<TRow> workspace, GroupingOptions options)
    {
        _workspace = workspace;
        _options = options;
    }

    /// <summary>
    /// Reduces <paramref name="runs"/> to at most the fan-in (eagerly, writing intermediate runs and
    /// deleting consumed inputs), then returns the final streaming merge. <paramref name="baselineT"/>
    /// is the intake-final initial spill payload; the reduction halts before any batch that would push
    /// live bytes past <c>3·baselineT</c>.
    /// </summary>
    public IEnumerable<RankedRow<TRow>> Merge(List<SpoolRunHandle> runs, long baselineT, CancellationToken cancellationToken)
    {
        var bound = baselineT > long.MaxValue / 3 ? long.MaxValue : 3 * baselineT;

        while (runs.Count > _options.MaxMergeFanIn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new List<SpoolRunHandle>();
            foreach (var batch in runs.Chunk(_options.MaxMergeFanIn))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Retry any previously-failed deletions before projecting live bytes, so a transient
                // failure is cleared before it can force an avoidable 3T escalation (D-082).
                _workspace.RetryPendingDeletions();

                var liveBytes = _workspace.LiveBytes;
                _options.Observer?.LiveBytes(liveBytes);

                var batchOutput = SumSizes(batch);
                if (liveBytes + batchOutput > bound)
                {
                    _workspace.RecordInPathFailure(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, null); // record at source (first-occurrence), before the throw
                    throw new GroupingStorageException(
                        GroupingOperation.CleanupDelete,
                        SpoolFailureKind.DeleteFailed,
                        null,
                        $"Spool cleanup fell behind: retained runs ({liveBytes} bytes) plus the next merge batch ({batchOutput} bytes) would exceed the degraded maximum of {bound} bytes (3T, T = {baselineT}).");
                }

                var output = _workspace.WriteRun(MergeBatch(batch, cancellationToken), GroupingOperation.MergeWrite);
                foreach (var handle in batch)
                {
                    _workspace.DeleteRun(handle);
                }

                next.Add(output);
            }

            runs = next;
        }

        _options.Observer?.LiveBytes(_workspace.LiveBytes);
        return MergeBatch(runs, cancellationToken);
    }

    private static long SumSizes(IReadOnlyList<SpoolRunHandle> batch)
    {
        long sum = 0;
        foreach (var handle in batch)
        {
            sum += handle.SizeBytes;
        }

        return sum;
    }

    // A streaming k-way merge over one batch (≤ fan-in readers), yielding by (Rank, Seq). Seq is a
    // global arrival counter, so priorities never tie. Readers close on enumeration end or disposal.
    private IEnumerable<RankedRow<TRow>> MergeBatch(IReadOnlyList<SpoolRunHandle> batch, CancellationToken cancellationToken)
    {
        var readers = new SpoolRunReader<TRow>[batch.Count];
        var current = new RankedRow<TRow>[batch.Count];
        var heap = new PriorityQueue<int, (int Rank, long Seq)>();
        var opened = 0;

        // A reader read-fault is an in-path failure: record it at source (before the finally reader-close
        // that its unwinding triggers) so the storage ledger stays in first-occurrence order (D-082).
        bool ReadNext(int i, out RankedRow<TRow> entry)
        {
            try
            {
                return readers[i].TryRead(out entry);
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
                if (ReadNext(i, out var entry))
                {
                    current[i] = entry;
                    heap.Enqueue(i, (entry.Rank, entry.Seq));
                }
            }

            while (heap.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = heap.Dequeue();
                yield return current[index];
                if (ReadNext(index, out var next))
                {
                    current[index] = next;
                    heap.Enqueue(index, (next.Rank, next.Seq));
                }
            }
        }
        finally
        {
            // Reader-close failures route to the non-throwing cleanup channel (CleanupClose Warning), so a
            // failing Dispose never escapes to replace the primary result/cancellation/in-path failure.
            for (var i = 0; i < opened; i++)
            {
                _workspace.CloseRun(readers[i]);
                _options.Observer?.RunClosed(batch[i].Path);
            }
        }
    }
}
