using FcaBedrock.Conversion;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec;

namespace FcaBedrock.Golden.Tests;

// Drives the full pipeline for one fixture: read .bed (Spec) -> wide source
// (Sources) -> plan (Core) -> emit (Conversion) -> write (Export). The single
// orchestrator the golden harness exercises (later, M7's CLI plays this role).
internal static class GoldenConversion
{
    public static async Task<byte[]> WriteCxtAsync(FixtureCase fixture, WriterOptions options)
    {
        var (plan, source) = await PrepareAsync(fixture);
        using var stream = new MemoryStream();
        await CxtWriter.WriteAsync(plan, () => EmitAsync(plan, source), options, stream);
        return stream.ToArray();
    }

    public static async Task<byte[]> WriteDatAsync(FixtureCase fixture, WriterOptions options)
    {
        var (plan, source) = await PrepareAsync(fixture);
        using var stream = new MemoryStream();
        await DatWriter.WriteAsync(EmitAsync(plan, source), options, stream);
        return stream.ToArray();
    }

    private static async Task<(ConversionPlan Plan, IRecordSource Source)> PrepareAsync(FixtureCase fixture)
    {
        var document = BedReader.Read(await File.ReadAllTextAsync(fixture.BedPath));
        var spec = BedToSpec.ToSpec(document, fixture.Binding);

        var dataPath = fixture.DataPath;
        var source = new WideCsvSource(() => File.OpenRead(dataPath), fixture.Binding);

        var schema = await source.GetSchemaAsync();
        var planned = ConversionPlanner.Plan(spec, schema);
        Assert.False(planned.HasErrors, Describe(planned.Diagnostics));
        Assert.True(planned.TryGetValue(out var plan));

        return (plan, source);
    }

    // Emit ignores data-level diagnostics here: the M1 fixtures are clean (no
    // unknown values), and a stray diagnostic would surface as a byte mismatch.
    private static IAsyncEnumerable<EmittedObject> EmitAsync(ConversionPlan plan, IRecordSource source) =>
        Emitter.EmitAsync(plan, source, new List<BedrockDiagnostic>());

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));
}
