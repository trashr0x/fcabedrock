using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Planning;

public sealed class ConversionPlannerTests
{
    private static readonly string[] ExpectedMushroomNames =
    [
        "bruises?", "gill-size-broad", "gill-size-narrow", "veil-type-partial",
        "veil-type-universal", "ring-number-none", "ring-number-one", "ring-number-two",
    ];

    [Fact]
    public void Plan_WhenMiniMushroom_ThenFormalAttributesMatchV2OrderAndNames()
    {
        var result = ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5));

        Assert.True(result.TryGetValue(out var plan));
        Assert.Equal(ExpectedMushroomNames, plan.FormalAttributes.Select(f => f.RenderedName).ToArray());
    }

    [Fact]
    public void Plan_WhenDichotomic_ThenSingleColumnNamedAttributeOnlyAndCrossesTrueValue()
    {
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        var bruises = plan.Attributes.Single(a => a.Name == "bruises?");
        Assert.True(bruises.KnownBins.SetEquals(["t", "f"]));
        Assert.Equal([0], bruises.CrossesByBin["t"]);
        Assert.False(bruises.CrossesByBin.ContainsKey("f")); // recognized bin that crosses nothing
    }

    [Fact]
    public void Plan_WhenNominal_ThenEachBinCrossesItsOwnFormalAttribute()
    {
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        var gill = plan.Attributes.Single(a => a.Name == "gill-size");
        Assert.Equal([1], gill.CrossesByBin["b"]);
        Assert.Equal([2], gill.CrossesByBin["n"]);
    }

    [Fact]
    public void Plan_WhenNumericCutsNominal_ThenStyleChangesInteriorNamesButNotIdentityOrCrossings()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.NumericCuts("age", 0, [30, 40, 50], new NominalScale())]);

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1), LabelStyle.Native).TryGetValue(out var native));
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1), LabelStyle.V2Compat).TryGetValue(out var v2));

        Assert.Equal(
            ["age-<30", "age-[30, 40)", "age-[40, 50)", "age->=50"],
            native.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(
            ["age-<30", "age-30to<40", "age-40to<50", "age->=50"],
            v2.FormalAttributes.Select(f => f.RenderedName));

        // D-035: rendering differs by style; identity and crossings do not.
        Assert.Equal(native.FormalAttributes.Select(f => f.Identity), v2.FormalAttributes.Select(f => f.Identity));
        var ageNative = native.Attributes.Single(a => a.Name == "age");
        var ageV2 = v2.Attributes.Single(a => a.Name == "age");
        Assert.True(ageNative.KnownBins.SetEquals(["<30", "[30, 40)", "[40, 50)", ">=50"]));
        Assert.True(ageNative.KnownBins.SetEquals(ageV2.KnownBins));
        Assert.Equal([1], ageNative.CrossesByBin["[30, 40)"]); // canonical key, identical under both styles
        Assert.Equal([1], ageV2.CrossesByBin["[30, 40)"]);
    }

    [Fact]
    public void Plan_WhenNumericCutsOrdinalLe_ThenCumulativeCrossingsAndStyleIndependentThresholdNames()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.NumericCuts("age", 0, [30, 40, 50], new OrdinalScale(OrdinalDirection.Le))]);

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1), LabelStyle.Native).TryGetValue(out var native));
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1), LabelStyle.V2Compat).TryGetValue(out var v2));

        // Threshold labels come from the cuts (no interval), so they are style-independent.
        string[] expected = ["age-<30", "age-<40", "age-<50", "age-all"];
        Assert.Equal(expected, native.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(expected, v2.FormalAttributes.Select(f => f.RenderedName));

        var age = native.Attributes.Single(a => a.Name == "age");
        Assert.Equal([0, 1, 2, 3], age.CrossesByBin["<30"]);       // crossed by <30, <40, <50, all
        Assert.Equal([1, 2, 3], age.CrossesByBin["[30, 40)"]);     // crossed by <40, <50, all
        Assert.Equal([2, 3], age.CrossesByBin["[40, 50)"]);        // crossed by <50, all
        Assert.Equal([3], age.CrossesByBin[">=50"]);               // crossed by all only
    }

    [Fact]
    public void Plan_WhenAttributeExcluded_ThenItProducesNoFormalAttributes()
    {
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        Assert.DoesNotContain(plan.Attributes, a => a.Name == "class");
        Assert.All(plan.FormalAttributes, f => Assert.NotEqual("class", f.Identity.AttributeName));
    }

    [Fact]
    public void Plan_WhenCalledTwice_ThenProducesIdenticalFormalAttributeOrder()
    {
        var schema = new SourceSchema(5);

        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), schema).TryGetValue(out var first));
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), schema).TryGetValue(out var second));

        Assert.Equal(
            first.FormalAttributes.Select(f => f.RenderedName),
            second.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenDuplicateAttributeName_ThenReportsAttributeNameDuplicate()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
        [
            SpecFixtures.Nominal("dup", 0, ["a"]),
            SpecFixtures.Nominal("dup", 1, ["b"]),
        ]);

        AssertFailsWith(ConversionPlanner.Plan(spec, new SourceSchema(2)), DiagnosticCode.AttributeNameDuplicate);
    }

    [Fact]
    public void Plan_WhenExcludedAttributeRetainsEmittedConfig_ThenIgnoredWithoutError()
    {
        // include = false is an authoring toggle (D-049): retained discretizer/scale/
        // domain/labels are parked, not rejected, and contribute no formal attributes.
        var parked = new AttributeSpec("x", new ColumnSource(0), Include: false,
            new IdentityDiscretizer(), new NominalScale(), ["a"],
            new Dictionary<string, string> { ["a"] = "Alpha" },
            MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [parked]);

        var result = ConversionPlanner.Plan(spec, new SourceSchema(1));

        Assert.True(result.TryGetValue(out var plan));
        Assert.False(result.HasErrors);
        Assert.DoesNotContain(plan.Attributes, a => a.Name == "x");
        Assert.All(plan.FormalAttributes, f => Assert.NotEqual("x", f.Identity.AttributeName));
    }

    [Fact]
    public void Plan_WhenActiveCutBasedAttributeHasDormantValueLabels_ThenIgnoredWithoutError()
    {
        // §10.8 / D-049: value_labels under a cut-based discretizer is dormant — it is
        // ignored, so a key absent from declared_domain is NOT ValueLabelKeyNotInDomain.
        var age = new AttributeSpec("age", new ColumnSource(0), Include: true,
            new ManualCutsDiscretizer([30.0, 40.0], BinEnds.Open, CultureInfo.InvariantCulture),
            new NominalScale(), DeclaredDomain: [],
            new Dictionary<string, string> { ["old"] = "Old retained label" },
            MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [age]);

        var result = ConversionPlanner.Plan(spec, new SourceSchema(1));

        Assert.True(result.TryGetValue(out _));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyNotInDomain);
    }

    [Fact]
    public void Plan_WhenCutBasedAttributeHasValueLabelsMatchingBinLabel_ThenLabelsAreIgnoredInNames()
    {
        // §10.8 / D-049: value_labels is dormant under a cut discretizer — it must not
        // change rendered names, even when a key happens to match a cut-bin label.
        var age = new AttributeSpec("age", new ColumnSource(0), Include: true,
            new ManualCutsDiscretizer([30.0], BinEnds.Open, CultureInfo.InvariantCulture),
            new NominalScale(), DeclaredDomain: [],
            new Dictionary<string, string> { ["<30"] = "Young" },
            MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [age]);

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["age-<30", "age->=30"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenValueLabelKeyNotInDomain_ThenReportsValueLabelKeyNotInDomain()
    {
        var attr = SpecFixtures.Nominal("g", 0, ["b", "n"], new Dictionary<string, string> { ["x"] = "broad" });
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]);

        AssertFailsWith(ConversionPlanner.Plan(spec, new SourceSchema(1)), DiagnosticCode.ValueLabelKeyNotInDomain);
    }

    [Fact]
    public void Plan_WhenRenderedNamesCollide_ThenReportsNameCollisionButNotIdentityCollision()
    {
        // "a"+"b-x" and "a-b"+"x" both render "a-b-x" but have distinct identities.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
        [
            SpecFixtures.Nominal("a", 0, ["b-x"]),
            SpecFixtures.Nominal("a-b", 1, ["x"]),
        ]);

        var result = ConversionPlanner.Plan(spec, new SourceSchema(2));

        AssertFailsWith(result, DiagnosticCode.FormalAttributeNameCollision);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeCollision);
    }

    [Fact]
    public void Plan_WhenDeclaredDomainHasDuplicate_ThenReportsIdentityCollision()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("a", 0, ["x", "x"])]);

        AssertFailsWith(ConversionPlanner.Plan(spec, new SourceSchema(1)), DiagnosticCode.FormalAttributeCollision);
    }

    private static void AssertFailsWith(Diagnosed<ConversionPlan> result, DiagnosticCode code)
    {
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }
}
