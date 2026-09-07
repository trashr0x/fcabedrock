using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// Authored W16 specs that pressure one calibration property at a time.
/// <para>
/// These are <b>spec</b> variants over the existing W16 corpus rather than new corpora, and that is
/// the point: the data is held fixed so the difference between two measurements is the spec and
/// nothing else. A new corpus alongside a new spec would confound the two.
/// </para>
/// </summary>
internal static class W16PressureSpecs
{
    /// <summary>The bin counts each numeric column is calibrated at, ascending.</summary>
    public static IReadOnlyList<int> QuantileBinCounts { get; } = [4, 8, 16, 32];

    /// <summary>The W16 numeric columns, in physical order, with their spec names.</summary>
    public static IReadOnlyList<(string Name, int Column)> NumericColumns { get; } =
    [
        ("n_seq", W16Corpus.ColSeq),
        ("n_ties", W16Corpus.ColTies),
        ("n_skew", W16Corpus.ColSkew),
        ("n_wide", W16Corpus.ColWide),
    ];

    /// <summary>
    /// The <b>many count-sensitive attributes</b> spec: sixteen equal-frequency attributes, four
    /// bin counts over each of the four numeric columns.
    /// <para>
    /// Exact count-sensitive calibration retains one bounded accumulator per attribute, and the
    /// buffer bound is <c>max(budget, attributeCount x FloorBytes)</c> (D-095/D-103): the floor arm
    /// only becomes visible when there are enough attributes for it to exceed the budget, and four
    /// numeric columns are not enough. Sixteen attributes over the same four columns give that
    /// pressure honestly — every one is a real, independently configured attribute the calibrator
    /// must resolve, not a duplicate of another.
    /// </para>
    /// <para>
    /// It also crosses the two hard cases deliberately: <c>n_seq</c> is maximally
    /// high-cardinality (every value distinct), while <c>n_ties</c> and <c>n_skew</c> are heavily
    /// tied, so the boundary-feasibility path and the distinct-value path both run in one pass.
    /// </para>
    /// </summary>
    public static string ManyQuantiles { get; } = BuildManyQuantiles(ManyQuantileAttributeCount);

    /// <summary>The number of attributes <see cref="ManyQuantiles"/> declares.</summary>
    public static int ManyQuantileAttributeCount => NumericColumns.Count * QuantileBinCounts.Count;

    /// <summary>
    /// The same spec truncated to its first <paramref name="attributes"/> entries — the axis a
    /// controlled check varies when the <b>number</b> of simultaneous count-sensitive accumulators
    /// must be the only difference between two runs.
    /// <para>
    /// Enumeration is column-major, so the small counts stay on <c>n_seq</c>: it is maximally
    /// high-cardinality, which is what makes a one-attribute spec spill at all and therefore what
    /// makes it comparable with a sixteen-attribute one.
    /// </para>
    /// </summary>
    public static string ManyQuantilesOf(int attributes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attributes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(attributes, ManyQuantileAttributeCount);
        return BuildManyQuantiles(attributes);
    }

    /// <summary>The spec-attribute name for one column at one bin count.</summary>
    public static string QuantileAttributeName(string column, int bins) =>
        string.Create(CultureInfo.InvariantCulture, $"{column}_q{bins}");

    private static string BuildManyQuantiles(int attributes)
    {
        var spec = new StringBuilder(1 << 12);
        spec.Append(
            """
            # W16 many-quantile: exact equal-frequency attributes in one calibration pass, four bin
            # counts over each of the four numeric columns (sixteen at full width). The point is the
            # attribute count, which is the arm of the D-095 buffer bound a four-attribute spec never
            # reaches, and the scope at which D-082's shared-workspace merge allowance must be read.
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true
            delimiter = ","
            missing_token = "?"

            """);

        var written = 0;
        foreach (var (name, column) in NumericColumns)
        {
            foreach (var bins in QuantileBinCounts)
            {
                if (written++ == attributes)
                {
                    return spec.ToString();
                }

                spec.Append(CultureInfo.InvariantCulture, $"[[attribute]]\nname = \"{QuantileAttributeName(name, bins)}\"\n");
                spec.Append(CultureInfo.InvariantCulture, $"source = {{ kind = \"column\", index = {column}, value_type = \"number\" }}\n");
                spec.Append(CultureInfo.InvariantCulture, $"discretizer = {{ kind = \"equal_frequency\", bins = {bins} }}\n");
                spec.Append("scale = { kind = \"nominal\" }\n\n");
            }
        }

        return spec.ToString();
    }
}
