using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// The .cxt writer replays the emitted-object stream (pass 1 names, pass 2 incidence), so a naive shared
// diagnostics collector would double-count. EmitReplaySession collects data diagnostics on the first
// pass only (later passes discard them) and aggregates grouping storage failures across passes, flushing
// the finals at disposal. These pin the data-collection behaviour + session lifecycle; storage
// aggregation is covered in GroupingStorageSessionTests.
public sealed class EmitReplayTests
{
    [Fact]
    public async Task Session_WhenWideStreamReplayed_ThenDataDiagnosticsCollectedOnce()
    {
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        var diagnostics = new List<BedrockDiagnostic>();
        using (var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan, source, sink), diagnostics))
        {
            await DrainAsync(session.Open()); // pass 1 (names)
            await DrainAsync(session.Open()); // pass 2 (incidence)
        }

        // Scoped to the code under test: the whole-stream observability warnings (§16.4/D-105)
        // land in the same collector — this tiny fixture leaves a column empty — and are
        // single-counted by the same first-pass claim, proven directly in EmitObservabilityTests.
        // "Exactly one of THIS code" is what keeps the not-one-per-pass teeth.
        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task Session_WhenTripleStreamReplayed_ThenDataDiagnosticsCollectedOnce()
    {
        // Shape-agnostic: EmitTripleAsync feeds the session identically.
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);
        var (plan, source) = await TriplePrepAsync(spec, "s,a,z"); // z is out of domain → UnknownValueObserved
        var diagnostics = new List<BedrockDiagnostic>();
        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink), diagnostics))
        {
            await DrainAsync(session.Open());
            await DrainAsync(session.Open());
        }

        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
    }

    [Fact]
    public async Task Session_WhenPassesOpenedBeforeEnumeration_ThenDataStillCollectedOnce()
    {
        // The data-sink claim is tied to first ENUMERATION, not Open(): opening both passes up front
        // must not make both collect. Pins the deferred, first-MoveNext claim.
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        var diagnostics = new List<BedrockDiagnostic>();
        using (var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan, source, sink), diagnostics))
        {
            var first = session.Open();
            var second = session.Open();
            await DrainAsync(first);
            await DrainAsync(second);
        }

        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
    }

    [Fact]
    public async Task Session_WhenStructuralHalt_ThenBothPassesTruncateConsistentlyAndDiagnosticOnce()
    {
        // Discarding the pass-2 diagnostic must not change WHERE the stream stops: a non-contiguous
        // subject yields TripleSubjectNotContiguous + halt, driven by the data. Both passes emit the
        // identical (truncated) object sequence, and the diagnostic is collected once.
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);
        var (plan, source) = await TriplePrepAsync(spec, "s1,a,x\ns2,a,y\ns1,b,z"); // s1 recurs at record 2
        var diagnostics = new List<BedrockDiagnostic>();
        List<string> firstPass;
        List<string> secondPass;
        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan, source, sink), diagnostics))
        {
            firstPass = await DrainNamesAsync(session.Open());
            secondPass = await DrainNamesAsync(session.Open());
        }

        Assert.Equal(firstPass, secondPass); // consistent truncation across passes
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.TripleSubjectNotContiguous, diagnostic.Code);
    }

    [Fact]
    public async Task Session_WhenOpenDuringActivePass_ThenThrowsInvalidOperation()
    {
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        using var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan, source, sink), new List<BedrockDiagnostic>());

        await using var enumerator = session.Open().GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync()); // a pass is now active
        Assert.Throws<InvalidOperationException>(session.Open);
    }

    [Fact]
    public async Task Session_WhenDisposeDuringActivePass_ThenThrowsInvalidOperation()
    {
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan, source, sink), new List<BedrockDiagnostic>());

        await using var enumerator = session.Open().GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Throws<InvalidOperationException>(session.Dispose);
    }

    [Fact]
    public async Task Session_WhenDisposedTwice_ThenIdempotentAndOpenThrows()
    {
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        var diagnostics = new List<BedrockDiagnostic>();
        var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan, source, sink), diagnostics);
        await DrainAsync(session.Open());

        session.Dispose();
        var afterFirst = diagnostics.Count;
        session.Dispose(); // idempotent no-op — no duplicate diagnostics, no exception

        Assert.Equal(afterFirst, diagnostics.Count);
        Assert.Throws<ObjectDisposedException>(session.Open);
    }

    [Fact]
    public async Task Session_WhenDisposedBeforePreOpenedStreamStarts_ThenStartThrowsObjectDisposed()
    {
        // The start-vs-dispose race: a stream opened while idle, the session disposed (Idle → Disposed),
        // then the pre-opened stream starts → its atomic claim sees Disposed and throws (F5).
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan, source, sink), new List<BedrockDiagnostic>());

        var stream = session.Open(); // deferred, not yet advanced
        session.Dispose();

        await using var enumerator = stream.GetAsyncEnumerator();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task Session_WhenTwoStreamsStartConcurrently_ThenSecondThrowsInvalidOperation()
    {
        // Two streams opened before either advances: the first to start claims Active atomically; the
        // second's start loses the race and throws, so the ledger is never touched by two passes (F5).
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        using var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan, source, sink), new List<BedrockDiagnostic>());

        var first = session.Open();
        var second = session.Open();

        await using var e1 = first.GetAsyncEnumerator();
        Assert.True(await e1.MoveNextAsync()); // first claims Active

        await using var e2 = second.GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await e2.MoveNextAsync());
    }

    private static BedrockSpec UnknownValueWideSpec() =>
        new(ConversionFixtures.Wide(hasHeader: false), [ConversionFixtures.Nominal("a", 0, "x", "y")]);

    private static async Task<(ConversionPlan Plan, IRecordSource Source)> WidePrepAsync(
        BedrockSpec spec, string csv, Binding binding)
    {
        var source = ConversionFixtures.SourceOver(csv, binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return (plan!, source);
    }

    private static async Task<(ConversionPlan Plan, ITripleRowSource Source)> TriplePrepAsync(
        BedrockSpec spec, string tripleData)
    {
        var source = ConversionFixtures.TripleSourceOver(tripleData, ConversionFixtures.Triple());
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return (plan!, source);
    }

    private static async Task DrainAsync(IAsyncEnumerable<EmittedObject> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private static async Task<List<string>> DrainNamesAsync(IAsyncEnumerable<EmittedObject> stream)
    {
        var names = new List<string>();
        await foreach (var obj in stream)
        {
            names.Add(obj.Name);
        }

        return names;
    }
}
