namespace FcaBedrock.Spec.Tests.Toml;

// Authored TOML fixture strings (inline, per the BedFixtures convention).
// MiniMushroom/MiniAdult/MiniAdultTriples transcribe the spec's §19.1–§19.3
// worked examples (comments included — they exercise trivia handling);
// §19.3's "other attributes analogous" placeholder is completed with real
// attributes. KitchenSink covers the full M2-modelled surface in one document.
internal static class TomlFixtures
{
    /// <summary>Spec §19.1 — mini-mushroom (v2 compat).</summary>
    public const string MiniMushroom = """
        [spec]
        version = 1
        description = "v2 mini-mushroom spec, modernized"

        [binding]
        shape = "wide"
        has_header = true
        missing_token = "?"

        [binding.object_key]
        mode = "row_index"

        [[attribute]]
        name = "class"
        source = { kind = "column", index = 0 }
        include = false                              # was [Convert Attribute] = False
        # excluded → emits nothing; discretizer/scale/etc. are optional here and ignored
        # while off (D-049, an authoring toggle). class is dropped from the analysis.

        [[attribute]]
        name = "bruises?"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "dichotomic", true_value = "t" }
        declared_domain = ["t", "f"]

        [[attribute]]
        name = "gill-size"
        source = { kind = "column", index = 2 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["b", "n"]
        value_labels    = { b = "broad", n = "narrow" }

        [[attribute]]
        name = "veil-type"
        source = { kind = "column", index = 3 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["p", "u"]
        value_labels    = { p = "partial", u = "universal" }

        [[attribute]]
        name = "ring-number"
        source = { kind = "column", index = 4 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["n", "o", "t"]
        value_labels    = { n = "none", o = "one", t = "two" }
        """;

    /// <summary>Spec §19.2 — mini-adult (v2 compat).</summary>
    public const string MiniAdult = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = false
        missing_token = "?"

        [binding.object_key]
        mode = "row_index"

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "manual_cuts", cuts = [30, 40, 50], ends = "open" }
        scale = { kind = "nominal" }                 # v2's "discrete" continuous mode

        [[attribute]]
        name = "education"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["Bachelors", "Masters", "11th", "HS-grad"]

        [[attribute]]
        name = "employment"
        source = { kind = "column", index = 2 }
        discretizer = { kind = "ordered_cuts", order = ["Unskilled", "Clerical", "Professional", "Managerial"], cuts = ["Managerial"], ends = "open" }
        scale = { kind = "ordinal", direction = "le" }

        [[attribute]]
        name = "sex"
        source = { kind = "column", index = 3 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["Male", "Female"]

        [[attribute]]
        name = "US-citizen"
        source = { kind = "column", index = 4 }
        discretizer = { kind = "identity" }
        scale = { kind = "dichotomic", true_value = "Yes" }
        declared_domain = ["Yes", "No"]

        [[attribute]]
        name = "class"
        source = { kind = "column", index = 5 }
        include = false
        """;

    /// <summary>Spec §19.3 — mini-adult triples (named subjects); placeholder attributes completed.</summary>
    public const string MiniAdultTriples = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "subject_grouped"
        columns = { subject = 0, predicate = 1, value = 2 }
        missing_token = "?"

        # binding.object_key defaults to mode = "column", column = subject column

        [[attribute]]
        name = "age"
        source = { kind = "predicate", name = "age" }
        discretizer = { kind = "manual_cuts", cuts = [30, 40, 50], ends = "open" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "education"
        source = { kind = "predicate", name = "education" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["Bachelors", "Masters", "11th", "HS-grad"]

        [[attribute]]
        name = "class"
        source = { kind = "predicate", name = "class" }
        include = false
        """;

    /// <summary>
    /// One document exercising every M2-modelled field: full [spec]/[provenance]/
    /// [binding]/[defaults]/[output] surface, a composite object key carrier, a
    /// non-standard quote_char (a carrier, D-054), mixed restrict_to (D-057), an
    /// authored-empty declared_domain (D-071), as_attribute (D-068), an
    /// authored-equals-default value, a value-bin ordinal with order, a deferred
    /// scale carrier (D-010), and a parked attribute (D-049).
    /// </summary>
    public const string KitchenSink = """
        [spec]
        version = 1
        schema_fingerprint = "sha256:abc123"
        cxt_output_fingerprint = "sha256:def456"
        dat_output_fingerprint = "sha256:7890ab"
        description = "kitchen sink"

        [provenance]
        author = "Constantinos Orphanides"
        created_at = 2026-05-09T10:00:00Z
        source_url = "https://example.org/data"
        source_hash = "sha256:feed"
        derived_from = "fcabedrock-v2/example.bed"
        notes = "unicode café 日本"

        [binding]
        shape = "wide"
        encoding = "utf-8"
        delimiter = ";"
        quote_char = "'"
        has_header = true
        locale = "en-GB"
        missing_token = ""

        [binding.object_key]
        mode = "composite"
        columns = ["customer_id", "session_id"]
        aggregate = "union"

        [defaults]
        include = true
        missing_policy = "skip"
        unknown_value_policy = "warn"
        duplicate_object_policy = "dedupe"
        ordinal_direction = "le"
        ordinal_boundary = "strict"

        [output]
        bin_label_unicode = true

        [output.cxt]
        line_endings = "crlf"
        trailing_newline = false
        size_advisory_bytes = 1073741824

        [output.dat]
        line_endings = "lf"
        base_index = 0
        nonempty_line_trailing_space = true
        empty_line_trailing_space = true

        [[attribute]]
        name = "bruises?"
        source = { kind = "column", name = "bruises?", value_type = "string" }
        description = "weird name, bound by header"
        include = true
        declared_domain = ["t", "f"]
        value_labels = { t = "true", f = "false", "x y" = "spaced" }
        missing_policy = "as_attribute"
        unknown_value_policy = "include"
        discretizer = { kind = "identity" }
        scale = { kind = "dichotomic", true_value = "t" }

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 1, value_type = "number" }
        restrict_to = ["Bachelors", { from = 10, to = 20 }, { from = 90 }, { to = 5 }, {}]
        discretizer = { kind = "manual_cuts", cuts = [30, 40.5, 5e1], ends = "closed" }
        scale = { kind = "ordinal", direction = "ge", boundary = "inclusive", drop_top = true }

        [[attribute]]
        name = "education"
        source = { kind = "column", index = 2 }
        declared_domain = []
        discretizer = { kind = "identity" }
        scale = { kind = "ordinal", order = ["Pre-Uni", "Undergrad", "Postgrad"] }

        [[attribute]]
        name = "grade"
        source = { kind = "column", index = 3 }
        discretizer = { kind = "identity" }
        scale = { kind = "interordinal" }

        [[attribute]]
        name = "parked"
        source = { kind = "column", index = 4 }
        include = false
        declared_domain = ["a"]
        discretizer = { kind = "ordered_cuts", order = ["a", "b"], cuts = ["b"], ends = "open" }
        scale = { kind = "nominal" }
        """;

    /// <summary>
    /// §19.4 EMAGE trimmed to one attribute: exercises the D-070 recognized-deferred
    /// discretizer reject (value_groups carries no parameters into the document).
    /// </summary>
    public const string EmageDeferredKind = """
        [spec]
        version = 1

        [binding]
        shape = "wide"

        [[attribute]]
        name = "tissue"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "value_groups", groups = [{ label = "head", values = ["brain", "eye"] }], unmatched = "skip" }
        scale = { kind = "nominal" }
        """;
}
