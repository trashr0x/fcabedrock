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

        var computed = SpecFingerprints.ComputeNative(document, spec, plan);

        Assert.Equal(FingerprintCalculator.ComputeSchemaFingerprint(plan), computed.SchemaFingerprint);
        Assert.Equal(
            FingerprintCalculator.ComputeCxtOutputFingerprint(
                plan, spec, new CxtFingerprintInputs(LabelStyle.Native, BinLabelUnicode: false, LineEnding.Lf, TrailingNewline: true)),
            computed.CxtOutputFingerprint);
        Assert.Equal(
            FingerprintCalculator.ComputeDatOutputFingerprint(
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

        var computed = SpecFingerprints.ComputeNative(document, spec, plan);

        Assert.Equal(
            FingerprintCalculator.ComputeCxtOutputFingerprint(
                plan, spec, new CxtFingerprintInputs(LabelStyle.Native, BinLabelUnicode: true, LineEnding.Crlf, TrailingNewline: false)),
            computed.CxtOutputFingerprint);
        Assert.Equal(
            FingerprintCalculator.ComputeDatOutputFingerprint(
                plan, spec, new DatFingerprintInputs(BaseIndex: 0, LineEnding.Crlf, NonemptyLineTrailingSpace: true, EmptyLineTrailingSpace: true)),
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
            SpecFingerprints.ComputeNative(quietDoc, quietSpec, quietPlan),
            SpecFingerprints.ComputeNative(loudDoc, loudSpec, loudPlan));
    }

    [Fact]
    public void VerifyStored_WhenNoStoredFingerprints_ThenSilent()
    {
        var (document, spec, plan) = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));

        Assert.Empty(SpecFingerprints.VerifyStored(document, SpecFingerprints.ComputeNative(document, spec, plan)));
    }

    [Fact]
    public void VerifyStored_WhenAllStoredMatch_ThenSilent()
    {
        var bare = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));
        var computed = SpecFingerprints.ComputeNative(bare.Document, bare.Spec, bare.Plan);

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

        Assert.Empty(SpecFingerprints.VerifyStored(document, SpecFingerprints.ComputeNative(document, spec, plan)));
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
        var computed = SpecFingerprints.ComputeNative(document, spec, plan);

        var diagnostics = SpecFingerprints.VerifyStored(document, computed, "frozen.toml");

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

        var diagnostics = SpecFingerprints.VerifyStored(
            document, SpecFingerprints.ComputeNative(document, spec, plan));

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

    [Fact]
    public void ComputeNative_WhenComposedEqualsFlat_ThenAllThreeFingerprintsIdentical()
    {
        // §13: fingerprints are computed over the resolved (fully merged) plan,
        // so a derived spec and its flat equivalent fingerprint identically —
        // no encoder change is involved, only composition (D-078).
        var flat = Pipeline(TomlFixtures.MiniMushroom, new SourceSchema(5));
        var composed = ComposedPipeline(TomlFixtures.MiniMushroomDerived, new SourceSchema(5));

        Assert.Equal(
            SpecFingerprints.ComputeNative(flat.Document, flat.Spec, flat.Plan),
            SpecFingerprints.ComputeNative(composed.Document, composed.Spec, composed.Plan));
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

        Assert.Empty(SpecFingerprints.VerifyStored(document, SpecFingerprints.ComputeNative(document, spec, plan)));
    }

    [Fact]
    public void VerifyStored_WhenDerivedStoresFingerprints_ThenVerifiedAgainstComposedPlan()
    {
        // A derived spec's own stored fingerprints survive composition and are
        // verified against the composed plan: frozen values are silent, and a
        // perturbed one raises exactly its warning.
        var bare = ComposedPipeline(TomlFixtures.MiniMushroomDerived, new SourceSchema(5));
        var computed = SpecFingerprints.ComputeNative(bare.Document, bare.Spec, bare.Plan);

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
        Assert.Empty(SpecFingerprints.VerifyStored(
            silent.Document, SpecFingerprints.ComputeNative(silent.Document, silent.Spec, silent.Plan)));

        var perturbed = frozen.Replace(computed.SchemaFingerprint, "sha256:0000", StringComparison.Ordinal);
        var stale = ComposedPipeline(perturbed, new SourceSchema(5));
        var diagnostic = Assert.Single(SpecFingerprints.VerifyStored(
            stale.Document, SpecFingerprints.ComputeNative(stale.Document, stale.Spec, stale.Plan)));
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
            SpecFingerprints.ComputeNative(document, spec, plan));
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
            SpecFingerprints.ComputeNative(document, spec, plan));
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
        return SpecFingerprints.ComputeNative(document, spec, plan);
    }

    private static (SpecDocument Document, BedrockSpec Spec, ConversionPlan Plan) Pipeline(
        string toml, SourceSchema schema)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document),
            string.Join("; ", read.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var spec),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var planned = ConversionPlanner.Plan(spec, schema);
        Assert.True(planned.TryGetValue(out var plan),
            string.Join("; ", planned.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        return (document, spec, plan);
    }

    /// <summary>
    /// The composed twin of <see cref="Pipeline"/>: reads the derived TOML,
    /// composes it over "mushroom-base.toml", then resolves and plans the
    /// composed document (D-078).
    /// </summary>
    private static (SpecDocument Document, BedrockSpec Spec, ConversionPlan Plan) ComposedPipeline(
        string derivedToml, SourceSchema schema, string? baseToml = null)
    {
        var read = SpecReader.Read(derivedToml);
        Assert.True(read.TryGetValue(out var derived),
            string.Join("; ", read.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var composed = SpecComposer.Compose(
            derived, "derived.toml", new SingleBaseSource(baseToml ?? TomlFixtures.MiniMushroomBase));
        Assert.True(composed.TryGetValue(out var document),
            string.Join("; ", composed.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var spec),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var planned = ConversionPlanner.Plan(spec, schema);
        Assert.True(planned.TryGetValue(out var plan),
            string.Join("; ", planned.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        return (document, spec, plan);
    }

    private sealed class SingleBaseSource(string toml) : ISpecTextSource
    {
        public SpecSourceText? Load(string reference, string referrerKey) =>
            reference == "mushroom-base.toml" ? new SpecSourceText(reference, toml) : null;
    }
}
