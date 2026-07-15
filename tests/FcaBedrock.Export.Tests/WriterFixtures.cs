using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Export.Tests;

// Shared helpers for the writer tests: a one-attribute-pair plan, async wrapping,
// and byte capture.
internal static class WriterFixtures
{
    // ConversionPlan's constructor is planner-owned (internal, D-098), so the two-column
    // "a-x"/"a-y" plan is produced through the real pipeline: a nominal attribute over ["x","y"]
    // resolves, takes fully-declared calibrated state, and plans to the same two formal columns.
    public static ConversionPlan TwoColumnPlan()
    {
        var spec = new BedrockSpec(
            new Binding(SourceShape.Wide, "utf-8", ',', '"', HasHeader: true, "invariant", "?", new RowIndexObjectKey()),
            [
                new AttributeSpec(
                    "a", new ColumnSource(0, SourceValueType.String), Include: true,
                    new IdentityDiscretizer(), new NominalScale(), ["x", "y"], RestrictTo: [],
                    new Dictionary<string, string>(), MissingPolicy.Skip, UnknownValuePolicy.Warn),
            ]);
        var schema = new SourceSchema(1);
        var resolved = ResolvedSpec.Create(
            spec, schema,
            SourceReadSettings.Create(
                spec.Binding.Shape, spec.Binding.Encoding, spec.Binding.Delimiter, spec.Binding.QuoteChar,
                spec.Binding.HasHeader, spec.Binding.MissingToken, spec.Binding.Ordering),
            []);
        return ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(resolved)).Value!;
    }

    public static EmittedObject Object(string name, params int[] crossed) => new(name, crossed);

    public static async Task<string> WriteDatAsync(IReadOnlyList<EmittedObject> objects, WriterOptions options)
    {
        using var stream = new MemoryStream();
        await DatWriter.WriteAsync(ToAsync(objects), options, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static async Task<byte[]> WriteCxtBytesAsync(
        ConversionPlan plan, IReadOnlyList<EmittedObject> objects, WriterOptions options)
    {
        using var stream = new MemoryStream();
        await CxtWriter.WriteAsync(plan, () => ToAsync(objects), options, stream);
        return stream.ToArray();
    }

    public static async Task<string> WriteCxtAsync(
        ConversionPlan plan, IReadOnlyList<EmittedObject> objects, WriterOptions options) =>
        Encoding.UTF8.GetString(await WriteCxtBytesAsync(plan, objects, options));

    private static async IAsyncEnumerable<EmittedObject> ToAsync(IEnumerable<EmittedObject> objects)
    {
        foreach (var obj in objects)
        {
            yield return obj;
        }

        await Task.CompletedTask;
    }
}
