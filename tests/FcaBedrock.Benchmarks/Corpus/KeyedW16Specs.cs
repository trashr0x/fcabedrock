using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The authored spec the keyed-wide dedupe benchmarks convert under.
/// <para>
/// It is the W16 declared spec with three differences and no others: every column index is shifted
/// one place right to make room for the key column, <c>[binding.object_key]</c> names that column,
/// and <c>[defaults] duplicate_object_policy = "dedupe"</c> selects the merging path. The attribute
/// list — its order, its discretizers, its scales, its domains — is deliberately identical, so the
/// <b>formal-attribute layout is the same one</b> <see cref="W16Specs"/> freezes, and a deduped
/// object's expected crosses are just the union of its rows' plain W16 crosses. A test asserts that
/// identity rather than leaving it to inspection.
/// </para>
/// </summary>
internal static class KeyedW16Specs
{
    /// <summary>The keyed, deduping spec.</summary>
    public static string Declared { get; } = BuildDeclared();

    private static string BuildDeclared()
    {
        var spec = new StringBuilder();
        spec.Append(
            """
            # Keyed W16: the same attributes as the plain declared spec, over a corpus whose leading
            # column repeats. `dedupe` merges rows sharing a key onto one formal object, unioning
            # their crosses and keeping the first occurrence's position (§6.1, §17 rule 4).
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true
            delimiter = ","
            missing_token = "?"

            [binding.object_key]
            mode = "column"
            column = 0

            [defaults]
            duplicate_object_policy = "dedupe"

            """);

        Cuts(spec, "n_ties", W16Corpus.ColTies + 1, W16Specs.TiesCuts);
        Nominal(spec, "c0", W16Corpus.ColCategoricalFirst + 1, W16Corpus.StandardDomain, missingAsAttribute: false);
        spec.Append(
            """
            [[attribute]]
            name = "b0"
            source = { kind = "column", index = 13 }
            discretizer = { kind = "identity" }
            scale = { kind = "dichotomic", true_value = "yes" }
            declared_domain = ["yes", "no"]

            """);
        Nominal(spec, "c3", W16Corpus.ColCategoricalFirst + 3 + 1, W16Corpus.StandardDomain, missingAsAttribute: true);
        Cuts(spec, "n_skew", W16Corpus.ColSkew + 1, W16Specs.SkewCuts);
        Nominal(spec, "c6", W16Corpus.ColCategoricalFirst + 6 + 1, W16Corpus.QuotedDomain, missingAsAttribute: false);
        return spec.ToString();
    }

    private static void Cuts(StringBuilder spec, string name, int column, IReadOnlyList<int> cuts)
    {
        spec.Append(CultureInfo.InvariantCulture, $"[[attribute]]\nname = \"{name}\"\n");
        spec.Append(CultureInfo.InvariantCulture, $"source = {{ kind = \"column\", index = {column}, value_type = \"number\" }}\n");
        spec.Append("discretizer = { kind = \"manual_cuts\", cuts = [");
        spec.Append(string.Join(", ", cuts.Select(cut => cut.ToString(CultureInfo.InvariantCulture))));
        spec.Append("], ends = \"open\" }\nscale = { kind = \"nominal\" }\n\n");
    }

    private static void Nominal(
        StringBuilder spec, string name, int column, IReadOnlyList<string> domain, bool missingAsAttribute)
    {
        spec.Append(CultureInfo.InvariantCulture, $"[[attribute]]\nname = \"{name}\"\n");
        spec.Append(CultureInfo.InvariantCulture, $"source = {{ kind = \"column\", index = {column} }}\n");
        spec.Append("discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n");
        if (missingAsAttribute)
        {
            spec.Append("missing_policy = \"as_attribute\"\n");
        }

        spec.Append("declared_domain = [");
        spec.Append(string.Join(", ", domain.Select(value => "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"")));
        spec.Append("]\n\n");
    }
}
