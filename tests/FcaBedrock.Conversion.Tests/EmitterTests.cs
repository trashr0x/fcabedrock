using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

public sealed class EmitterTests
{
    [Fact]
    public async Task EmitAsync_WhenMiniMushroom_ThenCrossesMatchV2Incidence()
    {
        var (objects, diagnostics) = await RunAsync(
            ConversionFixtures.MushroomSpec(), ConversionFixtures.MushroomCsv, ConversionFixtures.Wide());

        Assert.Empty(diagnostics);
        Assert.Equal(["0", "1", "2", "3", "4"], objects.Select(o => o.Name));
        Assert.Equal([0, 1, 3, 5], objects[0].CrossedFormalAttributeIds); // XX.X.X..
        Assert.Equal([0, 2, 3, 7], objects[1].CrossedFormalAttributeIds); // X.XX...X
        Assert.Equal([2, 3, 5], objects[2].CrossedFormalAttributeIds);    // ..XX.X..
        Assert.Equal([0, 1, 3, 6], objects[3].CrossedFormalAttributeIds); // XX.X..X.
        Assert.Equal([2, 3, 5], objects[4].CrossedFormalAttributeIds);    // ..XX.X..
    }

    [Fact]
    public async Task EmitAsync_WhenValueMissing_ThenNoCrossAndNoDiagnostic()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Nominal("a", 0, "x", "y")]);

        var (objects, diagnostics) = await RunAsync(spec, "?", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(diagnostics);
        Assert.Empty(Assert.Single(objects).CrossedFormalAttributeIds);
    }

    [Fact]
    public async Task EmitAsync_WhenValueOutsideDeclaredDomain_ThenWarnsAndCrossesNothing()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Nominal("a", 0, "x", "y")]);

        var (objects, diagnostics) = await RunAsync(spec, "z", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(Assert.Single(objects).CrossedFormalAttributeIds);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.UnknownValueObserved && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task EmitAsync_WhenRunTwice_ThenProducesIdenticalCrosses()
    {
        var (first, _) = await RunAsync(
            ConversionFixtures.MushroomSpec(), ConversionFixtures.MushroomCsv, ConversionFixtures.Wide());
        var (second, _) = await RunAsync(
            ConversionFixtures.MushroomSpec(), ConversionFixtures.MushroomCsv, ConversionFixtures.Wide());

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].CrossedFormalAttributeIds, second[i].CrossedFormalAttributeIds);
        }
    }

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunAsync(
        BedrockSpec spec, string csv, Binding binding)
    {
        var source = ConversionFixtures.SourceOver(csv, binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionPlanner.Plan(spec, schema).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (objects, diagnostics);
    }
}
