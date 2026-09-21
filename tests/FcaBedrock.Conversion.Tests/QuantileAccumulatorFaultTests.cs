using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// What a spilling <see cref="QuantileAccumulator"/> does when storage fails or the caller cancels
/// around a during-intake merge, the final merge, or the replay.
/// <para>
/// The obligations are the contract's: an in-path failure is recorded <b>at source</b> before the
/// unwinding it triggers and yields <b>no</b> calibrated result, cleanup warnings never mask the
/// primary result, cancellation is not a diagnostic, and a counting overflow is never reported as a
/// storage failure. Faults are injected by operation count at the filesystem seam, so no test
/// depends on which physical run a merge touches first.
/// </para>
/// </summary>
public sealed class QuantileAccumulatorFaultTests
{
    private const int Capacity = 3;
    private const int FanIn = 2;

    private sealed record Rig(
        QuantileAccumulator Accumulator,
        SpoolWorkspace<ValueCount> Workspace,
        GroupingReports Reports,
        RecordingCalibrationObserver Observer,
        FakeSpoolFileSystem Files) : IDisposable
    {
        public void Dispose() => Workspace.Cleanup();
    }

    private static Rig Build(FakeSpoolFileSystem files, CancellationToken token = default, int fanIn = FanIn)
    {
        var observer = new RecordingCalibrationObserver();
        var options = new GroupingOptions(
            QuantileAccumulator.Modeled(Capacity), fanIn, tempDirectory: null, files, observer);
        var reports = new GroupingReports();
        var workspace = new SpoolWorkspace<ValueCount>(
            options, ValueCountCodec.Instance, reports, QuantileAccumulator.MaxPendingDeletions(fanIn));
        var budget = new CalibrationBudget(options.MaxBufferedBytes, 1, observer);
        var accumulator = new QuantileAccumulator(
            "score", CultureInfo.InvariantCulture, budget, workspace, options, observer, token);
        return new Rig(accumulator, workspace, reports, observer, files);
    }

    private static void Feed(QuantileAccumulator accumulator, int values, int from = 1)
    {
        var tally = new DiagnosticTally();
        for (var i = 0; i < values; i++)
        {
            accumulator.Observe((from + i).ToString(CultureInfo.InvariantCulture), tally);
        }
    }

    // --- Output faults on a during-intake merge -----------------------------------------------

    [Fact]
    public void IntakeMerge_WhenItsOutputCannotBeCreated_ThenItIsAnInPathFailureWithNoResult()
    {
        var files = new FakeSpoolFileSystem();
        var created = 0;
        files.OnCreateRun = _ =>
        {
            created++;

            // Let the first two original spills through and fail the third create. At fan-in two
            // that is the first merge output: the carry that follows the second spill.
            return created >= 3 ? StorageFaults.DiskFull() : null;
        };

        using var rig = Build(files);

        var thrown = Assert.Throws<GroupingStorageException>(() => Feed(rig.Accumulator, Capacity * 6));

        // Recorded at source, as an Error, before the unwinding it triggers.
        Assert.Equal(GroupingOperation.MergeWrite, thrown.Operation);
        var ledger = rig.Reports.Aggregates();
        Assert.Contains(ledger, entry => entry.Failure.Operation == GroupingOperation.MergeWrite);

        // No calibrated result can follow: the accumulator never reaches an extraction.
        Assert.True(rig.Accumulator.Spilled);
        Assert.Equal(0, rig.Observer.PeakPendingDeletions);
    }

    [Fact]
    public void IntakeMerge_WhenAnInputCannotBeOpened_ThenTheFailureIsRecordedAtSourceAndReadersStayWithinFanIn()
    {
        var files = new FakeSpoolFileSystem();
        var opens = 0;
        files.OnOpenRun = _ =>
        {
            opens++;
            return opens == 2 ? StorageFaults.AccessDenied() : null; // fail the second reader of the batch
        };

        using var rig = Build(files);
        var thrown = Assert.Throws<GroupingStorageException>(() => Feed(rig.Accumulator, Capacity * 6));

        Assert.Equal(GroupingOperation.MergeRead, thrown.Operation);
        Assert.Contains(rig.Reports.Aggregates(), entry => entry.Failure.Operation == GroupingOperation.MergeRead);

        // Readers stayed within the fan-in.
        Assert.True(rig.Observer.PeakOpenReaders <= FanIn);
    }

    [Fact]
    public void IntakeMerge_WhenAnInputIsTruncatedOrCorrupt_ThenItIsAnOwnedStorageFailure()
    {
        // A run whose bytes are replaced by a shorter, structurally impossible stream: the reader
        // must map it to an owned storage failure, never a malformed row or silent corruption.
        var files = new FakeSpoolFileSystem { WrapReadStream = (_, stream) => new TruncatingStream(stream) };
        using var rig = Build(files);

        var thrown = Assert.Throws<GroupingStorageException>(() => Feed(rig.Accumulator, Capacity * 6));
        Assert.Equal(GroupingOperation.MergeRead, thrown.Operation);
        Assert.Contains(
            thrown.Kind,
            (SpoolFailureKind[])[SpoolFailureKind.TruncatedRun, SpoolFailureKind.CorruptRun]);
        Assert.Contains(rig.Reports.Aggregates(), entry => entry.Failure.Operation == GroupingOperation.MergeRead);
    }

