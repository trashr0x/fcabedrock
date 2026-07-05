using FcaBedrock.Conversion;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Golden.Tests;

// Drives the full pipeline for one fixture: read .bed -> migrate to a spec
// document -> resolve (Spec) -> wide source (Sources) -> plan (Core) -> emit
// (Conversion) -> write (Export). The single orchestrator the golden harness
// exercises (later, M7's CLI plays this role); byte-equality here is also the
// gate on the migrate->resolve route reproducing v2 (D-079).
internal static class GoldenConversion
{
    public static async Task<byte[]> WriteCxtAsync(FixtureCase fixture, WriterOptions options)
    {
        var (plan, source) = await PrepareAsync(fixture, options);
        using var stream = new MemoryStream();
        await CxtWriter.WriteAsync(plan, () => EmitAsync(plan, source), options, stream);
        return stream.ToArray();
    }

    public static async Task<byte[]> WriteDatAsync(FixtureCase fixture, WriterOptions options)
    {
        var (plan, source) = await PrepareAsync(fixture, options);
        using var stream = new MemoryStream();
        await DatWriter.WriteAsync(EmitAsync(plan, source), options, stream);
        return stream.ToArray();
    }

    private static async Task<(ConversionPlan Plan, IRecordSource Source)> PrepareAsync(
        FixtureCase fixture, WriterOptions options)
    {
        var read = BedReader.Read(await File.ReadAllTextAsync(fixture.BedPath), fixture.BedPath);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));

        var migrated = BedMigrator.Migrate(document, fixture.Binding, fixture.ScalingMode, fixture.BedPath);
        Assert.True(migrated.TryGetValue(out var specDocument), Describe(migrated.Diagnostics));

        var resolved = SpecResolver.Resolve(specDocument);
        Assert.True(resolved.TryGetValue(out var spec), Describe(resolved.Diagnostics));

        var dataPath = fixture.DataPath;
        var source = new WideCsvSource(() => File.OpenRead(dataPath), spec.Binding);

        var schema = await source.GetSchemaAsync();
        var planned = ConversionPlanner.Plan(spec, schema, LabelStyleFor(options));
        Assert.False(planned.HasErrors, Describe(planned.Diagnostics));
        Assert.True(planned.TryGetValue(out var plan));

        return (plan, source);
    }

    // One v2 intent, two layers: v2-compat writer bytes imply the v2-compat label
    // style at plan time (the cut-bin `30to<40` form). Native otherwise. (D-044)
    private static LabelStyle LabelStyleFor(WriterOptions options) =>
        options == WriterOptions.V2Compat ? LabelStyle.V2Compat : LabelStyle.Native;

    // Emit ignores data-level diagnostics here: the M1 fixtures are clean (no
    // unknown values), and a stray diagnostic would surface as a byte mismatch.
    private static IAsyncEnumerable<EmittedObject> EmitAsync(ConversionPlan plan, IRecordSource source) =>
        Emitter.EmitAsync(plan, source, new List<BedrockDiagnostic>());

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));
}
