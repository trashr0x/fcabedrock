using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Conversion.Tests;

// The EmitReplaySession's cross-pass storage aggregation (D-082): storage failures are captured on
// every pass and flushed as one final per identity at disposal — so a pass-2-only failure is never
// lost, per-pass counts combine, severities promote, and nothing lands before disposal.
public sealed class GroupingStorageSessionTests
{
    [Fact]
    public async Task Session_WhenStorageFailsBothPasses_ThenOneFinalAtDisposalNotBefore()
    {
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);
        var (plan, source) = await PrepAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y");
        var diagnostics = new List<BedrockDiagnostic>();

        int countBeforeDisposal;
        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink, options), diagnostics))
        {
            await DrainAsync(session.Open());
            await DrainAsync(session.Open());
            countBeforeDisposal = diagnostics.Count;
        }

        Assert.Equal(0, countBeforeDisposal); // storage intercepted, not appended until disposal
        var (failure, severity) = AssertSingleStorage(diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, severity);
        Assert.Equal(GroupingOperation.CleanupDelete, failure.Operation);
    }

    [Fact]
    public async Task Session_WhenStorageFailsOnlyOnSecondPass_ThenStillCapturedAtDisposal()
    {
        // Deletes succeed on pass 1 and fail on pass 2 (a pass-2-only failure that the old first-pass-
        // only helper would have dropped) — the session captures it at disposal.
        var passWorkspaces = 0;
        var fs = new FakeSpoolFileSystem
        {
            OnCreateWorkspace = () => { passWorkspaces++; return null; },
            OnDeleteRun = _ => passWorkspaces >= 2 ? StorageFaults.AccessDenied() : null,
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);
        var (plan, source) = await PrepAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y");
        var diagnostics = new List<BedrockDiagnostic>();

        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink, options), diagnostics))
        {
            await DrainAsync(session.Open());
            await DrainAsync(session.Open());
        }

        var (failure, severity) = AssertSingleStorage(diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, severity);
        Assert.Equal(GroupingOperation.CleanupDelete, failure.Operation);
    }

    [Fact]
    public async Task Session_WhenDistinctOperationsAcrossPasses_ThenSeparateAggregatesInFirstOccurrenceOrder()
    {
        // Pass 1: a cleanup Warning (CleanupDelete). Pass 2: an in-path spill Error (Spill). Two
        // distinct identities → two aggregates, ordered by first logical occurrence (pass 1 then pass 2).
        var passWorkspaces = 0;
        var fs = new FakeSpoolFileSystem
        {
            OnCreateWorkspace = () => { passWorkspaces++; return null; },
            OnDeleteRun = _ => passWorkspaces == 1 ? StorageFaults.AccessDenied() : null,
            OnCreateRun = _ => passWorkspaces >= 2 ? StorageFaults.DiskFull() : null,
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);
        var (plan, source) = await PrepAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y");
        var diagnostics = new List<BedrockDiagnostic>();

        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink, options), diagnostics))
        {
            await DrainAsync(session.Open());
            await DrainAsync(session.Open());
        }

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticCode.GroupingStorageFailed, d.Code));
        Assert.Equal(GroupingOperation.CleanupDelete, ((GroupingStorageFailure)diagnostics[0].Context!).Operation);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostics[0].Severity);
        Assert.Equal(GroupingOperation.Spill, ((GroupingStorageFailure)diagnostics[1].Context!).Operation);
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[1].Severity);
    }

    [Fact]
    public async Task Session_WhenDrivenByCxtWriter_ThenFinalsFlushOnSuccess()
    {
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);
        var (plan, source) = await PrepAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y");
        var diagnostics = new List<BedrockDiagnostic>();

        using var output = new MemoryStream();
        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink, options), diagnostics))
        {
            await CxtWriter.WriteAsync(plan, session.Open, WriterOptions.Native, output);
        }

        Assert.True(output.Length > 0); // a valid .cxt was written (cleanup failure is non-halting)
        AssertSingleStorage(diagnostics);
    }

    [Fact]
    public async Task Session_WhenWriterThrows_ThenFinalsStillFlushedViaDisposalInFinally()
    {
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);
        var (plan, source) = await PrepAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y");
        var diagnostics = new List<BedrockDiagnostic>();

        var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink, options), diagnostics);
        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                CxtWriter.WriteAsync(plan, session.Open, WriterOptions.Native, new ThrowOnWriteStream()));
        }
        finally
        {
            session.Dispose();
        }

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
    }

    [Fact]
    public async Task Session_WhenPassDisposedEarly_ThenCleanupWarningStillFlushesAtDisposal()
    {
        // The emit is suspended at a yield and disposed early (the consumer throws mid-stream); the
        // grouping's disposal-time cleanup fails, and FlushStorage — now in a finally — records it (F3).
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);
        var (plan, source) = await PrepAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y");
        var diagnostics = new List<BedrockDiagnostic>();

        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink, options), diagnostics))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in session.Open())
                {
                    throw new InvalidOperationException("stop mid-stream");
                }
            });
        }

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Session_WhenWriterFaultsMidPass2_ThenCleanupWarningOnlyAfterDisposal()
    {
        // 40 spilled subjects × 40 attributes: the name block stays under the .cxt writer's char buffer
        // while the wide pass-2 rows cross it, so the throw-on-write faults while pass 2 is enumerating.
        const int subjects = 40;
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [.. Enumerable.Range(0, 40).Select(i => ConversionFixtures.PredicateNominal($"a{i}", $"a{i}", ["x"]))]);
        var source = ConversionFixtures.TripleSourceOver(
            string.Join('\n', Enumerable.Range(0, subjects).Select(i => $"s{i},a0,x")), ConversionFixtures.Triple(TripleOrdering.Unordered));
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var passIndex = 0;
        var pass2Yielded = 0;

        async IAsyncEnumerable<EmittedObject> CountPass2(IAsyncEnumerable<EmittedObject> inner, bool isPass2)
        {
            await foreach (var obj in inner)
            {
                if (isPass2)
                {
                    pass2Yielded++;
                }

                yield return obj;
            }
        }

        int countBeforeDisposal;
        var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink, options), diagnostics);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => CxtWriter.WriteAsync(
                plan,
                () => CountPass2(session.Open(), passIndex++ == 1),
                WriterOptions.Native,
                new ThrowOnWriteStream()));

            // Scoped to the code under test. Pass 1 completes normally before the writer faults
            // in pass 2, so its ordinary emit aggregates — here the whole-stream observability
            // warnings (§16.4/D-105), since this fixture leaves a column empty — have already
            // legitimately landed in the collector. What this test pins is narrower and
            // unchanged: a STORAGE diagnostic is intercepted on every pass and appended only at
            // disposal.
            countBeforeDisposal = diagnostics.Count(d => d.Code == DiagnosticCode.GroupingStorageFailed);
        }
        finally
        {
            session.Dispose();
        }

        // Explicitly prove pass 2 was active (suspended at a yielded object) when the writer threw.
        Assert.True(pass2Yielded > 0 && pass2Yielded < subjects, $"pass 2 yielded {pass2Yielded}/{subjects} — the writer must fault while pass 2 is active");
        Assert.Equal(0, countBeforeDisposal); // storage intercepted, appended only at disposal
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Session_WhenInPathThenCleanupWithinAPass_ThenOrderPreservedAcrossDisposal()
    {
        // Each pass: the reader ctor's stream.Length faults (in-path MergeRead) and disposing the opened
        // stream also faults (CleanupClose). The session captures both per pass and, at disposal, flushes
        // them in first-occurrence order — MergeRead then CleanupClose — preserved across passes (Codex
        // point 3, assertion 3).
        var fs = new FakeSpoolFileSystem { WrapReadStream = (_, stream) => new ThrowOnLengthStream(stream, throwOnDispose: true) };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fs);
        var (plan, source) = await PrepAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y");
        var diagnostics = new List<BedrockDiagnostic>();

        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink, options), diagnostics))
        {
            await DrainAsync(session.Open());
            await DrainAsync(session.Open());
        }

        var storage = diagnostics.Where(d => d.Code == DiagnosticCode.GroupingStorageFailed).ToList();
        Assert.Equal(2, storage.Count);
        Assert.Equal(GroupingOperation.MergeRead, ((GroupingStorageFailure)storage[0].Context!).Operation);
        Assert.Equal(DiagnosticSeverity.Error, storage[0].Severity);
        Assert.Equal(GroupingOperation.CleanupClose, ((GroupingStorageFailure)storage[1].Context!).Operation);
        Assert.Equal(DiagnosticSeverity.Warning, storage[1].Severity);
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

    private static async Task DrainAsync(IAsyncEnumerable<EmittedObject> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private static (GroupingStorageFailure Failure, DiagnosticSeverity Severity) AssertSingleStorage(
        IReadOnlyList<BedrockDiagnostic> diagnostics)
    {
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.GroupingStorageFailed, diagnostic.Code);
        return (Assert.IsType<GroupingStorageFailure>(diagnostic.Context), diagnostic.Severity);
    }

    // A stream that faults on write, to exercise a thrown-writer-failure path.
    private sealed class ThrowOnWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("simulated write failure");
    }
}
