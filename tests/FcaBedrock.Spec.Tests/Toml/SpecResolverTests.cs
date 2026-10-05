using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

public sealed class SpecResolverTests
{
    // Resolve returns Diagnosed<ResolvedDocument> (D-098); these tests assert over the
    // resolved BedrockSpec and diagnostics, so this helper unwraps the token's spec. A schema-less
    // resolve produces a schema-less token (legal for spec tooling); an authored extends still throws.
    private static Diagnosed<BedrockSpec> Resolve(SpecDocument document, SourceSchema? schema = null)
    {
        var resolved = SpecResolver.Resolve(document, schema);
        return resolved.TryGetValue(out var doc)
            ? Diagnosed<BedrockSpec>.Ok(doc.Resolved.Spec, resolved.Diagnostics)
            : Diagnosed<BedrockSpec>.Failed(resolved.Diagnostics);
    }

    // Plans a resolved spec + schema through the fully-declared calibrated state (D-098) for the
    // determinism-bridge tests; these fixtures are fully-declared.
    private static Diagnosed<ConversionPlan> Plan(BedrockSpec spec, SourceSchema schema) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(
            ResolvedSpec.Create(
                spec, schema,
                SourceReadSettings.Create(
                    spec.Binding.Shape, spec.Binding.Encoding, spec.Binding.Delimiter, spec.Binding.QuoteChar,
                    spec.Binding.HasHeader, spec.Binding.MissingToken, spec.Binding.Ordering),
                [])));

    // --- Happy paths ---

    [Fact]
    public void Resolve_WhenMinimalWideDocument_ThenBindingAndPolicyDefaultsApply()
    {
        var result = Resolve(DocumentFixtures.Document([DocumentFixtures.Nominal("g", 0, ["b"])]));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);

        // §5.1 defaults.
        Assert.Equal(SourceShape.Wide, spec.Binding.Shape);
        Assert.Equal(',', spec.Binding.Delimiter);
        Assert.Equal('"', spec.Binding.QuoteChar);
        Assert.True(spec.Binding.HasHeader);
        Assert.Equal("invariant", spec.Binding.Locale);
        Assert.Equal("?", spec.Binding.MissingToken);
        Assert.IsType<RowIndexObjectKey>(spec.Binding.ObjectKey); // §5.4 wide default

        // §6/§10 hard defaults.
        var g = Assert.Single(spec.Attributes);
        Assert.True(g.Include);
        Assert.Equal(MissingPolicy.Skip, g.MissingPolicy);
        Assert.Equal(UnknownValuePolicy.Warn, g.UnknownValuePolicy);
        Assert.Empty(g.RestrictTo);
        Assert.Empty(g.ValueLabels);
    }

    [Fact]
    public void Resolve_WhenDefaultsSectionAuthored_ThenIncludeAndPoliciesMerge()
    {
        var defaults = new DefaultsSection(
            Include: false, MissingPolicy.AsAttribute, UnknownValuePolicy.Fail,
            DuplicateObjectPolicy: null, OrdinalDirection: null, OrdinalBoundary: null);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0))], defaults: defaults);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        var x = Assert.Single(spec.Attributes);
        Assert.False(x.Include); // [defaults].include fills the omitted field and parks it (D-049)
        Assert.Equal(MissingPolicy.AsAttribute, x.MissingPolicy);
        Assert.Equal(UnknownValuePolicy.Fail, x.UnknownValuePolicy);
    }

    [Fact]
    public void Resolve_WhenSourceBoundByName_ThenResolvesIndexThroughHeaderSchema()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.NamedColumn("age"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection())]);

        var result = Resolve(document, new SourceSchema(2, ["id", "age"]));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(1, Assert.IsType<ColumnSource>(Assert.Single(spec.Attributes).Source).Index);
    }

    [Theory]
    [InlineData("identity", SourceValueType.String)]
    [InlineData("manual_cuts", SourceValueType.Number)]
    [InlineData("ordered_cuts", SourceValueType.String)]
    public void Resolve_WhenValueTypeOmitted_ThenDefaultsPerDiscretizer(string kind, SourceValueType expected)
    {
        // D-061: string-fixing kinds default to string, number-fixing to number.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer(kind), scale: new NominalScaleSection())]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(expected, Assert.IsType<ColumnSource>(Assert.Single(spec.Attributes).Source).ValueType);
    }

    [Fact]
    public void Resolve_WhenValueTypeAuthoredOnExcludedAttribute_ThenAuthoredWinsOverParkedDiscretizerDefault()
    {
        // D-049/D-076: the D-061 matrix fires for included attributes only, so the
        // one legal authored-≠-derived pairing is on a parked attribute, where the
        // authored source type still wins over the parked discretizer's default.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0, SourceValueType.String),
                include: false, discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection())]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(SourceValueType.String, Assert.IsType<ColumnSource>(Assert.Single(spec.Attributes).Source).ValueType);
    }

    [Fact]
    public void Resolve_WhenOrdinalFieldsOmitted_ThenDefaultsSectionThenHardDefaultsFill()
    {
        // D-060(c): [defaults].ordinal_direction/_boundary fill omitted scale fields.
        var ordinal = DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
            discretizer: Discretizer("identity"),
            scale: new OrdinalScaleSection(Direction: null, Boundary: null, Order: ["x", "y"], DropTop: null));
        var defaults = new DefaultsSection(
            Include: null, MissingPolicy: null, UnknownValuePolicy: null, DuplicateObjectPolicy: null,
            OrdinalDirection.Le, OrdinalBoundary.Strict);

        Assert.True(Resolve(DocumentFixtures.Document([ordinal], defaults: defaults))
            .TryGetValue(out var withDefaults));
        Assert.True(Resolve(DocumentFixtures.Document([ordinal])).TryGetValue(out var withoutDefaults));

        var filled = Assert.IsType<OrdinalScale>(Assert.Single(withDefaults.Attributes).Scale);
        Assert.Equal(OrdinalDirection.Le, filled.Direction);
        Assert.Equal(OrdinalBoundary.Strict, filled.Boundary);
        Assert.Equal(["x", "y"], filled.Order);
        Assert.False(filled.DropTop);

        var hard = Assert.IsType<OrdinalScale>(Assert.Single(withoutDefaults.Attributes).Scale);
        Assert.Equal(OrdinalDirection.Ge, hard.Direction);
        Assert.Equal(OrdinalBoundary.Inclusive, hard.Boundary);
    }

    [Fact]
    public void Resolve_WhenOrdinalFieldsAuthored_ThenAuthoredWinOverDefaults()
    {
        var ordinal = DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
            discretizer: Discretizer("identity"),
            scale: new OrdinalScaleSection(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, Order: null, DropTop: true));
        var defaults = new DefaultsSection(
            Include: null, MissingPolicy: null, UnknownValuePolicy: null, DuplicateObjectPolicy: null,
            OrdinalDirection.Le, OrdinalBoundary.Strict);

        Assert.True(Resolve(DocumentFixtures.Document([ordinal], defaults: defaults))
            .TryGetValue(out var spec));

        var scale = Assert.IsType<OrdinalScale>(Assert.Single(spec.Attributes).Scale);
        Assert.Equal(OrdinalDirection.Ge, scale.Direction);
        Assert.Equal(OrdinalBoundary.Inclusive, scale.Boundary);
        Assert.True(scale.DropTop);
    }

    [Fact]
    public void Resolve_WhenObjectKeyColumnMode_ThenPolicyMergesFromDefaults()
    {
        var objectKey = new ObjectKeySection(ObjectKeyMode.Column, new IndexColumnRef(0), Columns: null, Aggregate: null);
        var defaults = new DefaultsSection(
            Include: null, MissingPolicy: null, UnknownValuePolicy: null, DuplicateObjectPolicy.Dedupe,
            OrdinalDirection: null, OrdinalBoundary: null);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"])],
            binding: DocumentFixtures.WideBinding(objectKey: objectKey), defaults: defaults);

        Assert.True(Resolve(document).TryGetValue(out var spec));

        var key = Assert.IsType<ColumnObjectKey>(spec.Binding.ObjectKey);
        Assert.Equal(0, key.Index);
        Assert.Equal(DuplicateObjectPolicy.Dedupe, key.Policy);
    }

    [Fact]
    public void Resolve_WhenObjectKeyColumnModeWithoutDefaults_ThenPolicyIsFail()
    {
        var objectKey = new ObjectKeySection(ObjectKeyMode.Column, new NameColumnRef("id"), Columns: null, Aggregate: null);
        var document = DocumentFixtures.Document([DocumentFixtures.Nominal("a", 0, ["x"])], binding: DocumentFixtures.WideBinding(objectKey: objectKey));

        Assert.True(Resolve(document, new SourceSchema(2, ["id", "age"])).TryGetValue(out var spec));

        var key = Assert.IsType<ColumnObjectKey>(spec.Binding.ObjectKey);
        Assert.Equal(0, key.Index); // resolved by header name
        Assert.Equal(DuplicateObjectPolicy.Fail, key.Policy); // §6.1 default
    }

    [Fact]
    public void Resolve_WhenAttributeExcluded_ThenParkedWithNullsWithoutError()
    {
        // D-049: dormant scaling sections are parked, not resolved and never an error,
        // even ones that would fail resolution if included (dichotomic without true_value).
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("bare", DocumentFixtures.Column(0), include: false),
            DocumentFixtures.Attribute("dormant", DocumentFixtures.Column(1), include: false,
                discretizer: Discretizer("identity"), scale: new DichotomicScaleSection(TrueValue: null)),
        ]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.All(spec.Attributes, a =>
        {
            Assert.False(a.Include);
            Assert.Null(a.Discretizer);
            Assert.Null(a.Scale);
        });
    }

    [Fact]
    public void Resolve_WhenTripleShape_ThenPredicateSourceRoleMapAndOrderingResolve()
    {
        // D-082: triple resolves fully: the predicate source, role→index map,
        // and ordering become Core.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("p", new PredicateSourceSection("pred", ValueType: null),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection())],
            binding: DocumentFixtures.TripleBinding(
                new TripleColumnsSection(new IndexColumnRef(1), new IndexColumnRef(2), new IndexColumnRef(3))));

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(SourceShape.Triple, spec.Binding.Shape);
        Assert.Equal(new TripleColumns(1, 2, 3), spec.Binding.TripleColumns);
        Assert.Equal(TripleOrdering.SubjectGrouped, spec.Binding.Ordering);

        var source = Assert.IsType<PredicateSource>(Assert.Single(spec.Attributes).Source);
        Assert.Equal("pred", source.Predicate);

        // §5.4 triple default: the resolved subject column keys objects.
        var key = Assert.IsType<ColumnObjectKey>(spec.Binding.ObjectKey);
        Assert.Equal(1, key.Index);
        Assert.Equal(DuplicateObjectPolicy.Fail, key.Policy);
    }

    [Fact]
    public void Resolve_WhenDeclaredDomainOmittedVersusAuthoredEmpty_ThenPresenceSurvives()
    {
        // D-122 part 15 (revising D-071): omission and an authored [] are distinct in Core: an omitted
        // domain resolves to null (calibrated where a discretizer consumes it), an authored [] to the
        // empty list (a complete fixed empty domain). The document keeps the authored form for round-trip.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("omitted", 0, domain: null),
            DocumentFixtures.Nominal("authoredEmpty", 1, domain: []),
        ]);

        Assert.True(Resolve(document).TryGetValue(out var spec));

        Assert.Null(spec.Attributes[0].DeclaredDomain);
        var authoredEmpty = spec.Attributes[1].DeclaredDomain;
        Assert.NotNull(authoredEmpty);
        Assert.Empty(authoredEmpty);
    }

    [Fact]
    public void Resolve_WhenRestrictToStringsOnStringSource_ThenCarriedIntoCore()
    {
        // D-057/D-091: carried verbatim into Core, where the planner turns it into an
        // executable PlannedRestriction (D-105). Entries must match the attribute's single
        // value_type (D-063), so each carrier test is same-typed.
        IReadOnlyList<RestrictToEntry> restrict = [new RestrictToValue("a"), new RestrictToValue("b")];
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["a", "b"], restrictTo: restrict)]);

        Assert.True(Resolve(document).TryGetValue(out var spec));

        Assert.Equal(restrict, Assert.Single(spec.Attributes).RestrictTo);
    }

    [Fact]
    public void Resolve_WhenRestrictToAuthoredEmpty_ThenNoFilterYetTheDocumentKeepsThePresence()
    {
        // §10.4: "empty or absent ⇒ no filter", so an authored [] and an omitted restrict_to
        // converge on the SAME resolved Core state, while the document snapshot keeps them
        // distinguishable, which is what round-trip fidelity (D-049) and §14's
        // present-only-when-non-empty `restrictions` container both rely on.
        var attribute = DocumentFixtures.Attribute("x", DocumentFixtures.Column(0),
            discretizer: Discretizer("identity"), scale: new NominalScaleSection(), declaredDomain: ["a"]);

        var authoredEmpty = SpecResolver.Resolve(
            DocumentFixtures.Document([attribute with { RestrictTo = [] }]), new SourceSchema(1));
        var omitted = SpecResolver.Resolve(DocumentFixtures.Document([attribute]), new SourceSchema(1));

        Assert.True(authoredEmpty.TryGetValue(out var empty));
        Assert.True(omitted.TryGetValue(out var absent));

        Assert.Empty(Assert.Single(empty!.Resolved.Spec.Attributes).RestrictTo);
        Assert.Empty(Assert.Single(absent!.Resolved.Spec.Attributes).RestrictTo);

        Assert.NotNull(empty.Document.Attributes[0].RestrictTo);
        Assert.Null(absent.Document.Attributes[0].RestrictTo);
    }

    [Fact]
    public void Resolve_WhenRestrictToRangesOnNumberSource_ThenCarriedIntoCore()
    {
        IReadOnlyList<RestrictToEntry> restrict = [new RestrictToRange(1, To: null), new RestrictToRange(null, 5)];
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(), restrictTo: restrict)]);

        Assert.True(Resolve(document).TryGetValue(out var spec));

        Assert.Equal(restrict, Assert.Single(spec.Attributes).RestrictTo);
    }

    [Fact]
    public void Resolve_WhenManualCutsInvalid_ThenFactoryDiagnosticsSurfaceThroughTheSeam()
    {
        // D-056: the Core smart factory owns cut validation; its diagnostics merge into
        // the resolve pass, attribute-scoped.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: new ManualCutsDiscretizerSection([50.0, 30.0], Ends: null),
                scale: new NominalScaleSection())]);

        var result = Resolve(document);

        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.DiscretizerCutsNotAscending);
        Assert.Equal("age", diagnostic.Location?.AttributeName);
    }

    // --- Extends / templates / matchers (D-078) ---

    [Fact]
    public void Resolve_WhenDocumentStillCarriesExtends_ThenThrowsArgumentException()
    {
        // Call-contract, not a diagnostic: Resolve never throws for valid inputs
        // under its contract, and a document with authored extends is invalid
        // input to Resolve: compose first (§13, D-078).
        var document = DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(extends: "base.toml"));

        var exception = Assert.Throws<ArgumentException>(() => Resolve(document));
        Assert.Contains("SpecComposer.Compose", exception.Message, StringComparison.Ordinal);
    }

    // --- Template identity, references, and the family ordering (D-121) ---

    [Fact]
    public void Resolve_WhenMatchersApplyTemplates_ThenTheyResolveCleanly()
    {
        // A matcher applies its template: the
        // attribute below authors name + source only; every scaling field arrives
        // through the template the matcher selects onto it.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("feature_1", DocumentFixtures.Column(0))],
            templates: [DocumentFixtures.Template("flag", discretizer: new IdentityDiscretizerSection(),
                scale: new NominalScaleSection(), declaredDomain: ["1", "0"])],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^feature_\\d+$", template: "flag")]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        var attribute = Assert.Single(spec.Attributes);
        Assert.IsType<IdentityDiscretizer>(attribute.Discretizer);
        Assert.IsType<NominalScale>(attribute.Scale);
        Assert.Equal(["1", "0"], attribute.DeclaredDomain);
    }

    [Fact]
    public void Resolve_WhenTemplateIdsAreMissingOrDuplicated_ThenIdentityErrorsReportInComposedOrder()
    {
        // §9.1: id is required and unique across the COMPOSED document. One Error per
        // id-less template and one per EXTRA declaration: the first declaration is not
        // itself an error, so three templates sharing an id yield two diagnostics.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            templates:
            [
                DocumentFixtures.Template("t"),
                DocumentFixtures.Template(null),
                DocumentFixtures.Template("t"),
                DocumentFixtures.Template("t"),
            ]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(
            [DiagnosticCode.TemplateIdMissing, DiagnosticCode.TemplateIdDuplicate, DiagnosticCode.TemplateIdDuplicate],
            result.Diagnostics.Select(d => d.Code));
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        // Deterministic identity: composed declaration ordinals, 1-based, in order.
        Assert.Contains("#2", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("#3", result.Diagnostics[1].Message, StringComparison.Ordinal);
        Assert.Contains("#4", result.Diagnostics[2].Message, StringComparison.Ordinal);
        Assert.Contains("\"t\"", result.Diagnostics[2].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenADuplicateIdIsDeclared_ThenTheFirstDeclarationStillResolvesReferences()
    {
        // Aggregation over cascade: the duplicate is an Error, but keeping the FIRST
        // declaration as the lookup means a referencing attribute reports its own real
        // problem rather than a spurious unknown-reference pile-up (EP-14).
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), template: "t")],
            templates: [DocumentFixtures.Template("t", scale: new NominalScaleSection()), DocumentFixtures.Template("t")]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Equal([DiagnosticCode.TemplateIdDuplicate, DiagnosticCode.AttributeScalingMissing],
            result.Diagnostics.Select(d => d.Code));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.TemplateReferenceUnknown);
    }

    [Fact]
    public void Resolve_WhenReferencesAreUnknown_ThenOnePerSiteWithDeterministicIdentity()
    {
        // §16.4 families 2 and 3: matcher sites first (declaration order), then attribute
        // sites (declaration order) carrying the AttributeName location. One per SITE.
        var document = DocumentFixtures.Document(
            [
                DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), template: "nope",
                    discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]),
                DocumentFixtures.Nominal("b", 1, ["y"]),
                DocumentFixtures.Attribute("c", DocumentFixtures.Column(2), template: "other",
                    discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["z"]),
            ],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^b$", template: "absent")]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        var unknown = result.Diagnostics.Where(d => d.Code == DiagnosticCode.TemplateReferenceUnknown).ToList();
        Assert.Equal(3, unknown.Count);

        // Matcher site (family 2) precedes both attribute sites (family 3), and carries no
        // AttributeName: it belongs to a matcher, not an attribute.
        Assert.Null(unknown[0].Location?.AttributeName);
        Assert.Contains("#1", unknown[0].Message, StringComparison.Ordinal);
        Assert.Contains("\"absent\"", unknown[0].Message, StringComparison.Ordinal);

        Assert.Equal(["a", "c"], unknown[1..].Select(d => d.Location?.AttributeName));
        Assert.Contains("\"nope\"", unknown[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenATemplateIsUnused_ThenItIsSemanticallyDormant()
    {
        // §9.2: an unused template is inert. Its body here would be a broken ATTRIBUTE
        // (a dichotomic scale with no true_value, and no discretizer), but it applies to
        // nothing, so it is never validated as a hypothetical attribute: parse-level
        // shape checks are the only thing that ever ran on it.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            templates: [DocumentFixtures.Template("unused", scale: new DichotomicScaleSection(null))]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Single(spec.Attributes);
    }

    [Fact]
    public void Resolve_WhenTemplateIdentityFailsAndBindingShapeMissing_ThenBothReport()
    {
        // §16.4: family 1 is emitted BEFORE the shape check, so a shape-less document still
        // reports its template-identity errors (EP-14). Families 2–5 do not run: matcher shape
        // compatibility has no shape to judge against.
        var document = new SpecDocument(
            DocumentFixtures.SpecV1(), null, null, null, null,
            [DocumentFixtures.Template(null)],
            [DocumentFixtures.Matcher(sourceIndexRange: [0, 1], template: "absent")],
            []);

        var result = Resolve(document);

        Assert.Equal([DiagnosticCode.TemplateIdMissing, DiagnosticCode.BindingShapeMissing],
            result.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Resolve_WhenEveryFamilyReports_ThenTheyAppearInTheDeterministicFamilyOrder()
    {
        // §16.4's family order, asserted as an exact SEQUENCE because order is the
        // contract: an unordered membership check would pass on any permutation. One
        // diagnostic per family, so the sequence is unambiguous:
        //
        //   1 template identity → [binding-section, established prefix] → 2 matcher
        //   references/shape → 3 attribute references → 4 effective validation →
        //   5 matcher warnings.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), template: "nope",
                discretizer: new IdentityDiscretizerSection())],
            binding: DocumentFixtures.WideBinding(quoteChar: '\''),
            templates: [DocumentFixtures.Template(null)],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^zz$", template: "absent")]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(
            [
                DiagnosticCode.TemplateIdMissing,             // family 1
                DiagnosticCode.QuoteCharNotSupportedV1,       // binding-section prefix
                DiagnosticCode.TemplateReferenceUnknown,      // family 2 (matcher site)
                DiagnosticCode.TemplateReferenceUnknown,      // family 3 (attribute site)
                DiagnosticCode.AttributeScalingMissing,       // family 4
                DiagnosticCode.MatcherSelectsNoAttributes,    // family 5
            ],
            result.Diagnostics.Select(d => d.Code));

        // The two same-code entries really are the two different sites.
        Assert.Null(result.Diagnostics[2].Location?.AttributeName);
        Assert.Equal("a", result.Diagnostics[3].Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenBothWarningKindsFire_ThenTheyInterleaveByMatcherDeclarationOrder()
    {
        // §16.4/D-116: family 5 is ONE traversal in matcher declaration order, so the two
        // warning kinds interleave by matcher rather than grouping by code. Shadowed,
        // zero-match, shadowed must come out in exactly that order, which is precisely
        // what a two-pass "all zero-match, then all shadowed" implementation gets wrong.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"]) with
            {
                MissingPolicy = MissingPolicy.Skip,
                UnknownValuePolicy = UnknownValuePolicy.Skip,
            }],
            templates:
            [
                DocumentFixtures.Template("t1", missingPolicy: MissingPolicy.AsAttribute),
                DocumentFixtures.Template("t3", unknownValuePolicy: UnknownValuePolicy.Warn),
            ],
            matchers:
            [
                DocumentFixtures.Matcher(nameRegex: "^a$", template: "t1"),
                DocumentFixtures.Matcher(nameRegex: "^zz$", template: "t1"),
                DocumentFixtures.Matcher(nameRegex: "^a$", template: "t3"),
            ]);

        var result = Resolve(document);

        // Warnings only: the document still resolves successfully and carries a value.
        Assert.True(result.TryGetValue(out var spec), string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Single(spec.Attributes);
        Assert.Equal(
            [
                DiagnosticCode.MatcherFullyShadowed,
                DiagnosticCode.MatcherSelectsNoAttributes,
                DiagnosticCode.MatcherFullyShadowed,
            ],
            result.Diagnostics.Select(d => d.Code));
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));

        // Each warning names its own matcher, so the interleaving is anchored to the
        // declaration ordinals rather than merely to a plausible code sequence.
        Assert.Contains("#1", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("#2", result.Diagnostics[1].Message, StringComparison.Ordinal);
        Assert.Contains("#3", result.Diagnostics[2].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenAnEarlierMatcherLosesToALaterOne_ThenOnlyTheEarlierIsFullyShadowed()
    {
        // Shadowing WITHIN tier 3: the duplicate-matcher case. Both templates author the
        // same single field, so §9.2's field-wise last-author-wins means matcher #2 takes
        // it and matcher #1 contributes nothing at all. That is the definition of fully
        // shadowed, so #1 warns and #2 must stay silent.
        //
        // Distinct from the explicit- and named-tier cases above: here the higher-precedence
        // source is a LATER MATCHER, not tier 4 or 5. A winner-map regression that still
        // produces the right final value but attributes the win to the wrong matcher would
        // pass every value-only assertion and fail here.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"])],
            templates:
            [
                DocumentFixtures.Template("first", missingPolicy: MissingPolicy.AsAttribute),
                DocumentFixtures.Template("second", missingPolicy: MissingPolicy.Skip),
            ],
            matchers:
            [
                DocumentFixtures.Matcher(nameRegex: "^a$", template: "first"),
                DocumentFixtures.Matcher(nameRegex: "^a$", template: "second"),
            ]);

        var result = Resolve(document);

        // Warning-only: resolution still succeeds and carries a value, with the LATER
        // matcher's field winning.
        Assert.True(result.TryGetValue(out var spec), string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(MissingPolicy.Skip, Assert.Single(spec.Attributes).MissingPolicy);

        // Exactly one warning, for matcher #1, named by ordinal AND reference (§16.4).
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.MatcherFullyShadowed, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("#1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("\"first\"", warning.Message, StringComparison.Ordinal);

        // The winner is silent: no second warning mentioning matcher #2 or its template.
        Assert.DoesNotContain("#2", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"second\"", warning.Message, StringComparison.Ordinal);

        // EP-7 for this shape specifically: the ordered signature repeats exactly.
        Assert.Equal(
            SpecResolver.Resolve(document).Diagnostics.Select(d => (d.Code, d.Severity, d.Message)),
            SpecResolver.Resolve(document).Diagnostics.Select(d => (d.Code, d.Severity, d.Message)));
    }

    [Fact]
    public void Resolve_WhenAMatcherLosesToTheNamedTemplate_ThenItIsFullyShadowed()
    {
        // Shadowing by tier 4 rather than tier 5: the "higher-precedence source" the
        // §9.2 definition names includes the attribute's directly named template.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), template: "named",
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"])],
            templates:
            [
                DocumentFixtures.Template("named", missingPolicy: MissingPolicy.Skip),
                DocumentFixtures.Template("matched", missingPolicy: MissingPolicy.AsAttribute),
            ],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^a$", template: "matched")]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Equal(DiagnosticCode.MatcherFullyShadowed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenAMatcherFieldWinsOnAnExcludedAttribute_ThenItIsNotShadowed()
    {
        // §9.2/D-116: shadowing is a MERGE-level determination, deliberately independent
        // of D-049 dormancy. The template's field wins the merge here, so the matcher is
        // doing something, even though the winning configuration is dormant while the
        // attribute is excluded. Warning would be wrong; silence is the contract.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), include: false)],
            templates: [DocumentFixtures.Template("t", missingPolicy: MissingPolicy.AsAttribute)],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^a$")]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenAMatcherWinsOnOneOfSeveralSelectedAttributes_ThenItIsNotShadowed()
    {
        // "Fully" matters: shadowing requires EVERY authored field to lose on
        // EVERY selected attribute. Winning on one attribute out of two is enough to
        // stay silent.
        var document = DocumentFixtures.Document(
            [
                DocumentFixtures.Nominal("a", 0, ["x"]) with { MissingPolicy = MissingPolicy.Skip },
                DocumentFixtures.Nominal("b", 1, ["x"]),
            ],
            templates: [DocumentFixtures.Template("t", missingPolicy: MissingPolicy.AsAttribute)],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^[ab]$")]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Equal([MissingPolicy.Skip, MissingPolicy.AsAttribute], spec.Attributes.Select(a => a.MissingPolicy));
    }

    [Fact]
    public void Resolve_WhenARangeMatcherMeetsTriple_ThenItBothErrorsAndWarns()
    {
        // The two conditions are independent and both hold: the selector is incompatible
        // with the shape (Error, family 2), AND it selected zero attributes (Warning,
        // family 5). The zero-match warning is selector-driven and is not suppressed by
        // an adjacent Error on the same matcher, so family 5 stays one uniform traversal
        // with no special cases (D-121).
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", new PredicateSourceSection("age", null),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"])],
            binding: DocumentFixtures.TripleBinding(),
            templates: [DocumentFixtures.Template("t", missingPolicy: MissingPolicy.AsAttribute)],
            matchers: [DocumentFixtures.Matcher(sourceIndexRange: [0, 4])]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(
            [DiagnosticCode.MatcherSelectorInvalidForShape, DiagnosticCode.MatcherSelectsNoAttributes],
            result.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Resolve_WhenTheSameDocumentIsResolvedTwice_ThenTheOrderedDiagnosticsAreIdentical()
    {
        // EP-7 over the whole template/matcher diagnostic surface: identity, references, effective
        // validation, and both warning kinds, compared on the full structured tuple
        // (code, severity, location, and message), not merely on codes.
        var document = DocumentFixtures.Document(
            [
                DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), template: "nope",
                    discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]),
                DocumentFixtures.Nominal("b", 1, ["y"]) with { MissingPolicy = MissingPolicy.Skip },
            ],
            templates:
            [
                DocumentFixtures.Template("t", missingPolicy: MissingPolicy.AsAttribute),
                DocumentFixtures.Template(null),
                DocumentFixtures.Template("t"),
            ],
            matchers:
            [
                DocumentFixtures.Matcher(nameRegex: "^b$"),
                DocumentFixtures.Matcher(nameRegex: "^zz$"),
            ]);

        static (DiagnosticCode, DiagnosticSeverity, DiagnosticLocation?, string)[] Signature(SpecDocument d) =>
            [.. SpecResolver.Resolve(d).Diagnostics.Select(x => (x.Code, x.Severity, x.Location, x.Message))];

        Assert.Equal(Signature(document), Signature(document));

        // Non-vacuity: the signature must cover these diagnostic families, or "identical"
        // would be a claim about an empty list.
        var codes = Signature(document).Select(s => s.Item1).ToList();
        Assert.Contains(DiagnosticCode.TemplateIdMissing, codes);
        Assert.Contains(DiagnosticCode.TemplateIdDuplicate, codes);
        Assert.Contains(DiagnosticCode.TemplateReferenceUnknown, codes);
        Assert.Contains(DiagnosticCode.MatcherFullyShadowed, codes);
        Assert.Contains(DiagnosticCode.MatcherSelectsNoAttributes, codes);
    }

    // --- Failures ---

    [Fact]
    public void Resolve_WhenSpecSectionOrVersionMissing_ThenSpecVersionUnsupportedFatal()
    {
        var noSpec = new SpecDocument(null, null, DocumentFixtures.WideBinding(), null, null, [], [], []);
        var noVersion = DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(version: null));

        foreach (var document in new[] { noSpec, noVersion })
        {
            var result = Resolve(document);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(DiagnosticCode.SpecVersionUnsupported, diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
            Assert.False(result.TryGetValue(out _));
        }
    }

    [Fact]
    public void Resolve_WhenVersionUnknown_ThenSpecVersionUnsupportedFatal()
    {
        var result = Resolve(DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(version: 2)));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecVersionUnsupported, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
    }

    [Fact]
    public void Resolve_WhenBindingOrShapeMissing_ThenBindingShapeMissing()
    {
        var noBinding = new SpecDocument(DocumentFixtures.SpecV1(), null, null, null, null, [], [], []);
        var noShape = DocumentFixtures.Document(binding: new BindingSection(
            Shape: null, Encoding: null, Delimiter: null, QuoteChar: null, HasHeader: null,
            Locale: null, MissingToken: null, Ordering: null, Columns: null, ObjectKey: null));

        foreach (var document in new[] { noBinding, noShape })
        {
            var result = Resolve(document);

            Assert.Equal(DiagnosticCode.BindingShapeMissing, Assert.Single(result.Diagnostics).Code);
            Assert.False(result.TryGetValue(out _));
        }
    }

    [Fact]
    public void Resolve_WhenColumnSourceUnresolvable_ThenSourceBindingInvalidPerVariant()
    {
        // §10.2: one code, message variants (D-067).
        (string Case, SourceSection Source, BindingSection Binding, SourceSchema? Schema)[] cases =
        [
            ("both index and name", new ColumnSourceSection(0, "age", null), DocumentFixtures.WideBinding(), null),
            ("neither index nor name", new ColumnSourceSection(null, null, null), DocumentFixtures.WideBinding(), null),
            ("name without header", DocumentFixtures.NamedColumn("age"), DocumentFixtures.WideBinding(hasHeader: false),
                new SourceSchema(2, ["id", "age"])),
            ("name without schema", DocumentFixtures.NamedColumn("age"), DocumentFixtures.WideBinding(), null),
            ("name not in header", DocumentFixtures.NamedColumn("weight"), DocumentFixtures.WideBinding(),
                new SourceSchema(2, ["id", "age"])),
            ("negative index", DocumentFixtures.Column(-1), DocumentFixtures.WideBinding(), null),
            ("index out of schema range", DocumentFixtures.Column(9), DocumentFixtures.WideBinding(),
                new SourceSchema(2, ["id", "age"])),
        ];

        foreach (var (name, source, binding, schema) in cases)
        {
            var document = DocumentFixtures.Document(
                [DocumentFixtures.Attribute("a", source,
                    discretizer: Discretizer("identity"), scale: new NominalScaleSection())],
                binding: binding);

            var result = Resolve(document, schema);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.True(DiagnosticCode.SourceBindingInvalid == diagnostic.Code, $"case '{name}': {diagnostic.Message}");
        }
    }

    [Fact]
    public void Resolve_WhenAttributeNameMissingOrEmpty_ThenAttributeNameMissing()
    {
        foreach (var name in new string?[] { null, "" })
        {
            var document = DocumentFixtures.Document([DocumentFixtures.Attribute(name, DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection())]);

            var result = Resolve(document);

            Assert.Equal(DiagnosticCode.AttributeNameMissing, Assert.Single(result.Diagnostics).Code);
        }
    }

    [Fact]
    public void Resolve_WhenIncludedAttributeScalingIncomplete_ThenAttributeScalingMissingPerVariant()
    {
        // §10.9/§12.2: one code, three message variants (D-067).
        (string Case, DiscretizerSection? Discretizer, ScaleSection? Scale)[] cases =
        [
            ("no discretizer", null, new NominalScaleSection()),
            ("no scale", Discretizer("identity"), null),
            ("dichotomic without true_value", Discretizer("identity"), new DichotomicScaleSection(TrueValue: null)),
        ];

        foreach (var (name, discretizer, scale) in cases)
        {
            var document = DocumentFixtures.Document(
                [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), discretizer: discretizer, scale: scale)]);

            var result = Resolve(document);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.True(DiagnosticCode.AttributeScalingMissing == diagnostic.Code, $"case '{name}': {diagnostic.Message}");
        }
    }

    [Fact]
    public void Resolve_WhenLocaleUnresolvable_ThenBindingLocaleInvalid()
    {
        var document = DocumentFixtures.Document([DocumentFixtures.Nominal("a", 0, ["x"])], binding: DocumentFixtures.WideBinding(locale: "xx-nope"));

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.BindingLocaleInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenObjectKeyColumnUnresolvable_ThenObjectKeyBindingInvalid()
    {
        (string Case, ObjectKeySection Section, SourceSchema? Schema)[] cases =
        [
            ("no column ref", new ObjectKeySection(ObjectKeyMode.Column, null, null, null), null),
            ("name without schema", new ObjectKeySection(ObjectKeyMode.Column, new NameColumnRef("id"), null, null), null),
            ("name not in header", new ObjectKeySection(ObjectKeyMode.Column, new NameColumnRef("nope"), null, null),
                new SourceSchema(1, ["id"])),
            ("negative index", new ObjectKeySection(ObjectKeyMode.Column, new IndexColumnRef(-1), null, null), null),
            ("index out of schema range", new ObjectKeySection(ObjectKeyMode.Column, new IndexColumnRef(5), null, null),
                new SourceSchema(1, ["id"])),
        ];

        foreach (var (name, section, schema) in cases)
        {
            var document = DocumentFixtures.Document([DocumentFixtures.Nominal("a", 0, ["x"])], binding: DocumentFixtures.WideBinding(objectKey: section));

            var result = Resolve(document, schema);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.True(DiagnosticCode.ObjectKeyBindingInvalid == diagnostic.Code, $"case '{name}': {diagnostic.Message}");
        }
    }

    [Fact]
    public void Resolve_WhenMultipleProblems_ThenAllDiagnosticsAggregate()
    {
        // EP-14/D-067: resolve + validate in one pass, reporting everything at once.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute(name: null, DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection()),
            DocumentFixtures.Attribute("noscale", DocumentFixtures.Column(1), discretizer: Discretizer("identity")),
        ],
        binding: DocumentFixtures.WideBinding(locale: "xx-nope"));

        var result = Resolve(document);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.BindingLocaleInvalid);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.AttributeNameMissing);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.AttributeScalingMissing);
        Assert.Equal(3, result.Diagnostics.Count);
    }

    // --- The composed-spec attribute minimum (§2, D-135) ---
    //
    // §2 asks the COMPOSED spec for at least one [[attribute]]: a base file of an extends chain
    // may declare none. So resolve owns the rule, because it sees the composed document, and
    // the reader, which sees one file, does not. These documents come through the real composer.

    [Theory]
    [InlineData("[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n", null)]
    [InlineData("[spec]\nversion = 1\nextends = \"base.toml\"\n", "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n")]
    public void Resolve_WhenTheComposedDocumentHasNoAttributes_ThenAttributesMissingIsTheOneError(
        string rootToml, string? baseToml)
    {
        var document = baseToml is null ? Compose(rootToml) : Compose(rootToml, ("base.toml", baseToml));
        Assert.Empty(document.Attributes);

        var result = SpecResolver.Resolve(document);

        Assert.False(result.TryGetValue(out _));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.AttributesMissing, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Null(diagnostic.Location); // a fact about the composed spec, not about one file
        Assert.Equal("The composed spec declares no [[attribute]]; at least one is required (§2).", diagnostic.Message);
    }

    [Fact]
    public void Resolve_WhenNoAttributesAndEveryOtherFamilyReports_ThenAttributesMissingOpensFamilyFour()
    {
        // §16.4's family order with the §2 minimum in its slot: it is the first entry of family
        // 4, after the binding prefix and the reference families and before the matcher
        // warnings, and it aggregates with all of them rather than stopping resolution.
        var document = DocumentFixtures.Document(
            attributes: [],
            binding: DocumentFixtures.WideBinding(quoteChar: '\''),
            templates: [DocumentFixtures.Template(null)],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^zz$", template: "absent")]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(
            [
                DiagnosticCode.TemplateIdMissing,             // family 1
                DiagnosticCode.QuoteCharNotSupportedV1,       // binding-section prefix
                DiagnosticCode.TemplateReferenceUnknown,      // family 2 (matcher site)
                DiagnosticCode.AttributesMissing,             // family 4, first entry
                DiagnosticCode.MatcherSelectsNoAttributes,    // family 5
            ],
            result.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Resolve_WhenAPrefixCheckStopsResolution_ThenTheAttributeMinimumIsNotJudged()
    {
        // The version and shape checks return before family 4, so a document that fails one of
        // them reports that check alone, never the attribute minimum as well.
        (SpecDocument Document, DiagnosticCode Check)[] cases =
        [
            (DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(version: null)), DiagnosticCode.SpecVersionUnsupported),
            (DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(version: 2)), DiagnosticCode.SpecVersionUnsupported),
            (new SpecDocument(DocumentFixtures.SpecV1(), null, null, null, null, [], [], []), DiagnosticCode.BindingShapeMissing),
        ];

        foreach (var (document, check) in cases)
        {
            Assert.Empty(document.Attributes);

            var result = Resolve(document);

            Assert.Equal(check, Assert.Single(result.Diagnostics).Code);
        }
    }

    [Fact]
    public void Resolve_WhenAnAttributeLessBaseGainsAnAttributeFromTheRoot_ThenTheComposedSpecResolves()
    {
        // The base declares a binding and no attribute; the root adds one. The minimum is met by
        // the composed spec, so neither file on its own has to meet it.
        var document = Compose(
            """
            [spec]
            version = 1
            extends = "base.toml"

            [[attribute]]
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["red", "green"]
            """,
            ("base.toml", "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\nhas_header = true\n"));

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Equal("colour", Assert.Single(spec.Attributes).Name);
    }

    [Fact]
    public void Resolve_WhenEveryAttributeIsExcludedOrFilterOnly_ThenItResolvesAndPlansToNoFormalAttributes()
    {
        // Distinct from an empty attribute list: the entries exist, so the minimum is met, and
        // the zero-column context they plan to keeps D-058's NoFormalAttributes Warning.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("parked", DocumentFixtures.Column(0), include: false),
            DocumentFixtures.Attribute("filter", DocumentFixtures.Column(1), include: false, restrictTo: [new RestrictToValue("x")]),
        ]);
        var schema = new SourceSchema(2, ["parked", "filter"]);

        var resolved = Resolve(document, schema);

        Assert.True(resolved.TryGetValue(out var spec));
        Assert.Empty(resolved.Diagnostics);

        var planned = Plan(spec, schema);

        Assert.True(planned.TryGetValue(out var plan));
        Assert.Empty(plan.FormalAttributes);
        var warning = Assert.Single(planned.Diagnostics);
        Assert.Equal(DiagnosticCode.NoFormalAttributes, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    // --- The binding-only stage (D-135) ---
    //
    // ResolveBinding resolves the [binding] alone, through the helpers Resolve runs for the
    // binding section. The triple probe uses it before its draft has any attribute.

    [Fact]
    public void ResolveBinding_WhenTheDocumentHasNoAttributes_ThenTheBindingResolvesWhileResolveReportsTheMinimum()
    {
        var document = DocumentFixtures.Document(
            attributes: [],
            binding: DocumentFixtures.TripleBinding(
                columns: new TripleColumnsSection(
                    new NameColumnRef("s"), new NameColumnRef("p"), new NameColumnRef("v")),
                ordering: TripleOrdering.Unordered,
                hasHeader: true));
        var schema = new SourceSchema(3, ["v", "s", "p"]);

        var binding = SpecResolver.ResolveBinding(document, schema);

        Assert.True(binding.TryGetValue(out var resolved));
        Assert.Empty(binding.Diagnostics);
        Assert.Equal(new TripleColumns(1, 2, 0), resolved.TripleColumns);
        Assert.Equal(TripleOrdering.Unordered, resolved.Ordering);

        Assert.Equal(
            DiagnosticCode.AttributesMissing,
            Assert.Single(SpecResolver.Resolve(document, schema).Diagnostics).Code);
    }

    [Fact]
    public void ResolveBinding_WhenTheBindingIsInvalid_ThenItReportsExactlyWhatResolveReportsForTheSection()
    {
        // One owner per condition: the stage and full resolution share the section's helpers, so
        // the same broken binding yields the same diagnostics, in the same order, from both.
        var wide = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"])],
            binding: DocumentFixtures.WideBinding(
                delimiter: '|', quoteChar: '|', locale: "xx-nope",
                objectKey: new ObjectKeySection(ObjectKeyMode.Column, new NameColumnRef("id"), null, null)));
        var triple = DocumentFixtures.Document(
            [TriplePredicate()],
            binding: DocumentFixtures.TripleBinding(
                columns: new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(0), null),
                ordering: null));

        foreach (var document in new[] { wide, triple })
        {
            var full = SpecResolver.Resolve(document);
            var stage = SpecResolver.ResolveBinding(document);

            Assert.False(stage.TryGetValue(out _));
            Assert.True(stage.Diagnostics.Count >= 2, "the fixture must break the binding more than once");
            Assert.Equal(full.Diagnostics, stage.Diagnostics);
        }
    }

    [Fact]
    public void ResolveBinding_WhenAPrefixCheckFails_ThenItReportsWhatResolveReports()
    {
        var failing = new[]
        {
            DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(version: null)),
            DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(version: 2)),
            new SpecDocument(DocumentFixtures.SpecV1(), null, null, null, null, [], [], []),
            DocumentFixtures.Document(binding: new BindingSection(
                Shape: null, Encoding: null, Delimiter: null, QuoteChar: null, HasHeader: null,
                Locale: null, MissingToken: null, Ordering: null, Columns: null, ObjectKey: null)),
        };

        foreach (var document in failing)
        {
            var stage = SpecResolver.ResolveBinding(document);

            Assert.False(stage.TryGetValue(out _));
            Assert.Equal(SpecResolver.Resolve(document).Diagnostics, stage.Diagnostics);
        }

        var uncomposed = DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(extends: "base.toml"));
        Assert.Throws<ArgumentException>(() => SpecResolver.ResolveBinding(uncomposed));
    }

    [Fact]
    public void ResolveBinding_WhenTemplatesMatchersAndAttributesAreBroken_ThenItDoesNotReadThem()
    {
        // Only the binding is this stage's concern; everything else is Resolve's to judge.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute(name: null, DocumentFixtures.Column(0))],
            templates: [DocumentFixtures.Template(null)],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^zz$", template: "absent")]);

        var stage = SpecResolver.ResolveBinding(document);

        Assert.True(stage.TryGetValue(out var binding));
        Assert.Empty(stage.Diagnostics);
        Assert.Equal(SourceShape.Wide, binding.Shape);
        Assert.NotEmpty(SpecResolver.Resolve(document).Diagnostics);
    }

    // --- Seam validation (D-054/D-060/D-061/D-063/D-064/D-076) ---

    [Fact]
    public void Resolve_WhenQuoteCharAuthoredNonStandard_ThenQuoteCharNotSupportedV1()
    {
        // D-054: the field parses (a retained carrier) but v1 rejects any quote
        // other than the standard double quote at the seam.
        var document = DocumentFixtures.Document([DocumentFixtures.Nominal("a", 0, ["x"])], binding: DocumentFixtures.WideBinding(quoteChar: '\''));

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.QuoteCharNotSupportedV1, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenQuoteCharAuthoredStandard_ThenNoDiagnostic()
    {
        var document = DocumentFixtures.Document([DocumentFixtures.Nominal("a", 0, ["x"])], binding: DocumentFixtures.WideBinding(quoteChar: '"'));

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenDelimiterEqualsDefaultedQuoteChar_ThenBindingDelimiterQuoteConflict()
    {
        // §5.1: the conflict is judged on the resolved pair: an authored '"'
        // delimiter collides with the defaulted quote.
        var document = DocumentFixtures.Document([DocumentFixtures.Nominal("a", 0, ["x"])], binding: DocumentFixtures.WideBinding(delimiter: '"'));

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.BindingDelimiterQuoteConflict, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenDelimiterAndQuoteCharBothAuthoredSame_ThenBothDiagnosticsFire()
    {
        // D-076: two distinct §5.1 conditions (the unsupported quote and the
        // delimiter conflict) report independently.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"])],
            binding: DocumentFixtures.WideBinding(delimiter: '|', quoteChar: '|'));

        var result = Resolve(document);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.QuoteCharNotSupportedV1);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.BindingDelimiterQuoteConflict);
        Assert.Equal(2, result.Diagnostics.Count);
    }

    [Theory]
    [InlineData("identity", SourceValueType.Number)]
    [InlineData("ordered_cuts", SourceValueType.Number)]
    [InlineData("manual_cuts", SourceValueType.String)]
    public void Resolve_WhenValueTypeConflictsWithTypeFixingDiscretizer_ThenSourceValueTypeInvalid(
        string kind, SourceValueType authored)
    {
        // §10.2 (D-061): identity/ordered_cuts are string-fixing, manual_cuts
        // number-fixing; the other authored type is invalid.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0, authored),
                discretizer: Discretizer(kind), scale: new NominalScaleSection(), declaredDomain: ["x"])]);

        var result = Resolve(document);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SourceValueTypeInvalid, diagnostic.Code);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
    }

    [Theory]
    [InlineData("identity", SourceValueType.String)]
    [InlineData("ordered_cuts", SourceValueType.String)]
    [InlineData("manual_cuts", SourceValueType.Number)]
    public void Resolve_WhenValueTypeMatchesTypeFixingDiscretizer_ThenNoDiagnostic(
        string kind, SourceValueType authored)
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0, authored),
                discretizer: Discretizer(kind), scale: new NominalScaleSection(), declaredDomain: ["x"])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenNumberSourceHasStringRestrictTo_ThenRestrictToNumericEntryRequired()
    {
        // §10.4 (D-063): this code, not SourceValueTypeInvalid, owns the
        // numeric-source/string-entry mismatch.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToValue("young")])]);

        var result = Resolve(document);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.RestrictToNumericEntryRequired, diagnostic.Code);
        Assert.Equal("age", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenNumberSourceHasExactNumericRestrictTo_ThenResolvesCleanly()
    {
        // §10.4/D-091: the exact { value = n } entry is the numeric form the bare-string reject
        // above points at, so the same attribute resolves cleanly once it is used.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToNumber(30), new RestrictToRange(10, 20)])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Equal([new RestrictToNumber(30), new RestrictToRange(10, 20)], spec.Attributes[0].RestrictTo);
    }

    [Fact]
    public void Resolve_WhenStringSourceHasExactNumericRestrictTo_ThenSourceValueTypeInvalid()
    {
        // §10.4/§10.2/D-091: a numeric EXACT entry on a string-typed source is the same
        // value-type mismatch a range is: the §10.2 code owns both directions of "numeric entry
        // on a string source".
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["a"], restrictTo: [new RestrictToNumber(30)])]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceValueTypeInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenPredicateNumberSourceHasBareString_ThenRestrictToNumericEntryRequired()
    {
        // The rename applies at predicate sources exactly as at column sources: the check is
        // source-kind agnostic (§10.2).
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("stage", new PredicateSourceSection("Stage", SourceValueType.Number),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToValue("early")])],
            binding: DocumentFixtures.TripleBinding());

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.RestrictToNumericEntryRequired, Assert.Single(result.Diagnostics).Code);
    }

    // --- RestrictToRangeInvalid (§10.4/D-091) --------------------------------

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Resolve_WhenExactRestrictValueIsNonFinite_ThenRestrictToRangeInvalid(double value)
    {
        // A non-finite exact value can match no usable observation, so it is authored nonsense
        // rather than a filter that happens to keep nothing. Diagnosed on the USER channel
        // (never an exception), because the reader can legitimately produce it from `nan`/`inf`.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToNumber(value)])]);

        var result = Resolve(document);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.RestrictToRangeInvalid, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("age", diagnostic.Location?.AttributeName);
        Assert.False(result.TryGetValue(out _));
    }

    [Theory]
    [InlineData(20.0, 20.0)]   // equal: half-open [20, 20) matches nothing
    [InlineData(50.0, 10.0)]   // reversed
    [InlineData(double.NaN, 20.0)]
    [InlineData(10.0, double.NaN)]
    [InlineData(double.NegativeInfinity, 20.0)]
    [InlineData(10.0, double.PositiveInfinity)]
    public void Resolve_WhenRangeIsInvalid_ThenRestrictToRangeInvalid(double from, double to)
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToRange(from, to)])]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.RestrictToRangeInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenRangeEndsAreOpen_ThenValidBecauseOpenIsNullNotInfinity()
    {
        // §10.4: {} means "any usable numeric value" and one-sided ranges are equally valid: an
        // omitted bound is open (null), never ±infinity, so the non-finite rule must not catch it.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToRange(null, null), new RestrictToRange(90, null), new RestrictToRange(null, 5)])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenOneEntryIsBothWronglyTypedAndInvalid_ThenBothCodesCoFire()
    {
        // EP-14 aggregation, on ONE entry: "numeric entry on a string source" and "that exact
        // value is not finite" are INDEPENDENT conditions, and both hold here. Reporting only the
        // first would hide the second edit the author still has to make: the same reasoning that
        // makes D-076's quote check and delimiter/quote conflict co-fire.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["a"], restrictTo: [new RestrictToNumber(double.NaN)])]);

        var result = Resolve(document);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueTypeInvalid);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.RestrictToRangeInvalid);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenOneRangeEntryIsBothWronglyTypedAndReversed_ThenBothCodesCoFire()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["a"], restrictTo: [new RestrictToRange(20, 10)])]);

        var result = Resolve(document);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueTypeInvalid);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.RestrictToRangeInvalid);
    }

    [Fact]
    public void Resolve_WhenAWronglyTypedEntryIsOtherwiseValid_ThenOnlyTheTypeMismatchReports()
    {
        // The complement, so the co-firing above is not just "always emit both": a well-formed
        // range on a string source is wrong for ONE reason only.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["a"], restrictTo: [new RestrictToRange(10, 20)])]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceValueTypeInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenSeveralRestrictEntriesAreInvalid_ThenAllAggregateWithoutThrowing()
    {
        // EP-14 + the D-098 success check: independent conditions aggregate, the result fails,
        // and NO strict factory runs, so an authored error never escapes as an exception.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo:
                [
                    new RestrictToNumber(double.NaN),      // RestrictToRangeInvalid
                    new RestrictToRange(50, 10),           // RestrictToRangeInvalid
                    new RestrictToValue("young"),          // RestrictToNumericEntryRequired
                ])]);

        var result = Resolve(document); // must not throw

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCode.RestrictToRangeInvalid));
        Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.RestrictToNumericEntryRequired);
    }

    // --- document snapshot (D-098) -------------------------------------------

    [Fact]
    public void Resolve_WhenCallerMutatesTheRestrictListAfterwards_ThenTheDocumentSnapshotIsUnaffected()
    {
        // D-098: ResolvedDocument holds a deep snapshot whose restriction list is its own immutable
        // copy, so a caller mutating the list it passed cannot reach the document the fingerprints
        // read. Discriminating on purpose: the injected entry is a NUMERIC exact one, so a snapshot
        // that copied only some variants (or aliased the list) would show it.
        var authored = new List<RestrictToEntry> { new RestrictToNumber(30) };
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: authored)]);

        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));
        Assert.True(resolved.TryGetValue(out var doc),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        authored.Add(new RestrictToNumber(99));

        Assert.Equal([new RestrictToNumber(30)], doc!.Document.Attributes[0].RestrictTo);
        Assert.Equal([new RestrictToNumber(30)], doc.Resolved.Spec.Attributes[0].RestrictTo);
    }

    [Fact]
    public void Resolve_WhenSnapshotted_ThenTheRestrictListIsNotCastableToAMutableCollection()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToNumber(30)])]);

        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));
        Assert.True(resolved.TryGetValue(out var doc));

        var entries = doc!.Document.Attributes[0].RestrictTo!;
        Assert.IsNotType<RestrictToEntry[]>(entries);
        Assert.IsNotType<List<RestrictToEntry>>(entries);
    }

    // §10.8/D-075: calibrate writes the snapshot back out, so every map keeps its stored order.
    // Six keys of one length, or keys whose lengths are far apart, are shapes a frozen map stores
    // in hash order; the last two rows are shapes a frozen map happened to keep.
    public static TheoryData<string[]> AuthoredLabelOrders() => new()
    {
        new[] { "red", "tan", "ash", "fig", "elm", "oak" },
        new[] { "WA", "CA", "NY", "TX", "FL", "IL" },
        new[] { "n", "y", "not_applicable" },
        new[] { "10.0", "20.0", "30.0", "40.0", "50.0", "60.0" },
        new[] { "m", "zz", "a", "bbbb", "ccc" },
        new[] { "Red", "red", "RED", "x y", "a.b", "café" },
    };

    [Theory]
    [MemberData(nameof(AuthoredLabelOrders))]
    public void Resolve_WhenValueLabelsAreAuthored_ThenTheSnapshotKeepsTheirOrder(string[] keys)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            labels[key] = "L-" + key;
        }

        var document = DocumentFixtures.Document(
            [
                DocumentFixtures.Attribute("k", DocumentFixtures.Column(0),
                    discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                    declaredDomain: keys, valueLabels: labels),
                DocumentFixtures.Attribute("parked", DocumentFixtures.Column(1), include: false, valueLabels: labels),
            ],
            templates: [DocumentFixtures.Template("t", valueLabels: labels)]);

        var resolved = SpecResolver.Resolve(document, new SourceSchema(2));

        Assert.True(resolved.TryGetValue(out var doc), string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var expected = keys.Select(key => KeyValuePair.Create(key, "L-" + key)).ToArray();
        Assert.Equal(expected, doc!.Document.Attributes[0].ValueLabels!.ToArray());
        Assert.Equal(expected, doc.Document.Attributes[1].ValueLabels!.ToArray());
        Assert.Equal(expected, doc.Document.Templates[0].ValueLabels!.ToArray());
        Assert.Equal(keys, doc.Document.Attributes[0].ValueLabels!.Keys);
    }

    [Fact]
    public void Resolve_WhenValueLabelKeysDifferOnlyInCase_ThenTheSnapshotLooksEachUpOrdinally()
    {
        string[] keys = ["Red", "red", "RED"];
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("k", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: keys, valueLabels: keys.ToDictionary(key => key, key => "L-" + key, StringComparer.Ordinal))]);

        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));

        Assert.True(resolved.TryGetValue(out var doc));
        var labels = doc!.Document.Attributes[0].ValueLabels!;
        Assert.Equal(3, labels.Count);
        Assert.Equal("L-red", labels["red"]);
        Assert.Equal("L-RED", labels["RED"]);
        Assert.True(labels.ContainsKey("Red"));
        Assert.False(labels.TryGetValue("rEd", out _));
    }

    [Fact]
    public void Resolve_WhenCallerMutatesTheValueLabelsAfterwards_ThenTheSnapshotTextAndFingerprintsAreUnaffected()
    {
        // D-098: the snapshot owns a copy, so the caller's later edits reach neither the snapshot
        // nor the text and fingerprints computed from the resolution.
        string[] keys = ["red", "tan", "ash", "fig", "elm", "oak"];
        var labels = keys.ToDictionary(key => key, key => "L-" + key, StringComparer.Ordinal);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("k", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: keys, valueLabels: labels)]);
        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));
        Assert.True(resolved.TryGetValue(out var doc));
        var plan = ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(doc!.Resolved)).Value!;
        var snapshot = doc.Document.Attributes[0].ValueLabels!.ToArray();
        var text = SpecWriter.Write(doc.Document);
        var fingerprints = SpecFingerprints.ComputeNative(doc, plan);

        labels["red"] = "changed";
        labels.Remove("tan");
        labels.Add("zzz", "added");

        Assert.Equal(snapshot, doc.Document.Attributes[0].ValueLabels!.ToArray());
        Assert.Equal(text, SpecWriter.Write(doc.Document));
        Assert.Equal(fingerprints, SpecFingerprints.ComputeNative(doc, plan));
    }

    [Fact]
    public void Resolve_WhenSnapshotted_ThenTheValueLabelsRefuseWritesButSyncRootReachesTheirOwnCopy()
    {
        // D-098: the read-only wrapper refuses every write through its mutable interfaces, but its
        // SyncRoot, and that of its Keys and Values, is the snapshot's own OrderedDictionary copy.
        // A change through that copy reaches what reads the snapshot (its written text), never the
        // caller's map, another resolution, the Core labels, the plan or its fingerprints.
        string[] keys = ["red", "tan", "ash", "fig", "elm", "oak"];
        var labels = keys.ToDictionary(key => key, key => "L-" + key, StringComparer.Ordinal);
        var authored = labels.ToArray();
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("k", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: keys, valueLabels: labels)]);
        Assert.True(SpecResolver.Resolve(document, new SourceSchema(1)).TryGetValue(out var doc));
        Assert.True(SpecResolver.Resolve(document, new SourceSchema(1)).TryGetValue(out var other));
        var plan = ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(doc!.Resolved)).Value!;
        var names = plan.FormalAttributes.Select(attribute => attribute.RenderedName).ToArray();
        var fingerprints = SpecFingerprints.ComputeNative(doc, plan);
        var snapshot = doc.Document.Attributes[0].ValueLabels!;

        var generic = Assert.IsAssignableFrom<IDictionary<string, string>>(snapshot);
        Assert.True(generic.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => generic.Add("x", "y"));
        Assert.Throws<NotSupportedException>(() => generic.Remove("red"));
        Assert.Throws<NotSupportedException>(() => generic["red"] = "z");
        Assert.Throws<NotSupportedException>(generic.Clear);
        var untyped = Assert.IsAssignableFrom<System.Collections.IDictionary>(snapshot);
        Assert.True(untyped.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => untyped.Add("x", "y"));
        Assert.Throws<NotSupportedException>(() => untyped.Remove("red"));
        Assert.Throws<NotSupportedException>(() => untyped["red"] = "z");
        Assert.Throws<NotSupportedException>(untyped.Clear);

        var copy = Assert.IsType<OrderedDictionary<string, string>>(SyncRootOf(snapshot));
        Assert.Same(copy, SyncRootOf(snapshot.Keys));
        Assert.Same(copy, SyncRootOf(snapshot.Values));
        Assert.NotSame(labels, copy);
        Assert.NotSame(copy, SyncRootOf(other!.Document.Attributes[0].ValueLabels!));

        copy["red"] = "changed";
        Assert.True(copy.Remove("tan"));
        copy.Add("zzz", "added");

        Assert.Equal("changed", snapshot["red"]);
        Assert.Contains(
            "value_labels = { red = \"changed\", ash = \"L-ash\", fig = \"L-fig\", elm = \"L-elm\", oak = \"L-oak\", zzz = \"added\" }",
            SpecWriter.Write(doc.Document), StringComparison.Ordinal);
        Assert.Equal(authored, labels.ToArray());
        Assert.Equal(authored, other.Document.Attributes[0].ValueLabels!.ToArray());
        Assert.Equal("L-red", doc.Resolved.Spec.Attributes[0].ValueLabels["red"]);
        Assert.Equal(names, plan.FormalAttributes.Select(attribute => attribute.RenderedName));
        Assert.Equal(fingerprints, SpecFingerprints.ComputeNative(doc, plan));
    }

    [Fact]
    public void Resolve_WhenValueLabelsAreEmptyOrOmitted_ThenTheSnapshotKeepsThePresenceDistinction()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("empty", DocumentFixtures.Column(0), discretizer: Discretizer("identity"),
                scale: new NominalScaleSection(), declaredDomain: ["x"], valueLabels: new Dictionary<string, string>()),
            DocumentFixtures.Attribute("omitted", DocumentFixtures.Column(1), discretizer: Discretizer("identity"),
                scale: new NominalScaleSection(), declaredDomain: ["x"]),
        ]);

        var resolved = SpecResolver.Resolve(document, new SourceSchema(2));

        Assert.True(resolved.TryGetValue(out var doc));
        Assert.NotNull(doc!.Document.Attributes[0].ValueLabels);
        Assert.Empty(doc.Document.Attributes[0].ValueLabels!);
        Assert.Null(doc.Document.Attributes[1].ValueLabels);

        // An empty map stays a frozen map, never ReadOnlyDictionary.Empty, which is one shared
        // instance over one mutable Dictionary.
        Assert.IsAssignableFrom<System.Collections.Frozen.FrozenDictionary<string, string>>(doc.Document.Attributes[0].ValueLabels);
    }

    private static object SyncRootOf(object collection) => ((System.Collections.ICollection)collection).SyncRoot;

    // --- D-105 zero canonicalization at the seam ------------------------------

    [Fact]
    public void Resolve_WhenExactRestrictValueIsNegativeZero_ThenItResolvesAsPositiveZero()
    {
        // D-105, at the site that owns it: the seam canonicalizes what it resolves, so an authored
        // -0 resolves (and therefore matches, plans, and hashes) identically to 0. This is the
        // "already-numeric" arm of the chain (the value arrives as a TOML double; there is no
        // text to parse). CanonicalJson.AppendNumber is untouched and still formats -0.0 as "-0",
        // which is exactly why the canonicalization must happen HERE.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToNumber(-0.0)])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        var entry = Assert.IsType<RestrictToNumber>(Assert.Single(spec.Attributes[0].RestrictTo));
        Assert.Equal(0.0, entry.Value);
        Assert.False(double.IsNegative(entry.Value)); // Assert.Equal cannot tell -0 from 0
    }

    [Fact]
    public void Resolve_WhenRangeBoundsAreNegativeZero_ThenTheyResolveAsPositiveZero()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToRange(-0.0, 5)])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        var range = Assert.IsType<RestrictToRange>(Assert.Single(spec.Attributes[0].RestrictTo));
        Assert.False(double.IsNegative(range.From!.Value));
    }

    [Fact]
    public void Resolve_WhenRestrictEntriesRepeat_ThenAuthoredOrderAndDuplicatesSurviveResolution()
    {
        // Resolved Core state mirrors the document (D-057). Canonical sorting/deduplication is a
        // FINGERPRINT projection only (§14) and must not rewrite authored state: the plan and
        // emit see what the author wrote.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToNumber(30), new RestrictToRange(10, 20), new RestrictToNumber(30)])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(
            [new RestrictToNumber(30), new RestrictToRange(10, 20), new RestrictToNumber(30)],
            spec.Attributes[0].RestrictTo);
    }

    [Fact]
    public void Resolve_WhenFilterOnlyAttributeHasAnInvalidRange_ThenItStillReports()
    {
        // §10.1/D-049/D-076: restrict_to is LIVE on an excluded attribute (the filter-only
        // pattern), so its shape is validated even though the attribute's discretizer/scale/domain
        // are parked and unread.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0), include: false,
                discretizer: Discretizer("manual_cuts"), restrictTo: [new RestrictToRange(50, 10)])]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.RestrictToRangeInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenStringSourceHasRangeRestrictTo_ThenSourceValueTypeInvalid()
    {
        // §10.4 (D-063): the mirror case (a range entry on a string-typed source)
        // is owned by the §10.2 code.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["a"], restrictTo: [new RestrictToRange(1, 5)])]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceValueTypeInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenParkedCutDiscretizerTypesLiveRestrictTo_ThenStillRejected()
    {
        // D-076: restrict_to is live on an excluded (filter-only) attribute, and a
        // parked numeric-cut discretizer legitimately types it (§10.4: "a numeric
        // source … or a numeric-cut discretizer"); the string entry still rejects.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0), include: false,
                discretizer: Discretizer("manual_cuts"), restrictTo: [new RestrictToValue("young")])]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.RestrictToNumericEntryRequired, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenFilterOnlyStringRestrictTo_ThenResolvesClean()
    {
        // §19.4: the filter-only pattern: string entries over an untyped,
        // discretizer-less source default to string and validate clean.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("Gene", DocumentFixtures.Column(0), include: false,
                restrictTo: [new RestrictToValue("Bmp5")])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenFilterOnlySourceDeclaresNumber_ThenNumericRestrictToResolvesClean()
    {
        // §10.2: a source with no discretizer takes its authored value_type, so a filter-only
        // attribute with numeric entries declares "number".
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("stage", DocumentFixtures.Column(0, SourceValueType.Number),
                include: false, restrictTo: [new RestrictToRange(3, 9), new RestrictToNumber(5)])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(SourceValueType.Number, Assert.IsType<ColumnSource>(Assert.Single(spec.Attributes).Source).ValueType);
    }

    [Fact]
    public void Resolve_WhenFilterOnlySourceOmitsValueType_ThenNumericRestrictToIsSourceValueTypeInvalid()
    {
        // §10.2: with no discretizer and no authored value_type the source is "string", and a
        // string source with a numeric restrict_to entry is SourceValueTypeInvalid.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("stage", DocumentFixtures.Column(0), include: false,
                restrictTo: [new RestrictToRange(3, 9)])]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(DiagnosticCode.SourceValueTypeInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenRestrictToValueNotInExplicitDomain_ThenWarningAndStillResolves()
    {
        // §10.4 (D-063): the typo-catcher warns without failing the resolve.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["Bachelors", "Masters"],
                restrictTo: [new RestrictToValue("Bachelors"), new RestrictToValue("Bachelor")])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.RestrictToValueNotInDomain, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Bachelor", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenDomainAbsent_ThenNoDomainTypoWarning()
    {
        // The typo-catcher needs a non-empty declared domain to check against, so neither an
        // omitted domain nor an authored [] produces RestrictToValueNotInDomain (D-063).
        foreach (var domain in new IReadOnlyList<string>?[] { null, [] })
        {
            var document = DocumentFixtures.Document(
                [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                    discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                    declaredDomain: domain, restrictTo: [new RestrictToValue("x")])]);

            var result = Resolve(document);

            Assert.True(result.TryGetValue(out _));
            Assert.Empty(result.Diagnostics);
        }
    }

    [Fact]
    public void Resolve_WhenRestrictToValueNotInParkedDomain_ThenNoWarning()
    {
        // D-049/D-076: declared_domain is emitted-shaping config, parked when the
        // attribute is excluded; the live restrict_to is not checked against it.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0), include: false,
                discretizer: Discretizer("identity"),
                declaredDomain: ["Bachelors"], restrictTo: [new RestrictToValue("nope")])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("manual_cuts")]
    [InlineData("ordered_cuts")]
    public void Resolve_WhenOrdinalOrderAuthoredOverCutDiscretizer_ThenOrdinalOrderNotAllowedWithCuts(string kind)
    {
        // §12.3 (D-060(a)): the cut geometry is the single source of bin order.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer(kind),
                scale: new OrdinalScaleSection(Direction: null, Boundary: null, Order: ["lo", "hi"], DropTop: null))]);

        var result = Resolve(document);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.OrdinalOrderNotAllowedWithCuts, diagnostic.Code);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenOrdinalOrderAuthoredEmptyOverCuts_ThenPresenceStillRejects()
    {
        // §12.3 "MUST NOT be present": an authored [] is still an order
        // declaration over cut bins.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(Direction: null, Boundary: null, Order: [], DropTop: null))]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.OrdinalOrderNotAllowedWithCuts, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Inclusive)]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Strict)]
    public void Resolve_WhenAuthoredBoundaryStraddlesCutGeometry_ThenOrdinalBoundaryIncompatibleWithCuts(
        OrdinalDirection direction, OrdinalBoundary boundary)
    {
        // §12.3 (D-060(b)): over half-open cut bins 'le' pairs with '<' and 'ge'
        // with '>='; an authored straddling boundary is rejected at the seam.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(direction, boundary, Order: null, DropTop: null))]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenAuthoredBoundaryStraddlesDefaultedDirection_ThenStillRejects()
    {
        // D-060: authoredness is judged on the per-attribute boundary; the
        // geometry is judged on the resolved direction, here from [defaults].
        var defaults = new DefaultsSection(
            Include: null, MissingPolicy: null, UnknownValuePolicy: null, DuplicateObjectPolicy: null,
            OrdinalDirection.Le, OrdinalBoundary: null);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(Direction: null, OrdinalBoundary.Inclusive, Order: null, DropTop: null))],
            defaults: defaults);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenBoundaryDefaultedOverCuts_ThenNeverTrips()
    {
        // D-060(c): a boundary arriving via [defaults].ordinal_boundary is
        // defaulted, not authored: over cut bins it never selects the operator
        // and never trips the check, whatever its value.
        var defaults = new DefaultsSection(
            Include: null, MissingPolicy: null, UnknownValuePolicy: null, DuplicateObjectPolicy: null,
            OrdinalDirection: null, OrdinalBoundary.Strict);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(OrdinalDirection.Ge, Boundary: null, Order: null, DropTop: null))],
            defaults: defaults);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        // The defaulted value still fills the resolved carrier; only the check
        // distinguishes defaulted from authored.
        Assert.Equal(OrdinalBoundary.Strict, Assert.IsType<OrdinalScale>(Assert.Single(spec.Attributes).Scale).Boundary);
    }

    [Theory]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Strict)]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Inclusive)]
    public void Resolve_WhenAlignedBoundaryAuthoredOverCuts_ThenNoDiagnostic(
        OrdinalDirection direction, OrdinalBoundary boundary)
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(direction, boundary, Order: null, DropTop: null))]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenOrdinalOverCutsConfigParked_ThenNeverAnError()
    {
        // D-049/D-060: the ordinal-over-cuts contract applies to active attributes;
        // parked scale config never blocks.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), include: false,
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(OrdinalDirection.Ge, OrdinalBoundary.Strict, Order: ["x"], DropTop: null))]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenObjectKeyRowIndexUnderTriple_ThenObjectKeyModeInvalidForShape()
    {
        // §5.4 (D-064): row_index is positional over wide rows; under triple the
        // subject keys objects.
        var objectKey = new ObjectKeySection(ObjectKeyMode.RowIndex, Column: null, Columns: null, Aggregate: null);
        var binding = new BindingSection(SourceShape.Triple, Encoding: null, Delimiter: null, QuoteChar: null,
            HasHeader: null, Locale: null, MissingToken: null, TripleOrdering.SubjectGrouped,
            new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(2)), objectKey);
        var document = DocumentFixtures.Document([TriplePredicate()], binding: binding);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.ObjectKeyModeInvalidForShape, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    // --- D-101 zero canonicalization of authored cuts and manual bounds -------

    [Fact]
    public void Resolve_WhenAManualCutIsNegativeZero_ThenItResolvesAsPositiveZeroAndTheDocumentKeepsTheSign()
    {
        // §11.2/D-101: the seam canonicalizes an authored -0 cut, so it renders and hashes like 0,
        // while the caller's list and the document snapshot keep the authored value. Only the bit
        // pattern can show this, since -0.0 == 0.0.
        var authored = new List<double> { -0.0, 10.0 };
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0, SourceValueType.Number),
                discretizer: new ManualCutsDiscretizerSection(authored, Ends: null), scale: new NominalScaleSection())]);

        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));

        Assert.True(resolved.TryGetValue(out var doc), string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var cuts = Assert.IsType<ManualCutsDiscretizer>(doc!.Resolved.Spec.Attributes[0].Discretizer).Cuts;
        Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(cuts[0]));
        Assert.True(double.IsNegative(authored[0]));
        var snapshot = Assert.IsType<ManualCutsDiscretizerSection>(doc.Document.Attributes[0].Discretizer);
        Assert.True(double.IsNegative(snapshot.Cuts![0]));
    }

    [Theory]
    [InlineData("vmin")]
    [InlineData("vmax")]
    public void Resolve_WhenAManualEqualWidthBoundIsNegativeZero_ThenItResolvesAsPositiveZeroWithTheSameCuts(string bound)
    {
        // §11.4/D-101: the bound resolves as +0, and the derived cut has the same bits either way.
        var (vmin, vmax) = bound == "vmin" ? (-0.0, 10.0) : (-10.0, -0.0);
        var negative = ResolvedEqualWidth(vmin, vmax);
        var positive = ResolvedEqualWidth(vmin == 0.0 ? 0.0 : vmin, vmax == 0.0 ? 0.0 : vmax);

        Assert.Equal(BitConverter.DoubleToInt64Bits(positive.VMin!.Value), BitConverter.DoubleToInt64Bits(negative.VMin!.Value));
        Assert.Equal(BitConverter.DoubleToInt64Bits(positive.VMax!.Value), BitConverter.DoubleToInt64Bits(negative.VMax!.Value));
        Assert.Equal(positive.Cuts.Select(BitConverter.DoubleToInt64Bits), negative.Cuts.Select(BitConverter.DoubleToInt64Bits));
    }

    [Fact]
    public void Resolve_WhenSignedZeroCutsAreInvalid_ThenTheyReportExactlyLikeTheirPositiveZeroTwins()
    {
        // D-056/D-101: zero is made positive before the factory validates, so the codes, severities,
        // count and order are the 0 twin's, and a message shows the resolved 0.
        (double[] Negative, double[] Positive, BinEnds Ends)[] cases =
        [
            (new[] { 0.0, -0.0 }, new[] { 0.0, 0.0 }, BinEnds.Open),
            (new[] { -0.0, double.NaN }, new[] { 0.0, double.NaN }, BinEnds.Open),
            (new[] { -0.0 }, new[] { 0.0 }, BinEnds.Closed),
        ];

        foreach (var (negative, positive, ends) in cases)
        {
            var negativeResult = Resolve(CutsDocument(negative, ends), new SourceSchema(1));
            var positiveResult = Resolve(CutsDocument(positive, ends), new SourceSchema(1));

            Assert.False(negativeResult.TryGetValue(out _));
            Assert.NotEmpty(positiveResult.Diagnostics);
            Assert.Equal(
                positiveResult.Diagnostics.Select(d => (d.Code, d.Severity, d.Message)),
                negativeResult.Diagnostics.Select(d => (d.Code, d.Severity, d.Message)));
        }
    }

    [Fact]
    public void Resolve_WhenASignedZeroManualRangeIsInvalid_ThenItReportsExactlyLikeItsPositiveZeroTwin()
    {
        // EqualWidthRangeInvalid for the empty span [0, 0]; EqualWidthCutsCollapsed for bins = 4 over
        // [0, 1] with round_to = 1, whose first two cuts both round to 0.
        (long Bins, double VMax, CutPrecision? Precision)[] cases =
        [
            (2, 0.0, null),
            (4, 1.0, RoundToPrecision.Create(1.0)),
        ];

        foreach (var (bins, vmax, precision) in cases)
        {
            var negativeResult = Resolve(EqualWidthDocument(bins, -0.0, vmax, precision), new SourceSchema(1));
            var positiveResult = Resolve(EqualWidthDocument(bins, 0.0, vmax, precision), new SourceSchema(1));

            Assert.False(negativeResult.TryGetValue(out _));
            Assert.NotEmpty(positiveResult.Diagnostics);
            Assert.Equal(
                positiveResult.Diagnostics.Select(d => (d.Code, d.Severity, d.Message)),
                negativeResult.Diagnostics.Select(d => (d.Code, d.Severity, d.Message)));
        }
    }

    [Fact]
    public void Resolve_WhenManualCutsHoldNoNegativeZero_ThenEveryBitSurvivesResolution()
    {
        double[] authored = [-5.0, 0.0, 2.5, 9007199254740992.0, 1e17];

        var resolved = Resolve(CutsDocument(authored, BinEnds.Open), new SourceSchema(1));

        Assert.True(resolved.TryGetValue(out var spec));
        var cuts = Assert.IsType<ManualCutsDiscretizer>(spec.Attributes[0].Discretizer).Cuts;
        Assert.Equal(authored.Select(BitConverter.DoubleToInt64Bits), cuts.Select(BitConverter.DoubleToInt64Bits));
    }

    private static SpecDocument CutsDocument(IReadOnlyList<double> cuts, BinEnds ends) =>
        DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0, SourceValueType.Number),
                discretizer: new ManualCutsDiscretizerSection(cuts, ends), scale: new NominalScaleSection())]);

    private static SpecDocument EqualWidthDocument(long bins, double vmin, double vmax, CutPrecision? precision) =>
        DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0, SourceValueType.Number),
                discretizer: new EqualWidthDiscretizerSection(bins, EqualWidthRange.Manual, vmin, vmax, precision),
                scale: new NominalScaleSection())]);

    private static EqualWidthDiscretizer ResolvedEqualWidth(double vmin, double vmax)
    {
        var resolved = Resolve(EqualWidthDocument(2, vmin, vmax, precision: null), new SourceSchema(1));
        Assert.True(resolved.TryGetValue(out var spec), string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.IsType<EqualWidthDiscretizer>(spec.Attributes[0].Discretizer);
    }

    // --- Duplicate names + value_labels re-homed to the seam (D-080) ---

    [Fact]
    public void Resolve_WhenDuplicateAttributeName_ThenAttributeNameDuplicate()
    {
        // §10.2 (D-080): the dup-name reject fires at the seam, not the planner.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("dup", 0, ["a"]), DocumentFixtures.Nominal("dup", 1, ["b"])]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Equal("dup", Assert.Single(
            result.Diagnostics, d => d.Code == DiagnosticCode.AttributeNameDuplicate).Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenNameRepeatedThrice_ThenOneDuplicateDiagnosticPerExtraOccurrence()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("dup", 0, ["a"]),
            DocumentFixtures.Nominal("dup", 1, ["b"]),
            DocumentFixtures.Nominal("dup", 2, ["c"]),
        ]);

        var result = Resolve(document);

        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCode.AttributeNameDuplicate));
    }

    [Fact]
    public void Resolve_WhenDuplicateNameAndSiblingSourceBroken_ThenBothAggregateInOnePass()
    {
        // The D-080 payoff: the dup check reads the document sections, so a
        // duplicate whose sibling field fails to resolve (ResolveAttribute drops
        // it) still surfaces, alongside that sibling's own diagnostic (EP-14). A
        // check over resolved attributes would lose it.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("dup", 0, ["a"]),
            DocumentFixtures.Attribute("dup", DocumentFixtures.Column(-1),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["b"]),
        ]);

        var result = Resolve(document);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.AttributeNameDuplicate);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SourceBindingInvalid);
    }

    [Fact]
    public void Resolve_WhenValueLabelKeyNotInDomain_ThenValueLabelKeyNotInDomain()
    {
        // §10.8 (D-080): the live typo-catcher for identity value labels, at the seam.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b", "n"], new Dictionary<string, string> { ["x"] = "broad" })]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Equal("g", Assert.Single(
            result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyNotInDomain).Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenValueLabelsUnderCutDiscretizer_ThenDormantAndNoDiagnostic()
    {
        // §10.8 / D-049: value_labels is dormant under a cut discretizer (its bin
        // labels are not raw values): ignored, never ValueLabelKeyNotInDomain.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: new ManualCutsDiscretizerSection([30.0], BinEnds.Open),
                scale: new NominalScaleSection(),
                valueLabels: new Dictionary<string, string> { ["old"] = "Old label" })]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyNotInDomain);
    }

    [Fact]
    public void Resolve_WhenExcludedIdentityHasStaleValueLabels_ThenParkedAndNoDiagnostic()
    {
        // D-049: value_labels on an excluded attribute is parked config: the seam
        // check runs only for included attributes, so a stale key never blocks a toggled-off attribute.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("g", DocumentFixtures.Column(0), include: false,
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["b"], valueLabels: new Dictionary<string, string> { ["x"] = "stale" })]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenDuplicateNameAndStaleLabel_ThenBothSeamChecksAggregate()
    {
        // EP-14: the two re-homed seam checks aggregate with each other and the rest
        // of the resolve pass rather than short-circuiting.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("g", 0, ["b"]),
            DocumentFixtures.Nominal("g", 1, ["b"], new Dictionary<string, string> { ["x"] = "stale" }),
        ]);

        var result = Resolve(document);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.AttributeNameDuplicate);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyNotInDomain);
    }

    // --- free_per_value + numeric identity (§11.3 / §10.3 / §10.8 / §12.3, D-061/D-096) ---

    private static AttributeSection FreePerValue(
        string name, int index, SourceValueType? valueType = null,
        IReadOnlyList<string>? domain = null,
        IReadOnlyDictionary<string, string>? valueLabels = null,
        ScaleSection? scale = null) =>
        DocumentFixtures.Attribute(name, DocumentFixtures.Column(index, valueType),
            discretizer: new FreePerValueDiscretizerSection(),
            scale: scale ?? new NominalScaleSection(),
            declaredDomain: domain, valueLabels: valueLabels);

    [Fact]
    public void Resolve_WhenFreePerValueNoAuthoredType_ThenStringModeDefault()
    {
        // D-061: free_per_value is type-flexible; absent value_type defaults to string.
        var result = Resolve(DocumentFixtures.Document([FreePerValue("g", 0, domain: ["b", "n"])]));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(SourceValueType.String, Assert.IsType<FreePerValueDiscretizer>(spec.Attributes[0].Discretizer).ValueType);
        Assert.Equal(["b", "n"], spec.Attributes[0].DeclaredDomain); // string domain stays verbatim
    }

    [Theory]
    [InlineData(SourceValueType.String)]
    [InlineData(SourceValueType.Number)]
    public void Resolve_WhenFreePerValueAuthoredType_ThenThatMode(SourceValueType valueType)
    {
        var domain = valueType == SourceValueType.Number ? new[] { "90" } : ["b"];
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, valueType, domain)]));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(valueType, Assert.IsType<FreePerValueDiscretizer>(spec.Attributes[0].Discretizer).ValueType);
    }

    [Fact]
    public void Resolve_WhenIdentityAuthoredNumber_ThenSourceValueTypeInvalid()
    {
        // D-061: identity remains string-fixing: number + identity stays invalid; the numeric
        // distinct binner is free_per_value.
        var document = DocumentFixtures.Document([DocumentFixtures.Attribute(
            "g", DocumentFixtures.Column(0, SourceValueType.Number),
            discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["b"])]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueTypeInvalid);
    }

    [Fact]
    public void Resolve_WhenNumericFreePerValueDomain_ThenNormalizedToCanonicalKeysInDeclarationOrder()
    {
        // §10.3/D-096: numeric domain entries parse to canonical numeric identities under locale;
        // declaration order is preserved over first occurrence of each identity.
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["90.0", "5e0", "-0"])]));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(["90", "5", "0"], spec.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void Resolve_WhenNumericFreePerValueDomainOmittedVersusAuthoredEmpty_ThenPresenceSurvivesNormalization()
    {
        // D-122 part 15 through the numeric-normalization branch (NormalizeNumericDomain): omission and an
        // authored [] stay distinct in Core even where the numeric seam runs. An omitted numeric domain
        // resolves to null and still requests calibration; an authored [] resolves to a non-null empty
        // list (a complete fixed empty domain), mints no DeclaredDomainInvalid, and requests none.
        var omitted = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, domain: null)]));
        Assert.True(omitted.TryGetValue(out var omittedSpec));
        Assert.Null(omittedSpec.Attributes[0].DeclaredDomain);
        Assert.True(CalibratedSpec.RequiresData(omittedSpec));

        var authoredEmpty = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, domain: [])]));
        Assert.True(authoredEmpty.TryGetValue(out var authoredEmptySpec));
        var domain = authoredEmptySpec.Attributes[0].DeclaredDomain;
        Assert.NotNull(domain);
        Assert.Empty(domain);
        Assert.DoesNotContain(authoredEmpty.Diagnostics, d => d.Code == DiagnosticCode.DeclaredDomainInvalid);
        Assert.False(CalibratedSpec.RequiresData(authoredEmptySpec));
    }

    [Fact]
    public void Resolve_WhenNumericDomainNormalizationDuplicate_ThenDeclaredDomainInvalid()
    {
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["90", "90.0"])]));

        Assert.False(result.TryGetValue(out _));
        Assert.Equal("v", Assert.Single(
            result.Diagnostics, d => d.Code == DiagnosticCode.DeclaredDomainInvalid).Location?.AttributeName);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void Resolve_WhenNumericDomainUnparseableOrNonFinite_ThenDeclaredDomainInvalid(string entry)
    {
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, [entry])]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.DeclaredDomainInvalid);
    }

    [Fact]
    public void Resolve_WhenNumericValueLabelsNormalized_ThenKeyedByCanonicalIdentity()
    {
        // §10.8/D-096: numeric value_labels keys normalize to the same canonical identity as the domain.
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["90"],
            new Dictionary<string, string> { ["90.0"] = "ninety" })]));

        Assert.True(result.TryGetValue(out var spec));
        Assert.True(spec.Attributes[0].ValueLabels.TryGetValue("90", out var label));
        Assert.Equal("ninety", label);
    }

    [Fact]
    public void Resolve_WhenNumericValueLabelsCollapseToOneIdentity_ThenValueLabelKeyDuplicate()
    {
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["90"],
            new Dictionary<string, string> { ["90"] = "a", ["90.0"] = "b" })]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyDuplicate);
    }

    [Fact]
    public void Resolve_WhenNumericValueLabelKeyNotInNormalizedDomain_ThenValueLabelKeyNotInDomain()
    {
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["5"],
            new Dictionary<string, string> { ["90"] = "a" })]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyNotInDomain);
    }

    [Fact]
    public void Resolve_WhenStringFreePerValueValueLabelKeyNotInDomain_ThenValueLabelKeyNotInDomain()
    {
        // String free_per_value consults value_labels like identity: verbatim membership (§10.8).
        var result = Resolve(DocumentFixtures.Document([FreePerValue("g", 0, SourceValueType.String, ["b"],
            new Dictionary<string, string> { ["x"] = "y" })]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyNotInDomain);
    }

    [Fact]
    public void Resolve_WhenNumericOrder_ThenNormalizedToCanonicalOrderOnScale()
    {
        // §12.3/D-096: numeric scale.order entries normalize to canonical identities.
        var scale = new OrdinalScaleSection(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, ["90.0", "5"], DropTop: null);
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["5", "90"], scale: scale)]));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(["90", "5"], Assert.IsType<OrdinalScale>(spec.Attributes[0].Scale).Order);
    }

    [Fact]
    public void Resolve_WhenNumericOrderNormalizationDuplicate_ThenOrderDomainInvalid()
    {
        var scale = new OrdinalScaleSection(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, ["90", "90.0"], DropTop: null);
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["90"], scale: scale)]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.OrderDomainInvalid);
    }

    [Fact]
    public void Resolve_WhenNumericOrderUnparseable_ThenOrderDomainInvalid()
    {
        var scale = new OrdinalScaleSection(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, ["abc"], DropTop: null);
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["90"], scale: scale)]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.OrderDomainInvalid);
    }

    [Fact]
    public void Resolve_WhenNumericDomainAndLabelErrors_ThenBothAggregateDeterministically()
    {
        // EP-14: the D-096 seam checks aggregate independently rather than short-circuiting.
        var result = Resolve(DocumentFixtures.Document([FreePerValue("v", 0, SourceValueType.Number, ["90", "90.0"],
            new Dictionary<string, string> { ["abc"] = "x" })]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.DeclaredDomainInvalid);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyNotInDomain);
    }

    [Fact]
    public void Resolve_WhenStringFreePerValueRestrictToOutOfDomain_ThenRestrictToValueNotInDomainWarning()
    {
        // §10.4/D-101: the restrict_to domain typo-catcher applies to string free_per_value like
        // identity (both consult the declared domain). A Warning; resolve still succeeds.
        var document = DocumentFixtures.Document([DocumentFixtures.Attribute("g", DocumentFixtures.Column(0),
            discretizer: new FreePerValueDiscretizerSection(), scale: new NominalScaleSection(),
            declaredDomain: ["b", "n"], restrictTo: [new RestrictToValue("z")])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Equal("g", Assert.Single(
            result.Diagnostics, d => d.Code == DiagnosticCode.RestrictToValueNotInDomain).Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenStringFreePerValueRestrictToInDomain_ThenNoTypoWarning()
    {
        var document = DocumentFixtures.Document([DocumentFixtures.Attribute("g", DocumentFixtures.Column(0),
            discretizer: new FreePerValueDiscretizerSection(), scale: new NominalScaleSection(),
            declaredDomain: ["b", "n"], restrictTo: [new RestrictToValue("b")])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.RestrictToValueNotInDomain);
    }

    // --- Value-bin ordinal order shape (D-081) ---

    private static OrdinalScaleSection OrdinalOrder(IReadOnlyList<string> order) =>
        new(Direction: null, Boundary: null, order, DropTop: null);

    [Fact]
    public void Resolve_WhenValueBinOrderHasDuplicateEntries_ThenOrderDomainInvalid()
    {
        // §12.3 (D-081): over a non-cut discretizer the authored scale.order must
        // have distinct, non-empty entries: OrderDomainInvalid, broadened from
        // ordered_cuts to any authored order.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: OrdinalOrder(["a", "a"]), declaredDomain: ["a"])]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.OrderDomainInvalid);
    }

    [Fact]
    public void Resolve_WhenValueBinOrderHasEmptyEntry_ThenOrderDomainInvalid()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: OrdinalOrder(["a", ""]), declaredDomain: ["a"])]);

        var result = Resolve(document);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.OrderDomainInvalid);
    }

    [Fact]
    public void Resolve_WhenValueBinOrderMalformedButExcluded_ThenParkedAndNoDiagnostic()
    {
        // D-049: the order-shape check runs only for included attributes, like the ordinal-over-cuts
        // checks, so a parked order never blocks a toggled-off attribute.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0), include: false,
                discretizer: new IdentityDiscretizerSection(), scale: OrdinalOrder(["a", "a"]), declaredDomain: ["a"])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenValueBinOrderValid_ThenResolvesWithoutDiagnostic()
    {
        // A well-formed order resolves cleanly; the order-vs-domain permutation is a
        // plan-phase check (OrdinalOrderMissing / OrdinalOrderHasUnknownValue), not this.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: OrdinalOrder(["a", "b", "c"]),
                declaredDomain: ["a", "b", "c"])]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    // --- Determinism bridge (EP-7) ---

    [Fact]
    public void Resolve_WhenResolvedTwice_ThenPlansIdentically()
    {
        Assert.True(Resolve(DocumentFixtures.MiniMushroom()).TryGetValue(out var first));
        Assert.True(Resolve(DocumentFixtures.MiniMushroom()).TryGetValue(out var second));

        Assert.True(Plan(first, new SourceSchema(5)).TryGetValue(out var firstPlan));
        Assert.True(Plan(second, new SourceSchema(5)).TryGetValue(out var secondPlan));

        Assert.Equal(
            firstPlan.FormalAttributes.Select(f => (f.RenderedName, f.Identity)),
            secondPlan.FormalAttributes.Select(f => (f.RenderedName, f.Identity)));
    }

    [Fact]
    public void Resolve_WhenMiniMushroomDocument_ThenPlansToTheBedPathSchema()
    {
        // The hand-built document twin must plan to the exact formal-attribute
        // schema the .bed migration path yields (EP-7: one schema, two producers).
        var viaDocument = Resolve(DocumentFixtures.MiniMushroom());
        Assert.True(BedReader.Read(BedFixtures.MushroomBed).TryGetValue(out var bedDocument));
        var migrated = BedMigrator.Migrate(
            bedDocument,
            new BindingSection(SourceShape.Wide, Encoding: null, ',', QuoteChar: null, HasHeader: true,
                Locale: null, MissingToken: null, Ordering: null, Columns: null, ObjectKey: null));
        Assert.True(migrated.TryGetValue(out var bedSpecDocument));
        Assert.True(Resolve(bedSpecDocument).TryGetValue(out var viaBed));

        Assert.True(viaDocument.TryGetValue(out var documentSpec));
        Assert.True(Plan(documentSpec, new SourceSchema(5)).TryGetValue(out var documentPlan));
        Assert.True(Plan(viaBed, new SourceSchema(5)).TryGetValue(out var bedPlan));

        Assert.Equal(
            bedPlan.FormalAttributes.Select(f => (f.RenderedName, f.Identity)),
            documentPlan.FormalAttributes.Select(f => (f.RenderedName, f.Identity)));
    }

    // --- Triple binding resolution + static validation (D-082 / D-085) ---

    [Fact]
    public void Resolve_WhenTripleColumnsByName_ThenResolvesToIndices()
    {
        var document = DocumentFixtures.Document(
            [TriplePredicate()],
            binding: DocumentFixtures.TripleBinding(
                new TripleColumnsSection(new NameColumnRef("s"), new NameColumnRef("p"), new NameColumnRef("o")),
                hasHeader: true));

        var result = Resolve(document, new SourceSchema(3, ["s", "p", "o"]));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new TripleColumns(0, 1, 2), spec.Binding.TripleColumns);
        // §5.4: the resolved subject column keys objects.
        Assert.Equal(0, Assert.IsType<ColumnObjectKey>(spec.Binding.ObjectKey).Index);
    }

    [Fact]
    public void Resolve_WhenTripleColumnsNameVsIndex_ThenSameResolvedBinding()
    {
        // D-082: a role bound by header name resolves to the same index as the
        // equivalent index bind (both need has_header = true), so the resolved
        // binding (and therefore the output fingerprint) is identical.
        var schema = new SourceSchema(3, ["s", "p", "o"]);
        var byName = Resolve(DocumentFixtures.Document([TriplePredicate()],
            binding: DocumentFixtures.TripleBinding(
                new TripleColumnsSection(new NameColumnRef("s"), new NameColumnRef("p"), new NameColumnRef("o")),
                hasHeader: true)), schema);
        var byIndex = Resolve(DocumentFixtures.Document([TriplePredicate()],
            binding: DocumentFixtures.TripleBinding(
                new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(2)),
                hasHeader: true)), schema);

        Assert.True(byName.TryGetValue(out var nameSpec));
        Assert.True(byIndex.TryGetValue(out var indexSpec));
        Assert.Equal(indexSpec.Binding, nameSpec.Binding);
    }

    [Fact]
    public void Resolve_WhenTripleColumnsNotDistinct_ThenTripleColumnsNotDistinct()
    {
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(
            new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(0))));

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.TripleColumnsNotDistinct, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenTripleColumnsPartial_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(
            new TripleColumnsSection(new IndexColumnRef(0), Predicate: null, Value: null)));

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenTripleColumnsMixedAddressing_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(
            new TripleColumnsSection(new IndexColumnRef(0), new NameColumnRef("p"), new IndexColumnRef(2)),
            hasHeader: true));

        var result = Resolve(document, new SourceSchema(3, ["s", "p", "o"]));

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenTripleColumnsAllNameWithoutHeader_ThenSourceBindingInvalid()
    {
        // has_header defaults false for triple (§5.1), so name roles cannot resolve.
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(
            new TripleColumnsSection(new NameColumnRef("s"), new NameColumnRef("p"), new NameColumnRef("o"))));

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticCode.SourceBindingInvalid, d.Code));
    }

    [Fact]
    public void Resolve_WhenTripleColumnsByNameWithoutSchema_ThenSourceBindingInvalid()
    {
        // has_header = true, but no header schema is supplied to resolve the names.
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(
            new TripleColumnsSection(new NameColumnRef("s"), new NameColumnRef("p"), new NameColumnRef("o")),
            hasHeader: true));

        var result = Resolve(document); // schema: null

        Assert.False(result.TryGetValue(out _));
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticCode.SourceBindingInvalid, d.Code));
    }

    [Fact]
    public void Resolve_WhenTripleColumnNameNotInHeader_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(
            new TripleColumnsSection(new NameColumnRef("s"), new NameColumnRef("p"), new NameColumnRef("x")),
            hasHeader: true));

        var result = Resolve(document, new SourceSchema(3, ["s", "p", "o"]));

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenTripleColumnNameMatchesDuplicateHeader_ThenSourceBindingInvalid()
    {
        // §5.3/§10.2: a role name must resolve to exactly one column.
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(
            new TripleColumnsSection(new NameColumnRef("dup"), new NameColumnRef("p"), new NameColumnRef("o")),
            hasHeader: true));

        var result = Resolve(document, new SourceSchema(4, ["dup", "dup", "p", "o"]));

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenTripleColumnIndexOutOfRange_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(
            new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(5))));

        var result = Resolve(document, new SourceSchema(3));

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenColumnSourceUnderTriple_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", new ColumnSourceSection(0, Name: null, ValueType: null),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection())],
            binding: DocumentFixtures.TripleBinding());

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenPredicateSourceUnderWide_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", new PredicateSourceSection("p", ValueType: null),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection())]);

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenPredicateSourceHasNoName_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", new PredicateSourceSection(Name: null, ValueType: null),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection())],
            binding: DocumentFixtures.TripleBinding());

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenTripleOrderingMissing_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document(
            [TriplePredicate()],
            binding: DocumentFixtures.TripleBinding(ordering: null));

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenAuthoredObjectKeyColumnUnderTriple_ThenObjectKeyModeInvalidForShape()
    {
        // §5.4/D-082: ANY authored [binding.object_key] under triple is rejected,
        // not just row_index: triple identity is always the subject.
        var objectKey = new ObjectKeySection(ObjectKeyMode.Column, new IndexColumnRef(0), Columns: null, Aggregate: null);
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding(objectKey: objectKey));

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.ObjectKeyModeInvalidForShape, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenTripleHasHeaderDefaulted_ThenFalse()
    {
        var document = DocumentFixtures.Document([TriplePredicate()], binding: DocumentFixtures.TripleBinding());

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.False(spec.Binding.HasHeader);
    }

    [Fact]
    public void Resolve_WhenNonUtf8Encoding_ThenSourceBindingInvalid()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            binding: DocumentFixtures.WideBinding() with { Encoding = "latin1" });

        var result = Resolve(document, new SourceSchema(1));

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenEncodingUpperCaseUtf8_ThenCanonicalizesToUtf8()
    {
        var authored = Resolve(DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            binding: DocumentFixtures.WideBinding() with { Encoding = "UTF-8" }), new SourceSchema(1));
        var unspecified = Resolve(
            DocumentFixtures.Document([DocumentFixtures.Nominal("g", 0, ["b"])]), new SourceSchema(1));

        Assert.True(authored.TryGetValue(out var authoredSpec));
        Assert.True(unspecified.TryGetValue(out var unspecifiedSpec));
        Assert.Equal("utf-8", authoredSpec.Binding.Encoding);
        Assert.Equal(unspecifiedSpec.Binding.Encoding, authoredSpec.Binding.Encoding);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("Utf8")]
    [InlineData(" utf-8 ")]
    [InlineData("\tUTF8\t")]
    public void Resolve_WhenEncodingIsAnotherUtf8Spelling_ThenItResolvesToUtf8(string spelling)
    {
        // §5.1/D-082: utf-8 or utf8, in any letter case and with surrounding whitespace ignored.
        var result = Resolve(DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            binding: DocumentFixtures.WideBinding() with { Encoding = spelling }), new SourceSchema(1));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal("utf-8", spec.Binding.Encoding);
    }

    [Theory]
    [InlineData("utf-16")]
    [InlineData("UTF_8")]
    [InlineData("utf 8")]
    [InlineData("")]
    public void Resolve_WhenEncodingIsNotAUtf8Spelling_ThenSourceBindingInvalid(string encoding)
    {
        var result = Resolve(DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            binding: DocumentFixtures.WideBinding() with { Encoding = encoding }), new SourceSchema(1));

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenWideSourceNameMatchesDuplicateHeader_ThenSourceBindingInvalid()
    {
        // §10.2: a wide source name must resolve to exactly one column: a duplicate
        // matching header is invalid, not a silent first-match bind.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("g", DocumentFixtures.NamedColumn("age"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"])],
            binding: DocumentFixtures.WideBinding(hasHeader: true));

        var result = Resolve(document, new SourceSchema(2, ["age", "age"]));

        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenWideObjectKeyNameMatchesDuplicateHeader_ThenObjectKeyBindingInvalid()
    {
        // §5.4/§10.2: the object-key column name must resolve to exactly one column.
        var objectKey = new ObjectKeySection(ObjectKeyMode.Column, new NameColumnRef("id"), Columns: null, Aggregate: null);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            binding: DocumentFixtures.WideBinding(hasHeader: true, objectKey: objectKey));

        var result = Resolve(document, new SourceSchema(3, ["id", "id", "g"]));

        Assert.Equal(DiagnosticCode.ObjectKeyBindingInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenPredicateSourceValueTypeConflictsDiscretizer_ThenSourceValueTypeInvalid()
    {
        // §10.2: value_type is a source-level property of predicate sources too, so
        // a string value_type over the number-fixing manual_cuts is invalid.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", new PredicateSourceSection("p", SourceValueType.String),
                discretizer: new ManualCutsDiscretizerSection([30.0], BinEnds.Open), scale: new NominalScaleSection())],
            binding: DocumentFixtures.TripleBinding());

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.SourceValueTypeInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenPredicateSourceNumericWithBareStringRestrict_ThenRestrictToNumericEntryRequired()
    {
        // §10.4: the numeric-needs-range shape check applies to predicate sources too.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", new PredicateSourceSection("p", SourceValueType.Number),
                discretizer: new ManualCutsDiscretizerSection([30.0], BinEnds.Open), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToValue("high")])],
            binding: DocumentFixtures.TripleBinding());

        var result = Resolve(document);

        Assert.Equal(DiagnosticCode.RestrictToNumericEntryRequired, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    // --- Effective naming (§10.1/§10.7, D-120) ---

    [Fact]
    public void Resolve_WhenNoNamingAuthored_ThenDisplayNameIsTheNameAndFormatIsAbsent()
    {
        // The state every spec without naming keys resolves to: no format means the scale-specific
        // defaults stay in charge, and display_name defaults to name.
        Assert.True(Resolve(DocumentFixtures.Document([DocumentFixtures.Nominal("g", 0, ["b"])])).TryGetValue(out var spec));

        Assert.Equal("g", spec!.Attributes[0].DisplayName);
        Assert.Null(spec.Attributes[0].NameFormat);
    }

    [Fact]
    public void Resolve_WhenDisplayNameAuthored_ThenItIsCarriedToCore()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"]) with { DisplayName = "Gill" }]);

        Assert.True(Resolve(document).TryGetValue(out var spec));
        Assert.Equal("Gill", spec!.Attributes[0].DisplayName);
    }

    [Fact]
    public void Resolve_WhenOnlyDefaultsAuthorsTheFormat_ThenEveryAttributeTakesIt()
    {
        // §9.2 tier 2: [defaults] supplies the format to attributes that omit their own.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"]), DocumentFixtures.Nominal("b", 1, ["y"])],
            defaults: new DefaultsSection(null, null, null, null, null, null) { FormalAttributeFormat = "{value}" });

        Assert.True(Resolve(document, new SourceSchema(2)).TryGetValue(out var spec));
        Assert.All(spec!.Attributes, a => Assert.Equal("{value}", a.NameFormat?.Text));
    }

    [Fact]
    public void Resolve_WhenAttributeAndDefaultsBothAuthorTheFormat_ThenTheAttributeWins()
    {
        // §9.2: explicit per-attribute fields (tier 5) beat [defaults] (tier 2), and the
        // whole value is replaced: formats never merge (the D-114 compound rule).
        var document = DocumentFixtures.Document(
            [
                DocumentFixtures.Nominal("a", 0, ["x"]) with { FormalAttributeFormat = "{name}!{value}" },
                DocumentFixtures.Nominal("b", 1, ["y"]),
            ],
            defaults: new DefaultsSection(null, null, null, null, null, null) { FormalAttributeFormat = "{value}" });

        Assert.True(Resolve(document, new SourceSchema(2)).TryGetValue(out var spec));
        Assert.Equal("{name}!{value}", spec!.Attributes[0].NameFormat?.Text);
        Assert.Equal("{value}", spec.Attributes[1].NameFormat?.Text);
    }

    [Fact]
    public void Resolve_WhenAnUnusedTemplateAuthorsNaming_ThenItIsInertUntilApplicationLands()
    {
        // Template naming is CARRIED and parse-validated, but an unused template contributes
        // nothing to a resolved attribute. The absence of leakage
        // is the point: an unreferenced template must not silently name anything.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"])],
            templates:
            [
                new TemplateSection("t", null, null, null, null, null, null, null, null)
                {
                    DisplayName = "FromTemplate",
                    FormalAttributeFormat = "{value}",
                },
            ]);

        Assert.True(Resolve(document).TryGetValue(out var spec));
        Assert.Equal("a", spec!.Attributes[0].DisplayName);
        Assert.Null(spec.Attributes[0].NameFormat);
    }

    [Fact]
    public void Resolve_WhenExcludedAttributeAuthorsNaming_ThenItStillResolvesOntoTheSpec()
    {
        // D-049: an excluded attribute plans no column, so its naming is dormant, but the
        // resolved carrier is populated rather than dropped, exactly as its parked
        // discretizer/scale are.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", include: false) with { DisplayName = "A", FormalAttributeFormat = "{value}" }]);

        Assert.True(Resolve(document).TryGetValue(out var spec));
        Assert.Equal("A", spec!.Attributes[0].DisplayName);
        Assert.Equal("{value}", spec.Attributes[0].NameFormat?.Text);
    }

    [Fact]
    public void Resolve_WhenTheDocumentWasHandBuiltWithAnInvalidFormat_ThenItThrows()
    {
        // The reader validates every authored format, so reaching the resolver with an
        // invalid one means the document never came through SpecReader: a programmer error
        // on the exception channel, not authored input (EP-14). There is no resolve-phase
        // condition for it, and giving SpecFieldInvalid a second phase would break D-067's
        // one-code-one-phase rule.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"]) with { FormalAttributeFormat = "{nope}" }]);

        Assert.Throws<InvalidOperationException>(() => SpecResolver.Resolve(document));
    }

    // A fully-resolvable triple predicate attribute (identity + nominal).
    private static AttributeSection TriplePredicate(string name = "a", string predicate = "p") =>
        DocumentFixtures.Attribute(name, new PredicateSourceSection(predicate, ValueType: null),
            discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection());

    private static DiscretizerSection Discretizer(string kind) => kind switch
    {
        "identity" => new IdentityDiscretizerSection(),
        "manual_cuts" => new ManualCutsDiscretizerSection([30.0], BinEnds.Open),
        "ordered_cuts" => new OrderedCutsDiscretizerSection(["lo", "hi"], ["hi"], BinEnds.Open),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // Reads the root and composes it through the real §13 composer over in-memory base files,
    // so a rule about the composed spec is judged on exactly the document a chain produces.
    private static SpecDocument Compose(string rootToml, params (string Key, string Toml)[] bases)
    {
        var read = SpecReader.Read(rootToml, "root.toml");
        Assert.True(read.TryGetValue(out var root),
            string.Join("; ", read.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var composed = SpecComposer.Compose(root, "root.toml", new InMemorySpecFiles(bases));
        Assert.True(composed.TryGetValue(out var document),
            string.Join("; ", composed.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return document;
    }

    private sealed class InMemorySpecFiles((string Key, string Toml)[] files) : ISpecTextSource
    {
        public SpecSourceText? Load(string reference, string referrerKey)
        {
            foreach (var (key, toml) in files)
            {
                if (string.Equals(key, reference, StringComparison.Ordinal))
                {
                    return new SpecSourceText(key, toml);
                }
            }

            return null;
        }
    }
}
