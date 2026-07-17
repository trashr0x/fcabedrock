using System.Buffers.Binary;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

// Behavioural tests for the bounded grouping backend and its two-channel storage-failure model (D-082),
// driven end-to-end through the internal spill-forcing EmitTripleAsync overload with an injected
// filesystem and observer. Wide dedupe reuses the same backend (C3); triple unordered exercises it here.
public sealed class GroupingBackendTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GroupingOptions_WhenBudgetNonPositive_ThenThrows(long budget) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GroupingOptions(maxBufferedBytes: budget));

    [Fact]
    public void GroupingOptions_WhenFanInBelowTwo_ThenThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GroupingOptions(maxMergeFanIn: 1));

    [Fact]
    public async Task Backend_WhenZeroSpillUnderUnusableRoot_ThenSucceedsWithoutTouchingDisk()
    {
        // Lazy workspace: a tiny input under the default 64 MiB budget never spills, so CreateWorkspace
        // (which would fail) is never called — zero-spill needs no disk access at all.
        var fs = new FakeSpoolFileSystem { OnCreateWorkspace = () => new IOException("unusable temp root") };
        var options = new GroupingOptions(fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns0,a,y", options);

        Assert.Empty(diagnostics);
        Assert.Equal(["s0", "s1"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task Backend_WhenFirstSpillCannotCreateWorkspace_ThenErrorAndHalts()
    {
        var fs = new FakeSpoolFileSystem { OnCreateWorkspace = () => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs); // forces a spill

        var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns0,a,y", options);

        Assert.Empty(objects);
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Error);
        Assert.Equal(GroupingOperation.Workspace, failure.Operation);
        Assert.Equal(SpoolFailureKind.WorkspaceCreation, failure.Kind);
    }

    [Fact]
    public async Task Backend_WhenSpillWriteHitsDiskFull_ThenErrorStorageExhaustedAndHalts()
    {
        var fs = new FakeSpoolFileSystem { OnCreateRun = _ => StorageFaults.DiskFull() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns0,a,y", options);

        Assert.Empty(objects);
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Error);
        Assert.Equal(GroupingOperation.Spill, failure.Operation);
        Assert.Equal(SpoolFailureKind.StorageExhausted, failure.Kind);
    }

    [Fact]
    public async Task Backend_WhenRunCorruptDuringMerge_ThenErrorCorruptRunAndHalts()
    {
        var fs = new FakeSpoolFileSystem
        {
            WrapReadStream = (_, stream) =>
            {
                stream.Dispose();
                return new MemoryStream(CorruptRecord());
            },
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync(Rows(6), options);

        Assert.Empty(objects);
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Error);
        Assert.Equal(GroupingOperation.MergeRead, failure.Operation);
        Assert.Equal(SpoolFailureKind.CorruptRun, failure.Kind);
    }

    [Fact]
    public async Task Backend_WhenDeleteFailsPersistently_ThenOneWarningAggregateAndCompletes()
    {
        // Cleanup-class failure: consumed-run deletes fail, but the rows still stream to completion; the
        // failures aggregate to a single Warning with a bounded (≤ 3) sample, not one per run.
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs); // default fan-in → single-stage

        var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y", options);

        Assert.Equal(["s0", "s1", "s2"], objects.Select(o => o.Name)); // enumeration completed
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Warning);
        Assert.Equal(GroupingOperation.CleanupDelete, failure.Operation);
        Assert.Equal(SpoolFailureKind.DeleteFailed, failure.Kind);
        Assert.True(failure.Count >= 3);
        Assert.True(failure.PathSamples.Count <= 3);
    }

    [Fact]
    public async Task Backend_WhenDeleteDeniedViaDifferentExceptionTypes_ThenOneIdentity()
    {
        // Classification stability: delete failures surfacing as UnauthorizedAccessException and
        // IOException are one identity (CleanupDelete, DeleteFailed) — the operation, not the exception
        // type, classifies deletes.
        var toggle = 0;
        var fs = new FakeSpoolFileSystem
        {
            OnDeleteRun = _ => toggle++ % 2 == 0 ? StorageFaults.AccessDenied() : StorageFaults.DiskFull(),
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y", options);

        Assert.Equal(3, objects.Count);
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Warning);
        Assert.Equal(SpoolFailureKind.DeleteFailed, failure.Kind);
    }

    [Fact]
    public async Task Backend_WhenPersistentDeleteFailureMultiStage_ThenEscalatesBeforeExceeding3T()
    {
        var observer = new RecordingObserver();
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs, observer: observer);

        var (_, diagnostics) = await RunUnorderedAsync(Rows(40, subjects: 5), options);

        // The pre-batch escalation halts (Error) before any batch could push disk past 3T; the pre- and
        // escalation-time cleanup failures merge to one Error aggregate (same identity, worst severity).
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Error);
        Assert.Equal(GroupingOperation.CleanupDelete, failure.Operation);
        Assert.Equal(SpoolFailureKind.DeleteFailed, failure.Kind);

        var baselineT = observer.Written.Where(w => w.Initial).Sum(w => w.Size);
        Assert.True(baselineT > 0);
        Assert.True(observer.PeakLiveBytes <= 3 * baselineT, $"peak {observer.PeakLiveBytes} exceeded 3T = {3 * baselineT}");
    }

    [Fact]
    public async Task Backend_WhenMultiStageMerge_ThenBoundedOpenReadersInitialRunsAndConsumedDeletion()
    {
        var observer = new RecordingObserver();
        var options = new GroupingOptions(maxBufferedBytes: 80, maxMergeFanIn: 3, observer: observer);

        var (objects, diagnostics) = await RunUnorderedAsync(Rows(30, subjects: 6), options);

        Assert.Empty(diagnostics);
        Assert.Equal(6, objects.Count);
        Assert.True(observer.Written.Any(w => !w.Initial), "expected at least one intermediate (merge) run");
        Assert.True(observer.PeakOpenReaders <= options.MaxMergeFanIn, $"peak open readers {observer.PeakOpenReaders} > fan-in {options.MaxMergeFanIn}");
        Assert.All(
            observer.Written.Where(w => w.Initial),
            w => Assert.True(w.Size <= options.MaxBufferedBytes + 256, $"initial run {w.Size} exceeded budget+maxRow"));
        Assert.NotEmpty(observer.Deleted); // consumed runs were deleted between stages
    }

    [Fact]
    public async Task Backend_WhenCancelledDuringYield_ThenOperationCanceledAndNoLeftoverSpool()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "fcab-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var options = new GroupingOptions(maxBufferedBytes: 1, tempDirectory: tempRoot);
            var (plan, source) = await PrepAsync(Rows(6, subjects: 3));
            using var cts = new CancellationTokenSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in Emitter.EmitTripleAsync(plan, source, new List<BedrockDiagnostic>(), options, cts.Token))
                {
                    cts.Cancel(); // cancel after the first object; the next advance observes it
                }
            });

            Assert.Empty(Directory.GetDirectories(tempRoot, "fcabedrock-spool-*")); // workspace cleaned up
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Backend_WhenMergeReadFaultsWithIOException_ThenErrorMergeReadNoExceptionEscapes()
    {
        // A read fault after a run opens (mid-merge) is an owned Error, not a raw exception across the
        // seam (F1). WrapReadStream returns a stream that throws IOException on Read.
        var fs = new FakeSpoolFileSystem
        {
            WrapReadStream = (_, stream) =>
            {
                stream.Dispose();
                return new ThrowingReadStream();
            },
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync(Rows(6), options);

        Assert.Empty(objects);
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Error);
        Assert.Equal(GroupingOperation.MergeRead, failure.Operation);
    }

    [Fact]
    public async Task Backend_WhenDeleteFailsOnceThenSucceeds_ThenCompletesNoEscalationAndCountsTransient()
    {
        // A transient deletion failure is retried at the next batch boundary and succeeds, so the merge
        // completes without a 3T escalation; the transient failure is still counted in the Warning (F4).
        var failedOnce = false;
        var fs = new FakeSpoolFileSystem
        {
            OnDeleteRun = _ =>
            {
                if (failedOnce)
                {
                    return null; // all later deletes (including the retry) succeed
                }

                failedOnce = true;
                return StorageFaults.AccessDenied();
            },
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync(Rows(12, subjects: 4), options);

        Assert.Equal(4, objects.Count); // completed — no escalation
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Warning);
        Assert.Equal(GroupingOperation.CleanupDelete, failure.Operation);
        Assert.True(failure.Count >= 1); // the transient failure is represented
    }

    [Fact]
    public async Task Backend_WhenSpillForcedUnordered_ThenSameObjectsAndCrossesAsSubjectGrouped()
    {
        // Spilling never changes results (P-7): the interleaved mushroom data grouped via a spill-forced
        // unordered path emits the same objects (first-appearance order) and crosses as the contiguous
        // subject_grouped path over the equivalent contiguous data.
        var spec = ConversionFixtures.MushroomTripleSpec();

        var (unordered, unorderedDiags) = await RunTripleAsync(
            spec, ConversionFixtures.MushroomTripleDataInterleaved, TripleOrdering.Unordered, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2));
        var (grouped, groupedDiags) = await RunTripleAsync(
            spec, ConversionFixtures.MushroomTripleData, TripleOrdering.SubjectGrouped, GroupingOptions.Default);

        Assert.Empty(unorderedDiags);
        Assert.Empty(groupedDiags);
        Assert.Equal(grouped.Select(o => o.Name), unordered.Select(o => o.Name));
        Assert.Equal(grouped.Select(o => o.CrossedFormalAttributeIds), unordered.Select(o => o.CrossedFormalAttributeIds));
    }

    [Fact]
    public async Task Backend_WhenReaderCloseFailsDuringMerge_ThenCleanupCloseWarningAndCompletes()
    {
        // A reader whose Dispose faults is a cleanup-class failure: the merge still delivers every object
        // and the close failure surfaces as one CleanupClose Warning, never a raw exception escaping
        // disposal (F2). Reads succeed (the decorator delegates them); only Dispose throws.
        var fs = new FakeSpoolFileSystem { WrapReadStream = (_, stream) => new ThrowOnDisposeStream(stream) };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y", options);

        Assert.Equal(["s0", "s1", "s2"], objects.Select(o => o.Name)); // completed
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Warning);
        Assert.Equal(GroupingOperation.CleanupClose, failure.Operation);
    }

    [Fact]
    public async Task Backend_WhenReaderCloseFailsOnEarlyDisposal_ThenPrimaryPreservedAndWarning()
    {
        // The consumer throws mid-stream; the grouping's disposal-time reader-close then faults. The
        // consumer's exception stays the primary outcome and the close failure is a CleanupClose Warning.
        var fs = new FakeSpoolFileSystem { WrapReadStream = (_, stream) => new ThrowOnDisposeStream(stream) };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);
        var (plan, source) = await PrepAsync(Rows(6, subjects: 3));
        var diagnostics = new List<BedrockDiagnostic>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in Emitter.EmitTripleAsync(plan, source, diagnostics, options))
            {
                throw new InvalidOperationException("stop mid-stream");
            }
        });

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed
            && d.Severity == DiagnosticSeverity.Warning
            && ((GroupingStorageFailure)d.Context!).Operation == GroupingOperation.CleanupClose);
    }

    [Fact]
    public async Task Backend_WhenReaderCloseFailsDuringCancellation_ThenOperationCanceledPreservedAndWarning()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "fcab-cancel-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var fs = new FakeSpoolFileSystem { WrapReadStream = (_, stream) => new ThrowOnDisposeStream(stream) };
            var options = new GroupingOptions(maxBufferedBytes: 1, tempDirectory: tempRoot, fileSystem: fs);
            var (plan, source) = await PrepAsync(Rows(6, subjects: 3));
            var diagnostics = new List<BedrockDiagnostic>();
            using var cts = new CancellationTokenSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in Emitter.EmitTripleAsync(plan, source, diagnostics, options, cts.Token))
                {
                    cts.Cancel(); // the next advance observes cancellation; reader-close then faults during teardown
                }
            });

            // The cancellation is the primary outcome (not the close IOException), and the close failure is
            // a CleanupClose Warning; the workspace is still cleaned up (the decorator releases the handle).
            Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed
                && d.Severity == DiagnosticSeverity.Warning
                && ((GroupingStorageFailure)d.Context!).Operation == GroupingOperation.CleanupClose);
            Assert.Empty(Directory.GetDirectories(tempRoot, "fcabedrock-spool-*"));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Backend_WhenReaderLengthFails_ThenMergeReadErrorAndStreamDisposed()
    {
        // OpenRun succeeds but the SpoolRunReader ctor's stream.Length read faults: an in-path MergeRead
        // Error, and the already-open stream is disposed (never leaked) — F2 / Codex point 2.
        var streams = new List<ThrowOnLengthStream>();
        var fs = new FakeSpoolFileSystem
        {
            WrapReadStream = (_, stream) =>
            {
                var wrapped = new ThrowOnLengthStream(stream, throwOnDispose: false);
                streams.Add(wrapped);
                return wrapped;
            },
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync(Rows(6), options);

        Assert.Empty(objects);
        var failure = AssertSingleStorageFailure(diagnostics, DiagnosticSeverity.Error);
        Assert.Equal(GroupingOperation.MergeRead, failure.Operation);
        Assert.NotEmpty(streams);
        Assert.All(streams, s => Assert.True(s.Disposed, "the opened stream must be disposed, not leaked"));
    }

    [Fact]
    public async Task Backend_WhenReaderLengthFailsAndCloseFails_ThenMergeReadErrorThenCleanupCloseWarningInOrder()
    {
        // The reader ctor's stream.Length faults (in-path MergeRead), and disposing the opened stream
        // during cleanup ALSO faults (CleanupClose). Both surface, in first-occurrence order: MergeRead
        // then CleanupClose — the in-path failure is recorded before its cleanup (Codex point 3).
        var fs = new FakeSpoolFileSystem { WrapReadStream = (_, stream) => new ThrowOnLengthStream(stream, throwOnDispose: true) };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync(Rows(6), options);

        Assert.Empty(objects);
        var storage = diagnostics.Where(d => d.Code == DiagnosticCode.GroupingStorageFailed).ToList();
        Assert.Equal(2, storage.Count);
        Assert.Equal(GroupingOperation.MergeRead, ((GroupingStorageFailure)storage[0].Context!).Operation);
        Assert.Equal(DiagnosticSeverity.Error, storage[0].Severity);
        Assert.Equal(GroupingOperation.CleanupClose, ((GroupingStorageFailure)storage[1].Context!).Operation);
        Assert.Equal(DiagnosticSeverity.Warning, storage[1].Severity);
    }

    [Fact]
    public async Task Backend_WhenCleanupWarningPrecedesInPathError_ThenChronologicalOrderRetained()
    {
        // Multi-stage merge, budget = 1: 6 intake spills (creates 1–6), then level-1 merge writes create
        // 7, 8, 9. Consumed-run deletes always fail (a CleanupDelete Warning first appears at the create-7
        // batch's deletes); the create-8 merge write then fails (a MergeWrite Error). The ledger keeps the
        // true order [CleanupDelete Warning, MergeWrite Error] — the opposite of the MergeRead-first case
        // above, proving chronology, not a fixed rule (Codex point 3, assertion 2).
        var creates = 0;
        var fs = new FakeSpoolFileSystem
        {
            OnDeleteRun = _ => StorageFaults.AccessDenied(),
            OnCreateRun = _ => ++creates == 8 ? StorageFaults.DiskFull() : null,
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync(Rows(6, subjects: 3), options);

        Assert.Empty(objects);
        var storage = diagnostics.Where(d => d.Code == DiagnosticCode.GroupingStorageFailed).ToList();
        Assert.Equal(2, storage.Count);
        Assert.Equal(GroupingOperation.CleanupDelete, ((GroupingStorageFailure)storage[0].Context!).Operation);
        Assert.Equal(DiagnosticSeverity.Warning, storage[0].Severity);
        Assert.Equal(GroupingOperation.MergeWrite, ((GroupingStorageFailure)storage[1].Context!).Operation);
        Assert.Equal(DiagnosticSeverity.Error, storage[1].Severity);
    }

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunTripleAsync(
        BedrockSpec spec, string data, TripleOrdering ordering, GroupingOptions options)
    {
        var orderedSpec = new BedrockSpec(ConversionFixtures.Triple(ordering), spec.Attributes);
        var source = ConversionFixtures.TripleSourceOver(data, ConversionFixtures.Triple(ordering));
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(orderedSpec, schema).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan, source, diagnostics, options))
        {
            objects.Add(emitted);
        }

        // Observability warnings are orthogonal here and partitioned out (see DataDiagnostics).
        return (objects, ConversionFixtures.DataDiagnostics(diagnostics));
    }

    private static string Rows(int count, int subjects = 3) =>
        string.Join('\n', Enumerable.Range(0, count).Select(i => $"s{i % subjects},a,x"));

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunUnorderedAsync(
        string data, GroupingOptions options)
    {
        var (plan, source) = await PrepAsync(data);
        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan, source, diagnostics, options))
        {
            objects.Add(emitted);
        }

        // Observability warnings are orthogonal here and partitioned out (see DataDiagnostics).
        return (objects, ConversionFixtures.DataDiagnostics(diagnostics));
    }

    private static async Task<(ConversionPlan Plan, FcaBedrock.Sources.ITripleRowSource Source)> PrepAsync(string data)
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);
        var source = ConversionFixtures.TripleSourceOver(data, ConversionFixtures.Triple(TripleOrdering.Unordered));
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return (plan, source);
    }

    private static GroupingStorageFailure AssertSingleStorageFailure(
        IReadOnlyList<BedrockDiagnostic> diagnostics, DiagnosticSeverity severity)
    {
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.GroupingStorageFailed, diagnostic.Code);
        Assert.Equal(severity, diagnostic.Severity);
        return Assert.IsType<GroupingStorageFailure>(diagnostic.Context);
    }

    // A well-framed record whose subject string length overruns it — a small, safely-identifiable
    // corruption (never a huge allocation).
    private static byte[] CorruptRecord()
    {
        var record = new byte[24];
        var span = record.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, 20);       // recordLength
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 0);   // rank
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], 0);   // seq
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 0);  // recordIndex
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], 1000); // subject length overruns
        return record;
    }
}
