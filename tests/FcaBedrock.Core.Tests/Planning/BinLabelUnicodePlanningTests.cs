using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Planning;

/// <summary>
/// The <c>bin_label_unicode</c> presentation (spec §8): with the flag, the operators the planner
/// generates render as <c>≥</c>/<c>≤</c>, and nothing else in a name changes.
/// <para>
/// Every expected name is written out by hand. The generated operators are an ordinal scale's
/// <c>&gt;=</c>/<c>&lt;=</c> (in a default name and in <c>{scale_op}</c>) and the <c>&gt;=</c> of
/// an open high cut bin; <c>&gt;</c>, <c>&lt;</c>, <c>all</c> and the interior <c>[a, b)</c>
/// have no Unicode form. Authored text keeps its spelling even where it reads like an operator:
/// an ordered cut <c>=x</c> makes the open low bin <c>&lt;</c> + <c>=x</c>, which must stay
/// <c>&lt;=x</c>. <see cref="LabelStyle.V2Compat"/> always renders ASCII.
/// </para>
/// </summary>
public sealed class BinLabelUnicodePlanningTests
{
    // ---- ordinal operators over value bins: all four direction × boundary modes ----

    [Theory]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, "level-≥low", "level-≥mid", "level-≥high")]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Strict, "level->low", "level->mid", "level->high")]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Inclusive, "level-≤low", "level-≤mid", "level-≤high")]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Strict, "level-<low", "level-<mid", "level-<high")]
    public void Plan_WhenValueBinOrdinalIsUnicode_ThenOnlyInclusiveOperatorsChange(
        OrdinalDirection direction, OrdinalBoundary boundary, string first, string second, string third)
    {
        var attribute = ValueOrdinal(direction, boundary);

        Assert.Equal([first, second, third], Names(Plan(attribute, unicode: true)));
    }

    [Theory]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, "level->=low", "level->=mid", "level->=high")]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Strict, "level->low", "level->mid", "level->high")]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Inclusive, "level-<=low", "level-<=mid", "level-<=high")]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Strict, "level-<low", "level-<mid", "level-<high")]
    public void Plan_WhenValueBinOrdinalIsAscii_ThenTheOperatorsAreTheAsciiOnes(
        OrdinalDirection direction, OrdinalBoundary boundary, string first, string second, string third)
    {
        var attribute = ValueOrdinal(direction, boundary);

        Assert.Equal([first, second, third], Names(Plan(attribute, unicode: false)));
    }

    // ---- ordinal operators over numeric and text cuts, open and closed ends ----

    [Theory]
    [InlineData(BinEnds.Open, OrdinalDirection.Ge, new[] { "age-all", "age-≥30", "age-≥40", "age-≥50" })]
    [InlineData(BinEnds.Open, OrdinalDirection.Le, new[] { "age-<30", "age-<40", "age-<50", "age-all" })]
    [InlineData(BinEnds.Closed, OrdinalDirection.Ge, new[] { "age-≥30", "age-≥40" })]
    [InlineData(BinEnds.Closed, OrdinalDirection.Le, new[] { "age-<40", "age-<50" })]
    public void Plan_WhenNumericCutOrdinalIsUnicode_ThenGeThresholdsTakeTheUnicodeOperator(
        BinEnds ends, OrdinalDirection direction, string[] expected)
    {
        var attribute = NumericCuts([30, 40, 50], ends, new OrdinalScale(direction));

        Assert.Equal(expected, Names(Plan(attribute, unicode: true)));
    }

    [Fact]
    public void Plan_WhenTextCutOrdinalIsUnicode_ThenTheOperatorChangesAndTheCutTextDoesNot()
    {
        // The threshold at the cut "=x" is the generated operator followed by the authored cut:
        // "≥" + "=x". Only the operator is presentation.
        var attribute = TextCuts(["w", "=x", "b"], ["=x", "b"], BinEnds.Open, new OrdinalScale(OrdinalDirection.Ge));

        Assert.Equal(["grade-all", "grade-≥=x", "grade-≥b"], Names(Plan(attribute, unicode: true)));
        Assert.Equal(["grade-all", "grade->==x", "grade->=b"], Names(Plan(attribute, unicode: false)));
    }

    // ---- nominal cut bins: only the open high bin carries a generated operator ----

    [Fact]
    public void Plan_WhenNumericCutBinsAreUnicode_ThenOnlyTheOpenHighBinChanges()
    {
        var open = NumericCuts([30, 50], BinEnds.Open, new NominalScale());
        var closed = NumericCuts([30, 40, 50], BinEnds.Closed, new NominalScale());

        Assert.Equal(["age-<30", "age-[30, 50)", "age-≥50"], Names(Plan(open, unicode: true)));
        Assert.Equal(["age-<30", "age-[30, 50)", "age->=50"], Names(Plan(open, unicode: false)));
        Assert.Equal(["age-[30, 40)", "age-[40, 50)"], Names(Plan(closed, unicode: true)));
    }

    [Fact]
    public void Plan_WhenTheOpenLowTextCutReadsLikeAnOperator_ThenItStaysAsWritten()
    {
        // The open low bin is "<" + "=x": a strictly-below bin whose spelling happens to be "<=x".
        // Reading the label would turn it into "≤x", a different meaning; the bin's structure says
        // its only generated operator is "<", which has no Unicode form.
        var open = TextCuts(["w", "=x", "b"], ["=x", "b"], BinEnds.Open, new NominalScale());
        var closed = TextCuts(["w", "=x", "b"], ["=x", "b"], BinEnds.Closed, new NominalScale());

        Assert.Equal(["grade-<=x", "grade-[=x, b)", "grade-≥b"], Names(Plan(open, unicode: true)));
        Assert.Equal(["grade-[=x, b)"], Names(Plan(closed, unicode: true)));
    }

    // ---- v2 compatibility takes precedence; the old overload stays ASCII ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_WhenV2Compat_ThenAsciiIsEffectiveWhateverIsRequested(bool requested)
    {
        var attribute = NumericCuts([30, 50], BinEnds.Open, new NominalScale());

        var plan = Plan(attribute, requested, LabelStyle.V2Compat);

        Assert.False(plan.BinLabelUnicode);
        Assert.Equal(LabelStyle.V2Compat, plan.LabelStyle);
        Assert.Equal(["age-<30", "age-30to<50", "age->=50"], Names(plan));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_WhenNative_ThenTheEffectiveFlagIsTheRequestedOne(bool requested)
    {
        var plan = Plan(NumericCuts([30, 50], BinEnds.Open, new NominalScale()), requested);

        Assert.Equal(requested, plan.BinLabelUnicode);
        Assert.Equal(LabelStyle.Native, plan.LabelStyle);
    }

    [Fact]
    public void Plan_WhenTheTwoArgumentOverloadIsUsed_ThenItIsTheAsciiPlan()
    {
        // The existing overload keeps its exact behavior: ASCII, whatever the spec's [output]
        // says, because Core never reads [output]. It is the three-argument overload with false.
        var calibrated = Calibrated(NumericCuts([30, 50], BinEnds.Open, new OrdinalScale(OrdinalDirection.Ge)));

        var byDefault = Ok(ConversionPlanner.Plan(calibrated));
        var native = Ok(ConversionPlanner.Plan(calibrated, LabelStyle.Native));
        var explicitAscii = Ok(ConversionPlanner.Plan(calibrated, LabelStyle.Native, binLabelUnicode: false));

        Assert.False(byDefault.BinLabelUnicode);
        Assert.False(native.BinLabelUnicode);
        Assert.Equal(["age-all", "age->=30", "age->=50"], Names(byDefault));
        Assert.Equal(Names(explicitAscii), Names(byDefault));
        Assert.Equal(Names(explicitAscii), Names(native));
    }

    // ---- formats, labels and authored text ----

    [Fact]
    public void Plan_WhenAFormatUsesScaleOpAndValue_ThenScaleOpCarriesTheUnicodeOperator()
    {
        var ordinal = NumericCuts([30, 50], BinEnds.Open, new OrdinalScale(OrdinalDirection.Ge)) with
        {
            NameFormat = Format("{name}|{scale_op}{value}"),
        };
        var nominal = NumericCuts([30, 50], BinEnds.Open, new NominalScale()) with
        {
            NameFormat = Format("{column}/{value}"),
        };

        Assert.Equal(["age|all", "age|≥30", "age|≥50"], Names(Plan(ordinal, unicode: true)));
        Assert.Equal(["age/<30", "age/[30, 50)", "age/≥50"], Names(Plan(nominal, unicode: true)));
    }

    [Fact]
    public void Plan_WhenTheFormatLiteralHoldsOperatorText_ThenOnlyThePlaceholderChanges()
    {
        var attribute = ValueOrdinal(OrdinalDirection.Le, OrdinalBoundary.Inclusive) with
        {
            NameFormat = Format("{name} <= {scale_op}{value} >="),
        };

        Assert.Equal(
            ["level <= ≤low >=", "level <= ≤mid >=", "level <= ≤high >="],
            Names(Plan(attribute, unicode: true)));
    }

    [Fact]
    public void Plan_WhenValuesAndLabelsHoldOperatorText_ThenTheyStayAsWritten()
    {
        // Authored values and labels are opaque strings (§21 item 7): only the ordinal operator
        // the planner generates changes, never what follows it.
        var ordinal = ValueOrdinal(
            OrdinalDirection.Le, OrdinalBoundary.Inclusive,
            labels: new Dictionary<string, string> { ["low"] = ">=10", ["mid"] = "<=20" });
        var nominal = SpecFixtures.Nominal(
            "class", 0, ["<=50K", ">50K"], new Dictionary<string, string> { [">50K"] = ">=50K" });

        Assert.Equal(["level-≤>=10", "level-≤<=20", "level-≤high"], Names(Plan(ordinal, unicode: true)));
        Assert.Equal(["class-<=50K", "class->=50K"], Names(Plan(nominal, unicode: true)));
    }

    [Fact]
    public void Plan_WhenADichotomicValueIsRendered_ThenTheAuthoredTrueValueStaysAsWritten()
    {
        // A dichotomic {value} is the authored true_value: its structure carries no generated
        // operator, so it keeps its spelling.
        var attribute = SpecFixtures.Dichotomic("flag", 0, ">=1", [">=1", "0"]) with
        {
            NameFormat = Format("{column}-{value}"),
        };

        Assert.Equal(["flag->=1"], Names(Plan(attribute, unicode: true)));
    }

    [Fact]
    public void Plan_WhenTheMissingColumnIsPlanned_ThenItIsUnaffected()
    {
        var attribute = NumericCuts([30, 50], BinEnds.Open, new OrdinalScale(OrdinalDirection.Ge), MissingPolicy.AsAttribute);

        Assert.Equal(["age-all", "age-≥30", "age-≥50", "age-missing"], Names(Plan(attribute, unicode: true)));
    }

    // ---- the existing collision rule and the unchanged identity ----

    [Fact]
    public void Plan_WhenAUnicodeNameEqualsAnAuthoredName_ThenTheExistingNameCollisionIsReported()
    {
        // Unicode presentation can make a generated name equal an authored one that ASCII kept
        // apart; the planner's existing name-collision rule (§10.2) reports it, and nothing else.
        var age = NumericCuts([30, 50], BinEnds.Open, new NominalScale());
        var tag = SpecFixtures.Nominal("tag", 1, ["age-≥50"]) with { NameFormat = Format("{value}") };
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [age, tag]);

        var unicode = ConversionPlanner.Plan(Calibrated(spec, 2), LabelStyle.Native, binLabelUnicode: true);
        var ascii = ConversionPlanner.Plan(Calibrated(spec, 2), LabelStyle.Native, binLabelUnicode: false);

        var collision = Assert.Single(unicode.Diagnostics);
        Assert.Equal(DiagnosticCode.FormalAttributeNameCollision, collision.Code);
        Assert.Equal(DiagnosticSeverity.Error, collision.Severity);
        Assert.Equal("Formal attribute name 'age-≥50' (from attribute 'tag') collides with an earlier one.", collision.Message);
        Assert.False(unicode.TryGetValue(out _));
        Assert.Empty(ascii.Diagnostics);
    }

    [Fact]
    public void Plan_WhenOnlyTheFlagDiffers_ThenIdentitiesBinsIdsAndCrossingsAreEqual()
    {
        // The flag reaches rendered names only (§14): canonical identity (the ASCII operator
        // included), structural bin, id, recognized bins and every bin's crossing ids are the
        // same, which is what keeps the schema and .dat fingerprints and the incidence equal.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            NumericCuts([30, 50], BinEnds.Open, new NominalScale(), MissingPolicy.AsAttribute),
            NumericCuts([30, 50], BinEnds.Open, new OrdinalScale(OrdinalDirection.Ge)) with { Name = "age_ge" },
            TextCuts(["w", "=x", "b"], ["=x", "b"], BinEnds.Open, new NominalScale()) with { Source = new ColumnSource(1, SourceValueType.String) },
            ValueOrdinal(OrdinalDirection.Le, OrdinalBoundary.Inclusive) with { Source = new ColumnSource(2, SourceValueType.String) },
        ]);

        var unicode = Ok(ConversionPlanner.Plan(Calibrated(spec, 3), LabelStyle.Native, binLabelUnicode: true));
        var ascii = Ok(ConversionPlanner.Plan(Calibrated(spec, 3), LabelStyle.Native, binLabelUnicode: false));

        Assert.Equal(
            ascii.FormalAttributes.Select(f => (f.Id, f.Identity, f.Bin)),
            unicode.FormalAttributes.Select(f => (f.Id, f.Identity, f.Bin)));
        Assert.Contains(unicode.FormalAttributes, f => f.Identity.Operator == ">=");
        Assert.Contains(unicode.FormalAttributes, f => f.Identity.Operator == "<=");
        Assert.NotEqual(Names(ascii), Names(unicode));

        Assert.Equal(ascii.Attributes.Count, unicode.Attributes.Count);
        for (var i = 0; i < ascii.Attributes.Count; i++)
        {
            Assert.Equal(ascii.Attributes[i].MissingFormalAttributeId, unicode.Attributes[i].MissingFormalAttributeId);
            Assert.Equal(ascii.Attributes[i].KnownBins.Order(StringComparer.Ordinal), unicode.Attributes[i].KnownBins.Order(StringComparer.Ordinal));
            Assert.Equal(
                ascii.Attributes[i].CrossesByBin.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, string.Join(",", pair.Value))),
                unicode.Attributes[i].CrossesByBin.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, string.Join(",", pair.Value))));
        }
    }

    [Fact]
    public void Plan_WhenUnicodeIsPlannedTwice_ThenTheNamesAreIdentical()
    {
        var attribute = TextCuts(["w", "=x", "b"], ["=x", "b"], BinEnds.Open, new OrdinalScale(OrdinalDirection.Ge));

        Assert.Equal(Names(Plan(attribute, unicode: true)), Names(Plan(attribute, unicode: true)));
    }

    // ---- helpers ----

    private static AttributeSpec ValueOrdinal(
        OrdinalDirection direction, OrdinalBoundary boundary, IReadOnlyDictionary<string, string>? labels = null) =>
        SpecFixtures.OrdinalValueBins(
            "level", 0, ["low", "mid", "high"],
            new OrdinalScale(direction, DropTop: false, boundary, Order: ["low", "mid", "high"]),
            labels);

    private static AttributeSpec NumericCuts(
        IReadOnlyList<double> cuts, BinEnds ends, Scale scale, MissingPolicy missing = MissingPolicy.Skip) =>
        new("age", new ColumnSource(0, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create(cuts, ends, CultureInfo.InvariantCulture).Value!, scale,
            DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, missing, UnknownValuePolicy.Warn);

    private static AttributeSpec TextCuts(
        IReadOnlyList<string> order, IReadOnlyList<string> cuts, BinEnds ends, Scale scale) =>
        new("grade", new ColumnSource(0, SourceValueType.String), Include: true,
            OrderedCutsDiscretizer.Create(order, cuts, ends).Value!, scale,
            DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static NameFormat Format(string text)
    {
        Assert.True(NameFormat.TryCreate(text, out var parsed, out var error), error);
        return parsed;
    }

    private static CalibratedSpec Calibrated(AttributeSpec attribute) =>
        Calibrated(new BedrockSpec(SpecFixtures.WideRowIndex(), [attribute]), 1);

    // Plans a hand-built spec the way production does (D-098): resolve the token (the trust
    // boundary), then take the fully-declared calibrated state.
    private static CalibratedSpec Calibrated(BedrockSpec spec, int columns) =>
        CalibratedSpec.FromFullyDeclared(SpecFixtures.Resolve(spec, new SourceSchema(columns)));

    private static ConversionPlan Plan(AttributeSpec attribute, bool unicode, LabelStyle style = LabelStyle.Native) =>
        Ok(ConversionPlanner.Plan(Calibrated(attribute), style, unicode));

    private static string[] Names(ConversionPlan plan) =>
        [.. plan.FormalAttributes.Select(f => f.RenderedName)];

    private static ConversionPlan Ok(Diagnosed<ConversionPlan> result)
    {
        Assert.True(result.TryGetValue(out var plan), string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return plan!;
    }
}
