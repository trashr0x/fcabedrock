using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// §13 composition (D-027/D-052/D-078): the chain walk (root/base version
/// gates, missing-base, cycles, base parse aggregation) and the merge fold —
/// per-field binding/defaults with presence semantics, whole-value nested
/// binding tables, per-leaf output, position-preserving whole-attribute
/// override, template/matcher carrier composition, per-spec [spec]/provenance,
/// and the composed≡flat canonical-text equivalence.
/// </summary>
public sealed class SpecComposerTests
{
    // --- Chain mechanics ---

    [Fact]
    public void Compose_WhenNoExtends_ThenDocumentPassesThroughUnchanged()
    {
        var document = Read(TomlFixtures.MiniMushroom);
        var source = new InMemorySpecTextSource();

        var result = SpecComposer.Compose(document, "root.toml", source);

        Assert.True(result.TryGetValue(out var composed));
        Assert.Same(document, composed);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, source.Loads);
    }

    [Theory]
    [InlineData("[binding]\nshape = \"wide\"\n")] // no [spec] at all
    [InlineData("[spec]\nversion = 2\nextends = \"base.toml\"\n")]
    public void Compose_WhenRootVersionMissingOrNotOne_ThenFatalBeforeAnyLoad(string rootToml)
    {
        // D-078: the root gate precedes any source consultation, so an
        // unversioned root can never drive extends semantics or surface a
        // missing-base diagnostic first.
        var source = new InMemorySpecTextSource();

        var result = SpecComposer.Compose(Read(rootToml), "root.toml", source);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecVersionUnsupported, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        Assert.Equal("root.toml", diagnostic.Location?.File);
        Assert.Equal(0, source.Loads);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Compose_WhenThreeFileChain_ThenMergeAppliesBaseMostFirst()
    {
        // missing_token: grandbase only → survives; delimiter: grandbase + root
        // → root's; has_header: grandbase + mid → mid's; description/[spec] are
        // per-spec → root's. Attribute "a": overridden at every level → root's
        // version, in the grandbase position; mid's new "b" precedes root's "c".
        var source = new InMemorySpecTextSource()
            .Add("grand.toml",
                "[spec]\nversion = 1\ndescription = \"grand\"\n" +
                "[binding]\nshape = \"wide\"\nmissing_token = \"?\"\ndelimiter = \";\"\nhas_header = false\n" +
                Attribute("a", 0, "grand") + Attribute("z", 9, "grand"))
            .Add("mid.toml",
                "[spec]\nversion = 1\nextends = \"grand.toml\"\ndescription = \"mid\"\n" +
                "[binding]\nhas_header = true\n" +
                Attribute("a", 0, "mid") + Attribute("b", 1, "mid"));
        var root = Read(
            "[spec]\nversion = 1\nextends = \"mid.toml\"\ndescription = \"root\"\n" +
            "[binding]\ndelimiter = \"|\"\n" +
            Attribute("a", 0, "root") + Attribute("c", 2, "root"));

        var composed = ComposeOk(root, "root.toml", source);

        Assert.Equal("root", composed.Spec?.Description);
        Assert.Null(composed.Spec?.Extends);
        Assert.Equal("?", composed.Binding?.MissingToken);
        Assert.Equal('|', composed.Binding?.Delimiter);
        Assert.True(composed.Binding?.HasHeader);
        Assert.Equal(["a", "z", "b", "c"], composed.Attributes.Select(a => a.Name));
        Assert.Equal("root", composed.Attributes[0].Description); // overridden in place at each step
        Assert.Equal("grand", composed.Attributes[1].Description);
    }

    [Fact]
    public void Compose_WhenSelfExtends_ThenSpecExtendsCycleFatal()
    {
        var toml = "[spec]\nversion = 1\nextends = \"root.toml\"\n";
        var source = new InMemorySpecTextSource().Add("root.toml", toml);

        var result = SpecComposer.Compose(Read(toml), "root.toml", source);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecExtendsCycle, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Compose_WhenCycleAtDepthTwo_ThenChainRendersInMessage()
    {
        var source = new InMemorySpecTextSource()
            .Add("a.toml", "[spec]\nversion = 1\nextends = \"b.toml\"\n")
            .Add("b.toml", "[spec]\nversion = 1\nextends = \"a.toml\"\n");

        var result = SpecComposer.Compose(Read("[spec]\nversion = 1\nextends = \"b.toml\"\n"), "a.toml", source);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecExtendsCycle, diagnostic.Code);
        Assert.Contains("a.toml -> b.toml -> a.toml", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("b.toml", diagnostic.Location?.File); // the referrer whose extends closes the cycle
    }

    [Fact]
    public void Compose_WhenBaseMissing_ThenNotFoundNamesReferenceAndReferrer()
    {
        var result = SpecComposer.Compose(
            Read("[spec]\nversion = 1\nextends = \"../missing.toml\"\n"),
            "derived.toml",
            new InMemorySpecTextSource());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecExtendsNotFound, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        Assert.Contains("../missing.toml", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("derived.toml", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Compose_WhenBaseTomlInvalid_ThenBaseParseDiagnosticsCarryBaseLocation()
    {
        var source = new InMemorySpecTextSource().Add("base.toml", "not = valid = toml\n");

        var result = SpecComposer.Compose(Read(DerivedMinimal), "derived.toml", source);

        Assert.False(result.TryGetValue(out _));
        var diagnostic = result.Diagnostics.First(d => d.Code == DiagnosticCode.SpecTomlInvalid);
        Assert.Equal("base.toml", diagnostic.Location?.File);
    }

    [Theory]
    [InlineData("[spec]\nversion = 3\n")]
    [InlineData("[binding]\nshape = \"wide\"\n")] // no [spec] at all
    public void Compose_WhenBaseVersionMissingOrNotOne_ThenFatalAtBaseLocation(string baseToml)
    {
        var source = new InMemorySpecTextSource().Add("base.toml", baseToml);

        var result = SpecComposer.Compose(Read(DerivedMinimal), "derived.toml", source);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecVersionUnsupported, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        Assert.Equal("base.toml", diagnostic.Location?.File);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Compose_WhenComposed_ThenExtendsIsConsumed()
    {
        var composed = ComposeBaseDerived();

        Assert.Null(composed.Spec?.Extends);
        Assert.DoesNotContain("extends", SpecWriter.Write(composed), StringComparison.Ordinal);
    }

    // --- Merge semantics ---

    [Fact]
    public void Compose_WhenBindingAndDefaultsAuthoredInBoth_ThenDerivedOverridesPerField()
    {
        var composed = ComposeBaseDerived();

        Assert.Equal('|', composed.Binding?.Delimiter); // authored in both → derived
        Assert.Equal(MissingPolicy.Skip, composed.Defaults?.MissingPolicy); // authored in both → derived
    }

    [Fact]
    public void Compose_WhenDerivedFieldUnauthored_ThenAuthoredBaseFieldSurvives()
    {
        // The presence core: null = unauthored never overrides an authored value.
        var composed = ComposeBaseDerived();

        Assert.Equal("?", composed.Binding?.MissingToken);
        Assert.True(composed.Binding?.HasHeader);
        Assert.Equal(Core.Scaling.OrdinalDirection.Ge, composed.Defaults?.OrdinalDirection);
    }

    [Fact]
    public void Compose_WhenObjectKeyAuthoredInBoth_ThenDerivedTableReplacesWhole()
    {
        // D-078 refinement of §13 rule 1: no leftover base fields — a per-leaf
        // merge would compose a row_index-mode key still carrying base's column.
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
            "[binding.object_key]\nmode = \"column\"\ncolumn = \"id\"\n");
        var root = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n" +
            "[binding.object_key]\nmode = \"row_index\"\n");

        var composed = ComposeOk(root, "derived.toml", source);

        Assert.Equal(ObjectKeyMode.RowIndex, composed.Binding?.ObjectKey?.Mode);
        Assert.Null(composed.Binding?.ObjectKey?.Column);
    }

    [Fact]
    public void Compose_WhenTripleColumnsAuthoredInBoth_ThenDerivedTableReplacesWhole()
    {
        // Same D-078 rule for the triple role map: a partial remap must not
        // inherit the other roles (silent duplicate indices, unvalidated to M3).
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n[binding]\nshape = \"triple\"\nordering = \"subject_grouped\"\n" +
            "columns = { subject = 0, predicate = 1, value = 2 }\n");
        var root = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n" +
            "[binding]\ncolumns = { subject = 2, predicate = 0, value = 1 }\n");

        var composed = ComposeOk(root, "derived.toml", source);

        Assert.Equal(
            new TripleColumnsSection(new IndexColumnRef(2), new IndexColumnRef(0), new IndexColumnRef(1)),
            composed.Binding?.Columns);
        Assert.Equal(TripleOrdering.SubjectGrouped, composed.Binding?.Ordering); // scalar field still inherits
    }

    [Fact]
    public void Compose_WhenOutputAuthoredInBoth_ThenLeavesMergePerField()
    {
        // §13 rule 6's own example: base cxt line-endings and derived cxt
        // trailing-newline both survive.
        var composed = ComposeBaseDerived();

        Assert.Equal(LineEndings.Crlf, composed.Output?.Cxt?.LineEndings);
        Assert.False(composed.Output?.Cxt?.TrailingNewline);
        Assert.True(composed.Output?.BinLabelUnicode);
    }

    [Fact]
    public void Compose_WhenDerivedOverridesAttribute_ThenPositionAndOrderStable()
    {
        // D-052: base [a, b, c]; derived overrides b, adds d → [a, b', c, d].
        var composed = ComposeBaseDerived();

        Assert.Equal(["a", "b", "c", "d"], composed.Attributes.Select(a => a.Name));
        Assert.IsType<DichotomicScaleSection>(composed.Attributes[1].Scale); // b' is the derived section
    }

    [Fact]
    public void Compose_WhenOverrideOmitsInheritedFields_ThenTheyAreDropped()
    {
        // D-052 whole-attribute replacement: base a's restrict_to and domain are
        // gone unless the override repeats them.
        var composed = ComposeBaseDerived();

        var b = composed.Attributes[1];
        Assert.Null(b.RestrictTo);
        Assert.Null(b.DeclaredDomain);
    }

    [Fact]
    public void Compose_WhenDerivedSuppressesWithIncludeFalse_ThenComposedAttributeIsExcluded()
    {
        // §13/D-052: suppression repeats name + source (whole-attribute
        // replacement); the composed spec resolves cleanly with the attribute off.
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
            Attribute("a", 0) + Attribute("keep", 1));
        var root = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n" +
            "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 0 }\ninclude = false\n");

        var composed = ComposeOk(root, "derived.toml", source);
        var resolved = SpecResolver.Resolve(composed);

        Assert.True(resolved.TryGetValue(out var spec));
        Assert.Equal(["a", "keep"], spec.Attributes.Select(a => a.Name)); // position preserved
        Assert.False(spec.Attributes[0].Include);
    }

    [Fact]
    public void Compose_WhenDerivedDuplicatesAnAttributeName_ThenResolveSeamRejects()
    {
        // Fail-closed duplicate discipline (D-078): the merge never collapses
        // authoring duplicates — AttributeNameDuplicate owns the reject at the
        // resolve seam (D-080), exactly as it would for the same duplicate in a
        // flat file.
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" + Attribute("a", 0));
        var root = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n" +
            Attribute("a", 1, "first override") + Attribute("a", 2, "authoring duplicate"));

        var composed = ComposeOk(root, "derived.toml", source);

        Assert.Equal(["a", "a"], composed.Attributes.Select(a => a.Name));
        var resolved = SpecResolver.Resolve(composed);
        Assert.False(resolved.TryGetValue(out _));
        Assert.Contains(resolved.Diagnostics, d => d.Code == DiagnosticCode.AttributeNameDuplicate);
    }

    [Fact]
    public void Compose_WhenBaseHasProvenance_ThenComposedCarriesDerivedProvenanceOnly()
    {
        // §13 rule 7: provenance is per-spec, never inherited — derived has
        // none, so the composed document has none.
        var composed = ComposeBaseDerived();

        Assert.Null(composed.Provenance);
    }

    [Fact]
    public void Compose_WhenBaseStoresFingerprints_ThenComposedCarriesDerivedValuesOnly()
    {
        // §13: base-stored fingerprints are ignored, never merged.
        var composed = ComposeBaseDerived();

        Assert.Null(composed.Spec?.SchemaFingerprint);
        Assert.Null(composed.Spec?.CxtOutputFingerprint);
        Assert.Null(composed.Spec?.DatOutputFingerprint);
    }

    [Fact]
    public void Compose_WhenTemplatesShareId_ThenDerivedReplacesInPlaceAndNewIdsAppend()
    {
        // §13 rule 3 as carrier composition (D-078): same id replaces in place,
        // base position kept; new ids append after all inherited templates.
        var composed = ComposeBaseDerived();

        Assert.Equal(["shared", "base_only", "derived_only"], composed.Templates.Select(t => t.Id));
        Assert.Equal(["Y", "N"], composed.Templates[0].DeclaredDomain); // derived's body won
    }

    [Fact]
    public void Compose_WhenMatchersInBoth_ThenBaseThenDerivedOrder()
    {
        // §13 rule 4: concatenation, base first — derived precedence comes from
        // §9.2 last-match-wins when M6 applies them.
        var composed = ComposeBaseDerived();

        Assert.Equal(["^base", "^derived"], composed.Matchers.Select(m => m.Match?.NameRegex));
    }

    [Fact]
    public void Compose_WhenTripleBase_ThenComposedStillRejectsAtPlan()
    {
        // Composition is undisturbed by the D-082 triple resolution: the composed
        // triple spec resolves fully and the planner still refuses conversion.
        var source = new InMemorySpecTextSource().Add("base.toml", TomlFixtures.MiniAdultTriples);
        var root = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n" +
            "[binding]\nordering = \"unordered\"\n");

        var composed = ComposeOk(root, "derived.toml", source);

        Assert.Equal(TripleOrdering.Unordered, composed.Binding?.Ordering);
        Assert.True(SpecResolver.Resolve(composed).TryGetValue(out var spec));
        Assert.Equal(SourceShape.Triple, spec.Binding.Shape);
        Assert.NotEmpty(spec.Attributes);

        var plan = ConversionPlanner.Plan(spec, new SourceSchema(3));
        Assert.Contains(plan.Diagnostics, d => d.Code == DiagnosticCode.TripleSourceNotImplementedV1);
    }

    // --- Equivalence ---

    [Fact]
    public void Compose_WhenComposedEqualsFlatSpec_ThenCanonicalTextIsIdentical()
    {
        // §13: a derived spec and its flat equivalent are the same document —
        // proven at the canonical-text level (the fingerprint face is locked in
        // SpecFingerprintsTests).
        var composed = ComposeOk(Read(TomlFixtures.MiniMushroomDerived), "derived.toml",
            new InMemorySpecTextSource().Add("mushroom-base.toml", TomlFixtures.MiniMushroomBase));

        Assert.Equal(SpecWriter.Write(Read(TomlFixtures.MiniMushroom)), SpecWriter.Write(composed));
    }

    // --- Fixtures & helpers ---

    private const string DerivedMinimal = "[spec]\nversion = 1\nextends = \"base.toml\"\n";

    /// <summary>The workhorse base: every §13 merge rule has an input here.</summary>
    private const string BaseToml = """
        [spec]
        version = 1
        schema_fingerprint = "sha256:stalebase"
        cxt_output_fingerprint = "sha256:stalebase"
        dat_output_fingerprint = "sha256:stalebase"
        description = "base"

        [provenance]
        author = "Base Author"

        [binding]
        shape = "wide"
        delimiter = ";"
        has_header = true
        missing_token = "?"

        [defaults]
        missing_policy = "as_attribute"
        ordinal_direction = "ge"

        [output]
        bin_label_unicode = true

        [output.cxt]
        line_endings = "crlf"

        [[template]]
        id = "shared"
        declared_domain = ["Yes", "No"]

        [[template]]
        id = "base_only"
        declared_domain = ["a"]

        [[matcher]]
        match = { name_regex = "^base" }
        template = "shared"

        [[attribute]]
        name = "a"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["x", "y"]

        [[attribute]]
        name = "b"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["m"]
        restrict_to = ["m"]

        [[attribute]]
        name = "c"
        source = { kind = "column", index = 2 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["n"]
        """;

    /// <summary>The workhorse derived spec over <see cref="BaseToml"/>.</summary>
    private const string DerivedToml = """
        [spec]
        version = 1
        extends = "base.toml"
        description = "derived"

        [binding]
        delimiter = "|"

        [defaults]
        missing_policy = "skip"

        [output.cxt]
        trailing_newline = false

        [[template]]
        id = "shared"
        declared_domain = ["Y", "N"]

        [[template]]
        id = "derived_only"
        declared_domain = ["d"]

        [[matcher]]
        match = { name_regex = "^derived" }
        template = "shared"

        [[attribute]]
        name = "b"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "dichotomic", true_value = "m" }

        [[attribute]]
        name = "d"
        source = { kind = "column", index = 3 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["q"]
        """;

    private static SpecDocument ComposeBaseDerived() =>
        ComposeOk(Read(DerivedToml), "derived.toml", new InMemorySpecTextSource().Add("base.toml", BaseToml));

    private static string Attribute(string name, int index, string? description = null) =>
        $"[[attribute]]\nname = \"{name}\"\nsource = {{ kind = \"column\", index = {index} }}\n" +
        (description is null ? string.Empty : $"description = \"{description}\"\n") +
        "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\ndeclared_domain = [\"v\"]\n";

    private static SpecDocument ComposeOk(SpecDocument document, string documentKey, ISpecTextSource source)
    {
        var result = SpecComposer.Compose(document, documentKey, source);
        Assert.True(result.TryGetValue(out var composed),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return composed;
    }

    private static SpecDocument Read(string toml)
    {
        var result = SpecReader.Read(toml);
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return document;
    }

    /// <summary>
    /// Dictionary-backed source: canonical key = the reference verbatim. Counts
    /// loads so tests can assert the root gate precedes any consultation.
    /// </summary>
    private sealed class InMemorySpecTextSource : ISpecTextSource
    {
        private readonly Dictionary<string, string> _specs = new(StringComparer.Ordinal);

        public int Loads { get; private set; }

        public InMemorySpecTextSource Add(string key, string toml)
        {
            _specs[key] = toml;
            return this;
        }

        public SpecSourceText? Load(string reference, string referrerKey)
        {
            Loads++;
            return _specs.TryGetValue(reference, out var toml) ? new SpecSourceText(reference, toml) : null;
        }
    }
}
