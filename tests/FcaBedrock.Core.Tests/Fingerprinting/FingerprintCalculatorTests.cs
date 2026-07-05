using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Fingerprinting;

public sealed class FingerprintCalculatorTests
{
    // --- The D-069 canonical-stability golden -------------------------------
    //
    // The literals below ARE the pinned encoding (D-077): value bins, an open
    // cut-bin run (first/interior/last), an ordinal threshold + `all`, a
    // dichotomic "", and an as_attribute missing column. A diff here is an
    // encoding change and requires an fp_format bump, not a test edit.

    private const string GoldenSchemaArray =
        """
        [{"bin":{"hi":30,"hi_open":false,"lo":null,"lo_open":true},"name":"age","op":"","scale":"nominal"},{"bin":{"hi":50,"hi_open":false,"lo":30,"lo_open":false},"name":"age","op":"","scale":"nominal"},{"bin":{"hi":null,"hi_open":true,"lo":50,"lo_open":false},"name":"age","op":"","scale":"nominal"},{"bin":"30","name":"risk","op":"<","scale":"ordinal"},{"bin":"all","name":"risk","op":"","scale":"ordinal"},{"bin":"M","name":"sex","op":"","scale":"nominal"},{"bin":"F","name":"sex","op":"","scale":"nominal"},{"bin":"missing","name":"sex","op":"","scale":"nominal"},{"bin":"","name":"employed","op":"","scale":"dichotomic"}]
        """;

    private const string GoldenShared =
        """
        {"attributes":[{"declared_domain":[],"discretizer":{"cuts":[30,50],"ends":"open","kind":"manual_cuts"},"missing_policy":"skip","name":"age","scale":{"kind":"nominal"},"source":{"column":0,"value_type":"number"},"unknown_value_policy":"warn"},{"declared_domain":[],"discretizer":{"cuts":[30],"ends":"open","kind":"manual_cuts"},"missing_policy":"skip","name":"risk","scale":{"boundary":"inclusive","direction":"le","drop_top":false,"kind":"ordinal"},"source":{"column":1,"value_type":"number"},"unknown_value_policy":"warn"},{"declared_domain":["M","F"],"discretizer":{"kind":"identity"},"missing_policy":"as_attribute","name":"sex","scale":{"kind":"nominal"},"source":{"column":2,"value_type":"string"},"unknown_value_policy":"warn"},{"declared_domain":["t","f"],"discretizer":{"kind":"identity"},"missing_policy":"skip","name":"employed","scale":{"kind":"dichotomic","true_value":"t"},"source":{"column":3,"value_type":"string"},"unknown_value_policy":"warn"}],"binding":{"delimiter":",","encoding":"utf-8","has_header":true,"locale":"invariant","missing_token":"?","object_key":{"mode":"row_index"},"quote_char":"\"","shape":"wide"}}
        """;

    private const string GoldenRenderedNames =
        """
        ["age-<30","age-[30, 50)","age->=50","risk-<30","risk-all","sex-M","sex-F","sex-missing","employed"]
        """;

    private static BedrockSpec GoldenSpec(
        MissingPolicy sexMissing = MissingPolicy.AsAttribute,
        IReadOnlyDictionary<string, string>? sexLabels = null) =>
        new(SpecFixtures.WideRowIndex(), [
            SpecFixtures.NumericCuts("age", 0, [30, 50], new NominalScale()),
            SpecFixtures.NumericCuts("risk", 1, [30], new OrdinalScale(OrdinalDirection.Le)),
            SpecFixtures.Nominal("sex", 2, ["M", "F"], sexLabels, sexMissing),
            SpecFixtures.Dichotomic("employed", 3, "t", ["t", "f"]),
        ]);

