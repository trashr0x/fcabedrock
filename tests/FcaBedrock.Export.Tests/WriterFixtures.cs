using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Export.Tests;

// Shared helpers for the writer tests: a one-attribute-pair plan, async wrapping,
// and byte capture.
internal static class WriterFixtures
{
    // ConversionPlan's constructor is planner-owned (internal, D-098), so plans are produced through
    // the real pipeline: a nominal identity attribute over its declared domain resolves, takes
    // fully-declared calibrated state, and plans to one formal column ("a-<value>") per domain value.
    public static ConversionPlan TwoColumnPlan() => NominalPlan("x", "y");

    // A one-attribute nominal-identity plan whose formal columns render as "a-<value>" for each
    // declared-domain value (NominalPlan("café", "Ω") ⇒ columns "a-café", "a-Ω"). Lets a test author
    // non-ASCII rendered formal-attribute names through the real pipeline — names are baked by the
    // planner, never the writer (P-15).
    public static ConversionPlan NominalPlan(params string[] domain) => BuildPlan(include: true, domain);

    // A plan with ZERO formal attributes: a single excluded attribute contributes no column, so the
    // planner returns a structurally-valid plan carrying a NoFormalAttributes warning (§16.4). This
    // exercises the writer's zero-attribute degenerate (empty incidence rows).
    public static ConversionPlan ZeroAttributePlan() => BuildPlan(include: false, "x", "y");

    private static ConversionPlan BuildPlan(bool include, params string[] domain)
    {
        var spec = new BedrockSpec(
            new Binding(SourceShape.Wide, "utf-8", ',', '"', HasHeader: true, "invariant", "?", new RowIndexObjectKey()),
            [
                new AttributeSpec(
                    "a", new ColumnSource(0, SourceValueType.String), Include: include,
                    new IdentityDiscretizer(), new NominalScale(), domain, RestrictTo: [],
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

    // The advisory-carrying overload: writes the same bytes as the four-arg overload while appending
    // any OutputCxtSizeAdvisory into `diagnostics`. Used by the size-advisory tests.
    public static async Task<byte[]> WriteCxtBytesAsync(
        ConversionPlan plan, IReadOnlyList<EmittedObject> objects, WriterOptions options,
        ICollection<BedrockDiagnostic> diagnostics, long sizeAdvisoryBytes)
    {
        using var stream = new MemoryStream();
        await CxtWriter.WriteAsync(plan, () => ToAsync(objects), options, stream, diagnostics, sizeAdvisoryBytes);
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
