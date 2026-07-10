using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// The .cxt writer replays the emitted-object stream (pass 1 names, pass 2 incidence), so a naive
// shared diagnostics collector would double-count. EmitReplay.CollectDiagnosticsOnce collects on the
// first full enumeration only. These pin: single collection (wide + triple), the claim being tied to
// first enumeration (not factory invocation), and control flow being preserved under a structural halt.
public sealed class EmitReplayTests
{
    [Fact]
    public async Task CollectDiagnosticsOnce_WhenWideStreamReplayed_ThenDiagnosticsCollectedOnce()
    {
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        var diagnostics = new List<BedrockDiagnostic>();
        var replay = EmitReplay.CollectDiagnosticsOnce(sink => Emitter.EmitAsync(plan, source, sink), diagnostics);

        await DrainAsync(replay); // pass 1 (names)
        await DrainAsync(replay); // pass 2 (incidence)

        var diagnostic = Assert.Single(diagnostics); // not one-per-pass
        Assert.Equal(DiagnosticCode.UnknownValueObserved, diagnostic.Code);
    }

    [Fact]
    public async Task CollectDiagnosticsOnce_WhenTripleStreamReplayed_ThenDiagnosticsCollectedOnce()
    {
        // The helper is shape-agnostic: EmitTripleAsync feeds it identically (Slice G triple .cxt goldens).
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);
        var (plan, source) = await TriplePrepAsync(spec, "s,a,z"); // z is out of domain → UnknownValueObserved
        var diagnostics = new List<BedrockDiagnostic>();
        var replay = EmitReplay.CollectDiagnosticsOnce(sink => Emitter.EmitTripleAsync(plan, source, sink), diagnostics);

        await DrainAsync(replay);
        await DrainAsync(replay);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownValueObserved, diagnostic.Code);
    }

    [Fact]
    public async Task CollectDiagnosticsOnce_WhenFactoryInvokedBeforeEnumeration_ThenStillOnce()
    {
        // The claim is tied to first ENUMERATION, not factory invocation: obtaining both streams up
        // front must not make both collect. Pins the deferred, first-MoveNext claim (r3 #5).
        var (plan, source) = await WidePrepAsync(UnknownValueWideSpec(), "z", ConversionFixtures.Wide(hasHeader: false));
        var diagnostics = new List<BedrockDiagnostic>();
        var replay = EmitReplay.CollectDiagnosticsOnce(sink => Emitter.EmitAsync(plan, source, sink), diagnostics);

        var firstStream = replay();
        var secondStream = replay();
        await DrainAsync(firstStream);
        await DrainAsync(secondStream);

        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task CollectDiagnosticsOnce_WhenStructuralHalt_ThenBothPassesTruncateConsistentlyAndDiagnosticOnce()
    {
        // Discarding the pass-2 diagnostic must not change WHERE the stream stops: a non-contiguous
        // subject yields TripleSubjectNotContiguous + yield break, driven by the data. Both passes must
        // emit the identical (truncated) object sequence, and the diagnostic is collected once (r4 #3).
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);
        var (plan, source) = await TriplePrepAsync(spec, "s1,a,x\ns2,a,y\ns1,b,z"); // s1 recurs at record 2
        var diagnostics = new List<BedrockDiagnostic>();
        var replay = EmitReplay.CollectDiagnosticsOnce(sink => Emitter.EmitTripleAsync(plan, source, sink), diagnostics);

        var firstPass = await DrainNamesAsync(replay);
        var secondPass = await DrainNamesAsync(replay);

        Assert.Equal(firstPass, secondPass); // consistent truncation across passes
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.TripleSubjectNotContiguous, diagnostic.Code);
    }

    private static BedrockSpec UnknownValueWideSpec() =>
        new(ConversionFixtures.Wide(hasHeader: false), [ConversionFixtures.Nominal("a", 0, "x", "y")]);

    private static async Task<(ConversionPlan Plan, IRecordSource Source)> WidePrepAsync(
        BedrockSpec spec, string csv, Binding binding)
    {
        var source = ConversionFixtures.SourceOver(csv, binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionPlanner.Plan(spec, schema).TryGetValue(out var plan));
        return (plan!, source);
    }

    private static async Task<(ConversionPlan Plan, ITripleRowSource Source)> TriplePrepAsync(
        BedrockSpec spec, string tripleData)
    {
        var source = ConversionFixtures.TripleSourceOver(tripleData, ConversionFixtures.Triple());
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionPlanner.Plan(spec, schema).TryGetValue(out var plan));
        return (plan!, source);
    }

    private static async Task DrainAsync(Func<IAsyncEnumerable<EmittedObject>> factory)
    {
        await foreach (var _ in factory())
        {
        }
    }

    private static async Task DrainAsync(IAsyncEnumerable<EmittedObject> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private static async Task<List<string>> DrainNamesAsync(Func<IAsyncEnumerable<EmittedObject>> factory)
    {
        var names = new List<string>();
        await foreach (var obj in factory())
        {
            names.Add(obj.Name);
        }

        return names;
    }
}
