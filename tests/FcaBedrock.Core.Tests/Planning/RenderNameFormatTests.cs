using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Planning;

/// <summary>
/// Planner-owned name rendering under an explicit <c>formal_attribute_format</c>
/// (§10.7, D-117/D-120): the exact rendered names per scale kind and value route,
/// the total override including the missing column, and the plan-time
/// rendered-name backstop with its pinned message representation (D-116).
/// <para>
/// Rendering lives here and nowhere else — exporters are decision-free
/// serializers (P-15), so a name that reaches a writer is already final.
/// </para>
/// </summary>
public sealed class RenderNameFormatTests
{
    // ---- Rendering per scale kind and value route (§10.7's {value} table) ----

    [Fact]
    public void Plan_WhenNominalWithFormat_ThenEveryBinRendersThroughIt()
    {
        var plan = Plan(Nominal(["b", "n"], format: "{column}::{value}"));

        Assert.Equal(["gill-size::b", "gill-size::n"], Names(plan));
    }

    [Fact]
    public void Plan_WhenNominalWithLiveValueLabels_ThenValueRendersTheLabel()
    {
        // §10.8/§10.7: identity consults value_labels, so {value} is the display label.
        var plan = Plan(Nominal(["b", "n"], format: "{value}", labels: new Dictionary<string, string> { ["b"] = "broad" }));

        Assert.Equal(["broad", "n"], Names(plan));
    }

    [Fact]
    public void Plan_WhenDichotomicWithFormat_ThenValueIsTheTrueValue()
    {
        // §10.7: the dichotomic default omits {value}; opting into it resolves {value} to
        // the scale's true_value — the single formal attribute keeps its identity.
        var plan = Plan(Dichotomic("t", ["t", "f"], format: "{column}-{value}"));

        Assert.Equal(["bruises?-t"], Names(plan));
    }

    [Fact]
    public void Plan_WhenLabelledDichotomicWithFormat_ThenValueIsTheLabelledTrueValue()
    {
        // §10.7's worked example verbatim: true_value = "t" labelled "bruised" renders
        // "bruises?-bruised", NOT the raw "t" — §10.8's whole purpose is that labels are
        // how raw values appear in names.
        var plan = Plan(Dichotomic(
            "t", ["t", "f"], format: "{column}-{value}", labels: new Dictionary<string, string> { ["t"] = "bruised" }));

        Assert.Equal(["bruises?-bruised"], Names(plan));
    }

    [Fact]
    public void Plan_WhenOrdinalWithFormat_ThenScaleOpCarriesTheOperator()
    {
        var plan = Plan(OrdinalOverCuts([30, 40], format: "{name}|{scale_op}{value}"));

        Assert.Equal(["age|<30", "age|<40", "age|all"], Names(plan));
    }

    [Fact]
    public void Plan_WhenNonOrdinalWithScaleOp_ThenItRendersEmpty()
    {
        // §10.7: {scale_op} is "empty for non-ordinal scales" — so it contributes nothing
        // rather than being an error or a placeholder artifact.
        var plan = Plan(Nominal(["b"], format: "{column}[{scale_op}]{value}"));

        Assert.Equal(["gill-size[]b"], Names(plan));
    }

    [Fact]
    public void Plan_WhenNumericCutBins_ThenValueUsesTheStyleRenderedBinLabel()
    {
        // The cut-bin route: value_labels are dormant under a cut discretizer (D-049), so
        // {value} is the discretizer's canonical bin label for the style.
        var plan = Plan(NominalOverCuts([30, 40], format: "{column}/{value}"));

        Assert.Equal(["age/<30", "age/[30, 40)", "age/>=40"], Names(plan));
    }

