using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

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

    // Plans a hand-built spec + schema through the M4 pipeline (resolve token → fully-declared
    // calibrated state → plan), the way production does (D-098). The fixtures are fully-declared.
    private static ResolvedSpec Resolve(BedrockSpec spec, SourceSchema schema) =>
        ResolvedSpec.Create(
            spec, schema,
            SourceReadSettings.Create(
                spec.Binding.Shape, spec.Binding.Encoding, spec.Binding.Delimiter, spec.Binding.QuoteChar,
                spec.Binding.HasHeader, spec.Binding.MissingToken, spec.Binding.Ordering),
            []);

    private static Diagnosed<ConversionPlan> Diag(BedrockSpec spec, SourceSchema schema, LabelStyle style = LabelStyle.Native) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(Resolve(spec, schema)), style);

    private static ConversionPlan Plan(BedrockSpec spec, LabelStyle style = LabelStyle.Native)
    {
        Assert.True(Diag(spec, new SourceSchema(4), style).TryGetValue(out var plan));
        return plan!;
    }

    // The public output-fingerprint API drops the spec arg (it now reads plan.Calibrated.Spec,
    // D-098); these thin shims keep the existing (plan, spec, inputs) call shape in the tests, so
    // the pinned bytes/hashes are hashed identically (plan.Calibrated.Spec is the spec's snapshot).
    private static string ComputeCxt(ConversionPlan plan, BedrockSpec spec, CxtFingerprintInputs inputs)
    {
        _ = spec;
        return FingerprintCalculator.ComputeCxtOutputFingerprint(plan, inputs);
    }

    private static string ComputeDat(ConversionPlan plan, BedrockSpec spec, DatFingerprintInputs inputs)
    {
        _ = spec;
        return FingerprintCalculator.ComputeDatOutputFingerprint(plan, inputs);
    }

    private static CxtFingerprintInputs NativeCxt(
        LabelStyle style = LabelStyle.Native, bool unicode = false,
        LineEnding lineEnding = LineEnding.Lf, bool trailingNewline = true) =>
        new(style, unicode, lineEnding, trailingNewline);

    private static DatFingerprintInputs NativeDat(
        int baseIndex = 1, LineEnding lineEnding = LineEnding.Lf,
        bool nonemptySpace = false, bool emptySpace = false, bool trailingNewline = true) =>
        new(baseIndex, lineEnding, nonemptySpace, emptySpace) { TrailingNewline = trailingNewline };

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
    public void BuildDatOutputJson_WhenTrailingNewlineDisabled_ThenEncodesTrailingNewlineFalse() =>
        // D-087: the key is emitted (alphabetically last) ONLY when disabled; at the default
        // it is omitted, which is what keeps the pinned bytes above byte-identical.
        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false,\"trailing_newline\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + GoldenSchemaArray + ",\"shared\":" + GoldenShared + "}",
            FingerprintCalculator.BuildDatOutputJson(Plan(GoldenSpec()), GoldenSpec(), NativeDat(trailingNewline: false)));

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
        Assert.True(Diag(spec, new SourceSchema(1)).TryGetValue(out var plan));

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
            ComputeCxt(plan, spec, NativeCxt()),
            ComputeCxt(plan, spec, NativeCxt()));
        Assert.Equal(
            ComputeDat(plan, spec, NativeDat()),
            ComputeDat(plan, spec, NativeDat()));
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
        Assert.True(Diag(spec, new SourceSchema(1)).TryGetValue(out var plan));

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
        Assert.True(Diag(spec, new SourceSchema(1)).TryGetValue(out var plan));

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
        Assert.True(Diag(spec, new SourceSchema(1)).TryGetValue(out var plan));

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
            ComputeDat(barePlan, bare, NativeDat()),
            ComputeDat(labelledPlan, labelled, NativeDat()));
        Assert.NotEqual(
            ComputeCxt(barePlan, bare, NativeCxt()),
            ComputeCxt(labelledPlan, labelled, NativeCxt()));
    }

    [Fact]
    public void ComputeCxtOutputFingerprint_WhenInputStyleDiffersFromPlan_ThenThrows()
    {
        // §14/D-098: the cxt output fingerprint validates that the inputs' label style pairs with the
        // style the plan's rendered names were baked with, rather than hashing an inconsistent combo.
        var plan = Plan(GoldenSpec()); // Native

        Assert.Throws<ArgumentException>(() =>
            FingerprintCalculator.ComputeCxtOutputFingerprint(plan, NativeCxt(style: LabelStyle.V2Compat)));
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
            ComputeDat(native, spec, NativeDat()),
            ComputeDat(v2, spec, NativeDat()));
        Assert.NotEqual(
            ComputeCxt(native, spec, NativeCxt()),
            ComputeCxt(v2, spec, NativeCxt(style: LabelStyle.V2Compat)));
    }

    [Fact]
    public void Compute_WhenCxtOnlyKnobsChange_ThenSchemaAndDatFingerprintsAreStable()
    {
        var spec = GoldenSpec();
        var plan = Plan(spec);
        var baselineCxt = ComputeCxt(plan, spec, NativeCxt());
        var baselineDat = ComputeDat(plan, spec, NativeDat());

        Assert.NotEqual(baselineCxt, ComputeCxt(plan, spec, NativeCxt(trailingNewline: false)));
        Assert.NotEqual(baselineCxt, ComputeCxt(plan, spec, NativeCxt(unicode: true)));
        Assert.NotEqual(baselineCxt, ComputeCxt(plan, spec, NativeCxt(lineEnding: LineEnding.Crlf)));

        // None of those knobs perturbs schema or dat.
        Assert.Equal(baselineDat, ComputeDat(plan, spec, NativeDat()));
        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(plan),
            FingerprintCalculator.ComputeSchemaFingerprint(plan));
    }

    [Fact]
    public void Compute_WhenDatOnlyKnobsChange_ThenOnlyDatFingerprintMoves()
    {
        var spec = GoldenSpec();
        var plan = Plan(spec);
        var baseline = ComputeDat(plan, spec, NativeDat());

        Assert.NotEqual(baseline, ComputeDat(plan, spec, NativeDat(baseIndex: 0)));
        Assert.NotEqual(baseline, ComputeDat(plan, spec, NativeDat(lineEnding: LineEnding.Crlf)));
        Assert.NotEqual(baseline, ComputeDat(plan, spec, NativeDat(nonemptySpace: true)));
        Assert.NotEqual(baseline, ComputeDat(plan, spec, NativeDat(emptySpace: true)));
        Assert.NotEqual(baseline, ComputeDat(plan, spec, NativeDat(trailingNewline: false)));
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
            ComputeCxt(commaPlan, comma, NativeCxt()),
            ComputeCxt(semicolonPlan, semicolon, NativeCxt()));
        Assert.NotEqual(
            ComputeDat(commaPlan, comma, NativeDat()),
            ComputeDat(semicolonPlan, semicolon, NativeDat()));
    }

    [Fact]
    public void Compute_WhenColumnSetChanges_ThenAllThreeFingerprintsMove()
    {
        var four = GoldenSpec();
        var three = new BedrockSpec(SpecFixtures.WideRowIndex(), [.. four.Attributes.Take(3)]);
        var fourPlan = Plan(four);
        Assert.True(Diag(three, new SourceSchema(4)).TryGetValue(out var threePlan));

        Assert.NotEqual(
            FingerprintCalculator.ComputeSchemaFingerprint(fourPlan),
            FingerprintCalculator.ComputeSchemaFingerprint(threePlan!));
        Assert.NotEqual(
            ComputeCxt(fourPlan, four, NativeCxt()),
            ComputeCxt(threePlan!, three, NativeCxt()));
        Assert.NotEqual(
            ComputeDat(fourPlan, four, NativeDat()),
            ComputeDat(threePlan!, three, NativeDat()));
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
        Assert.True(Diag(spec, new SourceSchema(2)).TryGetValue(out var plan));

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
        // v2-compat, crlf. Triple and wide column keys — fail/keep and now dedupe —
        // are all plan-reachable at M3 (Slice F landed dedupe).
        var cuts = new AttributeSpec(
            "v", new ColumnSource(0, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create([10, 20], BinEnds.Closed, CultureInfo.InvariantCulture).Value!,
            new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Strict),
            DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Fail);
        var included = new AttributeSpec(
            "w", new ColumnSource(1, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(),
            DeclaredDomain: ["x"], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Include);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [cuts, included]);
        // The include attribute is data-dependent (D-098): it takes calibrated state with its
        // IncludeAdditions marker (zero additions here) rather than the fully-declared fast path.
        Assert.True(CalibratedSpec.Create(Resolve(spec, new SourceSchema(2)), [new IncludeAdditions("w", [])])
            .TryGetValue(out var calibrated));
        Assert.True(ConversionPlanner.Plan(calibrated, LabelStyle.V2Compat).TryGetValue(out var plan));

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

    // --- Wide column object key (D-083): plan-reachable at Slice E; index + policy are byte-affecting ---

    [Fact]
    public void BuildCxtOutputJson_WhenWideColumnKeepKey_ThenEncodesColumnIndexAndPolicy()
    {
        var spec = WideColumnKeySpec(0, DuplicateObjectPolicy.Keep);
        Assert.True(Diag(spec, new SourceSchema(2)).TryGetValue(out var plan));

        var json = FingerprintCalculator.BuildCxtOutputJson(plan, spec, NativeCxt());

        Assert.Contains(
            "\"object_key\":{\"column\":0,\"duplicate_object_policy\":\"keep\",\"mode\":\"column\"}",
            json, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeOutputFingerprint_WhenWideKeyPolicyOrIndexVaries_ThenBothFormatsMove()
    {
        // The object key rides in the SHARED binding payload, so both .cxt and .dat fingerprints move
        // when the key column index or policy changes (keep #N names are byte-affecting) — asserted per
        // format, since a tuple NotEqual would pass on either alone.
        var keep0 = WideKeyOutputFingerprints(0, DuplicateObjectPolicy.Keep);
        var fail0 = WideKeyOutputFingerprints(0, DuplicateObjectPolicy.Fail);
        var keep1 = WideKeyOutputFingerprints(1, DuplicateObjectPolicy.Keep);

        Assert.NotEqual(keep0.Cxt, fail0.Cxt); // policy
        Assert.NotEqual(keep0.Dat, fail0.Dat);
        Assert.NotEqual(keep0.Cxt, keep1.Cxt); // key column index
        Assert.NotEqual(keep0.Dat, keep1.Dat);
    }

    private static (string Cxt, string Dat) WideKeyOutputFingerprints(int keyIndex, DuplicateObjectPolicy policy)
    {
        var spec = WideColumnKeySpec(keyIndex, policy);
        Assert.True(Diag(spec, new SourceSchema(2)).TryGetValue(out var plan));
        var cxt = ComputeCxt(plan, spec, NativeCxt());
        var dat = ComputeDat(plan, spec, NativeDat());
        return (cxt, dat);
    }

    private static BedrockSpec WideColumnKeySpec(int keyIndex, DuplicateObjectPolicy policy) =>
        new(new Binding(SourceShape.Wide, "utf-8", ',', '"', HasHeader: true, "invariant", "?", new ColumnObjectKey(keyIndex, policy)),
            [SpecFixtures.Nominal("g", 1, ["b"])]);

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
        Assert.True(Diag(ValueBinOrdinalSpec(["a", "b", "c"]), new SourceSchema(1)).TryGetValue(out var plan));

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
        Assert.True(Diag(ValueBinOrdinalSpec(["a", "b", "c"]), new SourceSchema(1)).TryGetValue(out var plan));

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
        Assert.True(Diag(ValueBinOrdinalSpec(["a", "b", "c"]), new SourceSchema(1)).TryGetValue(out var abc));
        Assert.True(Diag(ValueBinOrdinalSpec(["a", "c", "b"]), new SourceSchema(1)).TryGetValue(out var acb));

        Assert.NotEqual(
            FingerprintCalculator.ComputeSchemaFingerprint(abc!),
            FingerprintCalculator.ComputeSchemaFingerprint(acb!));
    }

    // --- free_per_value discretizer encoding (D-094 golden-lock, M4 Slice B) --
    //
    // The M4 per-kind canonical bytes and their SHA-256 vectors are golden-locked
    // BEFORE the first M4 fingerprint is produced (§14/D-094). free_per_value adds
    // no config beyond the kind; its numeric identity rides on source.value_type and
    // its bins are the effective (canonical numeric) domain.

    private static BedrockSpec NumericFreePerValueSpec() =>
        new(SpecFixtures.WideRowIndex(), [
            SpecFixtures.FreePerValue("v", 0, SourceValueType.Number, ["90", "5"], new NominalScale()),
        ]);

    [Fact]
    public void BuildSchemaJson_WhenNumericFreePerValue_ThenCanonicalNumericBinsInDomainOrder() =>
        // The value-bin identities are the canonical numeric keys, in declared_domain order (§17 r3).
        Assert.Equal(
            "{\"attributes\":["
                + "{\"bin\":\"90\",\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"},"
                + "{\"bin\":\"5\",\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"}"
                + "],\"fp_format\":1,\"kind\":\"schema\"}",
            FingerprintCalculator.BuildSchemaJson(Plan(NumericFreePerValueSpec())));

    [Fact]
    public void ComputeSchemaFingerprint_WhenNumericFreePerValue_ThenMatchesHardcodedVector() =>
        // Hash literal computed independently over the pinned canonical bytes above (D-094 lock).
        Assert.Equal(
            "sha256:19f9b2826c4bb3b8453ca7b24d3b34e16b381886b0e33408f0b7ccf60e2fa5f3",
            FingerprintCalculator.ComputeSchemaFingerprint(Plan(NumericFreePerValueSpec())));

    [Fact]
    public void BuildCxtOutputJson_WhenFreePerValue_ThenDiscretizerEncodesKindOnlyOverEffectiveDomain()
    {
        var json = FingerprintCalculator.BuildCxtOutputJson(Plan(NumericFreePerValueSpec()), NumericFreePerValueSpec(), NativeCxt());

        // The discretizer sub-object is kind-only; the effective (canonical) numeric domain rides in
        // declared_domain, and source.value_type carries the numeric-vs-string identity (D-094).
        Assert.Contains(
            "\"declared_domain\":[\"90\",\"5\"],\"discretizer\":{\"kind\":\"free_per_value\"},",
            json, StringComparison.Ordinal);
        Assert.Contains("\"source\":{\"column\":0,\"value_type\":\"number\"}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenStringFreePerValue_ThenSameKindOnlyEncodingButStringSource()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.FreePerValue("g", 0, SourceValueType.String, ["b", "n"], new NominalScale()),
        ]);
        Assert.True(Diag(spec, new SourceSchema(1)).TryGetValue(out var plan));

        var json = FingerprintCalculator.BuildCxtOutputJson(plan!, spec, NativeCxt());

        Assert.Contains(
            "\"declared_domain\":[\"b\",\"n\"],\"discretizer\":{\"kind\":\"free_per_value\"},",
            json, StringComparison.Ordinal);
        Assert.Contains("\"source\":{\"column\":0,\"value_type\":\"string\"}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDatOutputJson_WhenNumericFreePerValue_ThenPinnedCanonicalBytesContainDiscretizerObject() =>
        // The {"kind":"free_per_value"} discretizer object rides in `shared`, so it feeds BOTH output
        // fingerprints (not schema_fingerprint). The complete dat output canonical bytes are pinned
        // here — the exact-byte lock the schema fingerprint cannot provide (§14/D-094).
        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + "[{\"bin\":\"90\",\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"},{\"bin\":\"5\",\"name\":\"v\",\"op\":\"\",\"scale\":\"nominal\"}],"
                + "\"shared\":{\"attributes\":[{\"declared_domain\":[\"90\",\"5\"],\"discretizer\":{\"kind\":\"free_per_value\"},"
                + "\"missing_policy\":\"skip\",\"name\":\"v\",\"scale\":{\"kind\":\"nominal\"},\"source\":{\"column\":0,\"value_type\":\"number\"},"
                + "\"unknown_value_policy\":\"warn\"}],\"binding\":{\"delimiter\":\",\",\"encoding\":\"utf-8\",\"has_header\":true,"
                + "\"locale\":\"invariant\",\"missing_token\":\"?\",\"object_key\":{\"mode\":\"row_index\"},\"quote_char\":\"\\\"\",\"shape\":\"wide\"}}}",
            FingerprintCalculator.BuildDatOutputJson(Plan(NumericFreePerValueSpec()), NumericFreePerValueSpec(), NativeDat()));

    [Fact]
    public void ComputeDatOutputFingerprint_WhenNumericFreePerValue_ThenMatchesHardcodedVector() =>
        // SHA-256 computed independently over the pinned dat bytes above — a change to the
        // free_per_value discretizer encoding moves this hash (D-094/D-101 output-encoding lock).
        Assert.Equal(
            "sha256:247648dc27afaf287d116650c01d71cf8d31860ea485069ef1d530f2d21bde88",
            ComputeDat(Plan(NumericFreePerValueSpec()), NumericFreePerValueSpec(), NativeDat()));

    // --- equal_width discretizer encoding (D-094 golden-lock, M4 Slice C) -----
    //
    // The M4 per-kind canonical bytes and their SHA-256 vectors are golden-locked BEFORE the
    // first M4 fingerprint is produced (§14/D-094). equal_width encodes its AUTHORED config —
    // bins/range/precision, plus vmin/vmax only under range = "manual". Its RESOLVED cuts are
    // deliberately absent from the sub-object: they already ride as `bin` objects in the schema
    // array (asserted below), so re-encoding them would be a second source of truth.
    //
    // The two specs below are the D-094 auto-vs-frozen pair in miniature: identical effective
    // cuts [25, 50, 75], hence an identical schema array — but different authored discretizer
    // objects, hence different output fingerprints. That asymmetry is the contract, not a bug.

    private const string EqualWidthSchemaArray =
        """
        [{"bin":{"hi":25,"hi_open":false,"lo":null,"lo_open":true},"name":"score","op":"","scale":"nominal"},{"bin":{"hi":50,"hi_open":false,"lo":25,"lo_open":false},"name":"score","op":"","scale":"nominal"},{"bin":{"hi":75,"hi_open":false,"lo":50,"lo_open":false},"name":"score","op":"","scale":"nominal"},{"bin":{"hi":null,"hi_open":true,"lo":75,"lo_open":false},"name":"score","op":"","scale":"nominal"}]
        """;

    // range = "manual", precision = { round_to = 1 }: over [0, 100] with 4 bins the cuts are
    // 25/50/75 both before and after rounding.
    private static BedrockSpec EqualWidthManualSpec() =>
        new(SpecFixtures.WideRowIndex(), [
            SpecFixtures.EqualWidthManual("score", 0, 4, 0, 100, new NominalScale(), RoundToPrecision.Create(1)),
        ]);

    // range = "min_max", precision = "exact": the calibrator's derived cuts, substituted into the
    // executable discretizer by CalibratedSpec.Create — the only way a data-range equal_width
    // becomes plannable (D-093).
    private static ConversionPlan EqualWidthMinMaxPlan()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.EqualWidthPending("score", 0, 4, new NominalScale()),
        ]);
        var calibrated = CalibratedSpec.Create(
            Resolve(spec, new SourceSchema(1)), [new CalibratedCuts("score", [25, 50, 75])]);
        Assert.True(calibrated.TryGetValue(out var state));
        Assert.True(ConversionPlanner.Plan(state!).TryGetValue(out var plan));
        return plan!;
    }

    [Fact]
    public void BuildCxtOutputJson_WhenEqualWidthManual_ThenDiscretizerEncodesAuthoredConfigWithBounds() =>
        // The pinned manual encoding (§14/D-094). Keys sort ordinal: bins < kind < precision <
        // range < vmax < vmin; precision mirrors its TOML object form.
        Assert.Contains(
            "\"discretizer\":{\"bins\":4,\"kind\":\"equal_width\",\"precision\":{\"round_to\":1},\"range\":\"manual\",\"vmax\":100,\"vmin\":0},",
            FingerprintCalculator.BuildCxtOutputJson(Plan(EqualWidthManualSpec()), EqualWidthManualSpec(), NativeCxt()),
            StringComparison.Ordinal);

    [Fact]
    public void BuildCxtOutputJson_WhenEqualWidthMinMax_ThenDiscretizerOmitsBoundsAndSpellsExactPrecision()
    {
        var plan = EqualWidthMinMaxPlan();
        var json = FingerprintCalculator.BuildCxtOutputJson(plan, plan.Calibrated.Spec, NativeCxt());

        // The pinned data-derived encoding: vmin/vmax are omitted (not authored under a data
        // range), and "exact" precision is the bare string form.
        Assert.Contains(
            "\"discretizer\":{\"bins\":4,\"kind\":\"equal_width\",\"precision\":\"exact\",\"range\":\"min_max\"},",
            json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"vmin\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"vmax\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenEqualWidth_ThenResolvedCutsAreNotInsideTheDiscretizerObject()
    {
        // D-094: the resolved cuts appear ONLY as schema bins. A "cuts" key inside the
        // equal_width sub-object would be the redundant second source of truth the rule forbids
        // (manual_cuts legitimately has one — hence the scoped assertion).
        var plan = EqualWidthMinMaxPlan();
        var json = FingerprintCalculator.BuildCxtOutputJson(plan, plan.Calibrated.Spec, NativeCxt());
        var discretizer = json[json.IndexOf("\"discretizer\":", StringComparison.Ordinal)..];

        Assert.DoesNotContain("\"cuts\"", discretizer[..discretizer.IndexOf('}', StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("\"schema\":" + EqualWidthSchemaArray, json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSchemaJson_WhenEqualWidthManual_ThenOpenEndedCutBinsInPlanOrder() =>
        Assert.Equal(
            "{\"attributes\":" + EqualWidthSchemaArray + ",\"fp_format\":1,\"kind\":\"schema\"}",
            FingerprintCalculator.BuildSchemaJson(Plan(EqualWidthManualSpec())));

    [Fact]
    public void ComputeSchemaFingerprint_WhenEqualWidthManual_ThenMatchesHardcodedVector() =>
        // Hash literal computed independently (an external SHA-256 of the hand-authored bytes
        // above), never copied from this encoder's output — that is what makes it a lock (D-094).
        Assert.Equal(
            "sha256:cd6f9e39b9c79d7a785870401624573c9debf76b95ee6980dd5cda73fc631459",
            FingerprintCalculator.ComputeSchemaFingerprint(Plan(EqualWidthManualSpec())));

    [Fact]
    public void BuildDatOutputJson_WhenEqualWidthManual_ThenCompletePinnedCanonicalBytes() =>
        // The complete dat output bytes: the exact-byte lock the schema fingerprint cannot give,
        // since the discretizer sub-object rides in `shared` and feeds only the output hashes.
        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + EqualWidthSchemaArray
                + ",\"shared\":{\"attributes\":[{\"declared_domain\":[],\"discretizer\":{\"bins\":4,\"kind\":\"equal_width\","
                + "\"precision\":{\"round_to\":1},\"range\":\"manual\",\"vmax\":100,\"vmin\":0},\"missing_policy\":\"skip\","
                + "\"name\":\"score\",\"scale\":{\"kind\":\"nominal\"},\"source\":{\"column\":0,\"value_type\":\"number\"},"
                + "\"unknown_value_policy\":\"warn\"}],\"binding\":{\"delimiter\":\",\",\"encoding\":\"utf-8\",\"has_header\":true,"
                + "\"locale\":\"invariant\",\"missing_token\":\"?\",\"object_key\":{\"mode\":\"row_index\"},\"quote_char\":\"\\\"\",\"shape\":\"wide\"}}}",
            FingerprintCalculator.BuildDatOutputJson(Plan(EqualWidthManualSpec()), EqualWidthManualSpec(), NativeDat()));

    [Fact]
    public void ComputeDatOutputFingerprint_WhenEqualWidthManual_ThenMatchesHardcodedVector() =>
        // SHA-256 computed independently over the pinned dat bytes above (D-094 lock).
        Assert.Equal(
            "sha256:fe3995b8ffe851732d546d8ce61e46d16d8f893ac3a4f1239f99a0f6cddf37f5",
            ComputeDat(Plan(EqualWidthManualSpec()), EqualWidthManualSpec(), NativeDat()));

    [Fact]
    public void BuildDatOutputJson_WhenEqualWidthMinMax_ThenCompletePinnedCanonicalBytes()
    {
        // The data-derived form's complete bytes, hand-authored: identical to the manual pin
        // above except for the authored discretizer sub-object — the one documented difference.
        var plan = EqualWidthMinMaxPlan();

        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + EqualWidthSchemaArray
                + ",\"shared\":{\"attributes\":[{\"declared_domain\":[],\"discretizer\":{\"bins\":4,\"kind\":\"equal_width\","
                + "\"precision\":\"exact\",\"range\":\"min_max\"},\"missing_policy\":\"skip\","
                + "\"name\":\"score\",\"scale\":{\"kind\":\"nominal\"},\"source\":{\"column\":0,\"value_type\":\"number\"},"
                + "\"unknown_value_policy\":\"warn\"}],\"binding\":{\"delimiter\":\",\",\"encoding\":\"utf-8\",\"has_header\":true,"
                + "\"locale\":\"invariant\",\"missing_token\":\"?\",\"object_key\":{\"mode\":\"row_index\"},\"quote_char\":\"\\\"\",\"shape\":\"wide\"}}}",
            FingerprintCalculator.BuildDatOutputJson(plan, plan.Calibrated.Spec, NativeDat()));
    }

    [Fact]
    public void ComputeDatOutputFingerprint_WhenEqualWidthMinMax_ThenMatchesHardcodedVector() =>
        // The data-derived twin's own independently-computed vector: same schema bins, different
        // authored discretizer object, therefore a different output hash (D-094).
        Assert.Equal(
            "sha256:c04cb8f7547f76ef2c6584047e97f58551658b3ce660875473906c751866a034",
            FingerprintCalculator.ComputeDatOutputFingerprint(EqualWidthMinMaxPlan(), NativeDat()));

    [Fact]
    public void ComputeFingerprints_WhenEqualWidthAutoVsFrozenManualCuts_ThenSchemaEqualAndOutputDiffersOnlyInTheDiscretizer()
    {
        // The D-094 one-directional guarantee, isolated: the frozen form (manual_cuts over the
        // calibrated cuts, open ends) and the auto form share every effective bin — so the schema
        // fingerprints match — while the output fingerprints differ, and differ ONLY through the
        // authored discretizer sub-object.
        var frozen = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.NumericCuts("score", 0, [25, 50, 75], new NominalScale()),
        ]);
        var auto = EqualWidthMinMaxPlan();
        var frozenPlan = Plan(frozen);

        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(frozenPlan),
            FingerprintCalculator.ComputeSchemaFingerprint(auto));
        Assert.NotEqual(
            FingerprintCalculator.ComputeDatOutputFingerprint(frozenPlan, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(auto, NativeDat()));

        // The documented difference, named: swapping just the discretizer sub-object makes the
        // two output payloads identical, so nothing else moved.
        var autoJson = FingerprintCalculator.BuildDatOutputJson(auto, auto.Calibrated.Spec, NativeDat());
        var frozenJson = FingerprintCalculator.BuildDatOutputJson(frozenPlan, frozen, NativeDat());
        Assert.Equal(
            frozenJson.Replace(
                "\"discretizer\":{\"cuts\":[25,50,75],\"ends\":\"open\",\"kind\":\"manual_cuts\"}",
                "\"discretizer\":{\"bins\":4,\"kind\":\"equal_width\",\"precision\":\"exact\",\"range\":\"min_max\"}",
                StringComparison.Ordinal),
            autoJson);
    }

    // --- equal_frequency + percentile encodings (D-094 golden-lock, M4 Slice D) --
    //
    // The same D-094 gate as Slice C: these bytes and their SHA-256 vectors land BEFORE the first
    // stored equal_frequency / percentile hash, because that first hash fossilizes them.
    // equal_frequency encodes its authored bins/tie_policy/cut_placement with the §11.5 defaults
    // SPELLED (resolution happens at the seam, so the fingerprint never sees "omitted") — and, as
    // with equal_width, its calibrated cuts ride only as schema bins.

    private const string EqualFrequencySchemaArray =
        """
        [{"bin":{"hi":2,"hi_open":false,"lo":null,"lo_open":true},"name":"score","op":"","scale":"nominal"},{"bin":{"hi":3,"hi_open":false,"lo":2,"lo_open":false},"name":"score","op":"","scale":"nominal"},{"bin":{"hi":null,"hi_open":true,"lo":3,"lo_open":false},"name":"score","op":"","scale":"nominal"}]
        """;

    // The only route to a plannable equal_frequency: a pending carrier whose cuts the calibrator
    // supplied and CalibratedSpec.Create substituted (D-093).
    private static ConversionPlan EqualFrequencyPlan(
        TiePolicy tie = TiePolicy.Left, CutPlacement placement = CutPlacement.RightValue)
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.EqualFrequencyPending("score", 0, 3, new NominalScale(), tie, placement),
        ]);
        var calibrated = CalibratedSpec.Create(
            Resolve(spec, new SourceSchema(1)), [new CalibratedCuts("score", [2, 3])]);
        Assert.True(calibrated.TryGetValue(out var state));
        Assert.True(ConversionPlanner.Plan(state!).TryGetValue(out var plan));
        return plan!;
    }

    private static ConversionPlan PercentilePlan()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.EqualWidthPending("score", 0, 4, new NominalScale(), EqualWidthRange.PercentileP1P99),
        ]);
        var calibrated = CalibratedSpec.Create(
            Resolve(spec, new SourceSchema(1)), [new CalibratedCuts("score", [25, 50, 75])]);
        Assert.True(calibrated.TryGetValue(out var state));
        Assert.True(ConversionPlanner.Plan(state!).TryGetValue(out var plan));
        return plan!;
    }

    [Fact]
    public void BuildCxtOutputJson_WhenEqualFrequency_ThenDiscretizerEncodesTheAuthoredConfigWithDefaultsSpelled()
    {
        var plan = EqualFrequencyPlan();
        var json = FingerprintCalculator.BuildCxtOutputJson(plan, plan.Calibrated.Spec, NativeCxt());

        // The pinned encoding (§14/D-094). Keys sort ordinal: bins < cut_placement < kind <
        // tie_policy. The resolved defaults are spelled, not omitted.
        Assert.Contains(
            "\"discretizer\":{\"bins\":3,\"cut_placement\":\"right_value\",\"kind\":\"equal_frequency\",\"tie_policy\":\"left\"},",
            json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenEqualFrequencyConfigVaries_ThenEachSpellingIsEncoded()
    {
        // Both enums' every member is reachable in the encoding, so a wrong spelling cannot hide.
        var plan = EqualFrequencyPlan(TiePolicy.Right, CutPlacement.Midpoint);

        Assert.Contains(
            "\"discretizer\":{\"bins\":3,\"cut_placement\":\"midpoint\",\"kind\":\"equal_frequency\",\"tie_policy\":\"right\"},",
            FingerprintCalculator.BuildCxtOutputJson(plan, plan.Calibrated.Spec, NativeCxt()),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenEqualFrequency_ThenCalibratedCutsAreNotInsideTheDiscretizerObject()
    {
        // D-094: the calibrated cuts appear ONLY as schema bins — re-encoding them in the authored
        // sub-object would be the second source of truth the rule forbids.
        var plan = EqualFrequencyPlan();
        var json = FingerprintCalculator.BuildCxtOutputJson(plan, plan.Calibrated.Spec, NativeCxt());
        var discretizer = json[json.IndexOf("\"discretizer\":", StringComparison.Ordinal)..];

        Assert.DoesNotContain("\"cuts\"", discretizer[..discretizer.IndexOf('}', StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("\"schema\":" + EqualFrequencySchemaArray, json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSchemaJson_WhenEqualFrequency_ThenOpenEndedCutBinsInPlanOrder() =>
        Assert.Equal(
            "{\"attributes\":" + EqualFrequencySchemaArray + ",\"fp_format\":1,\"kind\":\"schema\"}",
            FingerprintCalculator.BuildSchemaJson(EqualFrequencyPlan()));

    [Fact]
    public void ComputeSchemaFingerprint_WhenEqualFrequency_ThenMatchesHardcodedVector() =>
        // Hash literal computed independently (an external SHA-256 over the hand-authored bytes
        // above), never copied from this encoder's output — that is what makes it a lock (D-094).
        Assert.Equal(
            "sha256:bc7ef3c1e66cb4ca40eb9317de43fd7858c230fcc87da6d802638ba992efb2b0",
            FingerprintCalculator.ComputeSchemaFingerprint(EqualFrequencyPlan()));

    [Fact]
    public void BuildDatOutputJson_WhenEqualFrequency_ThenCompletePinnedCanonicalBytes()
    {
        var plan = EqualFrequencyPlan();

        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + EqualFrequencySchemaArray
                + ",\"shared\":{\"attributes\":[{\"declared_domain\":[],\"discretizer\":{\"bins\":3,"
                + "\"cut_placement\":\"right_value\",\"kind\":\"equal_frequency\",\"tie_policy\":\"left\"},"
                + "\"missing_policy\":\"skip\",\"name\":\"score\",\"scale\":{\"kind\":\"nominal\"},"
                + "\"source\":{\"column\":0,\"value_type\":\"number\"},"
                + "\"unknown_value_policy\":\"warn\"}],\"binding\":{\"delimiter\":\",\",\"encoding\":\"utf-8\",\"has_header\":true,"
                + "\"locale\":\"invariant\",\"missing_token\":\"?\",\"object_key\":{\"mode\":\"row_index\"},\"quote_char\":\"\\\"\",\"shape\":\"wide\"}}}",
            FingerprintCalculator.BuildDatOutputJson(plan, plan.Calibrated.Spec, NativeDat()));
    }

    [Fact]
    public void ComputeDatOutputFingerprint_WhenEqualFrequency_ThenMatchesHardcodedVector() =>
        // Independently computed over the pinned dat bytes above; the hash genuinely covers the
        // discretizer sub-object, which rides in `shared` and feeds only the output hashes.
        Assert.Equal(
            "sha256:a892e17fa5601401f7c29a8c320bca35325dc0a13b5b384245e56fd1b5c9a971",
            FingerprintCalculator.ComputeDatOutputFingerprint(EqualFrequencyPlan(), NativeDat()));

    [Fact]
    public void ComputeDatOutputFingerprint_WhenEqualFrequencyConfigVaries_ThenTheHashMoves() =>
        // Proves the pinned vector above actually covers the discretizer object rather than
        // hashing around it: the ONLY difference here is the authored tie_policy/cut_placement.
        Assert.NotEqual(
            FingerprintCalculator.ComputeDatOutputFingerprint(EqualFrequencyPlan(), NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(
                EqualFrequencyPlan(TiePolicy.Right, CutPlacement.Midpoint), NativeDat()));

    [Fact]
    public void BuildCxtOutputJson_WhenPercentileRange_ThenDiscretizerSpellsPercentileAndOmitsBounds()
    {
        var plan = PercentilePlan();
        var json = FingerprintCalculator.BuildCxtOutputJson(plan, plan.Calibrated.Spec, NativeCxt());

        // Slice C modelled this spelling but kept it unreachable; Slice D activates it, so its
        // bytes are pinned here before the first stored percentile hash (D-094).
        Assert.Contains(
            "\"discretizer\":{\"bins\":4,\"kind\":\"equal_width\",\"precision\":\"exact\",\"range\":\"percentile_p1_p99\"},",
            json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"vmin\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"vmax\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDatOutputJson_WhenPercentileRange_ThenCompletePinnedCanonicalBytes()
    {
        var plan = PercentilePlan();

        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + EqualWidthSchemaArray
                + ",\"shared\":{\"attributes\":[{\"declared_domain\":[],\"discretizer\":{\"bins\":4,\"kind\":\"equal_width\","
                + "\"precision\":\"exact\",\"range\":\"percentile_p1_p99\"},\"missing_policy\":\"skip\","
                + "\"name\":\"score\",\"scale\":{\"kind\":\"nominal\"},\"source\":{\"column\":0,\"value_type\":\"number\"},"
                + "\"unknown_value_policy\":\"warn\"}],\"binding\":{\"delimiter\":\",\",\"encoding\":\"utf-8\",\"has_header\":true,"
                + "\"locale\":\"invariant\",\"missing_token\":\"?\",\"object_key\":{\"mode\":\"row_index\"},\"quote_char\":\"\\\"\",\"shape\":\"wide\"}}}",
            FingerprintCalculator.BuildDatOutputJson(plan, plan.Calibrated.Spec, NativeDat()));
    }

    [Fact]
    public void ComputeDatOutputFingerprint_WhenPercentileRange_ThenMatchesHardcodedVector() =>
        Assert.Equal(
            "sha256:dc0d9ed4b00f05e9a184482dfaad2b9e34860e5664d4c3e083a4090f1c30cb34",
            FingerprintCalculator.ComputeDatOutputFingerprint(PercentilePlan(), NativeDat()));

    [Fact]
    public void ComputeFingerprints_WhenPercentileVsMinMaxOverTheSameCuts_ThenSchemaEqualAndOutputDiffers()
    {
        // The range mode is authored configuration, so two data-derived specs that happen to
        // calibrate to the same cuts share a schema fingerprint but not an output one — the same
        // D-094 asymmetry as auto-vs-frozen, within the auto family.
        var percentile = PercentilePlan();
        var minMax = EqualWidthMinMaxPlan();

        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(minMax),
            FingerprintCalculator.ComputeSchemaFingerprint(percentile));
        Assert.NotEqual(
            FingerprintCalculator.ComputeDatOutputFingerprint(minMax, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(percentile, NativeDat()));
    }

    [Fact]
    public void ComputeFingerprints_WhenEqualFrequencyAutoVsFrozenManualCuts_ThenSchemaEqualAndOutputDiffersOnlyInTheDiscretizer()
    {
        // The D-088/D-094 pair for equal_frequency: the frozen manual_cuts twin over the same
        // calibrated cuts shares every effective bin — hence the schema fingerprint — while the
        // output fingerprints differ ONLY through the authored discretizer sub-object.
        var frozen = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.NumericCuts("score", 0, [2, 3], new NominalScale()),
        ]);
        var auto = EqualFrequencyPlan();
        var frozenPlan = Plan(frozen);

        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(frozenPlan),
            FingerprintCalculator.ComputeSchemaFingerprint(auto));
        Assert.NotEqual(
            FingerprintCalculator.ComputeDatOutputFingerprint(frozenPlan, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(auto, NativeDat()));

        // The documented difference, isolated: swapping just the discretizer sub-object makes the
        // two payloads byte-identical, so nothing else moved.
        Assert.Equal(
            FingerprintCalculator.BuildDatOutputJson(frozenPlan, frozen, NativeDat()).Replace(
                "\"discretizer\":{\"cuts\":[2,3],\"ends\":\"open\",\"kind\":\"manual_cuts\"}",
                "\"discretizer\":{\"bins\":3,\"cut_placement\":\"right_value\",\"kind\":\"equal_frequency\",\"tie_policy\":\"left\"}",
                StringComparison.Ordinal),
            FingerprintCalculator.BuildDatOutputJson(auto, auto.Calibrated.Spec, NativeDat()));
    }

    // --- existing-kind regression: authored -0.0 manual cut stays -0 (G-6) ----

    [Fact]
    public void BuildSchemaJson_WhenAuthoredNegativeZeroManualCut_ThenEncodesMinusZeroUnderFpFormat1()
    {
        // G-6: the fp_format = 1 encoder is UNTOUCHED — an authored -0.0 manual cut is a valid
        // current spec whose stored hash embeds "-0"; CanonicalNumber.CanonicalizeZero is NOT applied
        // to existing-kind authored cuts, so those bytes must not move.
        var discretizer = ManualCutsDiscretizer.Create([-0.0, 10.0], BinEnds.Open, CultureInfo.InvariantCulture).Value!;
        var attribute = new AttributeSpec(
            "v", new ColumnSource(0, SourceValueType.Number), Include: true, discretizer, new NominalScale(),
            DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attribute]);
        Assert.True(Diag(spec, new SourceSchema(1)).TryGetValue(out var plan));

        var json = FingerprintCalculator.BuildCxtOutputJson(plan!, spec, NativeCxt());

        // The authored cut renders "-0" in both the schema bin bounds and the manual_cuts sub-object.
        Assert.Contains("\"lo\":-0,\"lo_open\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"cuts\":[-0,10]", json, StringComparison.Ordinal);
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

    // --- Triple binding: role map + predicate source (Slice B, D-082) -------
    //
    // These exercise the shared/binding encoding below the planner: a hand-built triple
    // Core spec paired with any valid (wide) plan — AppendShared/AppendBinding read only
    // the spec, so the borrowed plan supplies just the unread schema array. The spec uses
    // ordering = "unordered" (not a fingerprint input; both orderings hash identically),
    // keeping the fingerprint focus on the role map / binding rather than a full conversion.

    private static BedrockSpec TripleSpec(TripleColumns columns, string predicate = "age") =>
        new(new Binding(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", "?",
                new ColumnObjectKey(columns.Subject, DuplicateObjectPolicy.Fail), columns, TripleOrdering.Unordered),
            [
                new AttributeSpec(predicate, new PredicateSource(predicate, SourceValueType.String), Include: true,
                    new IdentityDiscretizer(), new NominalScale(), [predicate], [], SpecFixtures.NoLabels,
                    MissingPolicy.Skip, UnknownValuePolicy.Warn),
            ]);

    [Fact]
    public void BuildCxtOutputJson_WhenTripleBinding_ThenSharedEncodesRoleMapAndPredicateSource()
    {
        var json = FingerprintCalculator.BuildCxtOutputJson(
            Plan(GoldenSpec()), TripleSpec(new TripleColumns(0, 1, 2)), NativeCxt());

        // "columns" sorts before "delimiter"; role keys are ordinal-sorted
        // (predicate/subject/value); the triple `ordering` is deliberately absent.
        Assert.Contains(
            "\"binding\":{\"columns\":{\"predicate\":1,\"subject\":0,\"value\":2},\"delimiter\":\",\",\"encoding\":\"utf-8\"",
            json);
        Assert.Contains("\"source\":{\"predicate\":\"age\",\"value_type\":\"string\"}", json);
        Assert.DoesNotContain("ordering", json);
    }

    [Fact]
    public void ComputeOutputFingerprints_WhenTripleColumnsDiffer_ThenBothMoveButSameMapMatches()
    {
        // The output fingerprints now read the plan's own calibrated spec (D-098/D-094), so each
        // triple map is exercised through its own plan rather than one plan + varied spec args.
        var a = TripleSpec(new TripleColumns(0, 1, 2));
        var b = TripleSpec(new TripleColumns(2, 1, 0));
        Assert.True(Diag(a, new SourceSchema(3)).TryGetValue(out var planA));
        Assert.True(Diag(b, new SourceSchema(3)).TryGetValue(out var planB));
        Assert.True(Diag(TripleSpec(new TripleColumns(0, 1, 2)), new SourceSchema(3)).TryGetValue(out var planSameMap));

        // The resolved role→index map is a shared input, so it moves both formats.
        Assert.NotEqual(ComputeCxt(planA, a, NativeCxt()), ComputeCxt(planB, b, NativeCxt()));
        Assert.NotEqual(ComputeDat(planA, a, NativeDat()), ComputeDat(planB, b, NativeDat()));

        // The same map hashes identically (name-bound ≡ index-bound, D-082).
        Assert.Equal(ComputeCxt(planA, a, NativeCxt()), ComputeCxt(planSameMap, a, NativeCxt()));
    }
}
