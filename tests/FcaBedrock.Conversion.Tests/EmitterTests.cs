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

    [Fact]
    public async Task EmitAsync_WhenNumericValuesUnparseable_ThenKeptNoCrossAndOneAggregatedDiagnostic()
    {
        // §11.5 / D-050: a present-but-unparseable numeric keeps the object, emits no cross,
        // and is reported as ONE aggregated SourceValueUnparseable (count + sample), not per row.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Warn, 30, 40, 50)]);

        var (objects, diagnostics) = await RunAsync(spec, "abc\nxyz\n40", ConversionFixtures.Wide(hasHeader: false));

        Assert.Equal(3, objects.Count);
        Assert.Empty(objects[0].CrossedFormalAttributeIds);    // abc kept, no cross
        Assert.Empty(objects[1].CrossedFormalAttributeIds);    // xyz kept, no cross
        Assert.NotEmpty(objects[2].CrossedFormalAttributeIds); // 40 → "[40, 50)" crosses

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.SourceValueUnparseable, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("2 value(s)", diagnostic.Message); // aggregated count
        Assert.Contains("abc", diagnostic.Message);
        Assert.Contains("xyz", diagnostic.Message);
    }

    [Fact]
    public async Task EmitAsync_WhenUnparseableUnderSkip_ThenNoDiagnostic()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Skip, 30, 40, 50)]);

        var (_, diagnostics) = await RunAsync(spec, "abc", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task EmitAsync_WhenUnparseableUnderFail_ThenError()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Fail, 30, 40, 50)]);

        var (_, diagnostics) = await RunAsync(spec, "abc", ConversionFixtures.Wide(hasHeader: false));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.SourceValueUnparseable, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task EmitAsync_WhenOrderedCutsValueNotInOrder_ThenAggregatedUnknownValueObserved()
    {
        // §11.8: an ordered_cuts value not in order is unknown (subject to policy), no cross.
        string[] order = ["Unskilled", "Clerical", "Professional", "Managerial"];
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.OrderedCuts("job", 0, order, "Managerial")]);

        var (objects, diagnostics) = await RunAsync(spec, "Director\nClerical", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(objects[0].CrossedFormalAttributeIds); // Director unknown → no cross
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownValueObserved, diagnostic.Code);
        Assert.Contains("Director", diagnostic.Message);
    }

    [Fact]
    public async Task EmitAsync_WhenMultipleUnknownValues_ThenOneAggregatedDiagnosticWithCount()
    {
        // Identity domain mismatch: many unknowns aggregate to ONE diagnostic (§16.4), not one
        // per row — covering the KnownBins-mismatch path distinct from the ordered_cuts one.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Nominal("a", 0, "x", "y")]);

        var (_, diagnostics) = await RunAsync(spec, "z\nw\nx", ConversionFixtures.Wide(hasHeader: false));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownValueObserved, diagnostic.Code);
        Assert.Contains("2 value(s)", diagnostic.Message);
        Assert.Contains("z", diagnostic.Message);
        Assert.Contains("w", diagnostic.Message);
    }

    [Fact]
    public async Task EmitAsync_WhenRunTwiceWithUnparseable_ThenIdenticalDiagnostics()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Warn, 30, 40, 50)]);

        var (_, first) = await RunAsync(spec, "abc\nxyz", ConversionFixtures.Wide(hasHeader: false));
        var (_, second) = await RunAsync(spec, "abc\nxyz", ConversionFixtures.Wide(hasHeader: false));

        Assert.Equal(
            first.Select(d => (d.Code, d.Severity, d.Message)),
            second.Select(d => (d.Code, d.Severity, d.Message)));
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
