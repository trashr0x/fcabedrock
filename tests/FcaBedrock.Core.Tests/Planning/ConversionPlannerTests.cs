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
    public void Plan_WhenExcludedAttributeSetsScale_ThenReportsEmittedFieldOnExcludedAttribute()
    {
        var bad = new AttributeSpec("x", new ColumnSource(0), Include: false,
            new IdentityDiscretizer(), new NominalScale(), ["a"], SpecFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [bad]);

        AssertFailsWith(ConversionPlanner.Plan(spec, new SourceSchema(1)), DiagnosticCode.EmittedFieldOnExcludedAttribute);
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
