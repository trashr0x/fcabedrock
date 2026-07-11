using System.Runtime.CompilerServices;

namespace FcaBedrock.Conversion;

/// <summary>
/// Reorders a row stream so rows sharing a key are contiguous and the groups appear in
/// <b>first-appearance order</b> of the key — the shared core of the slow (non-single-pass) path for
/// triple <c>ordering = "unordered"</c> (D-082) and wide <c>duplicate_object_policy = "dedupe"</c>
/// (D-083). It buffers input rows and stable-sorts them by each distinct key's first-appearance rank;
/// when the resident buffer exceeds <see cref="GroupingOptions.MaxBufferedBytes"/> it spills a sorted
/// run to an owner-restricted spool workspace and merges the runs with bounded fan-in, so a zero-spill
/// enumeration stays entirely in memory (and touches no disk) while a large one stays bounded (P-16).
/// The single cleaned-key structure is the rank map — one name-cardinality collection, not two.
/// <para>
/// Deliberately <b>validity-agnostic</b>: it never inspects a key for "usability" and never raises a
/// diagnostic (a <c>null</c> key is ranked like any other). A structural-error boundary is the caller's
/// concern (it truncates its input before grouping). Storage failures use two channels (D-082):
/// in-path failures throw <see cref="GroupingStorageException"/> (the emitter records an Error and
/// halts); cleanup failures accrue to the <c>reports</c> channel as Warnings while enumeration
/// continues. Spilling is byte-neutral: the codec round-trips values exactly (P-7).
/// </para>
/// </summary>
internal static class FirstAppearanceGrouping
{
    /// <summary>
    /// Yields <paramref name="rows"/> reordered so rows with the same <paramref name="keyOf"/> value are
    /// contiguous, groups in first-appearance order of the key, input order preserved within a group.
    /// Ordinal keying (P-12) keeps ranks machine-stable.
    /// </summary>
    public static async IAsyncEnumerable<TRow> GroupByFirstAppearanceAsync<TRow>(
        IAsyncEnumerable<TRow> rows,
        Func<TRow, string?> keyOf,
        IRowCodec<TRow> codec,
        GroupingOptions options,
        GroupingReports reports,
        Action<TRow, bool>? onRow = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(reports);

        var workspace = new SpoolWorkspace<TRow>(options, codec, reports);
        try
        {
            // Intake: assign each distinct key a first-appearance rank over one counter (the single
            // key-identity structure), tagging each row with a global arrival Seq. Buffer until over
            // budget, then spill a sorted run. Ordinal comparison (P-12) is the grouping identity rule.
            var rankByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var nullRank = -1;
            var nextRank = 0;
            long seq = 0;
            var slotBytes = (long)Unsafe.SizeOf<RankedRow<TRow>>(); // exact array element stride
            var buffer = new List<RankedRow<TRow>>();
            long retainedBytes = 0; // Σ per-row retained referenced objects (the buffer/backing array is added separately)
            var runs = new List<SpoolRunHandle>();

            await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var key = keyOf(row);
                int rank;
                bool firstAppearance;
                if (key is null)
                {
                    if (nullRank < 0)
                    {
                        nullRank = nextRank++;
                        firstAppearance = true;
                    }
                    else
                    {
                        firstAppearance = false;
                    }

                    rank = nullRank;
                }
                else if (!rankByKey.TryGetValue(key, out rank))
                {
                    rank = nextRank++;
                    rankByKey[key] = rank;
                    firstAppearance = true;
                }
                else
                {
                    firstAppearance = false;
                }

                // Intake hook (D-083): fires in source order for every row, so the dedupe emitter can
                // detect duplicates without a second ordinal seen-set. No-op for triple (null hook).
                onRow?.Invoke(row, firstAppearance);

                buffer.Add(new RankedRow<TRow>(rank, seq++, row));
                retainedBytes = ResidentModel.SaturatingAdd(retainedBytes, codec.MeasureResident(row));

                // Conservative resident accounting = the buffer (List object + its real-capacity backing
                // array) + the retained referenced objects; over the padded x64 constants this bounds the
                // actual retained live-object graph (D-082).
                var residentBytes = ResidentModel.SaturatingAdd(ResidentModel.BufferBytes(buffer.Capacity, slotBytes), retainedBytes);

                // Spill when the resident accounting exceeds the budget; an over-budget single row spills
                // alone. Replace the buffer (not Clear) so the freed backing-array capacity is not retained
                // past retainedBytes = 0 (D-082).
                if (residentBytes > options.MaxBufferedBytes)
                {
                    Sort(buffer);
                    options.Observer?.BufferSpilled(residentBytes);
                    runs.Add(workspace.WriteRun(buffer, GroupingOperation.Spill));
                    buffer = [];
                    retainedBytes = 0;
                }
            }

            if (runs.Count == 0)
            {
                // Zero-spill: sort and yield in memory — no workspace, no disk.
                Sort(buffer);
                foreach (var entry in buffer)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return entry.Row;
                }

                yield break;
            }

            // Flush the final resident rows as a run, then capture T (total initial spill payload) once,
            // before the first merge batch (deletions only begin with merging).
            if (buffer.Count > 0)
            {
                Sort(buffer);
                runs.Add(workspace.WriteRun(buffer, GroupingOperation.Spill));
                buffer.Clear();
            }

            long baselineT = 0;
            foreach (var run in runs)
            {
                baselineT += run.SizeBytes;
            }

            var merger = new RunMerger<TRow>(workspace, options);
            foreach (var entry in merger.Merge(runs, baselineT, cancellationToken))
            {
                yield return entry.Row;
            }
        }
        finally
        {
            workspace.Cleanup();
        }
    }

    // Sort by (Rank, Seq): groups in first-appearance order, source order within a group. Seq is a
    // unique global arrival counter, so the comparison is total and the result is deterministic.
    private static void Sort<TRow>(List<RankedRow<TRow>> buffer) =>
        buffer.Sort(static (a, b) =>
        {
            var byRank = a.Rank.CompareTo(b.Rank);
            return byRank != 0 ? byRank : a.Seq.CompareTo(b.Seq);
        });
}
