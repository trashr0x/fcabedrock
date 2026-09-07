namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The W16 <b>auto</b> spec: every data-reading calibration kind at once, over one pass of one
/// corpus.
/// <para>
/// Four attributes, chosen so each exercises a different calibration shape and each has an
/// independently derivable expected outcome:
/// </para>
/// <list type="bullet">
/// <item><c>n_seq</c> is <b>equal-frequency</b> over a strictly increasing column, so its
/// population is the count-sensitive, maximally high-cardinality case — its distinct count equals
/// the record count, which is what forces the bounded quantile accumulator to spill at scale — and
/// its exact order statistics are known without sorting anything.</item>
/// <item><c>n_wide</c> is <b>equal-width over the observed range</b>, so it exercises the streaming
/// min/max pass, which retains two doubles and never sorts.</item>
/// <item><c>n_skew</c> is <b>equal-width over the 1st-to-99th percentile range</b>, so it exercises
/// exact percentile selection over a deliberately skewed population.</item>
/// <item><c>c2</c> omits its <c>declared_domain</c>, so Calibrate discovers the <b>observed
/// domain</b> in first-observation order.</item>
/// </list>
/// <para>
/// It is deliberately a separate spec from the declared one rather than a variant of it: the emit
/// benchmarks measure emission with calibration already resolved, and mixing the two would make
/// neither number mean what it says.
/// </para>
/// </summary>
internal static class W16CalibrationSpec
{
    /// <summary>Bins for the equal-frequency attribute.</summary>
    public const int SeqBins = 8;

    /// <summary>Bins for the min/max equal-width attribute.</summary>
    public const int WideBins = 4;

    /// <summary>Bins for the percentile-range equal-width attribute.</summary>
    public const int SkewBins = 4;

    /// <summary>The spec text.</summary>
    public static string Text { get; } =
        """
        # W16 auto: one pass, four calibration shapes. Each attribute's outcome is independently
        # derivable from the corpus definition, so the calibrated cuts and domains can be checked
        # rather than merely produced.
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true
        delimiter = ","
        missing_token = "?"

        [[attribute]]
        name = "n_seq"
        source = { kind = "column", index = 0, value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 8 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "n_wide"
        source = { kind = "column", index = 3, value_type = "number" }
        discretizer = { kind = "equal_width", bins = 4, range = "min_max" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "n_skew"
        source = { kind = "column", index = 2, value_type = "number" }
        discretizer = { kind = "equal_width", bins = 4, range = "percentile_p1_p99" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "c2"
        source = { kind = "column", index = 6 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        """;
}