    [Fact]
    public void Plan_WhenNumericFreePerValue_ThenValueUsesTheD092NumericIdentity()
    {
        // D-092/§10.7: a numeric free_per_value bin renders its parsed numeric identity in
        // the §14 invariant shortest form, so 90/90.0/9e1 share one bin rendered "90".
        var attribute = SpecFixtures.FreePerValue(
            "score", 0, SourceValueType.Number, ["90", "0"], new NominalScale()) with
        {
            NameFormat = Format("{column}={value}"),
        };

        Assert.Equal(["score=90", "score=0"], Names(Plan(attribute)));
    }

    // ---- The total override, including the missing column ----

    [Fact]
    public void Plan_WhenFormatAndMissingColumn_ThenTheMissingColumnRendersThroughTheFormatToo()
    {
        // §10.7/D-117: an explicit format is a TOTAL override for every formal attribute
        // the logical attribute emits — the missing column included, with {value} = the
        // literal "missing" instead of the default "{column}-missing".
        var plan = Plan(Dichotomic(
            "t", ["t", "f"], format: "{column}-{value}", missing: MissingPolicy.AsAttribute));

        Assert.Equal(["bruises?-t", "bruises?-missing"], Names(plan));
    }

    [Fact]
    public void Plan_WhenFormatOmitsColumnAndMissingColumn_ThenTheMissingColumnHasNoPrefixEither()
    {
        // The override really is total: a format without {column} strips the prefix from
        // the missing column as well, which the default path could never produce.
        var plan = Plan(Nominal(["b"], format: "{value}", missing: MissingPolicy.AsAttribute));

        Assert.Equal(["b", "missing"], Names(plan));
    }

    [Fact]
    public void Plan_WhenFormatAndMissingColumn_ThenItsPositionAndIdentityAreUnchanged()
    {
        // §10.5/§14: the format changes the missing column's NAME only — it still appends
        // after the scale's columns and keeps its canonical "missing" bin key.
        var plan = Plan(Nominal(["b", "n"], format: "{value}", missing: MissingPolicy.AsAttribute));

        var missing = plan.FormalAttributes[^1];
        Assert.Equal("missing", missing.Identity.BinKey);
        Assert.Equal("", missing.Identity.Operator);
    }

    [Fact]
    public void Plan_WhenNoFormat_ThenTheDefaultMissingColumnNameIsUnchanged()
    {
        // The pre-M6 default path, byte-for-byte: no format ⇒ "{column}-missing" (D-074).
        var plan = Plan(Nominal(["b"], format: null, missing: MissingPolicy.AsAttribute));

        Assert.Equal(["gill-size-b", "gill-size-missing"], Names(plan));
    }

    // ---- [defaults] versus explicit (the resolved-format axis Core sees) ----

