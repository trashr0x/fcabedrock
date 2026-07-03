using System.Globalization;
using System.Text;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// Test-only builders for the emitter tests. The mushroom data/spec are inlined so
// the test is self-contained (the byte-equal golden fixtures live in Golden.Tests).
internal static class ConversionFixtures
{
    public const string MushroomCsv =
        "class,bruises?,gill-size,veil-type,ring-number\n" +
        "e,t,b,p,n\n" +
        "e,t,n,p,t\n" +
        "e,f,n,p,n\n" +
        "e,t,b,p,o\n" +
        "e,f,n,p,n";

    public static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    public static Binding Wide(char delimiter = ',', bool hasHeader = true, string missingToken = "?") =>
        new(SourceShape.Wide, delimiter, '"', hasHeader, "invariant", missingToken, new RowIndexObjectKey());

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

    public static AttributeSpec Nominal(string name, int index, params string[] domain) =>
        Nominal(name, index, UnknownValuePolicy.Warn, domain);

    public static AttributeSpec Nominal(string name, int index, UnknownValuePolicy policy, params string[] domain) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(), new NominalScale(),
            domain, RestrictTo: [], NoLabels, MissingPolicy.Skip, policy);

    // A numeric manual_cuts attribute (open ends, nominal) with a configurable unknown-value
    // policy — for exercising the malformed-numeric path (§11.5 / D-050).
    public static AttributeSpec NumericCuts(string name, int index, UnknownValuePolicy policy, params double[] cuts) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!,
            new NominalScale(), DeclaredDomain: [], RestrictTo: [], NoLabels, MissingPolicy.Skip, policy);

    // An ordered_cuts attribute (open ends, nominal) over a category order with one cut.
    public static AttributeSpec OrderedCuts(string name, int index, IReadOnlyList<string> order, string cut) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true,
            OrderedCutsDiscretizer.Create(order, [cut], BinEnds.Open).Value!,
            new NominalScale(), DeclaredDomain: [], RestrictTo: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static Dictionary<string, string> Labels(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}
