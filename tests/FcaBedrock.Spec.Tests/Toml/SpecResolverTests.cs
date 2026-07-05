using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

public sealed class SpecResolverTests
{
    // --- Happy paths ---

    [Fact]
    public void Resolve_WhenMinimalWideDocument_ThenBindingAndPolicyDefaultsApply()
    {
        var result = SpecResolver.Resolve(DocumentFixtures.Document([DocumentFixtures.Nominal("g", 0, ["b"])]));

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

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        var x = Assert.Single(spec.Attributes);
        Assert.False(x.Include); // [defaults].include fills the omitted field — and parks it (D-049)
        Assert.Equal(MissingPolicy.AsAttribute, x.MissingPolicy);
        Assert.Equal(UnknownValuePolicy.Fail, x.UnknownValuePolicy);
    }

    [Fact]
    public void Resolve_WhenSourceBoundByName_ThenResolvesIndexThroughHeaderSchema()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.NamedColumn("age"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection())]);

        var result = SpecResolver.Resolve(document, new SourceSchema(2, ["id", "age"]));

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

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(expected, Assert.IsType<ColumnSource>(Assert.Single(spec.Attributes).Source).ValueType);
    }

    [Fact]
    public void Resolve_WhenValueTypeAuthoredOnExcludedAttribute_ThenAuthoredWinsOverParkedDiscretizerDefault()
    {
        // D-049/D-076: the D-061 matrix fires for included attributes only, so the
        // one legal authored-≠-derived pairing is on a parked attribute — where the
        // authored source type still wins over the parked discretizer's default.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0, SourceValueType.String),
                include: false, discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection())]);

        var result = SpecResolver.Resolve(document);

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

        Assert.True(SpecResolver.Resolve(DocumentFixtures.Document([ordinal], defaults: defaults))
            .TryGetValue(out var withDefaults));
        Assert.True(SpecResolver.Resolve(DocumentFixtures.Document([ordinal])).TryGetValue(out var withoutDefaults));

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

        Assert.True(SpecResolver.Resolve(DocumentFixtures.Document([ordinal], defaults: defaults))
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
            binding: DocumentFixtures.WideBinding(objectKey: objectKey), defaults: defaults);

        Assert.True(SpecResolver.Resolve(document).TryGetValue(out var spec));

        var key = Assert.IsType<ColumnObjectKey>(spec.Binding.ObjectKey);
        Assert.Equal(0, key.Index);
        Assert.Equal(DuplicateObjectPolicy.Dedupe, key.Policy);
    }

    [Fact]
    public void Resolve_WhenObjectKeyColumnModeWithoutDefaults_ThenPolicyIsFail()
    {
        var objectKey = new ObjectKeySection(ObjectKeyMode.Column, new NameColumnRef("id"), Columns: null, Aggregate: null);
        var document = DocumentFixtures.Document(binding: DocumentFixtures.WideBinding(objectKey: objectKey));

        Assert.True(SpecResolver.Resolve(document, new SourceSchema(2, ["id", "age"])).TryGetValue(out var spec));

        var key = Assert.IsType<ColumnObjectKey>(spec.Binding.ObjectKey);
        Assert.Equal(0, key.Index); // resolved by header name
        Assert.Equal(DuplicateObjectPolicy.Fail, key.Policy); // §6.1 default
    }

    [Fact]
    public void Resolve_WhenAttributeExcluded_ThenParkedWithNullsWithoutError()
    {
        // D-049: dormant scaling sections are parked, not resolved and never an error —
        // even ones that would fail resolution if included (dichotomic without true_value).
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("bare", DocumentFixtures.Column(0), include: false),
            DocumentFixtures.Attribute("dormant", DocumentFixtures.Column(1), include: false,
                discretizer: Discretizer("identity"), scale: new DichotomicScaleSection(TrueValue: null)),
        ]);

        var result = SpecResolver.Resolve(document);

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
    public void Resolve_WhenTripleShape_ThenTripleRejectCarrierWithNoAttributes()
    {
        // D-066/D-072: predicate sources are document-only, so nothing resolves under
        // triple; the carrier exists solely for the planner guard to refuse.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("p", new PredicateSourceSection("pred", ValueType: null))],
            binding: DocumentFixtures.TripleBinding(new TripleColumnsSection(1, 2, 3)));

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(SourceShape.Triple, spec.Binding.Shape);
        Assert.Empty(spec.Attributes);

        // §5.4 triple default: the subject column keys objects (inert behind the guard).
        var key = Assert.IsType<ColumnObjectKey>(spec.Binding.ObjectKey);
        Assert.Equal(1, key.Index);
        Assert.Equal(DuplicateObjectPolicy.Fail, key.Policy);
    }

    [Fact]
    public void Resolve_WhenDeclaredDomainOmittedOrAuthoredEmpty_ThenBothResolveEmpty()
    {
        // D-049/D-071: omitted and authored-[] coincide in Core; the authored form is
        // document provenance, kept for round-trip, not a resolved distinction.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("omitted", 0, domain: null),
            DocumentFixtures.Nominal("authoredEmpty", 1, domain: []),
        ]);

        Assert.True(SpecResolver.Resolve(document).TryGetValue(out var spec));

        Assert.All(spec.Attributes, a => Assert.Empty(a.DeclaredDomain));
    }

    [Fact]
    public void Resolve_WhenRestrictToStringsOnStringSource_ThenCarriedIntoCore()
    {
        // D-057: carried as an inert resolved carrier; execution deferral is the
        // plan-phase reject (RestrictToNotImplementedV1). Entries must match the
        // attribute's single value_type (D-063), so each carrier test is same-typed.
        IReadOnlyList<RestrictToEntry> restrict = [new RestrictToValue("a"), new RestrictToValue("b")];
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["a", "b"], restrictTo: restrict)]);

        Assert.True(SpecResolver.Resolve(document).TryGetValue(out var spec));

        Assert.Equal(restrict, Assert.Single(spec.Attributes).RestrictTo);
    }

    [Fact]
    public void Resolve_WhenRestrictToRangesOnNumberSource_ThenCarriedIntoCore()
    {
        IReadOnlyList<RestrictToEntry> restrict = [new RestrictToRange(1, To: null), new RestrictToRange(null, 5)];
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(), restrictTo: restrict)]);

        Assert.True(SpecResolver.Resolve(document).TryGetValue(out var spec));

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

        var result = SpecResolver.Resolve(document);

        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.DiscretizerCutsNotAscending);
        Assert.Equal("age", diagnostic.Location?.AttributeName);
    }

    // --- Extends / templates / matchers (Slice F, D-078) ---

    [Fact]
    public void Resolve_WhenDocumentStillCarriesExtends_ThenThrowsArgumentException()
    {
        // Call-contract, not a diagnostic: Resolve never throws for valid inputs
        // under its contract, and a document with authored extends is invalid
        // input to Resolve — compose first (§13, D-078).
        var document = DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(extends: "base.toml"));

        var exception = Assert.Throws<ArgumentException>(() => SpecResolver.Resolve(document));
        Assert.Contains("SpecComposer.Compose", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenMatcherPresent_ThenTemplateMatcherNotImplementedAggregated()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            matchers:
            [
                new MatcherSection(new MatchSection("^a$", null), "t"),
                new MatcherSection(new MatchSection(null, [0, 1]), "t"),
            ]);

        var result = SpecResolver.Resolve(document);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.TemplateMatcherNotImplementedV1, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("2 [[matcher]] entries", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenAttributesReferenceTemplates_ThenOneRejectPerAttribute()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), template: "boolean_yes_no",
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["x"]),
            DocumentFixtures.Nominal("b", 1, ["y"]),
            DocumentFixtures.Attribute("c", DocumentFixtures.Column(2), template: "other",
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["z"]),
        ]);

        var result = SpecResolver.Resolve(document);

        var rejects = result.Diagnostics.Where(d => d.Code == DiagnosticCode.TemplateMatcherNotImplementedV1).ToList();
        Assert.Equal(["a", "c"], rejects.Select(d => d.Location?.AttributeName));
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenOnlyUnreferencedTemplates_ThenResolvesCleanly()
    {
        // §9: an unreferenced [[template]] block is inert — it resolves (and
        // converts) without error; only *use* rejects before M6.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("g", 0, ["b"])],
            templates:
            [
                new TemplateSection("unused", null, new IdentityDiscretizerSection(),
                    new NominalScaleSection(), ["x"], null, null, null, null),
            ]);

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Empty(result.Diagnostics);
        Assert.Single(spec.Attributes);
    }

    [Fact]
    public void Resolve_WhenMatcherPresentAndBindingShapeMissing_ThenBothReport()
    {
        // P-13 aggregation: the template/matcher reject precedes the shape
        // gate's early return, so both surface in one pass.
        var document = new SpecDocument(
            DocumentFixtures.SpecV1(), null, null, null, null,
            [], [new MatcherSection(new MatchSection("^a$", null), "t")], []);

        var result = SpecResolver.Resolve(document);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.TemplateMatcherNotImplementedV1);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.BindingShapeMissing);
    }

    // --- Failures ---

    [Fact]
    public void Resolve_WhenSpecSectionOrVersionMissing_ThenSpecVersionUnsupportedFatal()
    {
        var noSpec = new SpecDocument(null, null, DocumentFixtures.WideBinding(), null, null, [], [], []);
        var noVersion = DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(version: null));

        foreach (var document in new[] { noSpec, noVersion })
        {
            var result = SpecResolver.Resolve(document);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(DiagnosticCode.SpecVersionUnsupported, diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
            Assert.False(result.TryGetValue(out _));
        }
    }

    [Fact]
    public void Resolve_WhenVersionUnknown_ThenSpecVersionUnsupportedFatal()
    {
        var result = SpecResolver.Resolve(DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(version: 2)));

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
            var result = SpecResolver.Resolve(document);

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

            var result = SpecResolver.Resolve(document, schema);

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

            var result = SpecResolver.Resolve(document);

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

            var result = SpecResolver.Resolve(document);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.True(DiagnosticCode.AttributeScalingMissing == diagnostic.Code, $"case '{name}': {diagnostic.Message}");
        }
    }

    [Fact]
    public void Resolve_WhenLocaleUnresolvable_ThenBindingLocaleInvalid()
    {
        var document = DocumentFixtures.Document(binding: DocumentFixtures.WideBinding(locale: "xx-nope"));

        var result = SpecResolver.Resolve(document);

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
            var document = DocumentFixtures.Document(binding: DocumentFixtures.WideBinding(objectKey: section));

            var result = SpecResolver.Resolve(document, schema);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.True(DiagnosticCode.ObjectKeyBindingInvalid == diagnostic.Code, $"case '{name}': {diagnostic.Message}");
        }
    }

    [Fact]
    public void Resolve_WhenMultipleProblems_ThenAllDiagnosticsAggregate()
    {
        // P-13/D-067: resolve + validate in one pass, reporting everything at once.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute(name: null, DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection()),
            DocumentFixtures.Attribute("noscale", DocumentFixtures.Column(1), discretizer: Discretizer("identity")),
        ],
        binding: DocumentFixtures.WideBinding(locale: "xx-nope"));

        var result = SpecResolver.Resolve(document);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.BindingLocaleInvalid);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.AttributeNameMissing);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.AttributeScalingMissing);
        Assert.Equal(3, result.Diagnostics.Count);
    }

    // --- Slice D seam validation (D-054/D-060/D-061/D-063/D-064/D-076) ---

    [Fact]
    public void Resolve_WhenQuoteCharAuthoredNonStandard_ThenQuoteCharNotSupportedV1()
    {
        // D-054: the field parses (a retained carrier) but v1 rejects any quote
        // other than the standard double quote at the seam.
        var document = DocumentFixtures.Document(binding: DocumentFixtures.WideBinding(quoteChar: '\''));

        var result = SpecResolver.Resolve(document);

        Assert.Equal(DiagnosticCode.QuoteCharNotSupportedV1, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenQuoteCharAuthoredStandard_ThenNoDiagnostic()
    {
        var document = DocumentFixtures.Document(binding: DocumentFixtures.WideBinding(quoteChar: '"'));

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenDelimiterEqualsDefaultedQuoteChar_ThenBindingDelimiterQuoteConflict()
    {
        // §5.1: the conflict is judged on the resolved pair — an authored '"'
        // delimiter collides with the defaulted quote.
        var document = DocumentFixtures.Document(binding: DocumentFixtures.WideBinding(delimiter: '"'));

        var result = SpecResolver.Resolve(document);

        Assert.Equal(DiagnosticCode.BindingDelimiterQuoteConflict, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenDelimiterAndQuoteCharBothAuthoredSame_ThenBothDiagnosticsFire()
    {
        // D-076: two distinct §5.1 conditions — the unsupported quote and the
        // delimiter conflict — report independently.
        var document = DocumentFixtures.Document(
            binding: DocumentFixtures.WideBinding(delimiter: '|', quoteChar: '|'));

        var result = SpecResolver.Resolve(document);

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

        var result = SpecResolver.Resolve(document);

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

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_WhenNumberSourceHasStringRestrictTo_ThenRestrictToOnNumericRequiresRange()
    {
        // §10.4 (D-063): this code — not SourceValueTypeInvalid — owns the
        // numeric-source/string-entry mismatch.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToValue("young")])]);

        var result = SpecResolver.Resolve(document);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.RestrictToOnNumericRequiresRange, diagnostic.Code);
        Assert.Equal("age", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenStringSourceHasRangeRestrictTo_ThenSourceValueTypeInvalid()
    {
        // §10.4 (D-063): the mirror case — a range entry on a string-typed source —
        // is owned by the §10.2 code.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                declaredDomain: ["a"], restrictTo: [new RestrictToRange(1, 5)])]);

        var result = SpecResolver.Resolve(document);

        Assert.Equal(DiagnosticCode.SourceValueTypeInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenParkedCutDiscretizerTypesLiveRestrictTo_ThenStillRejected()
    {
        // D-076: restrict_to is live on an excluded (filter-only) attribute, and a
        // parked numeric-cut discretizer legitimately types it (§10.4 — "a numeric
        // source … or a numeric-cut discretizer"); the string entry still rejects.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", DocumentFixtures.Column(0), include: false,
                discretizer: Discretizer("manual_cuts"), restrictTo: [new RestrictToValue("young")])]);

        var result = SpecResolver.Resolve(document);

        Assert.Equal(DiagnosticCode.RestrictToOnNumericRequiresRange, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenFilterOnlyStringRestrictTo_ThenResolvesClean()
    {
        // §19.4: the filter-only pattern — string entries over an untyped,
        // discretizer-less source default to string and validate clean.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("Gene", DocumentFixtures.Column(0), include: false,
                restrictTo: [new RestrictToValue("Bmp5")])]);

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
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

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out _));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.RestrictToValueNotInDomain, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Bachelor", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenDomainAbsent_ThenNoDomainTypoWarning()
    {
        // D-071: omitted and authored-[] both resolve absent — no explicit domain,
        // no typo-catcher; the plan-phase calibration reject owns the absence.
        foreach (var domain in new IReadOnlyList<string>?[] { null, [] })
        {
            var document = DocumentFixtures.Document(
                [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0),
                    discretizer: Discretizer("identity"), scale: new NominalScaleSection(),
                    declaredDomain: domain, restrictTo: [new RestrictToValue("x")])]);

            var result = SpecResolver.Resolve(document);

            Assert.True(result.TryGetValue(out _));
            Assert.Empty(result.Diagnostics);
        }
    }

    [Fact]
    public void Resolve_WhenRestrictToValueNotInParkedDomain_ThenNoWarning()
    {
        // D-049/D-076: declared_domain is emitted-shaping config, parked when the
        // attribute is excluded — the live restrict_to is not checked against it.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("edu", DocumentFixtures.Column(0), include: false,
                discretizer: Discretizer("identity"),
                declaredDomain: ["Bachelors"], restrictTo: [new RestrictToValue("nope")])]);

        var result = SpecResolver.Resolve(document);

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

        var result = SpecResolver.Resolve(document);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.OrdinalOrderNotAllowedWithCuts, diagnostic.Code);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenOrdinalOrderAuthoredEmptyOverCuts_ThenPresenceStillRejects()
    {
        // §12.3 "MUST NOT be present" — an authored [] is still an order
        // declaration over cut bins.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(Direction: null, Boundary: null, Order: [], DropTop: null))]);

        var result = SpecResolver.Resolve(document);

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

        var result = SpecResolver.Resolve(document);

        Assert.Equal(DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenAuthoredBoundaryStraddlesDefaultedDirection_ThenStillRejects()
    {
        // D-060: authoredness is judged on the per-attribute boundary; the
        // geometry is judged on the resolved direction — here from [defaults].
        var defaults = new DefaultsSection(
            Include: null, MissingPolicy: null, UnknownValuePolicy: null, DuplicateObjectPolicy: null,
            OrdinalDirection.Le, OrdinalBoundary: null);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(Direction: null, OrdinalBoundary.Inclusive, Order: null, DropTop: null))],
            defaults: defaults);

        var result = SpecResolver.Resolve(document);

        Assert.Equal(DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Resolve_WhenBoundaryDefaultedOverCuts_ThenNeverTrips()
    {
        // D-060(c): a boundary arriving via [defaults].ordinal_boundary is
        // defaulted, not authored — over cut bins it never selects the operator
        // and never trips the check, whatever its value.
        var defaults = new DefaultsSection(
            Include: null, MissingPolicy: null, UnknownValuePolicy: null, DuplicateObjectPolicy: null,
            OrdinalDirection: null, OrdinalBoundary.Strict);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: Discretizer("manual_cuts"),
                scale: new OrdinalScaleSection(OrdinalDirection.Ge, Boundary: null, Order: null, DropTop: null))],
            defaults: defaults);

        var result = SpecResolver.Resolve(document);

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

        var result = SpecResolver.Resolve(document);

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

        var result = SpecResolver.Resolve(document);

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
            new TripleColumnsSection(0, 1, 2), objectKey);
        var document = DocumentFixtures.Document(binding: binding);

        var result = SpecResolver.Resolve(document);

        Assert.Equal(DiagnosticCode.ObjectKeyModeInvalidForShape, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.TryGetValue(out _));
    }

    // --- Determinism bridge (P-7) ---

    [Fact]
    public void Resolve_WhenResolvedTwice_ThenPlansIdentically()
    {
        Assert.True(SpecResolver.Resolve(DocumentFixtures.MiniMushroom()).TryGetValue(out var first));
        Assert.True(SpecResolver.Resolve(DocumentFixtures.MiniMushroom()).TryGetValue(out var second));

        Assert.True(ConversionPlanner.Plan(first, new SourceSchema(5)).TryGetValue(out var firstPlan));
        Assert.True(ConversionPlanner.Plan(second, new SourceSchema(5)).TryGetValue(out var secondPlan));

        Assert.Equal(
            firstPlan.FormalAttributes.Select(f => (f.RenderedName, f.Identity)),
            secondPlan.FormalAttributes.Select(f => (f.RenderedName, f.Identity)));
    }

    [Fact]
    public void Resolve_WhenMiniMushroomDocument_ThenPlansToTheBedPathSchema()
    {
        // The hand-built document twin must plan to the exact formal-attribute
        // schema the .bed migration path yields (P-7 — one schema, two producers).
        var viaDocument = SpecResolver.Resolve(DocumentFixtures.MiniMushroom());
        Assert.True(BedReader.Read(BedFixtures.MushroomBed).TryGetValue(out var bedDocument));
        var migrated = BedMigrator.Migrate(
            bedDocument,
            new BindingSection(SourceShape.Wide, Encoding: null, ',', QuoteChar: null, HasHeader: true,
                Locale: null, MissingToken: null, Ordering: null, Columns: null, ObjectKey: null));
        Assert.True(migrated.TryGetValue(out var bedSpecDocument));
        Assert.True(SpecResolver.Resolve(bedSpecDocument).TryGetValue(out var viaBed));

        Assert.True(viaDocument.TryGetValue(out var documentSpec));
        Assert.True(ConversionPlanner.Plan(documentSpec, new SourceSchema(5)).TryGetValue(out var documentPlan));
        Assert.True(ConversionPlanner.Plan(viaBed, new SourceSchema(5)).TryGetValue(out var bedPlan));

        Assert.Equal(
            bedPlan.FormalAttributes.Select(f => (f.RenderedName, f.Identity)),
            documentPlan.FormalAttributes.Select(f => (f.RenderedName, f.Identity)));
    }

    private static DiscretizerSection Discretizer(string kind) => kind switch
    {
        "identity" => new IdentityDiscretizerSection(),
        "manual_cuts" => new ManualCutsDiscretizerSection([30.0], BinEnds.Open),
        "ordered_cuts" => new OrderedCutsDiscretizerSection(["lo", "hi"], ["hi"], BinEnds.Open),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
