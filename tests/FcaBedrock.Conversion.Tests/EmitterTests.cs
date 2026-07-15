using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
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
    public async Task EmitAsync_WhenValueMissingUnderAsAttribute_ThenCrossesMissingAttribute()
    {
        // §10.5 / D-068: an empty cell is missing → the {column}-missing column crosses.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Nominal("a", 0, UnknownValuePolicy.Warn, MissingPolicy.AsAttribute, "x", "y")]);

        var (objects, diagnostics) = await RunAsync(spec, ",pad", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(diagnostics);
        Assert.Equal([2], Assert.Single(objects).CrossedFormalAttributeIds); // a-x, a-y, a-missing
    }

    [Fact]
    public async Task EmitAsync_WhenMissingTokenMatchUnderAsAttribute_ThenCrossesMissingAttribute()
    {
        // The missing_token match is normalized to null by the source (§5.1), so the
        // as_attribute cross covers both missing forms end-to-end.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Nominal("a", 0, UnknownValuePolicy.Warn, MissingPolicy.AsAttribute, "x", "y")]);

        var (objects, diagnostics) = await RunAsync(spec, "?", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(diagnostics);
        Assert.Equal([2], Assert.Single(objects).CrossedFormalAttributeIds);
    }

    [Fact]
    public async Task EmitAsync_WhenValuePresentUnderAsAttribute_ThenOnlyValueBinCrosses()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Nominal("a", 0, UnknownValuePolicy.Warn, MissingPolicy.AsAttribute, "x", "y")]);

        var (objects, _) = await RunAsync(spec, "x", ConversionFixtures.Wide(hasHeader: false));

        Assert.Equal([0], Assert.Single(objects).CrossedFormalAttributeIds); // a-x only, never a-missing
    }

    [Fact]
    public async Task EmitAsync_WhenNumericUnparseableUnderAsAttribute_ThenNoMissingCross()
    {
        // D-050 boundary: a present-but-unparseable numeric is NOT missing — no
        // missing cross; it stays a SourceValueUnparseable at the policy's severity.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Warn, MissingPolicy.AsAttribute, 30, 40, 50)]);

        var (objects, diagnostics) = await RunAsync(spec, "abc", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(Assert.Single(objects).CrossedFormalAttributeIds);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.SourceValueUnparseable, diagnostic.Code);
    }

    [Fact]
    public async Task EmitAsync_WhenUnknownValueUnderAsAttribute_ThenNoMissingCross()
    {
        // An out-of-domain value is present, not missing (§10.6) — no missing cross.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Nominal("a", 0, UnknownValuePolicy.Warn, MissingPolicy.AsAttribute, "x", "y")]);

        var (objects, diagnostics) = await RunAsync(spec, "z", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(Assert.Single(objects).CrossedFormalAttributeIds);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
    }

    [Fact]
    public async Task EmitAsync_WhenDichotomicUnderAsAttribute_ThenFalsePoleNoCrossAndMissingCrosses()
    {
        // §12.2: true_value crosses when present, missing crosses when absent; the
        // false pole is present and crosses nothing.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Dichotomic("d", 0, "t", ["t", "f"], MissingPolicy.AsAttribute)]);

        var (objects, diagnostics) = await RunAsync(spec, "f\n?", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(diagnostics);
        Assert.Empty(objects[0].CrossedFormalAttributeIds);      // false pole: no cross
        Assert.Equal([1], objects[1].CrossedFormalAttributeIds); // missing: d-missing
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

    // --- numeric free_per_value emit (canonicalization + KnownBins gate, §11.3/D-096) ---

    private static AttributeSpec NumericFreePerValue(string name, int index, IReadOnlyList<string> domain) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            new FreePerValueDiscretizer(SourceValueType.Number, CultureInfo.InvariantCulture),
            new NominalScale(), domain, RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    [Fact]
    public async Task EmitAsync_WhenNumericFreePerValue_ThenCanonicalizesAtEmitAndGatesOnDomain()
    {
        // 90.0 and 9e1 canonicalize to the "90" bin at emit and cross it; an in-domain "5" crosses its
        // bin; a parseable-but-out-of-domain "999" is an unknown value (§10.6), so it crosses nothing.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [NumericFreePerValue("v", 0, ["90", "5"])]);

        var (objects, diagnostics) = await RunAsync(spec, "90.0\n5\n9e1\n999", ConversionFixtures.Wide(hasHeader: false));

        Assert.Equal([0], objects[0].CrossedFormalAttributeIds); // 90.0 -> "90" bin (id 0)
        Assert.Equal([1], objects[1].CrossedFormalAttributeIds); // 5    -> "5" bin (id 1)
        Assert.Equal([0], objects[2].CrossedFormalAttributeIds); // 9e1  -> "90" bin (id 0)
        Assert.Empty(objects[3].CrossedFormalAttributeIds);       // 999 out of domain -> unknown, no cross
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.UnknownValueObserved && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task EmitAsync_WhenNumericFreePerValueUnparseable_ThenNoCrossAndSourceValueUnparseable()
    {
        // A present-but-unparseable numeric is kept, crosses nothing, and reports SourceValueUnparseable (§11.5).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [NumericFreePerValue("v", 0, ["90"])]);

        var (objects, diagnostics) = await RunAsync(spec, "abc", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(Assert.Single(objects).CrossedFormalAttributeIds);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
    }

    [Fact]
    public async Task EmitAsync_WhenStringFreePerValue_ThenVerbatimBinsCrossAndAreDeterministic()
    {
        // String free_per_value keeps each spelling distinct; a repeat run is byte-identical (P-7).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [new AttributeSpec("g", new ColumnSource(0, SourceValueType.String), Include: true,
                new FreePerValueDiscretizer(SourceValueType.String, CultureInfo.InvariantCulture),
                new NominalScale(), ["b", "n"], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn)]);

        var (first, _) = await RunAsync(spec, "b\nn\nb", ConversionFixtures.Wide(hasHeader: false));
        var (second, _) = await RunAsync(spec, "b\nn\nb", ConversionFixtures.Wide(hasHeader: false));

        Assert.Equal([0], first[0].CrossedFormalAttributeIds); // b -> g-b (id 0)
        Assert.Equal([1], first[1].CrossedFormalAttributeIds); // n -> g-n (id 1)
        Assert.Equal([0], first[2].CrossedFormalAttributeIds);
        Assert.Equal(
            first.Select(o => o.CrossedFormalAttributeIds),
            second.Select(o => o.CrossedFormalAttributeIds));
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

    [Theory]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, new[] { 0 }, new[] { 0, 1 }, new[] { 0, 1, 2 })]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Strict, new int[0], new[] { 0 }, new[] { 0, 1 })]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Inclusive, new[] { 0, 1, 2 }, new[] { 1, 2 }, new[] { 2 })]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Strict, new[] { 1, 2 }, new[] { 2 }, new int[0])]
    public async Task EmitAsync_WhenValueBinOrdinal_ThenIncidenceMatchesThresholdSemantics(
        OrdinalDirection direction, OrdinalBoundary boundary, int[] low, int[] mid, int[] high)
    {
        // §12.3 worked example (D-081): identity value bins ordered low < mid < high
        // (order = the domain). The four direction × boundary combinations give the
        // cumulative-threshold incidence, all live over value bins (no cut geometry).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.OrdinalValueBins("edu", 0, ["low", "mid", "high"], direction, boundary)]);

        var (objects, diagnostics) = await RunAsync(spec, "low\nmid\nhigh", ConversionFixtures.Wide(hasHeader: false));

        Assert.Empty(diagnostics);
        Assert.Equal(low, objects[0].CrossedFormalAttributeIds);
        Assert.Equal(mid, objects[1].CrossedFormalAttributeIds);
        Assert.Equal(high, objects[2].CrossedFormalAttributeIds);
    }

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunAsync(
        BedrockSpec spec, string csv, Binding binding)
    {
        var source = ConversionFixtures.SourceOver(csv, binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (objects, diagnostics);
    }
}
