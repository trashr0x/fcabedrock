using System.Text;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Golden.Tests;

/// <summary>
/// The deterministic, synthetic Internet-Advertisements corpus — a wide headerless CSV that
/// mirrors the complete raw <c>ad.data</c> layout without copying any UCI data row.
/// <para>
/// <b>Determinism is the whole point</b> (P-7): no clock, random source, machine or culture
/// state, platform newline, or unordered enumeration enters the generated text. Every value is a
/// literal string spelling and the newline is a fixed <c>\n</c>. The row set is small — a handful
/// of rows — but the width is never reduced: 1,559 columns, always.
/// </para>
/// <para>
/// Layout: indexes 0-2 continuous numeric <c>height</c>/<c>width</c>/<c>aratio</c> (with
/// representative <c>?</c> missing cells); index 3 binary <c>local</c>; indexes 4-1557 exactly
/// 1,554 binary term columns (with a few <c>?</c>); index 1558 the <c>class</c> dichotomy.
/// </para>
/// </summary>
internal static class AdCorpus
{
    public const int ColumnCount = 1559;
    public const int FirstTermIndex = 4;
    public const int LastTermIndex = 1557;
    public const int TermCount = 1554; // 1557 - 4 + 1
    public const int ClassIndex = 1558;
    public const int RowCount = 6;
    public const string MissingToken = "?";

    // Continuous columns: finite invariant-culture spellings plus representative missing cells,
    // chosen so every manual-cut bin (height/width: 4 bins; aratio: 3 bins) is exercised.
    private static readonly string[] Height = ["125", "?", "60", "33", "90", "200"];
    private static readonly string[] Width = ["125", "45", "?", "90", "200", "20"];
    private static readonly string[] Aratio = ["1.0", "?", "2.5", "0.5", "1.5", "?"];

    // Binary local (no missing) and the class dichotomy, both outcomes present.
    private static readonly string[] Local = ["1", "0", "1", "0", "1", "0"];
    private static readonly string[] Class = ["ad.", "nonad.", "ad.", "nonad.", "ad.", "nonad."];

    // The 6 x 1,559 raw cell matrix, built once and kept PRIVATE and immutable-by-encapsulation:
    // no caller can reach the backing arrays to mutate shared global state. Static-init
    // order: the source arrays above, then this, then Csv.
    private static readonly string[][] Rows = BuildRows();

    /// <summary>The headerless CSV text, fixed <c>\n</c> line endings, one trailing newline.</summary>
    public static readonly string Csv = BuildCsvFrom(Rows);

    /// <summary>
    /// A freshly generated, independent copy of the corpus CSV — built from a brand-new row
    /// matrix each call, never the cached <see cref="Rows"/> — so callers that need
    /// independently generated input bytes (repeatability) get a value no earlier call can have
    /// perturbed. Determinism guarantees two calls are byte-identical to each other and to
    /// <see cref="Csv"/>.
    /// </summary>
    public static string GenerateCsv() => BuildCsvFrom(BuildRows());

    /// <summary>The logical attribute name the authored forms give physical column
    /// <paramref name="column"/> (the term columns encode their index).</summary>
    public static string ColumnName(int column) => column switch
    {
        0 => "height",
        1 => "width",
        2 => "aratio",
        3 => "local",
        ClassIndex => "class",
        _ => $"term_{column:D4}",
    };

