using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The authored spec the Ads-width benchmarks convert under, and the frozen formal-attribute layout
/// its oracle indexes into.
/// <para>
/// The spec is <b>generated</b> rather than typed out, because 1,559 columns is precisely the point:
/// a hand-written 1,554-attribute spec would be unreviewable, and the generator is the same kind of
/// definition the corpus itself has. It is still ordinary TOML read through the production
/// <c>SpecReader</c>, so the width pressure lands on the real authoring surface — the parser, the
/// resolver, and the planner — rather than on a hand-built Core graph.
/// </para>
/// <para>
/// Every attribute is fully declared, so no calibration pass runs and the column set does not depend
/// on the data. That keeps the Ads-width plan and emit measurements about width alone.
/// </para>
/// </summary>
internal static class AdsSpecs
{
    /// <summary>The <c>height</c> and <c>width</c> cuts, ascending.</summary>
    public static IReadOnlyList<int> SizeCuts { get; } = [120, 240, 480];

    /// <summary>The <c>aratio</c> cuts, ascending.</summary>
    public static IReadOnlyList<int> AspectCuts { get; } = [1, 3, 8];

    /// <summary>The fully declared Ads-width spec.</summary>
    public static string Declared { get; } = BuildDeclared();

    // ---- the frozen id layout the oracle indexes into --------------------------------------

    /// <summary>First id of <c>height</c>: four open-ended cut bins.</summary>
    public const int HeightBase = 0;

    /// <summary>Bin count for each of the three numeric attributes.</summary>
    public const int NumericBins = 4;

    /// <summary>First id of <c>width</c>.</summary>
    public const int WidthBase = HeightBase + NumericBins;

    /// <summary>First id of <c>aratio</c>.</summary>
    public const int AspectBase = WidthBase + NumericBins;

    /// <summary>The <c>local</c> dichotomic column.</summary>
    public const int LocalId = AspectBase + NumericBins;

    /// <summary>First id of the term flags; they are contiguous and in physical order.</summary>
    public const int TermBase = LocalId + 1;

    /// <summary>The <c>class</c> dichotomic column.</summary>
    public const int ClassId = TermBase + AdsCorpus.TermColumns;

    /// <summary>The spec's total formal-attribute count.</summary>
    public const int FormalAttributeCount = ClassId + 1;

    /// <summary>
    /// The formal attributes this spec plans, in plan order — the names a <c>.cxt</c> header
    /// carries. Spelled from the §10.7 defaults rather than borrowed from the planner: a nominal
    /// cut bin renders as <c>{column}-{label}</c>, and a dichotomic column renders as the column
    /// name alone (D-037(a)), with no value suffix.
    /// </summary>
    public static IReadOnlyList<string> FormalAttributeNames { get; } = BuildNames();

    private static IReadOnlyList<string> BuildNames()
    {
        var names = new List<string>(FormalAttributeCount);
        names.AddRange(CutBinLabels("height", SizeCuts));
        names.AddRange(CutBinLabels("width", SizeCuts));
        names.AddRange(CutBinLabels("aratio", AspectCuts));
        names.Add("local");
        for (var term = 0; term < AdsCorpus.TermColumns; term++)
        {
            names.Add(AdsCorpus.TermName(term));
        }

        names.Add("class");
        return names;
    }

    // The §11.2 open-ended cut labels, spelled independently of the production helper so the
    // expected names are an oracle rather than a restatement of the code under test.
    private static IEnumerable<string> CutBinLabels(string column, IReadOnlyList<int> cuts)
    {
        yield return column + "-<" + cuts[0].ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"{column}-[{cuts[i]}, {cuts[i + 1]})");
        }

        yield return column + "->=" + cuts[^1].ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildDeclared()
    {
        var spec = new StringBuilder(1 << 18);
        spec.Append(
            """
            # Ads-width declared: 1,559 physical columns, fully spec-determined so no calibration
            # pass runs and the column set cannot depend on the data. Generated, not typed: the
            # width is the case.
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true
            delimiter = ","

            """);

        AppendCuts(spec, "height", AdsCorpus.ColHeight, SizeCuts);
        AppendCuts(spec, "width", AdsCorpus.ColWidth, SizeCuts);
        AppendCuts(spec, "aratio", AdsCorpus.ColAspect, AspectCuts);
        AppendDichotomic(spec, "local", AdsCorpus.ColLocal, trueValue: "1", falseValue: "0");

        for (var term = 0; term < AdsCorpus.TermColumns; term++)
        {
            AppendDichotomic(
                spec, AdsCorpus.TermName(term), AdsCorpus.ColTermFirst + term, trueValue: "1", falseValue: "0");
        }

        AppendDichotomic(spec, "class", AdsCorpus.ColClass, AdsCorpus.AdLabel, AdsCorpus.NonAdLabel);
        return spec.ToString();
    }

    private static void AppendCuts(StringBuilder spec, string name, int column, IReadOnlyList<int> cuts)
    {
        spec.Append(CultureInfo.InvariantCulture, $"[[attribute]]\nname = \"{name}\"\n");
        spec.Append(CultureInfo.InvariantCulture, $"source = {{ kind = \"column\", index = {column}, value_type = \"number\" }}\n");
        spec.Append("discretizer = { kind = \"manual_cuts\", cuts = [");
        spec.Append(string.Join(", ", cuts.Select(cut => cut.ToString(CultureInfo.InvariantCulture))));
        spec.Append("], ends = \"open\" }\nscale = { kind = \"nominal\" }\n\n");
    }

    private static void AppendDichotomic(
        StringBuilder spec, string name, int column, string trueValue, string falseValue)
    {
        spec.Append(CultureInfo.InvariantCulture, $"[[attribute]]\nname = \"{name}\"\n");
        spec.Append(CultureInfo.InvariantCulture, $"source = {{ kind = \"column\", index = {column} }}\n");
        spec.Append("discretizer = { kind = \"identity\" }\n");
        spec.Append(CultureInfo.InvariantCulture, $"scale = {{ kind = \"dichotomic\", true_value = \"{trueValue}\" }}\n");
        spec.Append(CultureInfo.InvariantCulture, $"declared_domain = [\"{trueValue}\", \"{falseValue}\"]\n\n");
    }
}
