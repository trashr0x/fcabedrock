using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

// End-to-end numeric free_per_value conversion (calibrate -> plan -> emit): the canonical numeric
// identity is one bin across observed domain, plan, and emit (§11.3/§10.7/D-096/D-101).
public sealed class FreePerValueConversionTests
{
    private static AttributeSpec NumericFreePerValue(string name, int index, IReadOnlyList<string>? domain, Scale scale) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            new FreePerValueDiscretizer(SourceValueType.Number, CultureInfo.InvariantCulture),
            scale, domain, RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static async Task<(ConversionPlan Plan, List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)>
        CalibratePlanEmit(BedrockSpec spec, string csv)
    {
        var binding = spec.Binding;
        var source = ConversionFixtures.SourceOver(csv, binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);

        var calibration = await Calibrator.CalibrateAsync(resolved, source);
        Assert.True(calibration.TryGetValue(out var calibrated),
            string.Join("; ", calibration.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.True(ConversionPlanner.Plan(calibrated).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (plan, objects, diagnostics);
    }

    [Fact]
    public async Task CalibratePlanEmit_WhenNumericFreePerValueNominal_ThenObservedCanonicalColumnsAndCrosses()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [NumericFreePerValue("v", 0, null, new NominalScale())]);

        var (plan, objects, diagnostics) = await CalibratePlanEmit(spec, "90.0\n5\n9e1\n90");

        // The observed domain is the two canonical identities in first-observation order → two columns.
        Assert.Equal(["v-90", "v-5"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal([0], objects[0].CrossedFormalAttributeIds); // 90.0 -> 90
        Assert.Equal([1], objects[1].CrossedFormalAttributeIds); // 5
        Assert.Equal([0], objects[2].CrossedFormalAttributeIds); // 9e1  -> 90
        Assert.Equal([0], objects[3].CrossedFormalAttributeIds); // 90
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
    }

    [Fact]
    public async Task CalibratePlanEmit_WhenObservedThenFrozen_ThenByteIdenticalCrossesAndNames()
    {
        // The observed-domain calibration is byte-equivalent to freezing it into an explicit domain
        // (the observed-domain analogue of the D-088 auto/frozen equivalence): same columns, same crosses.
        const string csv = "90.0\n5\n9e1\n90";
        var auto = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [NumericFreePerValue("v", 0, null, new NominalScale())]);
        var (autoPlan, autoObjects, _) = await CalibratePlanEmit(auto, csv);

        // Freeze: the observed canonical domain declared explicitly.
        var frozen = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [NumericFreePerValue("v", 0, ["90", "5"], new NominalScale())]);
        var (frozenPlan, frozenObjects, _) = await CalibratePlanEmit(frozen, csv);

        Assert.Equal(
            autoPlan.FormalAttributes.Select(f => f.RenderedName),
            frozenPlan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(
            autoObjects.Select(o => o.CrossedFormalAttributeIds),
            frozenObjects.Select(o => o.CrossedFormalAttributeIds));
    }

    [Fact]
    public async Task CalibratePlanEmit_WhenNumericFreePerValueOrdinalNoOrder_ThenNaturalAscendingThresholds()
    {
        // §12.3/D-096: numeric free_per_value ordinal with no authored order derives natural ascending
        // order from the observed (canonical) domain, regardless of first-observation order.
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, Order: null);
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [NumericFreePerValue("v", 0, null, scale)]);

        var (plan, objects, _) = await CalibratePlanEmit(spec, "90\n5\n0");

        // Observed 90, 5, 0 → natural ascending thresholds >=0, >=5, >=90.
        Assert.Equal(["v->=0", "v->=5", "v->=90"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal([0, 1, 2], objects[0].CrossedFormalAttributeIds); // 90 >= 0, >= 5, >= 90
        Assert.Equal([0, 1], objects[1].CrossedFormalAttributeIds);    // 5  >= 0, >= 5
        Assert.Equal([0], objects[2].CrossedFormalAttributeIds);       // 0  >= 0
    }

    [Fact]
    public async Task CalibratePlanEmit_WhenRepeated_ThenDeterministic()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [NumericFreePerValue("v", 0, null, new NominalScale())]);

        var (firstPlan, firstObjects, _) = await CalibratePlanEmit(spec, "90.0\n5\n9e1");
        var (secondPlan, secondObjects, _) = await CalibratePlanEmit(spec, "90.0\n5\n9e1");

        Assert.Equal(
            firstPlan.FormalAttributes.Select(f => f.RenderedName),
            secondPlan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(
            firstObjects.Select(o => o.CrossedFormalAttributeIds),
            secondObjects.Select(o => o.CrossedFormalAttributeIds));
    }
}