    /// <summary>
    /// The distinct non-missing values of a physical column in <b>first-observation order</b>
    /// (§17 rule 3) — computed independently of the probe so it can verify the probe's own
    /// declared domain, and reused as the authored domain of the string-nominal forms.
    /// </summary>
    public static IReadOnlyList<string> DomainOf(int column)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var row in Rows)
        {
            var value = row[column];
            if (value == MissingToken)
            {
                continue;
            }

            if (seen.Add(value))
            {
                order.Add(value);
            }
        }

        return order;
    }

    private static string TermCell(int column, int row)
    {
        var localIndex = column - FirstTermIndex;

        // A sparse, deterministic scatter of missing cells (one row of ~16 columns), never
        // enough to make a column all-missing.
        if (localIndex % 100 == 3 && row == 4)
        {
            return MissingToken;
        }

        return (localIndex + row) % 2 == 0 ? "1" : "0";
    }

    private static string[][] BuildRows()
    {
        var rows = new string[RowCount][];
        for (var r = 0; r < RowCount; r++)
        {
            var row = new string[ColumnCount];
            row[0] = Height[r];
            row[1] = Width[r];
            row[2] = Aratio[r];
            row[3] = Local[r];
            for (var j = FirstTermIndex; j <= LastTermIndex; j++)
            {
                row[j] = TermCell(j, r);
            }

            row[ClassIndex] = Class[r];
            rows[r] = row;
        }

        return rows;
    }

    private static string BuildCsvFrom(string[][] rows)
    {
        var sb = new StringBuilder(RowCount * ColumnCount * 2);
        foreach (var row in rows)
        {
            sb.Append(string.Join(',', row)).Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>
/// Builds the exit spec forms — declarative, materialized, uncurated-draft-style, and the two
/// draft-level de-shadow forms — as TOML text over <see cref="AdCorpus"/>, plus the section
/// objects used to curate a real probe document. Each form is assembled independently (its own
/// builder, from shared primitive emitters), so an equivalence claim can never pass merely
/// because two sides share the same final text, resolved document, or plan.
/// </summary>
internal static class AdSpecs
{
    public const string SourceUrl = "https://doi.org/10.24432/C5V011";

    public const string Notes =
        "Synthetic layout mirroring Nicholas Kushmerick (1999), Internet Advertisements, "
        + "UCI Machine Learning Repository; no UCI data rows included.";

    private const string TermTemplateToml =
        "[[template]]\nid = \"term_flag\"\n"
        + "discretizer = { kind = \"identity\" }\n"
        + "scale = { kind = \"dichotomic\", true_value = \"1\" }\n"
        + "declared_domain = [\"1\", \"0\"]\n\n";

    private const string TermMatcherToml =
        "[[matcher]]\nmatch = { source_index_range = [4, 1557] }\ntemplate = \"term_flag\"\n\n";

    /// <summary>Form (i): numeric manual-cut columns, an explicit binary <c>local</c>, the 1,554
    /// bare term declarations configured by one template + matcher, and an explicit
    /// <c>class</c>. <paramref name="includeMatcher"/> false drops the template/matcher so the
    /// bare terms are left unscaled (the matcher-is-load-bearing sensitivity check).</summary>
    public static string Declarative(bool includeMatcher = true)
    {
        var sb = new StringBuilder(Header());
        if (includeMatcher)
        {
            sb.Append(TermTemplateToml).Append(TermMatcherToml);
        }

        AppendNumeric(sb, "height", 0, "[40, 80, 120]", asAttribute: false);
        AppendNumeric(sb, "width", 1, "[40, 80, 120]", asAttribute: false);
        AppendNumeric(sb, "aratio", 2, "[1, 2]", asAttribute: true);
        AppendDichotomic(sb, "local", 3);
        for (var j = AdCorpus.FirstTermIndex; j <= AdCorpus.LastTermIndex; j++)
        {
            AppendBare(sb, AdCorpus.ColumnName(j), j);
        }

        AppendNominal(sb, "class", AdCorpus.ClassIndex, AdCorpus.DomainOf(AdCorpus.ClassIndex));
        return sb.ToString();
    }

    /// <summary>Form (ii): identical to the declarative twin but with the dichotomic term config
    /// written on every term attribute and no template/matcher. <paramref
    /// name="term0004MissingColumn"/> adds an as_attribute to one term (the sensitivity anchor
    /// that makes the equivalence projections divergence-detecting).</summary>
    public static string Materialized(bool term0004MissingColumn = false)
    {
        var sb = new StringBuilder(Header());
        AppendNumeric(sb, "height", 0, "[40, 80, 120]", asAttribute: false);
        AppendNumeric(sb, "width", 1, "[40, 80, 120]", asAttribute: false);
        AppendNumeric(sb, "aratio", 2, "[1, 2]", asAttribute: true);
        AppendDichotomic(sb, "local", 3);
        for (var j = AdCorpus.FirstTermIndex; j <= AdCorpus.LastTermIndex; j++)
        {
            AppendDichotomic(sb, AdCorpus.ColumnName(j), j, asAttribute: term0004MissingColumn && j == AdCorpus.FirstTermIndex);
        }

        AppendNominal(sb, "class", AdCorpus.ClassIndex, AdCorpus.DomainOf(AdCorpus.ClassIndex));
        return sb.ToString();
    }

    /// <summary>Form (iii): probe-faithful — every attribute (numeric columns included, as
    /// strings) explicitly authors identity + nominal + its complete observed domain, plus the
    /// term template/matcher. With the template present, its three fields are shadowed on every
    /// term.</summary>
    public static string Uncurated(bool withTemplateMatcher)
    {
        var sb = new StringBuilder(Header());
        if (withTemplateMatcher)
        {
            sb.Append(TermTemplateToml).Append(TermMatcherToml);
        }

        for (var col = 0; col < AdCorpus.ColumnCount; col++)
        {
            AppendNominal(sb, AdCorpus.ColumnName(col), col, AdCorpus.DomainOf(col));
        }

        return sb.ToString();
    }

    /// <summary>De-shadow form (1): the probe-style string non-term columns kept, the three
    /// explicit term fields removed so the matcher wins.</summary>
    public static string DeshadowMatcher()
    {
        var sb = new StringBuilder(Header());
        sb.Append(TermTemplateToml).Append(TermMatcherToml);
        for (var col = 0; col < AdCorpus.ColumnCount; col++)
        {
            if (col is >= AdCorpus.FirstTermIndex and <= AdCorpus.LastTermIndex)
            {
                AppendBare(sb, AdCorpus.ColumnName(col), col);
            }
            else
            {
                AppendNominal(sb, AdCorpus.ColumnName(col), col, AdCorpus.DomainOf(col));
            }
        }

        return sb.ToString();
    }

    /// <summary>De-shadow form (2): the probe-style string non-term columns kept, the template's
    /// dichotomic config materialized onto every term, no template/matcher.</summary>
    public static string DeshadowMaterialized()
    {
        var sb = new StringBuilder(Header());
        for (var col = 0; col < AdCorpus.ColumnCount; col++)
        {
            if (col is >= AdCorpus.FirstTermIndex and <= AdCorpus.LastTermIndex)
            {
                AppendDichotomic(sb, AdCorpus.ColumnName(col), col);
            }
            else
            {
                AppendNominal(sb, AdCorpus.ColumnName(col), col, AdCorpus.DomainOf(col));
            }
        }

        return sb.ToString();
    }

    /// <summary>The <c>term_flag</c> template as a section object, for curating a probe document.</summary>
    public static TemplateSection TermTemplateSection() =>
        new(
            Id: "term_flag",
            Include: null,
            Discretizer: new IdentityDiscretizerSection(),
            Scale: new DichotomicScaleSection("1"),
            DeclaredDomain: ["1", "0"],
            RestrictTo: null,
            ValueLabels: null,
            MissingPolicy: null,
            UnknownValuePolicy: null);

    /// <summary>The <c>[4, 1557]</c> range matcher as a section object, for curating a probe document.</summary>
    public static MatcherSection TermMatcherSection() =>
        new(
            new MatchSection(NameRegex: null, SourceIndexRange: new long[] { AdCorpus.FirstTermIndex, AdCorpus.LastTermIndex }),
            "term_flag");

    private static string Header() =>
        "[spec]\nversion = 1\n\n"
        + "[provenance]\n"
        + "source_url = \"" + SourceUrl + "\"\n"
        + "notes = \"" + Notes + "\"\n\n"
        + "[binding]\nshape = \"wide\"\nhas_header = false\n\n";

    private static void AppendNumeric(StringBuilder sb, string name, int index, string cuts, bool asAttribute)
    {
        sb.Append("[[attribute]]\n");
        sb.Append("name = \"").Append(name).Append("\"\n");
        sb.Append("source = { kind = \"column\", index = ").Append(index).Append(", value_type = \"number\" }\n");
        sb.Append("discretizer = { kind = \"manual_cuts\", cuts = ").Append(cuts).Append(", ends = \"open\" }\n");
        sb.Append("scale = { kind = \"nominal\" }\n");
        if (asAttribute)
        {
            sb.Append("missing_policy = \"as_attribute\"\n");
        }

        sb.Append('\n');
    }

    private static void AppendDichotomic(StringBuilder sb, string name, int index, bool asAttribute = false)
    {
        sb.Append("[[attribute]]\n");
        sb.Append("name = \"").Append(name).Append("\"\n");
        sb.Append("source = { kind = \"column\", index = ").Append(index).Append(" }\n");
        sb.Append("discretizer = { kind = \"identity\" }\n");
        sb.Append("scale = { kind = \"dichotomic\", true_value = \"1\" }\n");
        sb.Append("declared_domain = [\"1\", \"0\"]\n");
        if (asAttribute)
        {
            sb.Append("missing_policy = \"as_attribute\"\n");
        }

        sb.Append('\n');
    }

    private static void AppendNominal(StringBuilder sb, string name, int index, IReadOnlyList<string> domain)
    {
        sb.Append("[[attribute]]\n");
        sb.Append("name = \"").Append(name).Append("\"\n");
        sb.Append("source = { kind = \"column\", index = ").Append(index).Append(" }\n");
        sb.Append("discretizer = { kind = \"identity\" }\n");
        sb.Append("scale = { kind = \"nominal\" }\n");
        sb.Append("declared_domain = ").Append(DomainLiteral(domain)).Append('\n');
        sb.Append('\n');
    }

    private static void AppendBare(StringBuilder sb, string name, int index)
    {
        sb.Append("[[attribute]]\n");
        sb.Append("name = \"").Append(name).Append("\"\n");
        sb.Append("source = { kind = \"column\", index = ").Append(index).Append(" }\n\n");
    }

    private static string DomainLiteral(IReadOnlyList<string> values)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append('"').Append(Escape(values[i])).Append('"');
        }

        return sb.Append(']').ToString();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
