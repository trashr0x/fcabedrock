namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The curated spec the UCI Adult benchmarks convert under.
/// <para>
/// It is authored FcaBedrock material, not part of the dataset: the choice of cuts, of which columns
/// become attributes, and of how missing cells are handled is an analyst's, and it is committed here
/// so that a measurement over real data is reproducible rather than improvised. The geometry is the
/// published one — fifteen headerless columns, comma-delimited, with <c>?</c> marking missing values
/// in <c>workclass</c>, <c>occupation</c>, and <c>native-country</c>.
/// </para>
/// <para>
/// Six of its attributes omit <c>declared_domain</c>, so a conversion runs a real calibration pass
/// and discovers those domains in first-observation order. That is deliberate: the Adult case exists
/// to exercise the pipeline on data nobody designed for it, and pre-declaring every domain would
/// remove the phase most sensitive to that.
/// </para>
/// </summary>
internal static class AdultSpecs
{
    /// <summary>The curated Adult spec.</summary>
    public static string Declared { get; } =
        """
        # UCI Adult (Becker & Kohavi 1996), training split, headerless, 15 columns.
        # The dataset is CC BY 4.0; this spec is authored FcaBedrock material.
        #
        # Cuts are conventional census bands rather than data-derived, so the column set stays
        # independent of the split being read. The three columns that carry `?` use
        # missing_policy = "as_attribute", which keeps "unknown" a fact about an object instead of
        # silently dropping it.
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = false
        delimiter = ","
        missing_token = "?"

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 0, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [25, 45, 65], ends = "open" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "workclass"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        missing_policy = "as_attribute"

        [[attribute]]
        name = "education"
        source = { kind = "column", index = 3 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "education-num"
        source = { kind = "column", index = 4, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [9, 12, 14], ends = "open" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "marital-status"
        source = { kind = "column", index = 5 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "occupation"
        source = { kind = "column", index = 6 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        missing_policy = "as_attribute"

        [[attribute]]
        name = "relationship"
        source = { kind = "column", index = 7 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "race"
        source = { kind = "column", index = 8 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "sex"
        source = { kind = "column", index = 9 }
        discretizer = { kind = "identity" }
        scale = { kind = "dichotomic", true_value = "Male" }
        declared_domain = ["Male", "Female"]

        [[attribute]]
        name = "capital-gain"
        source = { kind = "column", index = 10, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [1, 5000], ends = "open" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "capital-loss"
        source = { kind = "column", index = 11, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [1, 2000], ends = "open" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "hours-per-week"
        source = { kind = "column", index = 12, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [20, 40, 50], ends = "open" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "native-country"
        source = { kind = "column", index = 13 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        missing_policy = "as_attribute"

        [[attribute]]
        name = "class"
        source = { kind = "column", index = 14 }
        discretizer = { kind = "identity" }
        scale = { kind = "dichotomic", true_value = ">50K" }
        declared_domain = ["<=50K", ">50K"]

        """;
}
