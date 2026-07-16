namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The count-aggregating k-way merger (D-095/D-103): equal values fold into one row, counts are
/// checked, fan-in is bounded, and every output — intermediate <b>and</b> final — passes the 3T
/// preflight before it is opened.
/// </summary>
public sealed class ValueCountMergerTests
{
    private sealed record Harness(
        SpoolWorkspace<ValueCount> Workspace, GroupingOptions Options, GroupingReports Reports) : IDisposable
    {
        public void Dispose() => Workspace.Cleanup();
    }

    private static Harness Build(
        int fanIn = GroupingOptions.DefaultMaxMergeFanIn,
        ISpoolFileSystem? fileSystem = null,
        IGroupingObserver? observer = null,
        int? maxPendingDeletions = null)
    {
        var options = new GroupingOptions(
            GroupingOptions.DefaultMaxBufferedBytes, fanIn, tempDirectory: null, fileSystem, observer);
        var reports = new GroupingReports();
        var workspace = new SpoolWorkspace<ValueCount>(
            options, ValueCountCodec.Instance, reports,
            maxPendingDeletions ?? QuantileAccumulator.MaxPendingDeletions(fanIn));
        return new Harness(workspace, options, reports);
    }

    // Writes one ascending run of (value, count) pairs, as the accumulator's spill does.
    private static SpoolRunHandle WriteRun(Harness harness, params (double Value, long Count)[] rows)
    {
        long seq = 0;
        return harness.Workspace.WriteRun(
            rows.Select(r => new RankedRow<ValueCount>(0, seq++, new ValueCount(r.Value, r.Count))),
            GroupingOperation.Spill);
    }

    private static List<ValueCount> ReadRun(Harness harness, SpoolRunHandle handle)
    {
        var rows = new List<ValueCount>();
        var reader = harness.Workspace.OpenRun(handle);
        try
        {
            while (reader.TryRead(out var entry))
            {
                rows.Add(entry.Row);
            }
        }
        finally
        {
            harness.Workspace.CloseRun(reader);
        }

        return rows;
    }

    private static long TotalBytes(params SpoolRunHandle[] runs) => runs.Sum(r => r.SizeBytes);

    [Fact]
    public void Consolidate_WhenOneRun_ThenReturnedUntouchedWithoutOpeningAnything()
    {
        var observer = new RecordingObserver();
        using var harness = Build(observer: observer);
        var run = WriteRun(harness, (1, 1), (2, 2));

        var result = new ValueCountMerger(harness.Workspace, harness.Options, "score")
            .Consolidate([run], TotalBytes(run), CancellationToken.None);

        // Nothing to merge: copying it to itself would burn a 3T allowance for no aggregation.
        Assert.Equal(run, result);
        Assert.DoesNotContain(observer.Written, w => !w.Initial);
        Assert.Equal(0, observer.PeakOpenReaders);
    }

    [Fact]
    public void Consolidate_WhenRunsShareValues_ThenCountsAggregateIntoOneAscendingRow()
    {
        using var harness = Build();
        var a = WriteRun(harness, (1, 5), (3, 7));
        var b = WriteRun(harness, (2, 1), (3, 2));
        var c = WriteRun(harness, (1, 4), (4, 9));

        var result = new ValueCountMerger(harness.Workspace, harness.Options, "score")
            .Consolidate([a, b, c], TotalBytes(a, b, c), CancellationToken.None);

        // The population, not just an interleaving: 1 → 5+4, 3 → 7+2, and the order is ascending.
        Assert.Equal(
            [new ValueCount(1, 9), new ValueCount(2, 1), new ValueCount(3, 9), new ValueCount(4, 9)],
            ReadRun(harness, result));
    }

    [Fact]
    public void Consolidate_WhenMoreRunsThanTheFanIn_ThenMultiStageAndStillOneAggregatedRun()
    {
        var observer = new RecordingObserver();
        using var harness = Build(fanIn: 2, observer: observer);

        // Five runs at fan-in 2 forces several stages; every value appears in every run, so the
        // aggregation must survive being folded repeatedly.
        var runs = Enumerable.Range(0, 5).Select(_ => WriteRun(harness, (1, 1), (2, 1), (3, 1))).ToList();

        var result = new ValueCountMerger(harness.Workspace, harness.Options, "score")
            .Consolidate([.. runs], TotalBytes([.. runs]), CancellationToken.None);

        Assert.Equal(
            [new ValueCount(1, 5), new ValueCount(2, 5), new ValueCount(3, 5)],
            ReadRun(harness, result));

        // A merge holds at most fan-in readers plus one writer, however many stages it takes.
        Assert.True(observer.PeakOpenReaders <= 2, $"peak readers {observer.PeakOpenReaders} exceeded the fan-in");
        Assert.True(observer.Written.Count(w => !w.Initial) >= 2, "multi-stage merging must produce intermediate runs");
    }

    [Fact]
    public void Consolidate_WhenAMergedCountWouldOverflow_ThenCalibrationPopulationTooLargeIsSignalled()
    {
        using var harness = Build();
        var a = WriteRun(harness, (1, long.MaxValue));
        var b = WriteRun(harness, (1, 1));

        // Two runs each holding a legal count for the SAME value: the sum is where a population
        // beyond long first appears, and it must be attributable — never a storage failure, and
        // never a bare OverflowException escaping the seam.
        var ex = Assert.Throws<CalibrationPopulationOverflowException>(() =>
            new ValueCountMerger(harness.Workspace, harness.Options, "score")
                .Consolidate([a, b], TotalBytes(a, b), CancellationToken.None));

        Assert.Equal("score", ex.AttributeName);
    }

