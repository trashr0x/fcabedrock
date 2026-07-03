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
    public void Resolve_WhenValueTypeAuthored_ThenAuthoredWinsOverDiscretizerDefault()
    {
        // The compatibility matrix (SourceValueTypeInvalid, D-061) is the validation
        // slice; here the authored value simply wins.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0, SourceValueType.String),
                discretizer: Discretizer("manual_cuts"), scale: new NominalScaleSection())]);

        var result = SpecResolver.Resolve(document);

        Assert.True(result.TryGetValue(out var spec));
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
    public void Resolve_WhenRestrictToAuthored_ThenCarriedIntoCore()
    {
        // D-057: carried as an inert resolved carrier; plan-phase reject is Slice D.
        IReadOnlyList<RestrictToEntry> restrict = [new RestrictToValue("a"), new RestrictToRange(1, To: null)];
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("x", DocumentFixtures.Column(0),
                discretizer: Discretizer("identity"), scale: new NominalScaleSection(), restrictTo: restrict)]);

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

    // --- Failures ---

    [Fact]
    public void Resolve_WhenSpecSectionOrVersionMissing_ThenSpecVersionUnsupportedFatal()
    {
        var noSpec = new SpecDocument(null, null, DocumentFixtures.WideBinding(), null, null, []);
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
        var noBinding = new SpecDocument(DocumentFixtures.SpecV1(), null, null, null, null, []);
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
        var viaBed = BedToSpec.ToSpec(
            BedReader.Read(BedFixtures.MushroomBed),
            new Binding(SourceShape.Wide, ',', '"', HasHeader: true, "invariant", "?", new RowIndexObjectKey()));

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
