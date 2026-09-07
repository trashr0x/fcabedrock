using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The authored specs the T10 benchmarks convert under, and the frozen formal-attribute layout the
/// triple oracle indexes into.
/// <para>
/// Two specs, because they answer different questions. The <b>declared</b> one is fully
/// spec-determined, so it runs no calibration pass and its emit measurement is emission alone — and
/// because its column set does not depend on the data, the two physical layouts of the same
/// observations must produce <b>byte-identical</b> output, which is the load-bearing equivalence
/// this family exists to check. The <b>auto</b> one is count-sensitive: it draws its cuts from the
/// population, so under <c>unordered</c> it forces the calibrator's grouped second pass and the
/// subject-local deduplication rule that goes with it.
/// </para>
/// </summary>
internal static class T10Specs
{
    /// <summary>The <c>Stage</c> cuts, ascending, for the declared spec.</summary>
    public static IReadOnlyList<int> StageCuts { get; } = [50_000, 125_000, 200_000];

    /// <summary>Bins the declared <c>Stage</c> attribute produces (open ends).</summary>
    public static int StageBins => StageCuts.Count + 1;

    /// <summary>Fully declared: no calibration pass, and a data-independent column set.</summary>
    public static string Declared { get; } = BuildDeclared();

    /// <summary>
    /// Count-sensitive: <c>Stage</c> draws equal-frequency cuts from the population, and
    /// <c>Tissue</c> omits its domain so Calibrate observes it.
    /// </summary>
    public static string Auto { get; } = BuildAuto();

    /// <summary>The formal attributes the declared spec plans, in plan order.</summary>
    public static IReadOnlyList<string> DeclaredFormalAttributeNames { get; } = BuildDeclaredNames();

    // ---- the frozen id layout the declared oracle indexes into -------------------------------

    /// <summary>First id of <c>Tissue</c>: one nominal value bin per declared tissue.</summary>
    public const int TissueBase = 0;

    /// <summary>First id of <c>Signal</c>.</summary>
    public static int SignalBase => TissueBase + T10Corpus.TissueDomain.Count;

    /// <summary>First id of <c>Stage</c>: open-ended numeric cut bins.</summary>
    public static int StageBase => SignalBase + T10Corpus.SignalDomain.Count;

    /// <summary>The declared spec's total column count.</summary>
    public static int DeclaredFormalAttributeCount => StageBase + StageBins;

    private static string BuildDeclared()
    {
        var spec = new StringBuilder();
        spec.Append(
            """
            # T10 declared: fully spec-determined, so the column set cannot depend on the data - which
            # is what lets the grouped and interleaved layouts be compared byte for byte.
            [spec]
            version = 1

            [binding]
            shape = "triple"
            ordering = "unordered"
            columns = { subject = 0, predicate = 1, value = 2 }

            [[attribute]]
            name = "Tissue"
            source = { kind = "predicate", name = "Tissue" }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain =
            """);
        spec.Append(' ').Append(TomlArray(T10Corpus.TissueDomain));
        spec.Append(
            """


            [[attribute]]
            name = "Signal"
            source = { kind = "predicate", name = "Signal" }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain =
            """);
        spec.Append(' ').Append(TomlArray(T10Corpus.SignalDomain));
        spec.Append("\n\n");

        // Concatenated rather than interpolated: a raw interpolated string would need every brace of
        // TOML's inline tables doubled, which makes the spec text here harder to read than the file
        // it produces — and this text is meant to read as a spec.
        spec.Append(
            """
            [[attribute]]
            name = "Stage"
            source = { kind = "predicate", name = "Stage", value_type = "number" }
            discretizer = { kind = "manual_cuts", cuts = [
            """);
        spec.Append(string.Join(", ", StageCuts.Select(cut => cut.ToString(CultureInfo.InvariantCulture))));
        spec.Append(
            """
            ], ends = "open" }
            scale = { kind = "nominal" }

            """);
        return spec.ToString();
    }

    private static string BuildAuto() =>
        """
        # T10 auto: count-sensitive calibration. Stage's cuts come from the population, so under
        # `unordered` the calibrator must take its grouped second pass and apply the subject-local
        # deduplication rule; Tissue omits its domain, so Calibrate observes it.
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "unordered"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "Tissue"
        source = { kind = "predicate", name = "Tissue" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "Stage"
        source = { kind = "predicate", name = "Stage", value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 8 }
        scale = { kind = "nominal" }

        """;

    /// <summary>The same spec text with the triple ordering switched to <c>subject_grouped</c>.</summary>
    public static string AsGrouped(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Replace("ordering = \"unordered\"", "ordering = \"subject_grouped\"", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildDeclaredNames()
    {
        var names = new List<string>();
        names.AddRange(T10Corpus.TissueDomain.Select(value => "Tissue-" + value));
        names.AddRange(T10Corpus.SignalDomain.Select(value => "Signal-" + value));

        // The §11.2 open-ended cut labels, spelled independently of the production helper.
        names.Add("Stage-<" + StageCuts[0]);
        for (var i = 0; i + 1 < StageCuts.Count; i++)
        {
            names.Add($"Stage-[{StageCuts[i]}, {StageCuts[i + 1]})");
        }

        names.Add("Stage->=" + StageCuts[^1]);
        return names;
    }

    private static string TomlArray(IReadOnlyList<string> values) =>
        "[" + string.Join(", ", values.Select(value => "\"" + value + "\"")) + "]";
}
