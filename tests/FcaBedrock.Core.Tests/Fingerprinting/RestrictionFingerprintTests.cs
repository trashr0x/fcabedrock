using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Fingerprinting;

/// <summary>
/// The §14 <c>restrictions</c> container (D-091/D-094/D-105, governance items G-9/G-10).
/// <para>
/// The literals below <b>are</b> the pinned encoding, hand-authored — a diff here is an encoding
/// change requiring an <c>fp_format</c> bump, not a test edit (the D-069 → Slice-E precedent).
/// The container is golden-locked here <em>before</em> any stored restriction hash can ship.
/// </para>
/// </summary>
public sealed class RestrictionFingerprintTests
{
    // --- the pinned wide golden ---------------------------------------------
    //
    // Two attributes exercising both halves of the D-091 rule at once: `age` is
    // included-AND-restricted (it contributes a column AND a restriction), while `Gene` is
    // filter-only (include = false + restrict_to) and contributes ONLY a restriction — which is
    // why it appears in `restrictions` but never in `attributes`.

    private const string WideSchemaArray =
        """
        [{"bin":{"hi":30,"hi_open":false,"lo":null,"lo_open":true},"name":"age","op":"","scale":"nominal"},{"bin":{"hi":null,"hi_open":true,"lo":30,"lo_open":false},"name":"age","op":"","scale":"nominal"}]
        """;

    // Keys sort ordinal throughout: shared is attributes < binding < restrictions; each
    // restriction object is entries < source < unknown_value_policy.
    //
    // Entry order is CANONICAL, not authored: the spec authors [ {value=30}, {from=10,to=20},
    // {to=5} ] (below) and they encode sorted by complete canonical JSON — '{"from":1…' before
    // '{"from":n…' ('1' < 'n'), and both before '{"value"…' ('f' < 'v'). Restriction objects sort
    // the same way, so `age` precedes `Gene`.
    private const string WideShared =
        """
        {"attributes":[{"declared_domain":[],"discretizer":{"cuts":[30],"ends":"open","kind":"manual_cuts"},"missing_policy":"skip","name":"age","scale":{"kind":"nominal"},"source":{"column":0,"value_type":"number"},"unknown_value_policy":"warn"}],"binding":{"delimiter":",","encoding":"utf-8","has_header":true,"locale":"invariant","missing_token":"?","object_key":{"mode":"row_index"},"quote_char":"\"","shape":"wide"},"restrictions":[{"entries":[{"from":10,"to":20},{"from":null,"to":5},{"value":30}],"source":{"column":0,"value_type":"number"},"unknown_value_policy":"warn"},{"entries":[{"value":"Bmp5"}],"source":{"column":1,"value_type":"string"},"unknown_value_policy":"warn"}]}
        """;

    private const string WideCxtJson =
        """
        {"cxt":{"bin_label_unicode":false,"label_style":"native","line_endings":"lf","rendered_names":["age-<30","age->=30"],"trailing_newline":true},"fp_format":1,"kind":"cxt_output","schema":
        """;

    private static BedrockSpec WideRestrictedSpec(
        UnknownValuePolicy genePolicy = UnknownValuePolicy.Warn) =>
        new(SpecFixtures.WideRowIndex(), [
            SpecFixtures.NumericCuts("age", 0, [30], new NominalScale()) with
            {
                // Authored order is deliberately NOT canonical order, so the sort is proven.
                RestrictTo = [new RestrictToNumber(30), new RestrictToRange(10, 20), new RestrictToRange(null, 5)],
            },
            SpecFixtures.Excluded("Gene", 1) with
            {
                RestrictTo = [new RestrictToValue("Bmp5")],
                UnknownValuePolicy = genePolicy,
            },
        ]);