    private static ConversionPlan Plan(BedrockSpec spec, LabelStyle style = LabelStyle.Native)
    {
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(4), style).TryGetValue(out var plan));
        return plan!;
    }

    private static CxtFingerprintInputs NativeCxt(
        LabelStyle style = LabelStyle.Native, bool unicode = false,
        LineEnding lineEnding = LineEnding.Lf, bool trailingNewline = true) =>
        new(style, unicode, lineEnding, trailingNewline);

    private static DatFingerprintInputs NativeDat(
        int baseIndex = 1, LineEnding lineEnding = LineEnding.Lf,
        bool nonemptySpace = false, bool emptySpace = false) =>
        new(baseIndex, lineEnding, nonemptySpace, emptySpace);

    private static string Sha256Of(string canonical) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    [Fact]
    public void BuildSchemaJson_WhenGoldenPlan_ThenMatchesPinnedCanonicalBytes() =>
        Assert.Equal(
            "{\"attributes\":" + GoldenSchemaArray + ",\"fp_format\":1,\"kind\":\"schema\"}",
            FingerprintCalculator.BuildSchemaJson(Plan(GoldenSpec())));

    [Fact]
    public void BuildCxtOutputJson_WhenGoldenPlanNativeInputs_ThenMatchesPinnedCanonicalBytes() =>
        Assert.Equal(
            "{\"cxt\":{\"bin_label_unicode\":false,\"label_style\":\"native\",\"line_endings\":\"lf\",\"rendered_names\":"
                + GoldenRenderedNames + ",\"trailing_newline\":true},\"fp_format\":1,\"kind\":\"cxt_output\",\"schema\":"
                + GoldenSchemaArray + ",\"shared\":" + GoldenShared + "}",
            FingerprintCalculator.BuildCxtOutputJson(Plan(GoldenSpec()), GoldenSpec(), NativeCxt()));

    [Fact]
    public void BuildDatOutputJson_WhenGoldenPlanNativeInputs_ThenMatchesPinnedCanonicalBytes() =>
        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + GoldenSchemaArray + ",\"shared\":" + GoldenShared + "}",
            FingerprintCalculator.BuildDatOutputJson(Plan(GoldenSpec()), GoldenSpec(), NativeDat()));

    [Fact]
    public void ComputeSchemaFingerprint_WhenGoldenPlan_ThenSha256OfThePinnedBytes()
    {
        var fingerprint = FingerprintCalculator.ComputeSchemaFingerprint(Plan(GoldenSpec()));

        Assert.Equal(Sha256Of("{\"attributes\":" + GoldenSchemaArray + ",\"fp_format\":1,\"kind\":\"schema\"}"), fingerprint);
        Assert.Matches("^sha256:[0-9a-f]{64}$", fingerprint);
    }

    [Fact]
    public void ComputeSchemaFingerprint_WhenTrivialPlan_ThenMatchesHardcodedVector()
    {
        // Hash literal computed independently over the pinned canonical bytes
        // {"attributes":[{"bin":"x","name":"a","op":"","scale":"nominal"}],"fp_format":1,"kind":"schema"}
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("a", 0, ["x"])]);
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(
            "sha256:73104676228a99769adf96a9c468c54cc0b16381fcbc8f539425603675e93b4f",
            FingerprintCalculator.ComputeSchemaFingerprint(plan!));
    }

    [Fact]
    public void Compute_WhenCalledTwice_ThenAllThreeFingerprintsAreIdentical()
    {
        var spec = GoldenSpec();
        var plan = Plan(spec);

        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(plan),
            FingerprintCalculator.ComputeSchemaFingerprint(Plan(GoldenSpec())));
        Assert.Equal(
            FingerprintCalculator.ComputeCxtOutputFingerprint(plan, spec, NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(plan, spec, NativeCxt()));
        Assert.Equal(
            FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat()));
    }

    [Fact]
    public void ComputeSchemaFingerprint_WhenMissingPolicyFlips_ThenFingerprintChanges()
    {
        // The {column}-missing column is a planned column (D-068), so it is in
        // the hashed identity list — the D-035 "policies enter only through the
        // list" rule, exercised.
        var withMissing = FingerprintCalculator.ComputeSchemaFingerprint(Plan(GoldenSpec()));
        var without = FingerprintCalculator.ComputeSchemaFingerprint(Plan(GoldenSpec(sexMissing: MissingPolicy.Skip)));

        Assert.NotEqual(withMissing, without);
    }

    // --- Cut-bin structural encoding (review amendment 1) --------------------
    //
    // lo_open/hi_open mean "unbounded end" (null bound), never interval
    // inclusivity: <10 has hi_open:false because its high bound is finite,
    // even though 10 is exclusive (§11.2, D-077).

    [Fact]
    public void BuildSchemaJson_WhenOpenEnds_ThenFirstInteriorAndLastBinsEncodeUnboundedFlags()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.NumericCuts("v", 0, [10, 20, 30], new NominalScale()),
        ]);
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(
            "{\"attributes\":["
                + "{\"bin\":{\"hi\":10,\"hi_open\":false,\"lo\":null,\"lo_open\":true},\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"},"
                + "{\"bin\":{\"hi\":20,\"hi_open\":false,\"lo\":10,\"lo_open\":false},\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"},"
                + "{\"bin\":{\"hi\":30,\"hi_open\":false,\"lo\":20,\"lo_open\":false},\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"},"
                + "{\"bin\":{\"hi\":null,\"hi_open\":true,\"lo\":30,\"lo_open\":false},\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"}"
                + "],\"fp_format\":1,\"kind\":\"schema\"}",
            FingerprintCalculator.BuildSchemaJson(plan!));
    }

    [Fact]
    public void BuildSchemaJson_WhenClosedEnds_ThenOnlyInteriorAllBoundedBins()
    {
        var discretizer = ManualCutsDiscretizer.Create([10, 20, 30], BinEnds.Closed, CultureInfo.InvariantCulture).Value!;
        var attribute = new AttributeSpec(
            "v", new ColumnSource(0, SourceValueType.Number), Include: true, discretizer, new NominalScale(),
            DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attribute]);
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(
            "{\"attributes\":["
                + "{\"bin\":{\"hi\":20,\"hi_open\":false,\"lo\":10,\"lo_open\":false},\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"},"
                + "{\"bin\":{\"hi\":30,\"hi_open\":false,\"lo\":20,\"lo_open\":false},\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"}"
                + "],\"fp_format\":1,\"kind\":\"schema\"}",
            FingerprintCalculator.BuildSchemaJson(plan!));
    }

    [Fact]
    public void BuildSchemaJson_WhenOrderedCutsNominal_ThenTextCutBinsWithStringBounds()
    {
        var discretizer = OrderedCutsDiscretizer.Create(["low", "mid", "high"], ["mid"], BinEnds.Open).Value!;
        var attribute = new AttributeSpec(
            "edu", new ColumnSource(0, SourceValueType.String), Include: true, discretizer, new NominalScale(),
            DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attribute]);
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(
            "{\"attributes\":["
                + "{\"bin\":{\"hi\":\"mid\",\"hi_open\":false,\"lo\":null,\"lo_open\":true},\"name\":\"edu\",\"op\":\"\",\"scale\":\"nominal\"},"
                + "{\"bin\":{\"hi\":null,\"hi_open\":true,\"lo\":\"mid\",\"lo_open\":false},\"name\":\"edu\",\"op\":\"\",\"scale\":\"nominal\"}"
                + "],\"fp_format\":1,\"kind\":\"schema\"}",
            FingerprintCalculator.BuildSchemaJson(plan!));
    }

    // --- Scope split (D-035/D-051) -------------------------------------------

    [Fact]
    public void Compute_WhenValueLabelsAdded_ThenOnlyCxtFingerprintChanges()
    {
        var bare = GoldenSpec();
        var labelled = GoldenSpec(sexLabels: new Dictionary<string, string> { ["M"] = "male", ["F"] = "female" });
        var barePlan = Plan(bare);
        var labelledPlan = Plan(labelled);

        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(barePlan),
            FingerprintCalculator.ComputeSchemaFingerprint(labelledPlan));
        Assert.Equal(
            FingerprintCalculator.ComputeDatOutputFingerprint(barePlan, bare, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(labelledPlan, labelled, NativeDat()));
        Assert.NotEqual(
            FingerprintCalculator.ComputeCxtOutputFingerprint(barePlan, bare, NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(labelledPlan, labelled, NativeCxt()));
    }

    [Fact]
    public void Compute_WhenMatchedStylePairsDiffer_ThenOnlyCxtFingerprintChanges()
    {
        // Review amendment 2: the calculator's precondition is that inputs pair
        // the style the plan was produced with — Native/Native vs
        // V2Compat/V2Compat. Style is a cxt-only input (D-011/D-044).
        var spec = GoldenSpec();
        var native = Plan(spec, LabelStyle.Native);
        var v2 = Plan(spec, LabelStyle.V2Compat);

        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(native),
            FingerprintCalculator.ComputeSchemaFingerprint(v2));
        Assert.Equal(
            FingerprintCalculator.ComputeDatOutputFingerprint(native, spec, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(v2, spec, NativeDat()));
        Assert.NotEqual(
            FingerprintCalculator.ComputeCxtOutputFingerprint(native, spec, NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(v2, spec, NativeCxt(style: LabelStyle.V2Compat)));
    }

    [Fact]
    public void Compute_WhenCxtOnlyKnobsChange_ThenSchemaAndDatFingerprintsAreStable()
    {
        var spec = GoldenSpec();
        var plan = Plan(spec);
        var baselineCxt = FingerprintCalculator.ComputeCxtOutputFingerprint(plan, spec, NativeCxt());
        var baselineDat = FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat());

        Assert.NotEqual(baselineCxt, FingerprintCalculator.ComputeCxtOutputFingerprint(plan, spec, NativeCxt(trailingNewline: false)));
        Assert.NotEqual(baselineCxt, FingerprintCalculator.ComputeCxtOutputFingerprint(plan, spec, NativeCxt(unicode: true)));
        Assert.NotEqual(baselineCxt, FingerprintCalculator.ComputeCxtOutputFingerprint(plan, spec, NativeCxt(lineEnding: LineEnding.Crlf)));

        // None of those knobs perturbs schema or dat.
        Assert.Equal(baselineDat, FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat()));
        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(plan),
            FingerprintCalculator.ComputeSchemaFingerprint(plan));
    }

    [Fact]
    public void Compute_WhenDatOnlyKnobsChange_ThenOnlyDatFingerprintMoves()
    {
        var spec = GoldenSpec();
        var plan = Plan(spec);
        var baseline = FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat());

        Assert.NotEqual(baseline, FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat(baseIndex: 0)));
        Assert.NotEqual(baseline, FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat(lineEnding: LineEnding.Crlf)));
        Assert.NotEqual(baseline, FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat(nonemptySpace: true)));
        Assert.NotEqual(baseline, FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, NativeDat(emptySpace: true)));
    }

    [Fact]
    public void Compute_WhenSharedBindingKnobChanges_ThenBothOutputFingerprintsMoveButNotSchema()
    {
        var comma = GoldenSpec();
        var semicolon = new BedrockSpec(SpecFixtures.WideRowIndex(delimiter: ';'), [.. GoldenSpec().Attributes]);
        var commaPlan = Plan(comma);
        var semicolonPlan = Plan(semicolon);

        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(commaPlan),
            FingerprintCalculator.ComputeSchemaFingerprint(semicolonPlan));
        Assert.NotEqual(
            FingerprintCalculator.ComputeCxtOutputFingerprint(commaPlan, comma, NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(semicolonPlan, semicolon, NativeCxt()));
        Assert.NotEqual(
            FingerprintCalculator.ComputeDatOutputFingerprint(commaPlan, comma, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(semicolonPlan, semicolon, NativeDat()));
    }

    [Fact]
    public void Compute_WhenColumnSetChanges_ThenAllThreeFingerprintsMove()
    {
        var four = GoldenSpec();
        var three = new BedrockSpec(SpecFixtures.WideRowIndex(), [.. four.Attributes.Take(3)]);
        var fourPlan = Plan(four);
        Assert.True(ConversionPlanner.Plan(three, new SourceSchema(4)).TryGetValue(out var threePlan));

        Assert.NotEqual(
            FingerprintCalculator.ComputeSchemaFingerprint(fourPlan),
            FingerprintCalculator.ComputeSchemaFingerprint(threePlan!));
        Assert.NotEqual(
            FingerprintCalculator.ComputeCxtOutputFingerprint(fourPlan, four, NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(threePlan!, three, NativeCxt()));
        Assert.NotEqual(
            FingerprintCalculator.ComputeDatOutputFingerprint(fourPlan, four, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(threePlan!, three, NativeDat()));
    }

    [Fact]
    public void BuildCxtOutputJson_WhenAttributeExcludedAndDomainOnCuts_ThenSharedOmitsInertConfig()
    {
        // Excluded attributes contribute nothing to shared (they shape no output
        // in M2); a declared_domain on a cut discretizer is inert (§10.3) and
        // hashes as [] (D-077).
        var attribute = new AttributeSpec(
            "v", new ColumnSource(0, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create([10], BinEnds.Open, CultureInfo.InvariantCulture).Value!, new NominalScale(),
            DeclaredDomain: ["ignored"], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attribute, SpecFixtures.Excluded("parked", 1)]);
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(2)).TryGetValue(out var plan));

        var json = FingerprintCalculator.BuildCxtOutputJson(plan!, spec, NativeCxt());

        Assert.Contains("\"declared_domain\":[],\"discretizer\":{\"cuts\":[10]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("parked", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenReachableEnumsVary_ThenJsonUsesTheTomlVocabulary()
    {
        // The golden literals pin the default spellings; this pins the rest of
        // the M2-reachable vocabulary (D-077): ge/strict/closed/fail/include,
        // v2-compat, crlf. Plan-unreachable spellings (triple, column object
        // keys, keep/dedupe) join the goldens when their milestones land.
        var cuts = new AttributeSpec(
            "v", new ColumnSource(0, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create([10, 20], BinEnds.Closed, CultureInfo.InvariantCulture).Value!,
            new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Strict),
            DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Fail);
        var included = new AttributeSpec(
            "w", new ColumnSource(1, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(),
            DeclaredDomain: ["x"], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Include);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [cuts, included]);
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(2), LabelStyle.V2Compat).TryGetValue(out var plan));

        var json = FingerprintCalculator.BuildCxtOutputJson(
            plan!, spec, new CxtFingerprintInputs(LabelStyle.V2Compat, BinLabelUnicode: false, LineEnding.Crlf, TrailingNewline: true));

        Assert.Contains("\"label_style\":\"v2-compat\"", json, StringComparison.Ordinal);
        Assert.Contains("\"line_endings\":\"crlf\"", json, StringComparison.Ordinal);
        Assert.Contains("\"direction\":\"ge\"", json, StringComparison.Ordinal);
        Assert.Contains("\"boundary\":\"strict\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ends\":\"closed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"unknown_value_policy\":\"fail\"", json, StringComparison.Ordinal);
        Assert.Contains("\"unknown_value_policy\":\"include\"", json, StringComparison.Ordinal);
    }

    // --- Value-bin ordinal (D-081): the encoder is unchanged; these pin its output ---

    private static BedrockSpec ValueBinOrdinalSpec(IReadOnlyList<string> order) =>
        new(SpecFixtures.WideRowIndex(),
            [SpecFixtures.OrdinalValueBins("edu", 0, ["a", "b", "c"],
                new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, order))]);

    [Fact]
    public void BuildSchemaJson_WhenValueBinOrdinal_ThenColumnsCarryOpAndRawValueBinInPlanOrder()
    {
        // The schema identity of a value-bin ordinal column is {raw value, op}, in
        // plan (order) sequence — no encoder change was needed (D-081).
        Assert.True(ConversionPlanner.Plan(ValueBinOrdinalSpec(["a", "b", "c"]), new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(
            "{\"attributes\":["
                + "{\"bin\":\"a\",\"name\":\"edu\",\"op\":\">=\",\"scale\":\"ordinal\"},"
                + "{\"bin\":\"b\",\"name\":\"edu\",\"op\":\">=\",\"scale\":\"ordinal\"},"
                + "{\"bin\":\"c\",\"name\":\"edu\",\"op\":\">=\",\"scale\":\"ordinal\"}"
                + "],\"fp_format\":1,\"kind\":\"schema\"}",
            FingerprintCalculator.BuildSchemaJson(plan!));
    }

    [Fact]
    public void BuildCxtOutputJson_WhenValueBinOrdinal_ThenSharedScaleEncodesTheOrderArray()
    {
        // AppendScale already encodes the ordinal order/boundary/direction/drop_top;
        // for a value-bin ordinal the order array is the live config in the shared JSON.
        Assert.True(ConversionPlanner.Plan(ValueBinOrdinalSpec(["a", "b", "c"]), new SourceSchema(1)).TryGetValue(out var plan));

        var json = FingerprintCalculator.BuildCxtOutputJson(plan!, ValueBinOrdinalSpec(["a", "b", "c"]), NativeCxt());

        Assert.Contains(
            "\"scale\":{\"boundary\":\"inclusive\",\"direction\":\"ge\",\"drop_top\":false,\"kind\":\"ordinal\",\"order\":[\"a\",\"b\",\"c\"]}",
            json, StringComparison.Ordinal);
        Assert.Contains("\"declared_domain\":[\"a\",\"b\",\"c\"]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeSchemaFingerprint_WhenOrderPermuted_ThenSchemaFingerprintChanges()
    {
        // Two permutations of the same domain change the column identities/sequence,
        // so the schema fingerprint moves — the order is not inert (D-081).
        Assert.True(ConversionPlanner.Plan(ValueBinOrdinalSpec(["a", "b", "c"]), new SourceSchema(1)).TryGetValue(out var abc));
        Assert.True(ConversionPlanner.Plan(ValueBinOrdinalSpec(["a", "c", "b"]), new SourceSchema(1)).TryGetValue(out var acb));

        Assert.NotEqual(
            FingerprintCalculator.ComputeSchemaFingerprint(abc!),
            FingerprintCalculator.ComputeSchemaFingerprint(acb!));
    }

    // --- Canonical JSON byte rules (D-077 pins 2-4) ---------------------------

    [Fact]
    public void CanonicalJson_WhenStringHasEscapes_ThenOneDocumentedRuleApplies()
    {
        var builder = new StringBuilder();
        CanonicalJson.AppendString(builder, "a\"b\\c\bd\te\nf\ffg\rh" + (char)0x01 + "i" + (char)0x1F + "j");

        Assert.Equal("\"a\\\"b\\\\c\\bd\\te\\nf\\ffg\\rh\\u0001i\\u001fj\"", builder.ToString());
    }

    [Fact]
    public void CanonicalJson_WhenStringHasNonAscii_ThenUtf8Passthrough()
    {
        var builder = new StringBuilder();
        CanonicalJson.AppendString(builder, "é≥∞日本");

        Assert.Equal("\"é≥∞日本\"", builder.ToString());
    }

    // 30.0 and 3e1 are the same double, so the 30/30.0/3e1 spelling collapse is
    // locked at the TOML layer (SpecFingerprintsTests); here the pinned fact is
    // that an integral double renders bare.
    [Theory]
    [InlineData(30.0, "30")]
    [InlineData(34.25, "34.25")]
    [InlineData(0.1, "0.1")]
    [InlineData(1e-5, "1E-05")]
    [InlineData(-2.5, "-2.5")]
    public void CanonicalJson_WhenDouble_ThenInvariantShortestRoundTrippable(double value, string expected)
    {
        var builder = new StringBuilder();
        CanonicalJson.AppendNumber(builder, value);

        Assert.Equal(expected, builder.ToString());
    }

    [Fact]
    public void CanonicalJson_WhenNonFiniteDouble_ThenThrows()
    {
        var builder = new StringBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalJson.AppendNumber(builder, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalJson.AppendNumber(builder, double.PositiveInfinity));
    }
}
