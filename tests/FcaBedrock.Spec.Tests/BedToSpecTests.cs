using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Tests;

public sealed class BedToSpecTests
{
    private static Binding Wide() =>
        new(SourceShape.Wide, ',', '"', HasHeader: true, "invariant", "?", new RowIndexObjectKey());

    private static BedrockSpec MushroomSpec() => BedToSpec.ToSpec(BedReader.Read(BedFixtures.MushroomBed), Wide());

    [Fact]
    public void ToSpec_WhenExcludedAttribute_ThenCarriesNoEmittedFields()
    {
        var cls = MushroomSpec().Attributes[0];

        Assert.False(cls.Include);
        Assert.Null(cls.Scale);
        Assert.Null(cls.Discretizer);
        Assert.Empty(cls.DeclaredDomain);
    }

    [Fact]
    public void ToSpec_WhenTypeB_ThenDichotomicWithFirstValueAsTrueValue()
    {
        var bruises = MushroomSpec().Attributes[1];

        var scale = Assert.IsType<DichotomicScale>(bruises.Scale);
        Assert.Equal("t", scale.TrueValue);
        Assert.Empty(bruises.ValueLabels);
    }

    [Fact]
    public void ToSpec_WhenTypeC_ThenNominalWithDomainAndValueLabels()
    {
        var gill = MushroomSpec().Attributes[2];

        Assert.IsType<IdentityDiscretizer>(gill.Discretizer);
        Assert.IsType<NominalScale>(gill.Scale);
        Assert.Equal(["b", "n"], gill.DeclaredDomain);
        Assert.Equal("broad", gill.ValueLabels["b"]);
        Assert.Equal("narrow", gill.ValueLabels["n"]);
    }

    [Fact]
    public void ToSpec_WhenPlannedEndToEnd_ThenFormalAttributesMatchV2Names()
    {
        Assert.True(ConversionPlanner.Plan(MushroomSpec(), new SourceSchema(5)).TryGetValue(out var plan));

        Assert.Equal(
            [
                "bruises?", "gill-size-broad", "gill-size-narrow", "veil-type-partial",
                "veil-type-universal", "ring-number-none", "ring-number-one", "ring-number-two",
            ],
            plan.FormalAttributes.Select(f => f.RenderedName));
    }
}