    [Fact]
    public void Consolidate_WhenDeletionsFailAndLiveBytesWouldPassThreeT_ThenHaltsWithoutOpeningTheOutput()
    {
        var fileSystem = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var observer = new RecordingObserver();
        using var harness = Build(fanIn: 2, fileSystem: fileSystem, observer: observer);
        var runs = Enumerable.Range(0, 4).Select(i => WriteRun(harness, (i, 1), (i + 10, 1))).ToList();

        // A baseline far below the real payload makes the very first batch breach 3T. Deletions
        // fail, so nothing can be reclaimed and the escalation is the only correct outcome.
        var ex = Assert.Throws<GroupingStorageException>(() =>
            new ValueCountMerger(harness.Workspace, harness.Options, "score")
                .Consolidate([.. runs], baselineT: 1, CancellationToken.None));

        Assert.Equal(GroupingOperation.CleanupDelete, ex.Operation);
        Assert.Equal(SpoolFailureKind.DeleteFailed, ex.Kind);

        // "Without opening the output" is the contract, not just "fails": no merge output exists.
        Assert.DoesNotContain(observer.Written, w => !w.Initial);
    }

    [Fact]
    public void Consolidate_WhenOnlyTheFinalOutputWouldPassThreeT_ThenTheFinalWriteIsGatedToo()
    {
        // The gate that is easiest to forget: merge output coexists with its inputs until they
        // delete, so the FINAL consolidated write is no safer than an intermediate one. Deletions
        // fail here, so live bytes never fall and only the last batch crosses the bound.
        var fileSystem = new FakeSpoolFileSystem();
        var observer = new RecordingObserver();
        using var harness = Build(fanIn: 2, fileSystem: fileSystem, observer: observer);
        var runs = Enumerable.Range(0, 4).Select(i => WriteRun(harness, (i, 1), (i + 10, 1))).ToList();
        var payload = TotalBytes([.. runs]);

        // Stage 1 (two batches) must fit; the final batch must not. Live bytes after stage 1 are
        // the four originals (undeleted) plus the two intermediates ≈ 1.5·payload; the final
        // projection adds the two intermediates again.
        fileSystem.OnDeleteRun = _ => StorageFaults.AccessDenied();
        var baseline = (long)(payload * 0.7); // 3T ≈ 2.1·payload: stage 1 fits, the final does not

        var ex = Assert.Throws<GroupingStorageException>(() =>
            new ValueCountMerger(harness.Workspace, harness.Options, "score")
                .Consolidate([.. runs], baseline, CancellationToken.None));

        Assert.Equal(GroupingOperation.CleanupDelete, ex.Operation);
        Assert.Equal(2, observer.Written.Count(w => !w.Initial)); // stage 1's two outputs, and no third
    }

    [Fact]
    public void Consolidate_WhenDeletionsFailPersistentlyOnRepeatedKeyRuns_ThenThePendingCapHaltsInPath()
    {
        // The counterexample the byte rule cannot catch: every run holds the SAME key, so merging
        // shrinks the payload and live bytes never approach 3T — yet each failed delete retains
        // another pending entry. Only a fixed cap bounds that bookkeeping.
        var fileSystem = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var observer = new RecordingObserver();
        using var harness = Build(fanIn: 2, fileSystem: fileSystem, observer: observer, maxPendingDeletions: 4);
        var runs = Enumerable.Range(0, 16).Select(_ => WriteRun(harness, (1, 1))).ToList();

        var ex = Assert.Throws<GroupingStorageException>(() =>
            new ValueCountMerger(harness.Workspace, harness.Options, "score")
                .Consolidate([.. runs], TotalBytes([.. runs]) * 1000, CancellationToken.None));

        Assert.Equal(GroupingOperation.CleanupDelete, ex.Operation);
        Assert.Equal(SpoolFailureKind.DeleteFailed, ex.Kind);

        // The bookkeeping stayed bounded right up to the halt — including the retry pass's copy.
        Assert.True(harness.Workspace.PendingDeletionCount <= 4, $"pending deletions reached {harness.Workspace.PendingDeletionCount}");
        Assert.True(observer.PeakPendingDeletions <= 4);
    }

    [Fact]
    public void Consolidate_WhenDeletionsSucceed_ThenConsumedInputsAreDroppedAndNothingIsPending()
    {
        var observer = new RecordingObserver();
        using var harness = Build(fanIn: 2, observer: observer);
        var runs = Enumerable.Range(0, 4).Select(i => WriteRun(harness, (i, 1))).ToList();

        var result = new ValueCountMerger(harness.Workspace, harness.Options, "score")
            .Consolidate([.. runs], TotalBytes([.. runs]), CancellationToken.None);

        Assert.Equal(0, harness.Workspace.PendingDeletionCount);
        Assert.Equal(1, harness.Workspace.LiveRunCount); // only the consolidated run survives
        Assert.Equal(4, ReadRun(harness, result).Count);
    }

    [Fact]
    public void Consolidate_WhenRepeated_ThenTheOutputIsDeterministic()
    {
        // P-7: same runs in, same aggregated bytes out — the merge's tie handling must not depend
        // on which reader happens to reach a shared value first.
        static List<ValueCount> Run()
        {
            using var harness = Build(fanIn: 2);
            var runs = new List<SpoolRunHandle>
            {
                WriteRun(harness, (1, 1), (5, 2)),
                WriteRun(harness, (1, 3), (2, 1)),
                WriteRun(harness, (2, 4), (5, 5)),
            };

            var result = new ValueCountMerger(harness.Workspace, harness.Options, "score")
                .Consolidate([.. runs], TotalBytes([.. runs]), CancellationToken.None);
            return ReadRun(harness, result);
        }

        Assert.Equal(Run(), Run());
        Assert.Equal([new ValueCount(1, 4), new ValueCount(2, 5), new ValueCount(5, 7)], Run());
    }
}
