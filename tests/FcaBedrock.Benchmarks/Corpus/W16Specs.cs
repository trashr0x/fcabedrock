using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The authored Bedrock specs the W16 benchmarks convert under, and the frozen formal-attribute
/// layout an oracle derives expectations from.
/// <para>
/// The specs are ordinary TOML text read through the production <c>SpecReader</c>, so the benchmark
/// exercises the real authoring surface rather than a hand-built Core graph — and the layout below
/// is asserted against the produced plan, so a planner change cannot silently drift the oracle out
/// of agreement with the code it validates.
/// </para>
/// </summary>
internal static class W16Specs
{
    // Static initializers run in textual order, so the two cut lists are declared FIRST: the spec
    // text and the expected-name list below are both derived from them.

    /// <summary>The <c>n_ties</c> cuts, ascending.</summary>
    public static IReadOnlyList<int> TiesCuts { get; } = [10, 20, 30, 40];

    /// <summary>The <c>n_skew</c> cuts, ascending.</summary>
    public static IReadOnlyList<int> SkewCuts { get; } = [8, 100, 1000];

    /// <summary>
    /// The <b>declared</b> W16 spec: fully spec-determined, so no data-reading calibration pass
    /// runs and the emit benchmark measures emission alone. Six logical attributes covering every
    /// scaling shape a small case needs — cut bins over a tied numeric and over a skewed one,
    /// nominal value bins, a dichotomic column, a value-bin column carrying the
    /// <c>as_attribute</c> missing column, and the quoted-domain column.
    /// </summary>
    public static string Declared { get; } = BuildDeclared();

    /// <summary>The formal attributes the <see cref="Declared"/> spec plans, in plan (column) order.</summary>
    public static IReadOnlyList<string> DeclaredFormalAttributeNames { get; } = BuildDeclaredNames();

    /// <summary>The number of formal attributes the <see cref="Declared"/> spec plans.</summary>
    public static int DeclaredFormalAttributeCount => DeclaredFormalAttributeNames.Count;

    // ---- the frozen id layout the oracle indexes into --------------------------------------
    //
    // Ids are assigned in plan order, which is spec-attribute order and, within an attribute, the
    // scale's own shape order (§17). Each constant below is the FIRST id of its attribute's block.

    /// <summary>First id of <c>n_ties</c>: five open-ended cut bins over [10, 20, 30, 40].</summary>
    public const int TiesBase = 0;

    /// <summary>Bin count for <c>n_ties</c>.</summary>
    public const int TiesBins = 5;

    /// <summary>First id of <c>c0</c>: eight nominal value bins.</summary>
    public const int C0Base = TiesBase + TiesBins;

    /// <summary>First (and only) id of <c>b0</c>: one dichotomic column, crossed by <c>yes</c>.</summary>
    public const int B0Id = C0Base + 8;

    /// <summary>First id of <c>c3</c>: eight nominal value bins followed by its missing column.</summary>
    public const int C3Base = B0Id + 1;

    /// <summary>The <c>c3-missing</c> column: appended after the value bins (§10.5 / D-074).</summary>
    public const int C3MissingId = C3Base + 8;

    /// <summary>First id of <c>n_skew</c>: four open-ended cut bins over [8, 100, 1000].</summary>
    public const int SkewBase = C3MissingId + 1;

    /// <summary>Bin count for <c>n_skew</c>.</summary>
    public const int SkewBins = 4;

    /// <summary>First id of <c>c6</c>: eight nominal value bins over the quoted domain.</summary>
    public const int C6Base = SkewBase + SkewBins;

    private static string BuildDeclared()
    {
        var spec = new StringBuilder();
        spec.Append(
            """
            # W16 declared: fully spec-determined, so convert never runs a calibration pass.
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true
            delimiter = ","
            missing_token = "?"

            [[attribute]]
            name = "n_ties"
            source = { kind = "column", index = 1, value_type = "number" }
            discretizer = { kind = "manual_cuts", cuts = [10, 20, 30, 40], ends = "open" }
            scale = { kind = "nominal" }

            [[attribute]]
            name = "c0"
            source = { kind = "column", index = 4 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain =
            """);
        spec.Append(' ').Append(TomlArray(W16Corpus.StandardDomain));
        spec.Append(
            """


            [[attribute]]
            name = "b0"
            source = { kind = "column", index = 12 }
            discretizer = { kind = "identity" }
            scale = { kind = "dichotomic", true_value = "yes" }
            declared_domain = ["yes", "no"]

            [[attribute]]
            name = "c3"
            source = { kind = "column", index = 7 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            missing_policy = "as_attribute"
            declared_domain =
            """);
        spec.Append(' ').Append(TomlArray(W16Corpus.StandardDomain));
        spec.Append(
            """


            [[attribute]]
            name = "n_skew"
            source = { kind = "column", index = 2, value_type = "number" }
            discretizer = { kind = "manual_cuts", cuts = [8, 100, 1000], ends = "open" }
            scale = { kind = "nominal" }

            [[attribute]]
            name = "c6"
            source = { kind = "column", index = 10 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain =
            """);
        spec.Append(' ').Append(TomlArray(W16Corpus.QuotedDomain));
        spec.Append('\n');
        return spec.ToString();
    }

    private static IReadOnlyList<string> BuildDeclaredNames()
    {
        var names = new List<string>();

        foreach (var label in CutBinLabels(TiesCuts))
        {
            names.Add("n_ties-" + label);
        }

        foreach (var value in W16Corpus.StandardDomain)
        {
            names.Add("c0-" + value);
        }

        // Dichotomic renders the column name alone — no value suffix (§10.7 / D-037(a)).
        names.Add("b0");

        foreach (var value in W16Corpus.StandardDomain)
        {
            names.Add("c3-" + value);
        }

        names.Add("c3-missing");

        foreach (var label in CutBinLabels(SkewCuts))
        {
            names.Add("n_skew-" + label);
        }

        foreach (var value in W16Corpus.QuotedDomain)
        {
            names.Add("c6-" + value);
        }

        return names;
    }

    // The §11.2 open-ended cut labels, spelled independently of the production helper so the
    // expected names are an oracle rather than a restatement of the code under test.
    private static IEnumerable<string> CutBinLabels(IReadOnlyList<int> cuts)
    {
        yield return "<" + cuts[0];
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            yield return $"[{cuts[i]}, {cuts[i + 1]})";
        }

        yield return ">=" + cuts[^1];
    }

    // A TOML basic-string array. The domains are authored values, so the escaping is the minimal
    // TOML one: a backslash and a double quote.
    private static string TomlArray(IReadOnlyList<string> values)
    {
        var text = new StringBuilder("[");
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            text.Append('"').Append(values[i].Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
        }

        return text.Append(']').ToString();
    }
}
