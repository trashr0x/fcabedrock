using System.Text;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Export.Tests;

// Shared helpers for the writer tests: a one-attribute-pair plan, async wrapping,
// and byte capture.
internal static class WriterFixtures
{
    public static ConversionPlan TwoColumnPlan() =>
        new(
            [
                new FormalAttribute(0, "a-x", new FormalAttributeIdentity("a", "nominal", "x", ""), new ValueBin("x")),
                new FormalAttribute(1, "a-y", new FormalAttributeIdentity("a", "nominal", "y", ""), new ValueBin("y")),
            ],
            [],
            new RowIndexObjectKey());

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
