using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Reader diagnostics tests (D-075): every parse-phase code with positions,
/// the D-070 three-tier discretizer dispatch, the closed per-table
/// deferred-surface set (positives per field, near-miss negatives), and
/// whole-read aggregation.
/// </summary>
public sealed class SpecReaderDiagnosticsTests
{
    [Fact]
    public void Read_WhenTomlSyntaxBroken_ThenSpecTomlInvalidFatalWithPosition()
    {
        var result = SpecReader.Read("[spec\nversion = 1\n", "broken.toml");

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == DiagnosticCode.SpecTomlInvalid &&
            d.Severity == DiagnosticSeverity.Fatal &&
            d.Location is { File: "broken.toml", Line: 1 });
    }

    [Fact]
    public void Read_WhenKeyDuplicated_ThenSpecTomlInvalid()
    {
        var result = SpecReader.Read("[spec]\nversion = 1\nversion = 2\n");

        AssertFailsWith(result, DiagnosticCode.SpecTomlInvalid);
    }

    [Fact]
    public void Read_WhenTableDuplicated_ThenSpecTomlInvalid()
    {
        var result = SpecReader.Read("[spec]\nversion = 1\n[spec]\ndescription = \"x\"\n");

        AssertFailsWith(result, DiagnosticCode.SpecTomlInvalid);
    }

    [Fact]
    public void Read_WhenKeyUnknown_ThenSpecKeyUnrecognizedWithPosition()
    {
        var result = SpecReader.Read("[binding]\nshape = \"wide\"\nmissing_polcy = \"skip\"\n", "spec.toml");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
        Assert.Contains("missing_polcy", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("spec.toml", diagnostic.Location?.File);
        Assert.Equal(3, diagnostic.Location?.Line);
        Assert.Equal(1, diagnostic.Location?.Column);
    }

    [Fact]
    public void Read_WhenTableUnknown_ThenSpecKeyUnrecognized()
    {
        AssertFailsWith(SpecReader.Read("[bindings]\nshape = \"wide\"\n"), DiagnosticCode.SpecKeyUnrecognized);
    }

    [Fact]
    public void Read_WhenRootLevelKey_ThenSpecKeyUnrecognized()
    {
        AssertFailsWith(SpecReader.Read("version = 1\n"), DiagnosticCode.SpecKeyUnrecognized);
    }

    [Theory]
    [InlineData("[spec]\nversion = 1.0\n")]
    [InlineData("[binding]\nhas_header = \"true\"\n")]
    [InlineData("[binding]\ndelimiter = \", \"\n")]
    [InlineData("[binding]\ndelimiter = \"\"\n")]
    [InlineData("[provenance]\ncreated_at = \"2026-05-09\"\n")]
    public void Read_WhenKnownKeyHasWrongShape_ThenSpecFieldInvalid(string toml)
    {
        AssertFailsWith(SpecReader.Read(toml), DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenEnumSpellingUnknown_ThenSpecFieldInvalidNamesAllowedSpellings()
    {
        var result = SpecReader.Read("[binding]\nshape = \"wibble\"\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("\"wide\" or \"triple\"", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("identity", "")]
    [InlineData("free_per_value", "")]
    [InlineData("manual_cuts", ", cuts = [30]")]
    [InlineData("ordered_cuts", ", order = [\"a\", \"b\"], cuts = [\"b\"]")]
    [InlineData("equal_width", ", bins = 4")]
    [InlineData("equal_frequency", ", bins = 4")]
    [InlineData("value_groups", ", groups = [{ label = \"g\", values = [\"a\"] }]")]
    public void Read_WhenAnyV1DiscretizerKindIsWellFormed_ThenItReadsCleanToItsCarrier(string kind, string parameters)
    {
        // D-070's tier-2 deferred-kind reject retired entirely at M4 Slice E (D-104): EVERY v1
        // discretizer kind now has a carrier, so a well-formed one of each must read clean.
        // Asserting a clean read of each kind — rather than the absence of a code that no longer
        // exists — is what keeps this test able to fail: re-deferring any kind would break it.
        var result = SpecReader.Read(Attribute($"discretizer = {{ kind = \"{kind}\"{parameters} }}"));

        Assert.True(result.IsOk, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Value!.Attributes[0].Discretizer);
    }

    [Fact]
    public void Read_WhenDiscretizerKindUnknown_ThenGenericFieldInvalid()
    {
        // D-070 tier 3: a typo gets the generic code, not the transitional one.
        AssertFailsWith(
            SpecReader.Read(Attribute("discretizer = { kind = \"identty\" }")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenScaleKindUnknown_ThenGenericFieldInvalid()
    {
        AssertFailsWith(
            SpecReader.Read(Attribute("scale = { kind = \"ordnal\" }")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Theory]
    [InlineData("[defaults]\nformal_attribute_format = \"{value}\"\n", "formal_attribute_format")]
    public void Read_WhenDeferredSurfaceAuthored_ThenSpecSurfaceNotYetSupported(string toml, string field)
    {
        // Slice F retired extends/template/matcher from this set (D-078); the
        // remaining entries belong to the naming-fidelity slice.
        var result = SpecReader.Read(toml);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecSurfaceNotYetSupported, diagnostic.Code);
        Assert.Contains(field, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("display_name = \"Education\"")]
    [InlineData("formal_attribute_format = \"{value}\"")]
    public void Read_WhenDeferredAttributeKeyAuthored_ThenSpecSurfaceNotYetSupported(string line)
    {
        var result = SpecReader.Read(Attribute(line));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecSurfaceNotYetSupported, diagnostic.Code);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
    }

    [Theory]
    [InlineData("display_name = \"Boolean\"")]
    [InlineData("formal_attribute_format = \"{value}\"")]
    public void Read_WhenDeferredKeyInsideTemplate_ThenSpecSurfaceNotYetSupported(string line)
    {
        // §9.1: a template may carry any attribute config field, so the
        // naming-deferred keys reject inside [[template]] exactly as on an
        // attribute (D-078).
        var result = SpecReader.Read($"[[template]]\nid = \"t\"\n{line}\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecSurfaceNotYetSupported, diagnostic.Code);
    }

    [Theory]
    [InlineData("name = \"a\"")]
    [InlineData("source = { kind = \"column\", index = 0 }")]
    [InlineData("description = \"per-attribute only\"")]
    public void Read_WhenTemplateDeclaresPerAttributeField_ThenSpecKeyUnrecognized(string line)
    {
        // §9.1 forbids name/source/description in a template — the D-075
        // listed-name-in-the-wrong-table stance, not the transitional code.
        var result = SpecReader.Read($"[[template]]\nid = \"t\"\n{line}\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
    }

    [Fact]
    public void Read_WhenTemplateDeclaresValueGroups_ThenItCarriesLikeAnyAttributeDiscretizer()
    {
        // A template body is the attribute config surface (§9.1/D-078), so value_groups gains its
        // carrier there too at Slice E (D-104) — it is no longer rejected for its kind. An unknown
        // key inside it still gets the ordinary SpecKeyUnrecognized, which is exactly the noise the
        // old deferred-kind reject suppressed by not walking the parameters at all.
        var result = SpecReader.Read(
            "[[template]]\nid = \"t\"\ndiscretizer = { kind = \"value_groups\", " +
            "groups = [{ label = \"g\", values = [\"a\"] }], n = 4 }\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
        Assert.Contains("n", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenMatcherHasUnknownKey_ThenSpecKeyUnrecognized()
    {
        var result = SpecReader.Read("[[matcher]]\npattern = \"x\"\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
        Assert.Contains("pattern", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[template]\nid = \"t\"\n")]
    [InlineData("[matcher]\ntemplate = \"t\"\n")]
    public void Read_WhenTemplateOrMatcherWrittenAsSingleTable_ThenSpecFieldInvalid(string toml)
    {
        AssertFailsWith(SpecReader.Read(toml), DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenValueTypeIsDate_ThenInterimSurfaceReject()
    {
        // D-038/D-075: reserved surface with no carrier; the v1 end-state is a
        // plan-phase DateValueTypeNotImplementedV1 once the carrier lands.
        AssertFailsWith(
            SpecReader.Read(Attribute(string.Empty, source: "{ kind = \"column\", index = 0, value_type = \"date\" }")),
            DiagnosticCode.SpecSurfaceNotYetSupported);
    }

    [Theory]
    [InlineData("display_nam = \"x\"")] // typo of a deferred key
    [InlineData("extends = \"base.toml\"")] // deferred key, wrong table
    public void Read_WhenNearMissOfDeferredSurface_ThenSpecKeyUnrecognizedNotTheTransitionalCode(string line)
    {
        // D-075: the deferred-surface set is closed and per-table — it must never
        // absorb typo-like unknown keys.
        var result = SpecReader.Read(Attribute(line));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
    }

    [Fact]
    public void Read_WhenDisplayNameUnderBinding_ThenSpecKeyUnrecognized()
    {
        var result = SpecReader.Read("[binding]\nshape = \"wide\"\ndisplay_name = \"x\"\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
    }

    [Fact]
    public void Read_WhenAttributeWrittenAsSingleTable_ThenSpecFieldInvalid()
    {
        AssertFailsWith(SpecReader.Read("[attribute]\nname = \"a\"\n"), DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenSingleTableWrittenAsArray_ThenSpecFieldInvalid()
    {
        AssertFailsWith(SpecReader.Read("[[binding]]\nshape = \"wide\"\n"), DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenSeveralProblems_ThenAllAggregateInOnePass()
    {
        // P-14: one read reports everything — a bad shape spelling, an unknown key, and an
        // unrecognized discretizer kind together, each on its own condition.
        var result = SpecReader.Read(
            "[binding]\nshape = \"wibble\"\nmissing_polcy = \"skip\"\n" +
            "[[attribute]]\nname = \"a\"\ndiscretizer = { kind = \"wibble_bins\" }\n");

        Assert.False(result.IsOk);
        Assert.Equal(3, result.Diagnostics.Count);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCode.SpecFieldInvalid));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecKeyUnrecognized);
    }

    [Fact]
    public void Read_WhenProblemInsideAttribute_ThenDiagnosticCarriesAttributeScope()
    {
        var result = SpecReader.Read(Attribute("wibble = 1"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
        Assert.NotNull(diagnostic.Location?.Line);
    }

    [Fact]
    public void Read_WhenSourceDeclaresNoKind_ThenSpecFieldInvalid()
    {
        AssertFailsWith(
            SpecReader.Read(Attribute(string.Empty, source: "{ index = 0 }")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenValueLabelValueNotString_ThenSpecFieldInvalid()
    {
        AssertFailsWith(
            SpecReader.Read(Attribute("value_labels = { b = 1 }")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenRestrictToEntryHasWrongShape_ThenSpecFieldInvalid()
    {
        AssertFailsWith(
            SpecReader.Read(Attribute("restrict_to = [10]")),
            DiagnosticCode.SpecFieldInvalid);
    }

    private static void AssertFailsWith(Diagnosed<FcaBedrock.Spec.Toml.SpecDocument> result, DiagnosticCode code)
    {
        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }

    private static string Attribute(string body, string source = "{ kind = \"column\", index = 0 }") =>
        $"[spec]\nversion = 1\n[[attribute]]\nname = \"a\"\nsource = {source}\n{body}\n";
}
