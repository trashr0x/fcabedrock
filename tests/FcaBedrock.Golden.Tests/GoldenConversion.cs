using FcaBedrock.Conversion;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Golden.Tests;

// Drives the full pipeline for one fixture: read .bed -> migrate to a spec document
// -> resolve (Spec) -> shape-matched source (Sources) -> plan (Core) -> emit
// (Conversion) -> write (Export). The single orchestrator the golden harness
// exercises (later, M7's CLI plays this role); byte-equality here is also the gate on
// the migrate->resolve route reproducing v2 (D-079). Wide and triple share the front
// half; the resolved shape picks the source and the emit entrypoint (D-082/D-086).
internal static class GoldenConversion
{
    // A prepared conversion: the plan plus a sink-aware emit delegate whose shape is
    // exactly EmitReplay.Begin's first parameter, so the .cxt two-pass and the
    // single-pass .dat both drive the same closure without a cast (D-082).
    internal sealed record PreparedConversion(
        ConversionPlan Plan,
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> Emit);

    public static async Task<byte[]> WriteCxtAsync(FixtureCase fixture, WriterOptions options)
    {
        var prepared = await PrepareAsync(fixture, options);
        using var stream = new MemoryStream();
        var diagnostics = new List<BedrockDiagnostic>();

        // The canonical sink-aware session brackets the two-pass write: data diagnostics
        // are collected once and grouping-storage failures aggregated across passes and
        // flushed at disposal, so only after the using block are they authoritative
        // (EmitReplay). Each pass re-invokes Emit -> a fresh source enumeration, so the
        // write is replayable by construction (P-16).
        using (var session = EmitReplay.Begin(prepared.Emit, diagnostics))
        {
            await CxtWriter.WriteAsync(prepared.Plan, session.Open, options, stream);
        }

        Assert.True(diagnostics.Count == 0, Describe(diagnostics));
        return stream.ToArray();
    }

    public static async Task<byte[]> WriteDatAsync(FixtureCase fixture, WriterOptions options)
    {
        var prepared = await PrepareAsync(fixture, options);
        using var stream = new MemoryStream();
        var diagnostics = new List<BedrockDiagnostic>();

        // .dat streams single-pass (no header count, so no replay); diagnostics are
        // authoritative once enumeration completes.
        await DatWriter.WriteAsync(prepared.Emit(diagnostics), DatOptionsFor(fixture, options), stream);

        Assert.True(diagnostics.Count == 0, Describe(diagnostics));
        return stream.ToArray();
    }

    private static async Task<PreparedConversion> PrepareAsync(FixtureCase fixture, WriterOptions options)
    {
        var read = BedReader.Read(await File.ReadAllTextAsync(fixture.BedPath), fixture.BedPath);
        AssertClean("read", read.Diagnostics);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));

        var migrated = BedMigrator.Migrate(document, fixture.Binding, fixture.ScalingMode, fixture.BedPath);
        AssertClean("migrate", migrated.Diagnostics);
        Assert.True(migrated.TryGetValue(out var specDocument), Describe(migrated.Diagnostics));

        var resolved = SpecResolver.Resolve(specDocument);
        AssertClean("resolve", resolved.Diagnostics);
        Assert.True(resolved.TryGetValue(out var spec), Describe(resolved.Diagnostics));

        var dataPath = fixture.DataPath;
        var labelStyle = LabelStyleFor(options);

        // The resolved shape decides the source and the emit entrypoint (D-082/D-086).
        // The triple emitter owns the unordered wrapper (§5.3 / D-082), so it takes the
        // raw TripleCsvSource; the delegate matches EmitReplay.Begin exactly.
        if (spec.Binding.Shape == SourceShape.Triple)
        {
            var source = new TripleCsvSource(() => File.OpenRead(dataPath), spec.Binding);
            var plan = await PlanAsync(spec, source.GetSchemaAsync(), labelStyle);

            // Every active triple golden is subject-interleaved, so the planner must have
            // resolved an unordered execution (§5.3 / D-082) — asserted on every run.
            Assert.Equal(new TripleExecution(TripleOrdering.Unordered), plan.Execution);
            return new PreparedConversion(plan, sink => Emitter.EmitTripleAsync(plan, source, sink));
        }
        else
        {
            var source = new WideCsvSource(() => File.OpenRead(dataPath), spec.Binding);
            var plan = await PlanAsync(spec, source.GetSchemaAsync(), labelStyle);
            return new PreparedConversion(plan, sink => Emitter.EmitAsync(plan, source, sink));
        }
    }

    private static async Task<ConversionPlan> PlanAsync(
        BedrockSpec spec, ValueTask<SourceSchema> schemaTask, LabelStyle labelStyle)
    {
        var schema = await schemaTask;
        var planned = ConversionPlanner.Plan(spec, schema, labelStyle);
        AssertClean("plan", planned.Diagnostics);
        Assert.True(planned.TryGetValue(out var plan));
        return plan;
    }

    // D-087: v2's .dat final newline is shape-dependent — wide -> present, triple ->
    // absent (v2's triple converter wrote no final .dat line terminator; the three triple
    // goldens end without CRLF). Native output instead honors the authored/default
    // [output.dat].trailing_newline. The override is applied here, not baked into the
    // V2Compat preset (the common baseline), so wide v2-compat .dat keeps its trailing CRLF.
    private static WriterOptions DatOptionsFor(FixtureCase fixture, WriterOptions options) =>
        options == WriterOptions.V2Compat && fixture.Binding.Shape == SourceShape.Triple
            ? options with { TrailingNewline = false }
            : options;

    // One v2 intent, two layers: v2-compat writer bytes imply the v2-compat label
    // style at plan time (the cut-bin `30to<40` form). Native otherwise. (D-044)
    private static LabelStyle LabelStyleFor(WriterOptions options) =>
        options == WriterOptions.V2Compat ? LabelStyle.V2Compat : LabelStyle.Native;

    // The nine active goldens are all completely clean: no stage may emit any diagnostic
    // (not even a Warning). A stray one is a real defect, so it fails the run with the
    // readable list rather than being permitted by TryGetValue/HasErrors.
    private static void AssertClean(string stage, IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        Assert.True(diagnostics.Count == 0, $"{stage}: {Describe(diagnostics)}");

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));
}
