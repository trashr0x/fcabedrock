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

    private static BedDocument EmploymentOrdinalDoc() => BedReader.Read(BedFixtures.EmploymentOrdinalBed);

    private static BedrockSpec EmploymentOrdinalSpec(ScalingMode mode = ScalingMode.Discrete) =>
        BedToSpec.ToSpec(EmploymentOrdinalDoc(), Wide(), mode);

    [Fact]
    public void ToSpec_WhenTypeO_ThenManualCutsFromNumericCutSpecWithNoValueLabels()
    {
        var age = EmploymentOrdinalSpec().Attributes[0];

        var cuts = Assert.IsType<ManualCutsDiscretizer>(age.Discretizer);
        Assert.Equal(new double[] { 30, 40, 50 }, cuts.Cuts);
        Assert.Equal(BinEnds.Open, cuts.Ends);
        Assert.Equal("[30, 40)", cuts.Discretize("35"));
        Assert.Empty(age.ValueLabels);
    }

    [Fact]
    public void ToSpec_WhenTypeN_ThenOrderedCutsOverDomainWithCutAtManagerial()
    {
        var employment = EmploymentOrdinalSpec().Attributes[2];

        var ordered = Assert.IsType<OrderedCutsDiscretizer>(employment.Discretizer);
        Assert.Equal(["Unskilled", "Clerical", "Professional", "Managerial"], ordered.Order);
        Assert.Equal(["Managerial"], ordered.Cuts);
        Assert.Equal("<Managerial", ordered.Discretize("Clerical"));
        Assert.Equal(">=Managerial", ordered.Discretize("Managerial"));
        Assert.Empty(employment.ValueLabels);
    }

    [Fact]
    public void ToSpec_WhenDiscreteMode_ThenCutTypesUseNominalScale()
    {
        var spec = EmploymentOrdinalSpec(ScalingMode.Discrete);

        Assert.IsType<NominalScale>(spec.Attributes[0].Scale); // o (age)
        Assert.IsType<NominalScale>(spec.Attributes[2].Scale); // n (employment)
    }

    [Fact]
    public void ToSpec_WhenProgressiveMode_ThenCutTypesUseOrdinalLeScale()
    {
        var spec = EmploymentOrdinalSpec(ScalingMode.Progressive);

        Assert.Equal(OrdinalDirection.Le, Assert.IsType<OrdinalScale>(spec.Attributes[0].Scale).Direction);
        Assert.Equal(OrdinalDirection.Le, Assert.IsType<OrdinalScale>(spec.Attributes[2].Scale).Direction);

        // Non-cut types are unaffected by the mode.
        Assert.IsType<NominalScale>(spec.Attributes[1].Scale);    // education (c)
        Assert.IsType<DichotomicScale>(spec.Attributes[4].Scale); // US-citizen (b)
    }

    [Fact]
    public void ToSpec_WhenBindingLocaleNonInvariant_ThenNumericDiscretizerParsesWithIt()
    {
        var deDe = new Binding(SourceShape.Wide, ',', '"', HasHeader: true, "de-DE", "?", new RowIndexObjectKey());

        var age = BedToSpec.ToSpec(EmploymentOrdinalDoc(), deDe).Attributes[0];

        Assert.Equal(">=50", Assert.IsType<ManualCutsDiscretizer>(age.Discretizer).Discretize("50,5"));
    }
}