    [Fact]
    public void Plan_WhenFormatIsNull_ThenTheScaleSpecificDefaultsApply()
    {
        // The null case is the normal one and must stay byte-identical to pre-M6: the
        // dichotomic default is the column alone, the nominal default is {column}-{value}.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.Dichotomic("bruises?", 0, "t", ["t", "f"]),
            SpecFixtures.Nominal("gill-size", 1, ["b", "n"]),
        ]);

        Assert.Equal(["bruises?", "gill-size-b", "gill-size-n"], Names(Plan(spec, new SourceSchema(2))));
    }

    [Fact]
    public void Plan_WhenTwoAttributesDifferOnlyByFormat_ThenOnlyTheFormattedOneMoves()
    {
        // The resolver has already collapsed the [defaults]-vs-explicit precedence into one
        // effective value per attribute, so Core's axis is simply "which format, if any" —
        // and a format set on one attribute never leaks to another.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.Nominal("a", 0, ["x"]),
            SpecFixtures.Nominal("b", 1, ["x"]) with { NameFormat = Format("{value}") },
        ]);

        Assert.Equal(["a-x", "x"], Names(Plan(spec, new SourceSchema(2))));
    }

    [Fact]
    public void Plan_WhenDisplayNameUnreferenced_ThenNamesAreUnchanged()
    {
        // D-119's neutrality row at the planner: a display_name no format references
        // changes no rendered name at all.
        var plain = Plan(Nominal(["b", "n"], format: null));
        var withDisplay = Plan(Nominal(["b", "n"], format: null) with { DisplayName = "Gill Size" });

        Assert.Equal(Names(plain), Names(withDisplay));
    }

    [Fact]
    public void Plan_WhenDisplayNameReferenced_ThenItRendersInsteadOfTheName()
    {
        var plan = Plan(Nominal(["b"], format: "{display_name}-{value}") with { DisplayName = "Gill Size" });

        Assert.Equal(["Gill Size-b"], Names(plan));
    }

    [Fact]
    public void Plan_WhenDisplayNameAbsent_ThenItDefaultsToTheAttributeName() =>
        // §10.1: display_name defaults to name, so {display_name} and {name} coincide.
        Assert.Equal(["gill-size|gill-size"], Names(Plan(Nominal(["b"], format: "{display_name}|{name}"))));

    // ---- Determinism ----

    [Fact]
    public void Plan_WhenPlannedTwice_ThenTheRenderedNamesAreIdentical()
    {
        // P-7: same spec ⇒ same rendered names, byte for byte.
        var first = Plan(Nominal(["b", "n"], format: "{display_name}::{scale_op}{value}", missing: MissingPolicy.AsAttribute));
        var second = Plan(Nominal(["b", "n"], format: "{display_name}::{scale_op}{value}", missing: MissingPolicy.AsAttribute));

        Assert.Equal(Names(first), Names(second));
    }

    [Fact]
    public void Plan_WhenFormatCollapsesTwoColumnsToOneName_ThenFormalAttributeNameCollision()
    {
        // §10.7's own warning: a format that discards the distinguishing part collides —
        // the plan-phase collision check still owns that condition, not the new one.
        var result = PlanResult(Nominal(["b", "n"], format: "{column}"));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameCollision);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
    }

    [Fact]
    public void Plan_WhenARealValueCollidesWithTheMissingColumn_ThenNameCollision()
    {
        // §10.7's named example: the category value "missing" colliding with the missing
        // column, reachable once a format renders both through the same shape.
        var result = PlanResult(Nominal(["missing"], format: "{value}", missing: MissingPolicy.AsAttribute));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameCollision);
    }

    // ---- The rendered-name backstop (D-117): empty, CR/LF, and the message ----

    [Fact]
    public void Plan_WhenFormatRendersAnEmptyName_ThenFormalAttributeNameInvalid()
    {
        // The EMPTY half is format-reachable only: "{value}" with a live empty label makes
        // every token render empty. (The default path always prefixes the column, so it
        // cannot produce an empty name — asserted below.)
        var result = PlanResult(Nominal(
            ["ok"], format: "{value}", labels: new Dictionary<string, string> { ["ok"] = "" }));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("gill-size", diagnostic.Location?.AttributeName);
        Assert.Equal(
            "Attribute 'gill-size' renders 1 invalid formal-attribute name(s) — empty or containing CR/LF: \"\" (§10.7).",
            diagnostic.Message);
    }

    [Fact]
    public void Plan_WhenDefaultPathRendersAnEmptyLabel_ThenTheNameIsStillValid()
    {
        // The corrected reachability note (D-117): under DEFAULT naming the same empty
        // label renders "gill-size-", which is non-empty and therefore valid. The backstop
        // must not over-reach into a name that is merely ugly.
        var result = PlanResult(Nominal(
            ["ok"], format: null, labels: new Dictionary<string, string> { ["ok"] = "" }));

        Assert.True(result.IsOk);
        Assert.Equal(["gill-size-"], Names(result.Value!));
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void Plan_WhenADomainValueInjectsCrLf_ThenTheDefaultPathAlsoFails(string value)
    {
        // The backstop is load-bearing beyond the M6 surface (D-117): CR/LF arriving
        // through a raw value reaches a rendered name on the DEFAULT path too — a route
        // that exists independently of formal_attribute_format, and one an RFC 4180
        // quoted field can legally produce.
        var result = PlanResult(Nominal([value], format: null));

        Assert.False(result.IsOk);
        Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
    }

    [Fact]
    public void Plan_WhenValueLabelInjectsCrLf_ThenItAlsoFails()
    {
        var result = PlanResult(Nominal(
            ["b"], format: null, labels: new Dictionary<string, string> { ["b"] = "br\noad" }));

        Assert.False(result.IsOk);
        Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
    }

    [Fact]
    public void Plan_WhenTheMissingColumnRendersInvalid_ThenItIsCaughtToo()
    {
        // The total override reaches the missing column, so the backstop must too.
        var result = PlanResult(Nominal(["b"], format: "{scale_op}", missing: MissingPolicy.AsAttribute));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
        Assert.Contains("renders 2 invalid", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WhenSeveralAttributesOffend_ThenOneDiagnosticEachInPlanOrder()
    {
        // D-116 granularity: one per affected LOGICAL attribute, in plan order — never one
        // per offending column and never a single spec-wide aggregate.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.Nominal("first", 0, ["a\nb"]),
            SpecFixtures.Nominal("clean", 1, ["ok"]),
            SpecFixtures.Nominal("second", 2, ["c\rd"]),
        ]);

        var result = PlanResult(spec, new SourceSchema(3));

        var invalid = result.Diagnostics.Where(d => d.Code == DiagnosticCode.FormalAttributeNameInvalid).ToArray();
        Assert.Equal(["first", "second"], invalid.Select(d => d.Location?.AttributeName ?? "").ToArray());
    }

    [Fact]
    public void Plan_WhenMoreThanThreeOffend_ThenThreeSamplesInRenderOrderThenTheTruncationTail()
    {
        // The pinned §3.5b representation: at most three samples, in RENDER order (the order
        // the planner emits that attribute's formal attributes — deliberately not sorted),
        // each quoted and escaped, with "(+N more)" only when truncated.
        var result = PlanResult(Nominal(
            ["a\nb", "ok", "c\rd", "e\nf", "g\rh"],
            format: "{value}",
            labels: new Dictionary<string, string> { ["ok"] = "" }));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
        Assert.Equal(
            "Attribute 'gill-size' renders 5 invalid formal-attribute name(s) — empty or containing CR/LF: " +
            "\"a\\nb\", \"\", \"c\\rd\" (+2 more) (§10.7).",
            diagnostic.Message);
    }

    [Fact]
    public void Plan_WhenExactlyThreeOffend_ThenNoTruncationTail()
    {
        // The boundary: the tail appears only ABOVE the limit, so three samples read clean.
        var result = PlanResult(Nominal(["a\nb", "c\nd", "e\nf"], format: null));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
        Assert.DoesNotContain("more)", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("\"gill-size-a\\nb\", \"gill-size-c\\nd\", \"gill-size-e\\nf\"", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WhenASampleContainsQuotesAndBackslashes_ThenExactlyFourEscapesApply()
    {
        // Exactly four escapes and no other transformation, applied per character so a
        // backslash cannot be double-escaped by a naive replace chain.
        var result = PlanResult(Nominal([@"a\b""c" + "\n"], format: "{value}"));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
        Assert.Contains(@"""a\\b\""c\n""", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WhenPlannedTwice_ThenTheInvalidNameMessageIsByteIdentical()
    {
        // P-7 over the message itself: the samples are drawn deterministically, so two runs
        // (and two machines) produce the same bytes.
        var first = PlanResult(Nominal(["a\nb", "c\rd", "e\nf", "g\rh"], format: "{value}"));
        var second = PlanResult(Nominal(["a\nb", "c\rd", "e\nf", "g\rh"], format: "{value}"));

        Assert.Equal(
            Assert.Single(first.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid).Message,
            Assert.Single(second.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid).Message);
    }

    [Fact]
    public void Plan_WhenANameIsInvalid_ThenTheWholeSharedPlanFailsAndYieldsNothing()
    {
        // §10.7: the plan is SHARED, so an invalid rendered name blocks .dat emission as
        // well as .cxt — even though .dat serializes no names. One attribute's bad name
        // therefore stops a plan whose other attributes are perfectly fine.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.Nominal("clean", 0, ["ok"]),
            SpecFixtures.Nominal("dirty", 1, ["a\nb"]),
        ]);

        var result = PlanResult(spec, new SourceSchema(2));

        Assert.False(result.IsOk);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Plan_WhenANameIsInvalidAndAnotherCollides_ThenBothConditionsReport()
    {
        // Independent conditions aggregate (P-14): registration still happens, so the
        // collision check sees exactly what a valid run would and both diagnostics surface.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.Nominal("dirty", 0, ["a\nb"]),
            SpecFixtures.Nominal("collide", 1, ["x", "y"]) with { NameFormat = Format("{column}") },
        ]);

        var result = PlanResult(spec, new SourceSchema(2));

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameCollision);
    }

    // ---- Helpers ----

    private static NameFormat Format(string text)
    {
        Assert.True(NameFormat.TryCreate(text, out var parsed, out var error), error);
        return parsed;
    }

    private static AttributeSpec Nominal(
        IReadOnlyList<string> domain,
        string? format,
        IReadOnlyDictionary<string, string>? labels = null,
        MissingPolicy missing = MissingPolicy.Skip) =>
        SpecFixtures.Nominal("gill-size", 0, domain, labels, missing) with
        {
            NameFormat = format is null ? null : Format(format),
        };

    private static AttributeSpec Dichotomic(
        string trueValue,
        IReadOnlyList<string> domain,
        string format,
        IReadOnlyDictionary<string, string>? labels = null,
        MissingPolicy missing = MissingPolicy.Skip) =>
        SpecFixtures.Dichotomic("bruises?", 0, trueValue, domain, missing) with
        {
            ValueLabels = labels ?? SpecFixtures.NoLabels,
            NameFormat = Format(format),
        };

    private static AttributeSpec NominalOverCuts(IReadOnlyList<double> cuts, string format) =>
        SpecFixtures.NumericCuts("age", 0, cuts, new NominalScale()) with { NameFormat = Format(format) };

    private static AttributeSpec OrdinalOverCuts(IReadOnlyList<double> cuts, string format) =>
        SpecFixtures.NumericCuts("age", 0, cuts, new OrdinalScale(OrdinalDirection.Le, DropTop: false, OrdinalBoundary.Strict, Order: null))
            with { NameFormat = Format(format) };

    private static string[] Names(ConversionPlan plan) =>
        plan.FormalAttributes.Select(f => f.RenderedName).ToArray();

    // Plans a hand-built spec the way production does (D-098): resolve the token (the
    // trust boundary), take the fully-declared calibrated state, then plan.
    private static Diagnosed<ConversionPlan> PlanResult(BedrockSpec spec, SourceSchema schema) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(SpecFixtures.Resolve(spec, schema)));

    private static Diagnosed<ConversionPlan> PlanResult(AttributeSpec attribute) =>
        PlanResult(new BedrockSpec(SpecFixtures.WideRowIndex(), [attribute]), new SourceSchema(1));

    private static ConversionPlan Plan(BedrockSpec spec, SourceSchema schema) => Ok(PlanResult(spec, schema));

    private static ConversionPlan Plan(AttributeSpec attribute) => Ok(PlanResult(attribute));

    private static ConversionPlan Ok(Diagnosed<ConversionPlan> result)
    {
        Assert.True(result.TryGetValue(out var plan), string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return plan!;
    }
}
