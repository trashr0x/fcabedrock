using System.Globalization;
using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// Test-only builders for the emitter tests. The mushroom data/spec are inlined so
// the test is self-contained (the byte-equal golden fixtures live in Golden.Tests).
internal static class ConversionFixtures
{
    // Builds a plan through the M4 pipeline (resolve token → fully-declared calibrated
    // state → plan) the way production does, so the emit-time provenance guard (D-098)
    // pairs the plan and a source built over the same binding + schema.
    public static Diagnosed<ConversionPlan> PlanFor(BedrockSpec spec, SourceSchema schema, LabelStyle style = LabelStyle.Native) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(ResolveFor(spec, schema)), style);

    // The resolved token for a hand-built spec + schema (empty name bindings — the
    // Conversion fixtures bind by index/predicate, never by header name).
    public static ResolvedSpec ResolveFor(BedrockSpec spec, SourceSchema schema) =>
        ResolvedSpec.Create(
            spec,
            schema,
            SourceReadSettings.Create(
                spec.Binding.Shape, spec.Binding.Encoding, spec.Binding.Delimiter, spec.Binding.QuoteChar,
                spec.Binding.HasHeader, spec.Binding.MissingToken, spec.Binding.Ordering),
            []);

    public const string MushroomCsv =
        "class,bruises?,gill-size,veil-type,ring-number\n" +
        "e,t,b,p,n\n" +
        "e,t,n,p,t\n" +
        "e,f,n,p,n\n" +
        "e,t,b,p,o\n" +
        "e,f,n,p,n";

    public static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    // The three whole-stream observability warnings (§16.4/D-105). They fire on normal
    // completion whenever a context has no rows, an empty row, or an empty column — all of
    // which these deliberately tiny fixtures produce by construction (a one-row fixture over a
    // two-value domain leaves a column uncrossed; a fixture probing "missing → no cross" emits
    // an object with no crosses at all). They are expected outcomes, not faults (§7: the column
    // vocabulary is fixed before any object is filtered), and they are orthogonal to what the
    // emit suites below assert.
    private static readonly HashSet<DiagnosticCode> ObservabilityCodes =
    [
        DiagnosticCode.NoObjectsEmitted,
        DiagnosticCode.ObjectHasNoCrosses,
        DiagnosticCode.AttributeHasNoCrosses,
    ];

    /// <summary>
    /// The emit diagnostics <em>excluding</em> the whole-stream observability warnings, so the
    /// suites keep asserting exact sets (<c>Assert.Empty</c> / <c>Assert.Single</c>) on the
    /// condition each one is actually about, rather than being weakened to
    /// "contains at least X". The observability warnings' own behaviour — counts, bounded
    /// samples, ordering, halt suppression, and replay single-counting — is proven directly in
    /// <c>EmitObservabilityTests</c>, which is the only suite that should assert on them.
    /// </summary>
    public static List<BedrockDiagnostic> DataDiagnostics(IEnumerable<BedrockDiagnostic> diagnostics) =>
        diagnostics.Where(d => !ObservabilityCodes.Contains(d.Code)).ToList();

    public static Binding Wide(char delimiter = ',', bool hasHeader = true, string missingToken = "?") =>
        new(SourceShape.Wide, "utf-8", delimiter, '"', hasHeader, "invariant", missingToken, new RowIndexObjectKey());

    // A wide binding whose object key is a source column (§5.4/§6.1). Headerless by default so tests
    // feed raw data rows; the first row sets the schema width for the plan-time index range-check.
    public static Binding WideWithKey(
        int index, DuplicateObjectPolicy policy, bool hasHeader = false, string missingToken = "?") =>
        new(SourceShape.Wide, "utf-8", ',', '"', hasHeader, "invariant", missingToken, new ColumnObjectKey(index, policy));

    public static WideCsvSource SourceOver(string text, Binding binding) =>
        new(() => new MemoryStream(Encoding.UTF8.GetBytes(text)), binding);

    public static BedrockSpec MushroomSpec() =>
        new(Wide(),
        [
            new AttributeSpec("class", new ColumnSource(0, SourceValueType.String), Include: false, null, null, [], RestrictTo: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn),
            new AttributeSpec("bruises?", new ColumnSource(1, SourceValueType.String), Include: true, new IdentityDiscretizer(), new DichotomicScale("t"), ["t", "f"], RestrictTo: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn),
            new AttributeSpec("gill-size", new ColumnSource(2, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(), ["b", "n"], RestrictTo: [], Labels(("b", "broad"), ("n", "narrow")), MissingPolicy.Skip, UnknownValuePolicy.Warn),
            new AttributeSpec("veil-type", new ColumnSource(3, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(), ["p", "u"], RestrictTo: [], Labels(("p", "partial"), ("u", "universal")), MissingPolicy.Skip, UnknownValuePolicy.Warn),
            new AttributeSpec("ring-number", new ColumnSource(4, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(), ["n", "o", "t"], RestrictTo: [], Labels(("n", "none"), ("o", "one"), ("t", "two")), MissingPolicy.Skip, UnknownValuePolicy.Warn),
        ]);

    // --- Triple builders (M3 Slice C) -----------------------------------------

    // The same five-attribute mushroom spec as MushroomSpec, but triple: each attribute binds a
    // predicate (its own name) instead of a column. Emitting it over MushroomTripleData must
    // reproduce the wide incidence exactly (§17 rule 8; the triple encoding is just another shape).
    public const string MushroomTripleData =
        "m0,class,e\nm0,bruises?,t\nm0,gill-size,b\nm0,veil-type,p\nm0,ring-number,n\n" +
        "m1,class,e\nm1,bruises?,t\nm1,gill-size,n\nm1,veil-type,p\nm1,ring-number,t\n" +
        "m2,class,e\nm2,bruises?,f\nm2,gill-size,n\nm2,veil-type,p\nm2,ring-number,n\n" +
        "m3,class,e\nm3,bruises?,t\nm3,gill-size,b\nm3,veil-type,p\nm3,ring-number,o\n" +
        "m4,class,e\nm4,bruises?,f\nm4,gill-size,n\nm4,veil-type,p\nm4,ring-number,n";

    // The same 25 triples as MushroomTripleData, reordered predicate-major (subject-interleaved):
    // every subject recurs non-contiguously, so subject_grouped would reject it — the unordered path
    // must reproduce the same objects (first-appearance m0..m4) and crosses.
    public const string MushroomTripleDataInterleaved =
        "m0,class,e\nm1,class,e\nm2,class,e\nm3,class,e\nm4,class,e\n" +
        "m0,bruises?,t\nm1,bruises?,t\nm2,bruises?,f\nm3,bruises?,t\nm4,bruises?,f\n" +
        "m0,gill-size,b\nm1,gill-size,n\nm2,gill-size,n\nm3,gill-size,b\nm4,gill-size,n\n" +
        "m0,veil-type,p\nm1,veil-type,p\nm2,veil-type,p\nm3,veil-type,p\nm4,veil-type,p\n" +
        "m0,ring-number,n\nm1,ring-number,t\nm2,ring-number,n\nm3,ring-number,o\nm4,ring-number,n";

    public static Binding Triple(TripleOrdering ordering = TripleOrdering.SubjectGrouped, string missingToken = "?") =>
        new(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", missingToken,
            new ColumnObjectKey(0, DuplicateObjectPolicy.Fail), new TripleColumns(0, 1, 2), ordering);

    public static TripleCsvSource TripleSourceOver(string text, Binding binding) =>
        new(() => new MemoryStream(Encoding.UTF8.GetBytes(text)), binding);

    public static AttributeSpec PredicateNominal(
        string name, string predicate, IReadOnlyList<string>? domain,
        IReadOnlyDictionary<string, string>? labels = null, MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new PredicateSource(predicate, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(),
            domain, RestrictTo: [], labels ?? NoLabels, missing, UnknownValuePolicy.Warn);

    public static AttributeSpec PredicateDichotomic(
        string name, string predicate, string trueValue, IReadOnlyList<string> domain, MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new PredicateSource(predicate, SourceValueType.String), Include: true, new IdentityDiscretizer(), new DichotomicScale(trueValue),
            domain, RestrictTo: [], NoLabels, missing, UnknownValuePolicy.Warn);

    public static AttributeSpec PredicateExcluded(string name, string predicate) =>
        new(name, new PredicateSource(predicate, SourceValueType.String), Include: false, null, null, [], RestrictTo: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    public static BedrockSpec MushroomTripleSpec() =>
        new(Triple(),
        [
            PredicateExcluded("class", "class"),
            PredicateDichotomic("bruises?", "bruises?", "t", ["t", "f"]),
            PredicateNominal("gill-size", "gill-size", ["b", "n"], Labels(("b", "broad"), ("n", "narrow"))),
            PredicateNominal("veil-type", "veil-type", ["p", "u"], Labels(("p", "partial"), ("u", "universal"))),
            PredicateNominal("ring-number", "ring-number", ["n", "o", "t"], Labels(("n", "none"), ("o", "one"), ("t", "two"))),
        ]);

    public static AttributeSpec Nominal(string name, int index, params string[] domain) =>
        Nominal(name, index, UnknownValuePolicy.Warn, domain);

    public static AttributeSpec Nominal(string name, int index, UnknownValuePolicy policy, params string[] domain) =>
        Nominal(name, index, policy, MissingPolicy.Skip, domain);

    public static AttributeSpec Nominal(string name, int index, UnknownValuePolicy policy, MissingPolicy missing, params string[] domain) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(),
            domain, RestrictTo: [], NoLabels, missing, policy);

    public static AttributeSpec Dichotomic(
        string name, int index, string trueValue, IReadOnlyList<string> domain,
        MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(), new DichotomicScale(trueValue),
            domain, RestrictTo: [], NoLabels, missing, UnknownValuePolicy.Warn);

    // A numeric manual_cuts attribute (open ends, nominal) with a configurable unknown-value
    // policy — for exercising the malformed-numeric path (§11.5 / D-050).
    public static AttributeSpec NumericCuts(string name, int index, UnknownValuePolicy policy, params double[] cuts) =>
        NumericCuts(name, index, policy, MissingPolicy.Skip, cuts);

    public static AttributeSpec NumericCuts(string name, int index, UnknownValuePolicy policy, MissingPolicy missing, params double[] cuts) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!,
            new NominalScale(), DeclaredDomain: [], RestrictTo: [], NoLabels, missing, policy);

    // An identity value-bin ordinal attribute (§12.3, D-081): the explicit string
    // order is the declared domain itself, with a configurable direction/boundary.
    public static AttributeSpec OrdinalValueBins(
        string name, int index, IReadOnlyList<string> domain, OrdinalDirection direction, OrdinalBoundary boundary) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true,
            new IdentityDiscretizer(), new OrdinalScale(direction, DropTop: false, boundary, domain),
            domain, RestrictTo: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    // An ordered_cuts attribute (open ends, nominal) over a category order with one cut.
    public static AttributeSpec OrderedCuts(string name, int index, IReadOnlyList<string> order, string cut) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true,
            OrderedCutsDiscretizer.Create(order, [cut], BinEnds.Open).Value!,
            new NominalScale(), DeclaredDomain: [], RestrictTo: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static Dictionary<string, string> Labels(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}