    // --- Deletion faults ------------------------------------------------------------------------

    [Fact]
    public void IntakeMerge_WhenConsumedRunDeletionFailsTransiently_ThenItIsRetriedAndNothingHalts()
    {
        var files = new FakeSpoolFileSystem();
        var attempts = 0;
        files.OnDeleteRun = _ =>
        {
            attempts++;
            return attempts == 1 ? StorageFaults.AccessDenied() : null; // one transient failure
        };

        using var rig = Build(files);
        Feed(rig.Accumulator, Capacity * 8);
        rig.Accumulator.EndIntake();
        rig.Accumulator.PrepareReplay();

        // A transient failure retries at the next batch boundary and clears; the primary result
        // survives, and the cleanup-channel Warning never replaces it.
        Assert.True(attempts > 1);
        var cuts = rig.Accumulator.TryExtractEqualFrequencyCuts(
            new PendingEqualFrequency(2, TiePolicy.Left, CutPlacement.RightValue), out var distinct);
        Assert.NotNull(cuts);
        Assert.Equal(Capacity * 8, distinct);
        rig.Accumulator.Release();
    }

    [Fact]
    public void IntakeMerge_WhenDeletionFailsPermanently_ThenThePendingCapHaltsInPath()
    {
        var files = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        using var rig = Build(files);

        // Enough spills that consumed runs accumulate past the 4*fan-in retained-deletion cap.
        var thrown = Assert.Throws<GroupingStorageException>(() => Feed(rig.Accumulator, Capacity * 400));
        Assert.Equal(GroupingOperation.CleanupDelete, thrown.Operation);
        Assert.Equal(SpoolFailureKind.DeleteFailed, thrown.Kind);
        Assert.True(
            rig.Observer.PeakPendingDeletions <= QuantileAccumulator.MaxPendingDeletions(FanIn),
            $"pending deletions reached {rig.Observer.PeakPendingDeletions}, above the cap");
    }

    // --- Cancellation ---------------------------------------------------------------------------

    [Fact]
    public void Intake_WhenCancelledBeforeAnyMerge_ThenItPropagatesWithNoCalibratedResult()
    {
        using var source = new CancellationTokenSource();
        var files = new FakeSpoolFileSystem();
        using var rig = Build(files, source.Token);

        // One spill, then cancel: the next merge observes it. Cancellation is not a diagnostic.
        Feed(rig.Accumulator, Capacity + 1);
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Feed(rig.Accumulator, Capacity * 8, from: 1000));
        Assert.Empty(rig.Reports.Aggregates());
    }

    [Fact]
    public void FinalMerge_WhenCancelled_ThenItPropagatesAndNoCutsAreProduced()
    {
        using var source = new CancellationTokenSource();
        var files = new FakeSpoolFileSystem();
        using var rig = Build(files, source.Token);

        Feed(rig.Accumulator, Capacity * 5);
        rig.Accumulator.EndIntake();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(rig.Accumulator.PrepareReplay);
        Assert.Empty(rig.Reports.Aggregates());
    }

    [Fact]
    public void Replay_WhenCancelled_ThenItPropagatesAndNoCutsAreProduced()
    {
        using var source = new CancellationTokenSource();
        var files = new FakeSpoolFileSystem();
        using var rig = Build(files, source.Token);

        Feed(rig.Accumulator, Capacity * 5);
        rig.Accumulator.EndIntake();
        rig.Accumulator.PrepareReplay();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => rig.Accumulator.TryExtractEqualFrequencyCuts(
            new PendingEqualFrequency(2, TiePolicy.Left, CutPlacement.RightValue), out _));
        rig.Accumulator.Release();
    }

    // --- Checked counting -----------------------------------------------------------------------

    [Fact]
    public void Intake_WhenTheRunningTotalOverflowsAfterSpilling_ThenItIsAPopulationOverflowNotAStorageFailure()
    {
        var files = new FakeSpoolFileSystem();
        using var rig = Build(files);

        // Seeding the running total to the top of the range makes the next observation's checked
        // add overflow. The accumulator has already spilled and carried, and the new key forces one
        // more spill first, so this is the population path with storage active — and it must not
        // surface as a storage failure.
        Feed(rig.Accumulator, Capacity * 4);
        rig.Accumulator.SeedTotalForTest(long.MaxValue);

        var thrown = Assert.Throws<CalibrationPopulationOverflowException>(
            () => Feed(rig.Accumulator, 1, from: 99_999));
        Assert.Equal("score", thrown.AttributeName);
        Assert.Empty(rig.Reports.Aggregates()); // a counting condition, never a storage failure
    }
}

// Returns a prefix of the run and then reports end of file, so the reader meets a record that runs
// past the file it was told the length of.
internal sealed class TruncatingStream(Stream inner) : Stream
{
    private long _returned;

    public override bool CanRead => true;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        // Serve the first few bytes honestly, then stop: the declared record cannot complete.
        if (_returned >= 8)
        {
            return 0;
        }

        var read = inner.Read(buffer);
        _returned += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
