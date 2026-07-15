using System.Text;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// Integration: Sources → Emitter → writers. The keep unique-name escalation is a converter concern
// (P-15) observable in the .cxt name block; .dat ignores names but preserves object count/order.
public sealed class WideColumnKeyWriterTests
{
    [Fact]
    public async Task CxtWriter_WhenKeepWithDuplicates_ThenNameBlockHasEscalatedNamesInRowOrder()
    {
        var (plan, source) = await PrepareKeepAsync();
        using var stream = new MemoryStream();

        await CxtWriter.WriteAsync(plan, () => Emit(plan, source), WriterOptions.Native, stream);

        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n');
        // Burmeister layout (§18.1): B / blank / objectCount / attrCount / blank / names… / attrs… / rows…
        Assert.Equal("3", lines[2]); // three objects, one per row
        Assert.Equal(["P001", "P002", "P001#2"], lines[5..8]); // names in source-row order, later dup escalated
    }

    [Fact]
    public async Task DatWriter_WhenKeepWithDuplicates_ThenOneLinePerObjectInRowOrder()
    {
        var (plan, source) = await PrepareKeepAsync();
        using var stream = new MemoryStream();

        await DatWriter.WriteAsync(Emit(plan, source), WriterOptions.Native, stream);

        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // .dat drops names but keeps one object per row, in order
    }

    private static IAsyncEnumerable<EmittedObject> Emit(ConversionPlan plan, WideCsvSource source) =>
        Emitter.EmitAsync(plan, source, new List<BedrockDiagnostic>());

    private static async Task<(ConversionPlan Plan, WideCsvSource Source)> PrepareKeepAsync()
    {
        var binding = ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep);
        var spec = new BedrockSpec(binding, [ConversionFixtures.Nominal("a", 1, "x", "y")]);
        var source = ConversionFixtures.SourceOver("P001,x\nP002,y\nP001,x", binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return (plan, source);
    }
}
