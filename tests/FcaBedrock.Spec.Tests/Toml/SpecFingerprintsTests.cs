using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// The Slice E spec-load face (D-051/D-069/D-077): native-settings resolution
/// feeding <c>FingerprintCalculator</c>, stored-fingerprint verification, the
/// roadmap 30/30.0/3e1 numeric-spelling golden, and end-to-end stability
/// baselines over the §19 worked examples.
/// </summary>
public sealed class SpecFingerprintsTests
{
    private const string CutSpellingTemplate = """
        [spec]
        version = 1

        [binding]
        shape = "wide"

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "manual_cuts", cuts = [{CUT}], ends = "open" }
        scale = { kind = "nominal" }
        """;

    [Fact]
    public void ComputeNative_WhenOutputAbsent_ThenSpecDefaultsFeedTheCalculator()
    {
        var (document, spec, plan) = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));

        var computed = ComputeNative(document, spec, plan);

        Assert.Equal(FingerprintCalculator.ComputeSchemaFingerprint(plan), computed.SchemaFingerprint);
        Assert.Equal(
            ComputeCxt(
                plan, spec, new CxtFingerprintInputs(LabelStyle.Native, BinLabelUnicode: false, LineEnding.Lf, TrailingNewline: true)),
            computed.CxtOutputFingerprint);
        Assert.Equal(
            ComputeDat(
                plan, spec, new DatFingerprintInputs(BaseIndex: 1, LineEnding.Lf, NonemptyLineTrailingSpace: false, EmptyLineTrailingSpace: false)),
            computed.DatOutputFingerprint);
    }

    [Fact]
    public void ComputeNative_WhenOutputAuthored_ThenAuthoredSettingsFeedTheCalculator()
    {
        var toml = MinimalSpec() + """

            [output]
            bin_label_unicode = true

            [output.cxt]
            line_endings = "crlf"
            trailing_newline = false

            [output.dat]
            line_endings = "crlf"
            base_index = 0
            nonempty_line_trailing_space = true
            empty_line_trailing_space = true
            """;
        var (document, spec, plan) = Pipeline(toml, new SourceSchema(1));

        var computed = ComputeNative(document, spec, plan);

        Assert.Equal(
            ComputeCxt(
                plan, spec, new CxtFingerprintInputs(LabelStyle.Native, BinLabelUnicode: true, LineEnding.Crlf, TrailingNewline: false)),
            computed.CxtOutputFingerprint);
        Assert.Equal(
            ComputeDat(
                plan, spec, new DatFingerprintInputs(BaseIndex: 0, LineEnding.Crlf, NonemptyLineTrailingSpace: true, EmptyLineTrailingSpace: true)),
            computed.DatOutputFingerprint);
    }

    [Fact]
    public void ComputeNative_WhenDatTrailingNewlineDisabled_ThenFeedsFalseAndDiffersFromDefault()
    {
        // D-087: an authored [output.dat] trailing_newline = false resolves into the dat
        // fingerprint inputs and yields a distinct dat fingerprint from the default (true),
        // whose native computation is unchanged (the omit-when-default backward-compat rule).
        var (document, spec, plan) = Pipeline(MinimalSpec() + "\n[output.dat]\ntrailing_newline = false\n", new SourceSchema(1));
        var (defaultDoc, defaultSpec, defaultPlan) = Pipeline(MinimalSpec(), new SourceSchema(1));

        var computed = ComputeNative(document, spec, plan);

        Assert.Equal(
            ComputeDat(
                plan, spec,
                new DatFingerprintInputs(BaseIndex: 1, LineEnding.Lf, NonemptyLineTrailingSpace: false, EmptyLineTrailingSpace: false)
                {
                    TrailingNewline = false,
                }),
            computed.DatOutputFingerprint);
        Assert.NotEqual(
            ComputeNative(defaultDoc, defaultSpec, defaultPlan).DatOutputFingerprint,
            computed.DatOutputFingerprint);
    }

    [Fact]
    public void ComputeNative_WhenOnlySizeAdvisoryDiffers_ThenFingerprintsAreIdentical()
    {
        // size_advisory_bytes changes a warning, never bytes — not a fingerprint
        // input (D-077; §3's byte-affecting definition).
        var quiet = MinimalSpec() + "\n[output.cxt]\nsize_advisory_bytes = 1\n";
        var loud = MinimalSpec() + "\n[output.cxt]\nsize_advisory_bytes = 999999999\n";
        var (quietDoc, quietSpec, quietPlan) = Pipeline(quiet, new SourceSchema(1));
        var (loudDoc, loudSpec, loudPlan) = Pipeline(loud, new SourceSchema(1));

        Assert.Equal(
            ComputeNative(quietDoc, quietSpec, quietPlan),
            ComputeNative(loudDoc, loudSpec, loudPlan));
    }

    [Fact]
    public void ComputeOutput_WhenTripleDuplicatePolicyVaries_ThenFingerprintsUnchanged()
    {
        // §6.1/D-077: duplicate_object_policy does not apply to triple (the subject is never a
        // duplicate-object condition) and triple emit ignores it. It rides in the SHARED binding
        // payload, so it must perturb neither output fingerprint (.cxt and .dat) — else two specs
        // with identical output bytes would hash differently.
        var none = TripleOutputFingerprints(null);

        Assert.Equal(none, TripleOutputFingerprints(DuplicateObjectPolicy.Keep));
        Assert.Equal(none, TripleOutputFingerprints(DuplicateObjectPolicy.Dedupe));
    }

    private static (string Cxt, string Dat) TripleOutputFingerprints(DuplicateObjectPolicy? defaultsPolicy)
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("p", new PredicateSourceSection("pred", ValueType: null),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["x", "y"])],
            binding: DocumentFixtures.TripleBinding(
                new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(2))),
            defaults: defaultsPolicy is { } p ? new DefaultsSection(null, null, null, p, null, null) : null);

        var (_, spec, plan) = Prepare(document, new SourceSchema(3));
        var cxt = ComputeCxt(
            plan, spec, new CxtFingerprintInputs(LabelStyle.Native, BinLabelUnicode: false, LineEnding.Lf, TrailingNewline: true));
        var dat = ComputeDat(
            plan, spec, new DatFingerprintInputs(BaseIndex: 1, LineEnding.Lf, NonemptyLineTrailingSpace: false, EmptyLineTrailingSpace: false));
        return (cxt, dat);
    }

    [Fact]
    public void VerifyStored_WhenNoStoredFingerprints_ThenSilent()
    {
        var (document, spec, plan) = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));

        Assert.Empty(VerifyStored(document, ComputeNative(document, spec, plan)));
    }

    [Fact]
    public void VerifyStored_WhenAllStoredMatch_ThenSilent()
    {
        var bare = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));
        var computed = ComputeNative(bare.Document, bare.Spec, bare.Plan);

        var frozen = TomlFixtures.MiniMushroom.Replace(
            "version = 1",
            $"""
            version = 1
            schema_fingerprint = "{computed.SchemaFingerprint}"
            cxt_output_fingerprint = "{computed.CxtOutputFingerprint}"
            dat_output_fingerprint = "{computed.DatOutputFingerprint}"
            """,
            StringComparison.Ordinal);
        var (document, spec, plan) = Pipeline(frozen, new SourceSchema(5));

        Assert.Empty(VerifyStored(document, ComputeNative(document, spec, plan)));
    }

    [Fact]
    public void VerifyStored_WhenAllThreeStale_ThenThreeIndependentWarnings()
    {
        var frozen = TomlFixtures.MiniMushroom.Replace(
            "version = 1",
            """
            version = 1
            schema_fingerprint = "sha256:0000"
            cxt_output_fingerprint = "sha256:1111"
            dat_output_fingerprint = "sha256:2222"
            """,
            StringComparison.Ordinal);
        var (document, spec, plan) = Pipeline(frozen, new SourceSchema(5));
        var computed = ComputeNative(document, spec, plan);

        var diagnostics = VerifyStored(document, computed, "frozen.toml");

        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.All(diagnostics, d => Assert.Equal("frozen.toml", d.Location?.File));
        Assert.Collection(
            diagnostics,
            d => Assert.Equal(DiagnosticCode.SchemaFingerprintStale, d.Code),
            d => Assert.Equal(DiagnosticCode.CxtOutputFingerprintStale, d.Code),
            d => Assert.Equal(DiagnosticCode.DatOutputFingerprintStale, d.Code));
        Assert.Contains("sha256:0000", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains(computed.SchemaFingerprint, diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyStored_WhenOnlyOneFieldStale_ThenExactlyItsWarning()
    {
        var frozen = TomlFixtures.MiniMushroom.Replace(
            "version = 1",
            """
            version = 1
            dat_output_fingerprint = "sha256:feed"
            """,
            StringComparison.Ordinal);
        var (document, spec, plan) = Pipeline(frozen, new SourceSchema(5));

        var diagnostics = VerifyStored(
            document, ComputeNative(document, spec, plan));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.DatOutputFingerprintStale, diagnostic.Code);
        Assert.Null(diagnostic.Location);
    }

    [Fact]
    public void ComputeNative_WhenCutSpelled30Or30Point0Or3e1_ThenSchemaFingerprintIsIdentical()
    {
        // The roadmap's fingerprint-stability golden: the canonical encoding
        // hashes the parsed value in shortest invariant form, so the three TOML
        // spellings collapse (D-053/D-069).
        var plain = ComputeFor(CutSpellingTemplate.Replace("{CUT}", "30", StringComparison.Ordinal));
        var pointZero = ComputeFor(CutSpellingTemplate.Replace("{CUT}", "30.0", StringComparison.Ordinal));
        var exponent = ComputeFor(CutSpellingTemplate.Replace("{CUT}", "3e1", StringComparison.Ordinal));

        Assert.Equal(plain, pointZero);
        Assert.Equal(plain, exponent);
    }

    // --- equal_width manual is fully-frozen-eligible (§14/§11.4, D-089/D-102) --

    private const string EqualWidthManualTemplate = """
        [spec]
        version = 1

        [binding]
        shape = "wide"

        [[attribute]]
        name = "score"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "equal_width", bins = 4, range = "manual", vmin = 0, vmax = 100 }
        scale = { kind = "nominal" }
        """;

    [Fact]
    public void VerifyStored_WhenManualEqualWidthFingerprintsFrozen_ThenSilent()
    {
        // §11.4/§14/D-089: a manual range is spec-determined — its cuts come from the spec text
        // alone, so unlike a data-derived range it IS eligible for stored fingerprints. This is
        // the stored-fingerprint round-trip: compute, freeze into [spec], re-read, verify silent.
        var computed = ComputeFor(EqualWidthManualTemplate);
        var frozen = EqualWidthManualTemplate.Replace(
            "version = 1",
            $"""
            version = 1
            schema_fingerprint = "{computed.SchemaFingerprint}"
            cxt_output_fingerprint = "{computed.CxtOutputFingerprint}"
            dat_output_fingerprint = "{computed.DatOutputFingerprint}"
            """,
            StringComparison.Ordinal);
        var (document, spec, plan) = Pipeline(frozen, new SourceSchema(1));

        Assert.Empty(VerifyStored(document, ComputeNative(document, spec, plan)));
    }

    [Fact]
    public void ComputeNative_WhenManualEqualWidthBoundsSpelledDifferently_ThenFingerprintsIdentical()
    {
        // The §14 canonical number rule reaches the authored bounds too: 0/0.0 and 100/1e2 are one
        // spec, exactly as the 30/30.0/3e1 cut golden pins for manual_cuts.
        var plain = ComputeFor(EqualWidthManualTemplate);
        var spelled = ComputeFor(EqualWidthManualTemplate.Replace(
            "vmin = 0, vmax = 100", "vmin = 0.0, vmax = 1e2", StringComparison.Ordinal));

        Assert.Equal(plain, spelled);
    }

    [Fact]
    public void ComputeNative_WhenEqualWidthPrecisionDiffers_ThenOutputFingerprintsDiffer()
    {
        // precision is authored configuration the discretizer sub-object carries (D-094), so two
        // otherwise-identical specs that round differently must not hash alike — even when, as
        // here, the effective cuts happen to coincide.
        var exact = ComputeFor(EqualWidthManualTemplate);
        var rounded = ComputeFor(EqualWidthManualTemplate.Replace(
            "vmax = 100 }", "vmax = 100, precision = { round_to = 1 } }", StringComparison.Ordinal));

        Assert.Equal(exact.SchemaFingerprint, rounded.SchemaFingerprint); // same effective bins
        Assert.NotEqual(exact.CxtOutputFingerprint, rounded.CxtOutputFingerprint);
        Assert.NotEqual(exact.DatOutputFingerprint, rounded.DatOutputFingerprint);
    }

    [Fact]
    public void ComputeNative_WhenComposedEqualsFlat_ThenAllThreeFingerprintsIdentical()
    {
        // §13: fingerprints are computed over the resolved (fully merged) plan,
        // so a derived spec and its flat equivalent fingerprint identically —
        // no encoder change is involved, only composition (D-078).
        var flat = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));
        var composed = ComposedPipeline(TomlFixtures.MiniMushroomDerived, new SourceSchema(5));

        Assert.Equal(
            ComputeNative(flat.Document, flat.Spec, flat.Plan),
            ComputeNative(composed.Document, composed.Spec, composed.Plan));
    }

    [Fact]
    public void VerifyStored_WhenBaseStoresGarbageFingerprints_ThenComposedVerifiesSilently()
    {
        // §13: base-stored fingerprints are ignored when resolving a derived
        // spec — they never reach the composed [spec], so nothing goes stale.
        var garbageBase = TomlFixtures.MiniMushroomBase.Replace(
            "version = 1",
            """
            version = 1
            schema_fingerprint = "sha256:0000"
            cxt_output_fingerprint = "sha256:1111"
            dat_output_fingerprint = "sha256:2222"
            """,
            StringComparison.Ordinal);
        var (document, spec, plan) = ComposedPipeline(TomlFixtures.MiniMushroomDerived, new SourceSchema(5), garbageBase);

        Assert.Empty(VerifyStored(document, ComputeNative(document, spec, plan)));
    }

    [Fact]
    public void VerifyStored_WhenDerivedStoresFingerprints_ThenVerifiedAgainstComposedPlan()
    {
        // A derived spec's own stored fingerprints survive composition and are
        // verified against the composed plan: frozen values are silent, and a
        // perturbed one raises exactly its warning.
        var bare = ComposedPipeline(TomlFixtures.MiniMushroomDerived, new SourceSchema(5));
        var computed = ComputeNative(bare.Document, bare.Spec, bare.Plan);

        var frozen = TomlFixtures.MiniMushroomDerived.Replace(
            "version = 1",
            $"""
            version = 1
            schema_fingerprint = "{computed.SchemaFingerprint}"
            cxt_output_fingerprint = "{computed.CxtOutputFingerprint}"
            dat_output_fingerprint = "{computed.DatOutputFingerprint}"
            """,
            StringComparison.Ordinal);
        var silent = ComposedPipeline(frozen, new SourceSchema(5));
        Assert.Empty(VerifyStored(
            silent.Document, ComputeNative(silent.Document, silent.Spec, silent.Plan)));

        var perturbed = frozen.Replace(computed.SchemaFingerprint, "sha256:0000", StringComparison.Ordinal);
        var stale = ComposedPipeline(perturbed, new SourceSchema(5));
        var diagnostic = Assert.Single(VerifyStored(
            stale.Document, ComputeNative(stale.Document, stale.Spec, stale.Plan)));
        Assert.Equal(DiagnosticCode.SchemaFingerprintStale, diagnostic.Code);
    }

    [Fact]
    public void ComputeNative_WhenMiniMushroom_ThenStabilityBaselineHolds()
    {
        // End-to-end stability baseline over the §19.1 worked example, pinned at
        // Slice E: a diff means the canonical encoding or the resolved plan
        // changed, and either needs an fp_format bump or a decision entry — not
        // a baseline edit.
        var (document, spec, plan) = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));

        Assert.Equal(
            new ComputedFingerprints(
                "sha256:6b97a3f3fcd2782781fd2420edde29259848281bfa4e887fc91258e435511f05",
                "sha256:6e1507c6735d0d4abcd6b146930d43b33a624746bc0d995accd2eed287b5752e",
                "sha256:2716ab601e2297bd61ee665b8361045ddc2806679498cf819b3464961715a124"),
            ComputeNative(document, spec, plan));
    }

    [Fact]
    public void ComputeNative_WhenMiniAdult_ThenStabilityBaselineHolds()
    {
        // As above, over §19.2 — covers manual_cuts, ordered_cuts and ordinal.
        var (document, spec, plan) = Pipeline(TomlFixtures.MiniAdult, new SourceSchema(6));

        Assert.Equal(
            new ComputedFingerprints(
                "sha256:651b257b061eae1a2162982fab8af67816b987075bfbdc27f751248463b6798f",
                "sha256:fecd0d102ae1dffa272237031e290e1d0429c256d8b31caac0d5ca7237296bf0",
                "sha256:1a25e5b44a116506a705c3957a9b8a4869d008c44e96b91fb6bf5e56774377c0"),
            ComputeNative(document, spec, plan));
    }

    [Fact]
    public void ComputeNative_WhenPlanIsFromADifferentResolution_ThenThrows()
    {
        // §14/D-098: ComputeNative accepts only the paired document/plan — a plan produced from a
        // different resolution fails the reference-identity guard.
        var a = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));
        var b = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5)); // a distinct resolution/plan

        Assert.Throws<ArgumentException>(() => SpecFingerprints.ComputeNative(a.Document, b.Plan));
    }

    [Fact]
    public void Resolve_WhenOriginalDocumentMutatedAfterResolve_ThenSnapshotAndFingerprintsUnchanged()
    {
        // D-098: the ResolvedDocument holds an immutable deep snapshot, so a caller mutating the
        // original document's reader-produced collections after resolution cannot reach fingerprinting.
        Assert.True(SpecReader.Read(TomlFixtures.MiniMushroom).TryGetValue(out var document));
        Assert.True(SpecResolver.Resolve(document, new SourceSchema(5)).TryGetValue(out var resolvedDoc));
        var plan = ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(resolvedDoc.Resolved)).Value!;
        var before = SpecFingerprints.ComputeNative(resolvedDoc, plan);
        var snapshotCount = resolvedDoc.Document.Attributes.Count;

        Assert.IsType<ImmutableArray<AttributeSection>>(resolvedDoc.Document.Attributes);
        if (document.Attributes is List<AttributeSection> mutable)
        {
            mutable.Add(mutable[0]);
        }

        Assert.Equal(snapshotCount, resolvedDoc.Document.Attributes.Count);
        Assert.Equal(before, SpecFingerprints.ComputeNative(resolvedDoc, plan));
    }

    private static string MinimalSpec() => """
        [spec]
        version = 1

        [binding]
        shape = "wide"

        [[attribute]]
        name = "a"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["x"]
        """;

    private static ComputedFingerprints ComputeFor(string toml)
    {
        var (document, spec, plan) = Pipeline(toml, new SourceSchema(1));
        return ComputeNative(document, spec, plan);
    }

    // ComputeNative now takes the paired ResolvedDocument (D-098); the plan carries the
    // CalibratedSpec whose Resolution IS resolvedDoc.Resolved, so the identity guard holds. The
    // tuple's Document field is the ResolvedDocument; these thin shims keep the prior call shapes.
    private static ComputedFingerprints ComputeNative(ResolvedDocument document, BedrockSpec spec, ConversionPlan plan)
    {
        _ = spec;
        return SpecFingerprints.ComputeNative(document, plan);
    }

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

    private static IReadOnlyList<BedrockDiagnostic> VerifyStored(
        ResolvedDocument document, ComputedFingerprints computed, string? file = null) =>
        SpecFingerprints.VerifyStored(document.Document, computed, file);

    private static (ResolvedDocument Document, BedrockSpec Spec, ConversionPlan Plan) Pipeline(
        string toml, SourceSchema schema)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document),
            string.Join("; ", read.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        return Prepare(document, schema);
    }

    /// <summary>
    /// The composed twin of <see cref="Pipeline"/>: reads the derived TOML,
    /// composes it over "mushroom-base.toml", then resolves and plans the
    /// composed document (D-078).
    /// </summary>
    private static (ResolvedDocument Document, BedrockSpec Spec, ConversionPlan Plan) ComposedPipeline(
        string derivedToml, SourceSchema schema, string? baseToml = null)
    {
        var read = SpecReader.Read(derivedToml);
        Assert.True(read.TryGetValue(out var derived),
            string.Join("; ", read.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var composed = SpecComposer.Compose(
            derived, "derived.toml", new SingleBaseSource(baseToml ?? TomlFixtures.MiniMushroomBase));
        Assert.True(composed.TryGetValue(out var document),
            string.Join("; ", composed.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        return Prepare(document, schema);
    }

    // Resolve → fully-declared calibrated state → plan (the M4 pipeline, D-098). The plan's
    // Calibrated.Resolution is resolvedDoc.Resolved, so ComputeNative's reference-identity guard holds.
    private static (ResolvedDocument Document, BedrockSpec Spec, ConversionPlan Plan) Prepare(
        SpecDocument document, SourceSchema schema)
    {
        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDoc),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var planned = ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(resolvedDoc.Resolved));
        Assert.True(planned.TryGetValue(out var plan),
            string.Join("; ", planned.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        return (resolvedDoc, resolvedDoc.Resolved.Spec, plan);
    }

    private sealed class SingleBaseSource(string toml) : ISpecTextSource
    {
        public SpecSourceText? Load(string reference, string referrerKey) =>
            reference == "mushroom-base.toml" ? new SpecSourceText(reference, toml) : null;
    }
}
