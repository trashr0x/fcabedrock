using FcaBedrock.Core.Calibration;
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
    // Resolve now returns Diagnosed<ResolvedDocument> (D-098); unwrap to the BedrockSpec for
    // the composition assertions, and plan the M4 way (fully-declared calibrated state).
    private static Diagnosed<BedrockSpec> Resolve(SpecDocument document, SourceSchema? schema = null)
    {
        var resolved = SpecResolver.Resolve(document, schema);
        return resolved.TryGetValue(out var doc)
            ? Diagnosed<BedrockSpec>.Ok(doc.Resolved.Spec, resolved.Diagnostics)
            : Diagnosed<BedrockSpec>.Failed(resolved.Diagnostics);
    }

    private static Diagnosed<ConversionPlan> Plan(BedrockSpec spec, SourceSchema schema) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(
            ResolvedSpec.Create(
                spec, schema,
                SourceReadSettings.Create(
                    spec.Binding.Shape, spec.Binding.Encoding, spec.Binding.Delimiter, spec.Binding.QuoteChar,
                    spec.Binding.HasHeader, spec.Binding.MissingToken, spec.Binding.Ordering),
                [])));

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
    public void Compose_WhenDatTrailingNewlineOnlyInBase_ThenDerivedInheritsIt()
    {
        // §13 rule 6 leaves-merge per field: the D-087 dat trailing_newline composes like
        // its cxt twin — a derived that overrides only base_index still inherits the base's
        // trailing_newline.
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n[output.dat]\ntrailing_newline = false\nbase_index = 0\n");
        var root = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n[output.dat]\nbase_index = 1\n");

        var composed = ComposeOk(root, "root.toml", source);

        Assert.False(composed.Output?.Dat?.TrailingNewline); // inherited from base
        Assert.Equal(1, composed.Output?.Dat?.BaseIndex);     // overridden by derived
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
    public void Compose_WhenAttributesCarryNumericRestrictions_ThenEveryFormComposesAndResolves()
    {
        // §13/D-052/D-091: composition is whole-attribute replacement over the document model, so
        // the exact numeric entry must survive a fold like any other carrier — a base's inherited
        // numeric restriction, and a derived override that replaces one.
        var source = new InMemorySpecTextSource()
            .Add("base.toml", """
                [spec]
                version = 1

                [binding]
                shape = "wide"

                [[attribute]]
                name = "age"
                source = { kind = "column", index = 0 }
                discretizer = { kind = "manual_cuts", cuts = [30], ends = "open" }
                scale = { kind = "nominal" }
                restrict_to = [{ value = 30 }, { from = 10, to = 20 }]

                [[attribute]]
                name = "stage"
                source = { kind = "column", index = 1 }
                discretizer = { kind = "manual_cuts", cuts = [5], ends = "open" }
                scale = { kind = "nominal" }
                restrict_to = [{ value = 3e1 }]
                """);
        var root = Read("""
            [spec]
            version = 1
            extends = "base.toml"

            [[attribute]]
            name = "stage"
            source = { kind = "column", index = 1 }
            discretizer = { kind = "manual_cuts", cuts = [5], ends = "open" }
            scale = { kind = "nominal" }
            restrict_to = [{ from = 3, to = 9 }]
            """);

        var composed = ComposeOk(root, "derived.toml", source);

        // Inherited verbatim…
        Assert.Equal(
            [new RestrictToNumber(30), new RestrictToRange(10, 20)],
            composed.Attributes[0].RestrictTo);

        // …and replaced wholesale (the base's { value = 3e1 } is gone, not merged).
        Assert.Equal([new RestrictToRange(3, 9)], composed.Attributes[1].RestrictTo);

        // The composed document still resolves and plans its restrictions.
        var resolved = SpecResolver.Resolve(composed, new SourceSchema(2));
        Assert.True(resolved.TryGetValue(out var doc),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal(
            [new RestrictToNumber(30), new RestrictToRange(10, 20)],
            doc!.Resolved.Spec.Attributes[0].RestrictTo);
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
        var resolved = Resolve(composed);

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
        var resolved = Resolve(composed);
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
        // §13 rule 4: concatenation, base first — which is what gives a derived matcher
        // precedence under §9.2's field-wise last-match-wins. The resolved consequence is
        // asserted in Compose_WhenBaseAndDerivedMatchersBothApply_…; this pins the carrier
        // order it depends on.
        var composed = ComposeBaseDerived();

        Assert.Equal(["^base", "^derived"], composed.Matchers.Select(m => m.Match?.NameRegex));
    }

    [Fact]
    public void Compose_WhenTripleBase_ThenComposedUnorderedPlansCleanly()
    {
        // Composition is undisturbed by the D-082 triple resolution: the composed triple spec
        // resolves fully, and the derived layer's ordering = "unordered" override is preserved
        // (asserted on Binding.Ordering). The composed spec plans cleanly; the resolved ordering rides
        // on the plan's SourceExecution and is honored later at emit (EmitTripleAsync).
        var source = new InMemorySpecTextSource().Add("base.toml", TomlFixtures.TripleSubjectGrouped);
        var root = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n" +
            "[binding]\nordering = \"unordered\"\n");

        var composed = ComposeOk(root, "derived.toml", source);

        Assert.Equal(TripleOrdering.Unordered, composed.Binding?.Ordering);
        Assert.True(Resolve(composed).TryGetValue(out var spec));
        Assert.Equal(SourceShape.Triple, spec.Binding.Shape);
        Assert.NotEmpty(spec.Attributes);

        var plan = Plan(spec, new SourceSchema(3));
        Assert.False(plan.HasErrors);
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

    // --- The naming carriers across composition (D-120) ---

    [Fact]
    public void Compose_WhenBaseDefaultsAuthorsNameFormat_ThenADerivedDefaultsAuthoringOtherFieldsPreservesIt()
    {
        // §13 rule 2 is a PER-FIELD merge, so a new [defaults] field must be carried
        // explicitly in MergeDefaults — and this is the case that would silently regress
        // otherwise: the derived [defaults] EXISTS and authors something else, so the base's
        // format is only preserved if the merge names it.
        var composed = ComposeNaming(
            baseDefaults: "formal_attribute_format = \"{column}-{value}\"",
            derivedDefaults: "missing_policy = \"skip\"");

        Assert.Equal("{column}-{value}", composed.Defaults!.FormalAttributeFormat);
        Assert.Equal(MissingPolicy.Skip, composed.Defaults.MissingPolicy);
    }

    [Fact]
    public void Compose_WhenDerivedDefaultsAuthorsNameFormat_ThenItOverridesTheBase()
    {
        var composed = ComposeNaming(
            baseDefaults: "formal_attribute_format = \"{column}-{value}\"",
            derivedDefaults: "formal_attribute_format = \"{value}\"");

        Assert.Equal("{value}", composed.Defaults!.FormalAttributeFormat);
    }

    [Fact]
    public void Compose_WhenDerivedHasNoDefaultsAtAll_ThenTheBaseFormatIsInherited()
    {
        var composed = ComposeNaming(baseDefaults: "formal_attribute_format = \"{value}\"", derivedDefaults: null);

        Assert.Equal("{value}", composed.Defaults!.FormalAttributeFormat);
    }

    [Fact]
    public void Compose_WhenBaseDefaultsFormatIsInherited_ThenItReachesTheResolvedAttributes()
    {
        // The resolver-visible end of the same contract: an inherited [defaults] format is
        // not merely carried in the document — it becomes the attribute's effective format,
        // exactly as a flat spec authoring it directly would.
        var composed = ComposeNaming(
            baseDefaults: "formal_attribute_format = \"{value}\"",
            derivedDefaults: "missing_policy = \"skip\"");

        Assert.True(Resolve(composed, new SourceSchema(1)).TryGetValue(out var spec));
        Assert.Equal("{value}", spec!.Attributes[0].NameFormat?.Text);
    }

    [Fact]
    public void Compose_WhenDerivedAttributeOverridesABaseOne_ThenTheNamingKeysTravelWithTheSection()
    {
        // §13 rule 5 is a WHOLE-SECTION replacement, so the new init properties ride along
        // automatically — and, symmetrically, the base's naming keys are dropped rather than
        // inherited field-wise. Both halves are the contract, which is why no composer change
        // was needed for attributes and templates.
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\n\n"
            + "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 0 }\n"
            + "display_name = \"Base\"\nformal_attribute_format = \"{name}\"\n");
        var derived = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n\n"
            + "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 0 }\n"
            + "display_name = \"Derived\"\n");

        var composed = ComposeOk(derived, "derived.toml", source);

        Assert.Equal("Derived", composed.Attributes[0].DisplayName);
        Assert.Null(composed.Attributes[0].FormalAttributeFormat); // the derived section wins entire
    }

    [Fact]
    public void Compose_WhenDerivedTemplateReplacesABaseOne_ThenTheNamingKeysTravelWithTheSection()
    {
        // §13 rule 3: the same whole-section semantics for templates.
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\n\n"
            + "[[template]]\nid = \"t\"\ndisplay_name = \"Base\"\nformal_attribute_format = \"{name}\"\n");
        var derived = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n\n"
            + "[[template]]\nid = \"t\"\nformal_attribute_format = \"{value}\"\n");

        var composed = ComposeOk(derived, "derived.toml", source);

        var template = Assert.Single(composed.Templates);
        Assert.Equal("{value}", template.FormalAttributeFormat);
        Assert.Null(template.DisplayName);
    }

    // --- Composed template/matcher application, resolver-visible (M6 Slice B, D-121) ---

    [Fact]
    public void Compose_WhenTemplatesAndMatchersAreInherited_ThenResolvedAttributesEqualTheFlatSpec()
    {
        // §9.2/§13: composition is authored-document→authored-document and application is
        // resolve-time, so an `extends` split and its flat equivalent must resolve to the
        // SAME effective attributes. Both sides are built independently — one file versus
        // two — rather than deriving one from the other.
        const string attributes =
            "[[attribute]]\nname = \"feature_1\"\nsource = { kind = \"column\", index = 0 }\n\n"
            + "[[attribute]]\nname = \"feature_2\"\nsource = { kind = \"column\", index = 1 }\n";
        const string templateAndMatcher =
            "[[template]]\nid = \"flag\"\ndiscretizer = { kind = \"identity\" }\n"
            + "scale = { kind = \"dichotomic\", true_value = \"1\" }\ndeclared_domain = [\"1\", \"0\"]\n\n"
            + "[[matcher]]\nmatch = { name_regex = \"^feature_\\\\d+$\" }\ntemplate = \"flag\"\n";

        var flat = Read("[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\nhas_header = false\n\n"
            + templateAndMatcher + "\n" + attributes);

        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\nhas_header = false\n\n" + templateAndMatcher);
        var composed = ComposeOk(
            Read("[spec]\nversion = 1\nextends = \"base.toml\"\n\n" + attributes),
            "derived.toml", source);

        Assert.Equal(Describe(ResolveOk(flat)), Describe(ResolveOk(composed)));
    }

    [Fact]
    public void Compose_WhenBaseAndDerivedMatchersBothApply_ThenDerivedLayersOverBase()
    {
        // §13 rule 4: base matchers precede derived ones in composed order, so for a field
        // BOTH author the derived matcher's template wins under §9.2's field-wise
        // last-author-wins — while a field only the base authors survives untouched.
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\nhas_header = false\n\n"
            + "[[template]]\nid = \"b\"\ndiscretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
            + "declared_domain = [\"x\"]\nmissing_policy = \"as_attribute\"\n\n"
            + "[[matcher]]\nmatch = { name_regex = \"^a$\" }\ntemplate = \"b\"\n\n"
            + "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 0 }\n");
        var composed = ComposeOk(
            Read("[spec]\nversion = 1\nextends = \"base.toml\"\n\n"
                + "[[template]]\nid = \"d\"\nmissing_policy = \"skip\"\n\n"
                + "[[matcher]]\nmatch = { name_regex = \"^a$\" }\ntemplate = \"d\"\n"),
            "derived.toml", source);

        var attribute = Assert.Single(ResolveOk(composed).Attributes);
        Assert.Equal(MissingPolicy.Skip, attribute.MissingPolicy); // derived matcher won the shared field
        Assert.Equal(["x"], attribute.DeclaredDomain);             // the base-only field survived
    }

    [Fact]
    public void Compose_WhenADerivedTemplateReplacesABaseIdInPlace_ThenTheInheritedMatcherRetargets()
    {
        // §13's late-binding consequence: composition (rule 3) completes before §9.2
        // resolution runs, so replacing a template by `id` re-targets every reference to
        // it — including an INHERITED base matcher that never mentions the derived file.
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\nhas_header = false\n\n"
            + "[[template]]\nid = \"t\"\ndiscretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
            + "declared_domain = [\"base\"]\n\n"
            + "[[matcher]]\nmatch = { name_regex = \"^a$\" }\ntemplate = \"t\"\n\n"
            + "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 0 }\n");
        var composed = ComposeOk(
            Read("[spec]\nversion = 1\nextends = \"base.toml\"\n\n"
                + "[[template]]\nid = \"t\"\ndiscretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
                + "declared_domain = [\"derived\"]\n"),
            "derived.toml", source);

        var attribute = Assert.Single(ResolveOk(composed).Attributes);
        Assert.Equal(["derived"], attribute.DeclaredDomain);

        // The replacement really was in place — one template, at the base position.
        Assert.Equal("t", Assert.Single(composed.Templates).Id);
    }

    /// <summary>
    /// A deterministic structural description of the resolved attributes — the
    /// resolver-visible half of §9.2's equivalence claim.
    /// <para>
    /// Spelled out rather than comparing <see cref="AttributeSpec"/> records directly:
    /// record equality compares each member with <c>EqualityComparer&lt;T&gt;.Default</c>,
    /// and the collection members are typed as interfaces, so two structurally identical
    /// domains held in different arrays compare UNEQUAL. Expanding them here compares
    /// what the contract is actually about, and prints a readable diff when it fails.
    /// </para>
    /// <para>
    /// Plan, fingerprint, and output-byte equivalence belong to the native pipeline suite
    /// in Golden.Tests, which owns the whole chain; duplicating it here would need the
    /// Conversion/Export references Spec.Tests deliberately does not have.
    /// </para>
    /// </summary>
    private static string[] Describe(BedrockSpec spec) =>
        [.. spec.Attributes.Select(a => string.Join(" | ",
            a.Name,
            a.Source,
            $"include={a.Include}",
            $"discretizer={a.Discretizer}",
            $"scale={a.Scale}",
            $"domain=[{string.Join(",", a.DeclaredDomain)}]",
            $"restrict=[{string.Join(",", a.RestrictTo)}]",
            $"labels=[{string.Join(",", a.ValueLabels.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}]",
            $"missing={a.MissingPolicy}",
            $"unknown={a.UnknownValuePolicy}",
            $"display={a.DisplayName}",
            $"format={a.NameFormat?.Text ?? "<none>"}"))];

    private static BedrockSpec ResolveOk(SpecDocument document)
    {
        var result = Resolve(document);
        Assert.True(result.TryGetValue(out var spec),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return spec;
    }

    // A base/derived pair differing only in their [defaults] bodies, over one resolvable
    // attribute — the smallest document that isolates the per-field defaults merge.
    private static SpecDocument ComposeNaming(string baseDefaults, string? derivedDefaults)
    {
        var source = new InMemorySpecTextSource().Add("base.toml",
            "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\n\n"
            + $"[defaults]\n{baseDefaults}\n\n"
            + "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\ndeclared_domain = [\"x\"]\n");
        var derived = Read(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n"
            + (derivedDefaults is null ? string.Empty : $"\n[defaults]\n{derivedDefaults}\n"));

        return ComposeOk(derived, "derived.toml", source);
    }

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
