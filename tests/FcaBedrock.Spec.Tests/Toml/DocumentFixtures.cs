using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

// Presence-tracked document builders for the resolver tests (D-066): null means
// "not authored", so each helper authors only what its test needs. Documents
// with a *missing* [spec]/[binding] section are constructed raw in the tests —
// the builders always supply a valid one.
internal static class DocumentFixtures
{
    public static SpecDocument Document(
        IReadOnlyList<AttributeSection>? attributes = null,
        BindingSection? binding = null,
        DefaultsSection? defaults = null,
        SpecSection? spec = null,
        IReadOnlyList<TemplateSection>? templates = null,
        IReadOnlyList<MatcherSection>? matchers = null,
        OutputSection? output = null,
        ProvenanceSection? provenance = null) =>
        new(spec ?? SpecV1(), provenance, binding ?? WideBinding(), defaults, output, templates ?? [], matchers ?? [], attributes ?? []);

    public static SpecSection SpecV1(long? version = 1, string? extends = null) =>
        new(version, null, null, null, extends, null);

    public static BindingSection WideBinding(
        char? delimiter = null,
        char? quoteChar = null,
        bool? hasHeader = null,
        string? locale = null,
        string? missingToken = null,
        ObjectKeySection? objectKey = null) =>
        new(SourceShape.Wide, Encoding: null, delimiter, quoteChar, hasHeader, locale, missingToken,
            Ordering: null, Columns: null, objectKey);

    public static BindingSection TripleBinding(
        TripleColumnsSection? columns = null,
        TripleOrdering? ordering = TripleOrdering.SubjectGrouped,
        bool? hasHeader = null,
        string? encoding = null,
        ObjectKeySection? objectKey = null) =>
        new(SourceShape.Triple, encoding, Delimiter: null, QuoteChar: null, hasHeader, Locale: null,
            MissingToken: null, ordering, columns, objectKey);

    public static AttributeSection Attribute(
        string? name = "a",
        SourceSection? source = null,
        bool? include = null,
        DiscretizerSection? discretizer = null,
        ScaleSection? scale = null,
        IReadOnlyList<string>? declaredDomain = null,
        IReadOnlyList<RestrictToEntry>? restrictTo = null,
        IReadOnlyDictionary<string, string>? valueLabels = null,
        MissingPolicy? missingPolicy = null,
        UnknownValuePolicy? unknownValuePolicy = null,
        string? template = null) =>
        new(name, source ?? Column(0), Description: null, include, template, discretizer, scale,
            declaredDomain, restrictTo, valueLabels, missingPolicy, unknownValuePolicy);

    // An included identity + nominal attribute — the smallest fully-resolvable shape.
    public static AttributeSection Nominal(
        string name,
        int index,
        IReadOnlyList<string>? domain = null,
        IReadOnlyDictionary<string, string>? valueLabels = null) =>
        Attribute(name, Column(index), discretizer: new IdentityDiscretizerSection(),
            scale: new NominalScaleSection(), declaredDomain: domain, valueLabels: valueLabels);

    // A [[template]] authoring only the fields a test cares about (§9.1's closed ten).
    // Every omitted field stays null — "not authored" — which is exactly what the §9.2
    // merge reads, so a template built here layers the same way an authored one does.
    public static TemplateSection Template(
        string? id = "t",
        bool? include = null,
        DiscretizerSection? discretizer = null,
        ScaleSection? scale = null,
        IReadOnlyList<string>? declaredDomain = null,
        IReadOnlyList<RestrictToEntry>? restrictTo = null,
        IReadOnlyDictionary<string, string>? valueLabels = null,
        MissingPolicy? missingPolicy = null,
        UnknownValuePolicy? unknownValuePolicy = null,
        string? displayName = null,
        string? formalAttributeFormat = null) =>
        new(id, include, discretizer, scale, declaredDomain, restrictTo, valueLabels,
            missingPolicy, unknownValuePolicy)
        {
            DisplayName = displayName,
            FormalAttributeFormat = formalAttributeFormat,
        };

    // A [[matcher]] with exactly one selector (§9.2) — the arity the reader enforces.
    public static MatcherSection Matcher(
        string? nameRegex = null,
        IReadOnlyList<long>? sourceIndexRange = null,
        string? template = "t") =>
        new(new MatchSection(nameRegex, sourceIndexRange), template);

    public static ColumnSourceSection Column(int index, SourceValueType? valueType = null) =>
        new(index, Name: null, valueType);

    public static ColumnSourceSection NamedColumn(string name, SourceValueType? valueType = null) =>
        new(Index: null, name, valueType);

    // The document-model twin of the .bed mini-mushroom (BedFixtures.MushroomBed):
    // must plan to the same formal-attribute schema as the migrated .bed path
    // (BedMigrator -> SpecResolver, P-7).
    public static SpecDocument MiniMushroom() =>
        Document(
        [
            Attribute("class", Column(0), include: false),
            Attribute("bruises?", Column(1), discretizer: new IdentityDiscretizerSection(),
                scale: new DichotomicScaleSection("t"), declaredDomain: ["t", "f"]),
            Nominal("gill-size", 2, ["b", "n"], new Dictionary<string, string> { ["b"] = "broad", ["n"] = "narrow" }),
            Nominal("veil-type", 3, ["p", "u"], new Dictionary<string, string> { ["p"] = "partial", ["u"] = "universal" }),
            Nominal("ring-number", 4, ["n", "o", "t"], new Dictionary<string, string> { ["n"] = "none", ["o"] = "one", ["t"] = "two" }),
        ]);
}