    private static ConversionPlan Plan(BedrockSpec spec, int columns = 2)
    {
        var resolved = SpecFixtures.Resolve(spec, new SourceSchema(columns));
        Assert.True(ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(resolved)).TryGetValue(out var plan));
        return plan!;
    }

    private static CxtFingerprintInputs NativeCxt() => new(LabelStyle.Native, false, LineEnding.Lf, true);

    private static DatFingerprintInputs NativeDat() => new(1, LineEnding.Lf, false, false);

    // Builds the canonical bytes the way the PUBLIC API does: over `plan.Calibrated.Spec` — the
    // ResolvedSpec snapshot — not over the caller's raw spec. That distinction is the whole point
    // of the -0 vectors below: the snapshot is where signed zero is canonicalized (D-105), so a
    // helper that passed the raw spec would test a path production never takes.
    private static string Cxt(BedrockSpec spec)
    {
        var plan = Plan(spec);
        return FingerprintCalculator.BuildCxtOutputJson(plan, plan.Calibrated.Spec, NativeCxt());
    }

    private static string Dat(BedrockSpec spec)
    {
        var plan = Plan(spec);
        return FingerprintCalculator.BuildDatOutputJson(plan, plan.Calibrated.Spec, NativeDat());
    }

    // --- the golden lock ----------------------------------------------------

    [Fact]
    public void BuildCxtOutputJson_WhenAttributesRestrict_ThenMatchesTheCompletePinnedCanonicalBytes() =>
        // The COMPLETE bytes, not a substring: the container's position in `shared`, its own
        // contents, and everything around it are all pinned at once.
        Assert.Equal(WideCxtJson + WideSchemaArray + ",\"shared\":" + WideShared + "}", Cxt(WideRestrictedSpec()));

    [Fact]
    public void BuildDatOutputJson_WhenAttributesRestrict_ThenCarriesTheSameSharedRestrictions() =>
        // restrict_to shapes which OBJECTS appear, so it feeds BOTH output fingerprints (§14) —
        // identical `shared` bytes on the .dat side.
        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + WideSchemaArray + ",\"shared\":" + WideShared + "}",
            Dat(WideRestrictedSpec()));

    [Fact]
    public void BuildDatOutputJson_WhenFilterOnlyPolicyIsFail_ThenTheCompleteBytesCarryTheFailPolicy() =>
        // The G-9 policy key on the .dat side too, as complete pinned bytes rather than a
        // substring — the container must be byte-identical across both output fingerprints, and
        // only `Gene`'s restriction policy differs from the warn golden above.
        Assert.Equal(
            "{\"dat\":{\"base_index\":1,\"empty_line_trailing_space\":false,\"line_endings\":\"lf\","
                + "\"nonempty_line_trailing_space\":false},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":"
                + WideSchemaArray + ",\"shared\":" + WideShared.Replace(
                    "{\"entries\":[{\"value\":\"Bmp5\"}],\"source\":{\"column\":1,\"value_type\":\"string\"},\"unknown_value_policy\":\"warn\"}",
                    "{\"entries\":[{\"value\":\"Bmp5\"}],\"source\":{\"column\":1,\"value_type\":\"string\"},\"unknown_value_policy\":\"fail\"}",
                    StringComparison.Ordinal) + "}",
            Dat(WideRestrictedSpec(UnknownValuePolicy.Fail)));

    [Fact]
    public void ComputeCxtOutputFingerprint_WhenAttributesRestrict_ThenMatchesTheHardcodedVector()
    {
        // A HARD vector over a payload that contains `restrictions` (D-094). The literal was
        // computed OUTSIDE the production helper — `printf '%s' <bytes> | sha256sum` over the
        // pinned UTF-8 canonical bytes above — and that external method was cross-checked by
        // reproducing an already-pinned vector
        // (ComputeSchemaFingerprint_WhenTrivialPlan_ThenMatchesHardcodedVector's
        // sha256:73104676…3b4f) before being trusted here. So this pins the bytes AND the hashing,
        // independently of the code under test.
        Assert.Equal(
            "sha256:12a0b2b3e11fb6c30fc04d87817ad3b6fe6bcb68f2f4016b52fa791df395c325",
            FingerprintCalculator.ComputeCxtOutputFingerprint(Plan(WideRestrictedSpec()), NativeCxt()));
    }

    // --- presence / omission ------------------------------------------------

    [Fact]
    public void BuildCxtOutputJson_WhenNoAttributeRestricts_ThenTheContainerIsAbsentEntirely()
    {
        // The omission rule is what keeps every restriction-free spec's pre-Slice-F bytes and
        // hashes byte-identical: absent, not an empty array (which would be new bytes).
        var json = Cxt(new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("a", 0, ["x"])]));

        Assert.DoesNotContain("restrictions", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"restrictions\":[]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDatOutputJson_WhenNoAttributeRestricts_ThenTheContainerIsAbsentEntirely()
    {
        // The .dat twin of the omission rule above. The container feeds BOTH output fingerprints
        // (§14), so "absent, not an empty array" has to hold on both sides — otherwise a
        // restriction-free spec would keep its .cxt hash while its .dat hash moved.
        var json = Dat(new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("a", 0, ["x"])]));

        Assert.DoesNotContain("restrictions", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeOutputFingerprints_WhenTheOnlyEntryIsTheUnboundedRange_ThenBothOutputsMoveButSchemaDoesNot()
    {
        // `{}` is ONE entry — the full usable-numeric range (§10.4/D-091) — not an empty list.
        // So it populates the container and moves both output fingerprints, which is exactly the
        // contrast that makes the absent/empty omission rule above meaningful rather than
        // vacuous. Columns are untouched, so the schema hash is shared.
        var unrestricted = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.NumericCuts("age", 0, [30], new NominalScale())]);
        var unbounded = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.NumericCuts("age", 0, [30], new NominalScale()) with
            {
                RestrictTo = [new RestrictToRange(null, null)],
            },
        ]);

        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(Plan(unrestricted, 1)),
            FingerprintCalculator.ComputeSchemaFingerprint(Plan(unbounded, 1)));
        Assert.NotEqual(
            FingerprintCalculator.ComputeCxtOutputFingerprint(Plan(unrestricted, 1), NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(Plan(unbounded, 1), NativeCxt()));
        Assert.NotEqual(
            FingerprintCalculator.ComputeDatOutputFingerprint(Plan(unrestricted, 1), NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(Plan(unbounded, 1), NativeDat()));
    }

    [Fact]
    public void ComputeSchemaFingerprint_WhenRestrictionsChange_ThenItIsUnaffected()
    {
        // §14: restrict_to changes which ROWS appear, not which COLUMNS exist — so it is excluded
        // from schema_fingerprint by construction. Two specs differing only in their restrictions
        // must share it.
        var restricted = FingerprintCalculator.ComputeSchemaFingerprint(Plan(WideRestrictedSpec()));

        var otherwiseIdentical = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.NumericCuts("age", 0, [30], new NominalScale()) with
            {
                RestrictTo = [new RestrictToRange(1000, 2000)], // different filter, same columns
            },
            SpecFixtures.Excluded("Gene", 1) with { RestrictTo = [new RestrictToValue("Wnt1")] },
        ]);

        Assert.Equal(restricted, FingerprintCalculator.ComputeSchemaFingerprint(Plan(otherwiseIdentical)));

        // …while the OUTPUT fingerprints must differ: the rows really do change.
        Assert.NotEqual(
            FingerprintCalculator.ComputeCxtOutputFingerprint(Plan(WideRestrictedSpec()), NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(Plan(otherwiseIdentical), NativeCxt()));
    }

    // --- entry encodings ----------------------------------------------------

    [Fact]
    public void BuildCxtOutputJson_WhenRangeBoundsAreOpen_ThenBothKeysAlwaysAppearWithNulls()
    {
        // Both keys always present, an omitted bound as null — so {} is {"from":null,"to":null}
        // and stays distinguishable from every bounded range.
        var spec = Restricting([new RestrictToRange(null, null), new RestrictToRange(90, null)]);

        Assert.Contains(
            "\"restrictions\":[{\"entries\":[{\"from\":90,\"to\":null},{\"from\":null,\"to\":null}]",
            Cxt(spec), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenStringExactEntry_ThenEncodesValueAsAString()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.Excluded("Gene", 0) with { RestrictTo = [new RestrictToValue("Bmp5")] },
            SpecFixtures.Nominal("t", 1, ["x"]),
        ]);

        Assert.Contains("{\"entries\":[{\"value\":\"Bmp5\"}]", Cxt(spec), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenExactNumberIsIntegral_ThenTheFormatterDropsTheFractionalPart()
    {
        // §14: the invariant shortest round-trippable formatter — the same one that keeps cuts
        // stable — so an integral exact value encodes bare.
        //
        // Spelling convergence (30 / 30.0 / 3e1) is deliberately NOT asserted here: those are one
        // and the same C# double, so a carrier-level test of it would be vacuous. Spelling exists
        // only in TOML text, so the convergence vector lives at the reader
        // (AttributeReaderTests / SpecRoundTripTests), which is the only place it can fail.
        Assert.Contains("\"entries\":[{\"value\":30}]", Cxt(Restricting([new RestrictToNumber(30)])), StringComparison.Ordinal);
        Assert.Contains("\"entries\":[{\"value\":30.5}]", Cxt(Restricting([new RestrictToNumber(30.5)])), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenExactNegativeZeroReachesCoreDirectly_ThenItEncodesAsPositiveZero()
    {
        // G-6/D-105 through the PUBLIC Core path, not the seam: ResolvedSpec.Create is the
        // rebuild boundary, so it canonicalizes signed zero and a resolved spec carries canonical
        // numeric restriction identities BY CONSTRUCTION.
        //
        // This matters because CanonicalJson.AppendNumber is deliberately untouched and still
        // formats -0.0 as "-0" (that is what keeps authored manual-cut bytes and fp_format = 1
        // stable). Without canonicalization at the boundary, a programmatic -0 would encode "-0"
        // — while MATCHING identically to 0, since IEEE says 0.0 == -0.0. Same behaviour, different
        // hash, is exactly the determinism bug P-7 forbids.
        var negative = Cxt(Restricting([new RestrictToNumber(-0.0)]));

        Assert.Contains("\"entries\":[{\"value\":0}]", negative, StringComparison.Ordinal);
        Assert.DoesNotContain("-0", negative, StringComparison.Ordinal);
        Assert.Equal(Cxt(Restricting([new RestrictToNumber(0.0)])), negative);
    }

    [Fact]
    public void ComputeCxtOutputFingerprint_WhenExactNegativeZeroReachesCoreDirectly_ThenTheHashMatchesPositiveZero()
    {
        // The same guarantee at the public fingerprint API, not just the byte builder.
        Assert.Equal(
            FingerprintCalculator.ComputeCxtOutputFingerprint(Plan(Restricting([new RestrictToNumber(0.0)])), NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(Plan(Restricting([new RestrictToNumber(-0.0)])), NativeCxt()));
    }

    [Fact]
    public void BuildCxtOutputJson_WhenNegativeAndPositiveZeroEntriesCoexist_ThenTheyDeduplicateToOne()
    {
        // The consequence of canonicalizing at the boundary: -0 and 0 are ONE entry, so listing
        // both collapses. Without it they would be two distinct canonical strings and survive
        // deduplication as separate entries.
        var both = Cxt(Restricting([new RestrictToNumber(-0.0), new RestrictToNumber(0.0)]));

        Assert.Contains("\"entries\":[{\"value\":0}]", both, StringComparison.Ordinal);
        Assert.DoesNotContain("-0", both, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenRangeBoundIsNegativeZeroInCore_ThenItEncodesAsPositiveZero()
    {
        var json = Cxt(Restricting([new RestrictToRange(-0.0, 5)]));

        Assert.Contains("\"entries\":[{\"from\":0,\"to\":5}]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("-0", json, StringComparison.Ordinal);
    }

    // --- sorting and deduplication (G-10) -----------------------------------

    [Fact]
    public void BuildCxtOutputJson_WhenEntriesAuthoredInAnyOrder_ThenTheEncodingIsTheSame()
    {
        // Restriction order is semantically immaterial (entries OR, restrictions AND), so two
        // specs that differ only in authored order are the SAME conversion and must hash alike —
        // the whole reason `restrictions` is the one §14 array that sorts.
        var authored = Restricting([new RestrictToNumber(30), new RestrictToRange(10, 20), new RestrictToRange(null, 5)]);
        var shuffled = Restricting([new RestrictToRange(null, 5), new RestrictToNumber(30), new RestrictToRange(10, 20)]);

        Assert.Equal(Cxt(authored), Cxt(shuffled));
    }

    [Fact]
    public void BuildCxtOutputJson_WhenCanonicallyIdenticalEntriesRepeat_ThenTheyDeduplicate()
    {
        // Canonically-identical entries ARE one entry (D-091). Authored duplicates survive in the
        // document and the plan — only this projection removes them.
        var once = Cxt(Restricting([new RestrictToNumber(30)]));
        var thrice = Cxt(Restricting([new RestrictToNumber(30), new RestrictToNumber(30.0), new RestrictToNumber(3e1)]));

        Assert.Equal(once, thrice);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenRangesOverlapButDiffer_ThenTheyAreNotMerged()
    {
        // Overlapping ranges are distinct authored intent and stay separate — merging them would
        // lose that intent for no determinism gain (D-091 rejected alternative).
        var json = Cxt(Restricting([new RestrictToRange(10, 30), new RestrictToRange(20, 40)]));

        Assert.Contains(
            "\"entries\":[{\"from\":10,\"to\":30},{\"from\":20,\"to\":40}]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenTwoAttributesShareAnIdenticalRestrictionObject_ThenTheyDeduplicate()
    {
        // Exact duplicate restriction OBJECTS are removed too — same source, same entries, same
        // policy is the same constraint stated twice.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [
            SpecFixtures.Excluded("a", 0) with { RestrictTo = [new RestrictToValue("x")] },
            SpecFixtures.Excluded("b", 0) with { RestrictTo = [new RestrictToValue("x")] },
            SpecFixtures.Nominal("t", 1, ["q"]),
        ]);

        var json = Cxt(spec);

        // One object survives: the attribute NAME is deliberately not encoded, so two attributes
        // restricting the same source identically are one constraint.
        Assert.Contains(
            "\"restrictions\":[{\"entries\":[{\"value\":\"x\"}],\"source\":{\"column\":0,\"value_type\":\"string\"},\"unknown_value_policy\":\"warn\"}]",
            json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenEntriesAreNonAscii_ThenTheySortByUtf16CodeUnitsNotUtf8Bytes()
    {
        // G-10, the discriminating vector. "" (a BMP private-use char, ONE UTF-16 code unit
        // 0xE000) versus "\U0001F600" (supplementary, encoded as the surrogate pair 0xD83D
        // 0xDE00). Under UTF-16 ordinal — what StringComparer.Ordinal compares, and what P-12
        // means by "ordinal" — 0xD83D < 0xE000, so the emoji sorts FIRST.
        //
        // Sorting the UTF-8 ENCODING would reverse this: U+E000 encodes EE 80 80 and U+1F600
        // encodes F0 9F 98 80, so 0xEE < 0xF0 would put the private-use char first. This test
        // fails if the sort is ever moved after UTF-8 encoding.
        var spec = Restricting(
            [new RestrictToValue(""), new RestrictToValue("\U0001F600")],
            SourceValueType.String);

        var json = Cxt(spec);

        var emoji = json.IndexOf("\U0001F600", StringComparison.Ordinal);
        var privateUse = json.IndexOf("", StringComparison.Ordinal);
        Assert.True(emoji >= 0 && privateUse >= 0, "both entries must be encoded");
        Assert.True(emoji < privateUse, "UTF-16 ordinal: the supplementary char's lead surrogate 0xD83D sorts before 0xE000");
    }

    // --- G-9: the policy key ------------------------------------------------

    [Fact]
    public void ComputeOutputFingerprints_WhenFilterOnlyPolicyDiffers_ThenBothFingerprintsDiffer()
    {
        // G-9, the reason the policy key exists. On a filter-only attribute unknown_value_policy
        // is LIVE, abort-vs-complete-affecting configuration (D-097: an unparseable filtered value
        // is an Error under `fail` and a Warning under `warn`), and a filter-only attribute never
        // enters `shared.attributes` — so without this key these two specs, which behave
        // differently, would hash identically.
        //
        // BOTH output fingerprints must move: `restrictions` lives in `shared`, which feeds each
        // of them (§14). A .cxt-only assertion would pass even if the container were wired into
        // the cxt builder alone.
        var warnPlan = Plan(WideRestrictedSpec(UnknownValuePolicy.Warn));
        var failPlan = Plan(WideRestrictedSpec(UnknownValuePolicy.Fail));

        Assert.NotEqual(
            FingerprintCalculator.ComputeCxtOutputFingerprint(warnPlan, NativeCxt()),
            FingerprintCalculator.ComputeCxtOutputFingerprint(failPlan, NativeCxt()));
        Assert.NotEqual(
            FingerprintCalculator.ComputeDatOutputFingerprint(warnPlan, NativeDat()),
            FingerprintCalculator.ComputeDatOutputFingerprint(failPlan, NativeDat()));

        // …while the schema fingerprint does not: the policy changes which rows survive, not
        // which columns exist.
        Assert.Equal(
            FingerprintCalculator.ComputeSchemaFingerprint(warnPlan),
            FingerprintCalculator.ComputeSchemaFingerprint(failPlan));

        Assert.Contains("\"unknown_value_policy\":\"fail\"", Cxt(WideRestrictedSpec(UnknownValuePolicy.Fail)), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCxtOutputJson_WhenIncludedAndRestricted_ThenThePolicyAppearsInBothPlaces()
    {
        // Deliberate uniform-shape redundancy (G-9), not D-035 double-counting: every restriction
        // object carries the key, so an included-and-restricted attribute's policy also appears in
        // shared.attributes. A value repeated, not a quantity summed.
        var json = Cxt(WideRestrictedSpec());

        var occurrences = json.Split("\"unknown_value_policy\":\"warn\"").Length - 1;
        Assert.Equal(3, occurrences); // age's attribute entry + age's restriction + Gene's restriction
    }

    // --- helpers ------------------------------------------------------------

    // A minimal restricted spec: one restricting attribute plus a plain one so the plan always has
    // a column (an all-filter-only plan is legal but noisier to assert on).
    private static BedrockSpec Restricting(
        IReadOnlyList<RestrictToEntry> entries, SourceValueType valueType = SourceValueType.Number)
    {
        var restricted = valueType == SourceValueType.Number
            ? SpecFixtures.NumericCuts("age", 0, [30], new NominalScale()) with { RestrictTo = entries }
            : SpecFixtures.Excluded("Gene", 0) with { RestrictTo = entries };

        return new BedrockSpec(SpecFixtures.WideRowIndex(), [restricted, SpecFixtures.Nominal("t", 1, ["q"])]);
    }
}
