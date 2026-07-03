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
    public void ToSpec_WhenExcludedAttribute_ThenRetainsEmittedConfig()
    {
        // include = false is an authoring toggle (D-049): the migrator preserves the
        // excluded attribute's v2 config (class is type c → identity + nominal, domain
        // e/p, value_labels e→edible / p→poisonous) so the spec round-trips and can be
        // switched back on. The planner ignores it while excluded.
        var cls = MushroomSpec().Attributes[0];

        Assert.False(cls.Include);
        Assert.IsType<IdentityDiscretizer>(cls.Discretizer);
        Assert.IsType<NominalScale>(cls.Scale);
        Assert.Equal(["e", "p"], cls.DeclaredDomain);
        Assert.Equal("edible", cls.ValueLabels["e"]);
        Assert.Equal("poisonous", cls.ValueLabels["p"]);
    }

    [Fact]
    public void ToSpec_WhenExcludedAttributeOfUnsupportedType_ThenMigratesAsBareExcluded()
    {
        // A deferred/unsupported v2 type (d = date) on an excluded attribute still
        // migrates — as a bare excluded attribute — rather than failing the whole
        // migration, preserving the pre-D-049 drop-on-exclude robustness.
        var document = new BedDocument(
            AttributeCount: 1, Names: ["when"], Categories: [["2020", "2021"]],
            Values: [["2020", "2021"]], Convert: [false], Types: ["d"], RestrictTo: [""]);

        var when = BedToSpec.ToSpec(document, Wide()).Attributes[0];

        Assert.False(when.Include);
        Assert.Null(when.Discretizer);
        Assert.Null(when.Scale);
        Assert.Empty(when.DeclaredDomain);
    }

    [Fact]
    public void ToSpec_WhenExcludedAttributeHasMalformedConfig_ThenMigratesAsBareExcluded()
    {
        // D-049: dormant config must never block migration. An excluded type-o column
        // with non-numeric cut tokens can't be recovered → bare excluded, not a throw.
        var document = new BedDocument(
            AttributeCount: 1, Names: ["age"], Categories: [["young", "old"]],
            Values: [["<", "abc", ">"]], Convert: [false], Types: ["o"], RestrictTo: [""]);

        var age = BedToSpec.ToSpec(document, Wide()).Attributes[0];

        Assert.False(age.Include);
        Assert.Null(age.Discretizer);
        Assert.Null(age.Scale);
        Assert.Empty(age.DeclaredDomain);
    }

    [Fact]
    public void ToSpec_WhenIncludedAttributeHasMalformedConfig_ThenMigrationFails()
    {
        // The recover-or-degrade path is for excluded (dormant) config only: an active
        // attribute with malformed config still fails migration, surfacing the error.
        var document = new BedDocument(
            AttributeCount: 1, Names: ["age"], Categories: [["young", "old"]],
            Values: [["<", "abc", ">"]], Convert: [true], Types: ["o"], RestrictTo: [""]);

        Assert.ThrowsAny<Exception>(() => BedToSpec.ToSpec(document, Wide()));
    }

    [Fact]
    public void ToSpec_WhenIncludedAttributeHasNonAscendingCuts_ThenMigrationFailsWithCutDiagnostic()
    {
        // D-056: cut validation runs in the smart factory wired into BedToSpec. An active
        // type-o column with descending cuts fails migration with a clear message — the
        // old behavior was silently wrong bins (or an opaque IndexOutOfRange).
        var document = new BedDocument(
            AttributeCount: 1, Names: ["age"], Categories: [["young", "old"]],
            Values: [["<", "50", "30", ">"]], Convert: [true], Types: ["o"], RestrictTo: [""]);

        var ex = Assert.ThrowsAny<Exception>(() => BedToSpec.ToSpec(document, Wide()));
        Assert.Contains("ascending", ex.Message);
    }

    [Fact]
    public void ToSpec_WhenExcludedAttributeHasNonAscendingCuts_ThenMigratesAsBareExcluded()
    {
        // The cut-validation failure flows into the D-049 excluded-recovery catch, so a
        // parked column with invalid cuts still migrates as bare excluded, never failing.
        var document = new BedDocument(
            AttributeCount: 1, Names: ["age"], Categories: [["young", "old"]],
            Values: [["<", "50", "30", ">"]], Convert: [false], Types: ["o"], RestrictTo: [""]);

        var age = BedToSpec.ToSpec(document, Wide()).Attributes[0];

        Assert.False(age.Include);
        Assert.Null(age.Discretizer);
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
        Assert.Equal(BinResult.Bin("[30, 40)"), cuts.Discretize("35"));
        Assert.Empty(age.ValueLabels);
    }

    [Fact]
    public void ToSpec_WhenTypeN_ThenOrderedCutsOverDomainWithCutAtManagerial()
    {
        var employment = EmploymentOrdinalSpec().Attributes[2];

        var ordered = Assert.IsType<OrderedCutsDiscretizer>(employment.Discretizer);
        Assert.Equal(["Unskilled", "Clerical", "Professional", "Managerial"], ordered.Order);
        Assert.Equal(["Managerial"], ordered.Cuts);
        Assert.Equal(BinResult.Bin("<Managerial"), ordered.Discretize("Clerical"));
        Assert.Equal(BinResult.Bin(">=Managerial"), ordered.Discretize("Managerial"));
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
    public void ToSpec_WhenStringFixingTypes_ThenSourceValueTypeString()
    {
        // D-061: c/b (identity) and n (ordered_cuts) are string-fixing.
        var spec = EmploymentOrdinalSpec();

        Assert.Equal(SourceValueType.String, Assert.IsType<ColumnSource>(spec.Attributes[1].Source).ValueType); // education (c)
        Assert.Equal(SourceValueType.String, Assert.IsType<ColumnSource>(spec.Attributes[2].Source).ValueType); // employment (n)
        Assert.Equal(SourceValueType.String, Assert.IsType<ColumnSource>(spec.Attributes[4].Source).ValueType); // US-citizen (b)
    }

    [Fact]
    public void ToSpec_WhenTypeO_ThenSourceValueTypeNumber()
    {
        // D-061: o migrates to manual_cuts, which is number-fixing.
        var age = EmploymentOrdinalSpec().Attributes[0];

        Assert.Equal(SourceValueType.Number, Assert.IsType<ColumnSource>(age.Source).ValueType);
    }

    [Fact]
    public void ToSpec_WhenMigrated_ThenRestrictToEmpty()
    {
        // Deliberate until the migrator rework (M2 Slice G): the v2 [Restrict To
        // Values] section stays unmapped, so every attribute resolves unrestricted.
        Assert.All(MushroomSpec().Attributes, a => Assert.Empty(a.RestrictTo));
    }

    [Fact]
    public void ToSpec_WhenBindingLocaleNonInvariant_ThenNumericDiscretizerParsesWithIt()
    {
        var deDe = new Binding(SourceShape.Wide, ',', '"', HasHeader: true, "de-DE", "?", new RowIndexObjectKey());

        var age = BedToSpec.ToSpec(EmploymentOrdinalDoc(), deDe).Attributes[0];

        Assert.Equal(BinResult.Bin(">=50"), Assert.IsType<ManualCutsDiscretizer>(age.Discretizer).Discretize("50,5"));
    }
}
