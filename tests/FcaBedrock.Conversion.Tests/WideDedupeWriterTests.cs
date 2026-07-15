using System.Text;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Conversion.Tests;

// Wide dedupe through the writers (D-083/D-082): the .cxt two-pass re-runs the dedupe grouping each
// pass and must produce identical bytes (the object-name sequence replays), and the .dat single pass
// carries each merged object's unioned crosses.
public sealed class WideDedupeWriterTests
{
    private static BedrockSpec DedupeSpec() =>
        new(ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe), [ConversionFixtures.Nominal("a", 1, "x", "y")]);

    [Fact]
    public async Task Cxt_WhenDedupeWrittenTwice_ThenIdenticalBytes()
    {
        var plan = await PlanAsync(DedupeSpec(), "k1,x\nk2,y\nk1,y");

        var first = await WriteCxtAsync(plan, "k1,x\nk2,y\nk1,y");
        var second = await WriteCxtAsync(plan, "k1,x\nk2,y\nk1,y");

        Assert.Equal(first, second);
        // First-occurrence order: k1 before k2, in the name block.
        var text = Encoding.UTF8.GetString(first);
        Assert.True(text.IndexOf("k1", StringComparison.Ordinal) < text.IndexOf("k2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dat_WhenDedupe_ThenEachMergedObjectCarriesItsUnionedCrosses()
    {
        var plan = await PlanAsync(DedupeSpec(), "k1,x\nk2,y\nk1,y");
        var source = ConversionFixtures.SourceOver("k1,x\nk2,y\nk1,y", DedupeSpec().Binding);

        using var output = new MemoryStream();
        await DatWriter.WriteAsync(Emitter.EmitAsync(plan, source, new List<BedrockDiagnostic>()), WriterOptions.Native, output);
        var lines = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // base_index 1: a-x → 1, a-y → 2. k1 unions x and y → "1 2"; k2 → "2".
        Assert.Equal(["1 2", "2"], lines);
    }

    private static async Task<ConversionPlan> PlanAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return plan;
    }

    private static async Task<byte[]> WriteCxtAsync(ConversionPlan plan, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, DedupeSpec().Binding);
        using var stream = new MemoryStream();
        await CxtWriter.WriteAsync(
            plan,
            () => Emitter.EmitAsync(plan, source, new List<BedrockDiagnostic>()),
            WriterOptions.Native,
            stream);
        return stream.ToArray();
    }
}
