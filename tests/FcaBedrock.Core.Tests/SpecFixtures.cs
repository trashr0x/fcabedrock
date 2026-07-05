using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests;

// Builders for spec models used across the Core tests. A test-only helper at the
// project root; it does not mirror a production type (per CLAUDE.md conventions).
internal static class SpecFixtures
{
    public static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    public static Binding WideRowIndex(char delimiter = ',', bool hasHeader = true) =>
        new(SourceShape.Wide, delimiter, '"', hasHeader, "invariant", "?", new RowIndexObjectKey());

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
