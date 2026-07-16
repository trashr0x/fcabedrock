using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests;

// Builders for spec models used across the Core tests. A test-only helper at the
// project root; it does not mirror a production type (per CLAUDE.md conventions).
internal static class SpecFixtures
{
    public static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    // The read settings for a resolved binding (the descriptor/session pairing shape, D-098).
    public static SourceReadSettings Settings(Binding binding) =>
        SourceReadSettings.Create(
            binding.Shape, binding.Encoding, binding.Delimiter, binding.QuoteChar,
            binding.HasHeader, binding.MissingToken, binding.Ordering);

    // Builds the resolution token for a hand-built spec + schema (D-098). Name bindings default
    // to empty (the Core fixtures bind by index/predicate).
    public static ResolvedSpec Resolve(BedrockSpec spec, SourceSchema schema, IReadOnlyList<ResolvedNameBinding>? nameBindings = null) =>
        ResolvedSpec.Create(spec, schema, Settings(spec.Binding), nameBindings ?? []);

    public static Binding WideRowIndex(char delimiter = ',', bool hasHeader = true) =>
        new(SourceShape.Wide, "utf-8", delimiter, '"', hasHeader, "invariant", "?", new RowIndexObjectKey());

    // A triple binding with the default (0,1,2) role map; the object key is always the subject
    // column (§5.4). ordering selects the streaming path (SubjectGrouped converts at Slice C).
    public static Binding TripleSubjectGrouped(TripleOrdering ordering = TripleOrdering.SubjectGrouped) =>
        new(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", "?",
            new ColumnObjectKey(0, DuplicateObjectPolicy.Fail), new TripleColumns(0, 1, 2), ordering);

    public static AttributeSpec PredicateNominal(
        string name, string predicate, IReadOnlyList<string> domain,
        MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new PredicateSource(predicate, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(),
            domain, RestrictTo: [], NoLabels, missing, UnknownValuePolicy.Warn);

    public static AttributeSpec Nominal(
        string name,
        int index,
        IReadOnlyList<string> domain,
        IReadOnlyDictionary<string, string>? valueLabels = null,
        MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(),
            domain, RestrictTo: [], valueLabels ?? NoLabels, missing, UnknownValuePolicy.Warn);

    public static AttributeSpec Dichotomic(
        string name, int index, string trueValue, IReadOnlyList<string> domain,
        MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(), new DichotomicScale(trueValue),
            domain, RestrictTo: [], NoLabels, missing, UnknownValuePolicy.Warn);

    // Numeric cut discretizer (open ends, invariant parse) paired with any scale —
    // nominal for discrete output, OrdinalScale for progressive.
    public static AttributeSpec NumericCuts(
        string name, int index, IReadOnlyList<double> cuts, Scale scale,
        MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!, scale,
            DeclaredDomain: [], RestrictTo: [], NoLabels, missing, UnknownValuePolicy.Warn);

    // A free_per_value attribute (§11.3, D-101). For a numeric source the domain entries are the
    // canonical numeric identities the seam normalizes to (D-096); tests pass them canonical.
    public static AttributeSpec FreePerValue(
        string name, int index, SourceValueType valueType, IReadOnlyList<string> domain, Scale scale,
        IReadOnlyDictionary<string, string>? valueLabels = null, MissingPolicy missing = MissingPolicy.Skip,
        UnknownValuePolicy policy = UnknownValuePolicy.Warn) =>
        new(name, new ColumnSource(index, valueType), Include: true,
            new FreePerValueDiscretizer(valueType, CultureInfo.InvariantCulture), scale,
            domain, RestrictTo: [], valueLabels ?? NoLabels, missing, policy);

    // A spec-determined equal_width attribute (§11.4, D-102): range = "manual", so its cuts
    // come from vmin/vmax alone and it needs no calibration.
    public static AttributeSpec EqualWidthManual(
        string name, int index, int bins, double vmin, double vmax, Scale scale,
        CutPrecision? precision = null, MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            EqualWidthDiscretizer.CreateManual(bins, vmin, vmax, precision ?? CutPrecision.Exact, CultureInfo.InvariantCulture).Value!,
            scale, DeclaredDomain: [], RestrictTo: [], NoLabels, missing, UnknownValuePolicy.Warn);

    // A data-derived equal_width attribute before calibration: the CalibrationPending carrier
    // the resolve seam produces, which Calibrate replaces with the executable discretizer (D-093).
    public static AttributeSpec EqualWidthPending(
        string name, int index, int bins, Scale scale,
        EqualWidthRange range = EqualWidthRange.MinMax, CutPrecision? precision = null,
        UnknownValuePolicy policy = UnknownValuePolicy.Warn) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            new CalibrationPending(new PendingEqualWidth(bins, range, precision ?? CutPrecision.Exact), CultureInfo.InvariantCulture),
            scale, DeclaredDomain: [], RestrictTo: [], NoLabels, MissingPolicy.Skip, policy);

    // An identity value-bin ordinal attribute (§12.3, D-081): an explicit order over
    // the declared domain, paired with an OrdinalScale carrying that order.
    public static AttributeSpec OrdinalValueBins(
        string name, int index, IReadOnlyList<string> domain, OrdinalScale scale,
        IReadOnlyDictionary<string, string>? valueLabels = null,
        MissingPolicy missing = MissingPolicy.Skip) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true,
            new IdentityDiscretizer(), scale, domain, RestrictTo: [], valueLabels ?? NoLabels, missing, UnknownValuePolicy.Warn);

    public static AttributeSpec Excluded(string name, int index) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: false, Discretizer: null, Scale: null,
            DeclaredDomain: [], RestrictTo: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    public static BedrockSpec MiniMushroom() =>
        new(WideRowIndex(), [
            Excluded("class", 0),
            Dichotomic("bruises?", 1, "t", ["t", "f"]),
            Nominal("gill-size", 2, ["b", "n"], new Dictionary<string, string> { ["b"] = "broad", ["n"] = "narrow" }),
            Nominal("veil-type", 3, ["p", "u"], new Dictionary<string, string> { ["p"] = "partial", ["u"] = "universal" }),
            Nominal("ring-number", 4, ["n", "o", "t"], new Dictionary<string, string> { ["n"] = "none", ["o"] = "one", ["t"] = "two" }),
        ]);
}
