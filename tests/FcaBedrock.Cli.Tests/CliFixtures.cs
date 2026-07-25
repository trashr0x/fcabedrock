namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Small hand-authored specs and data sources for the argv-boundary tests. They are
/// deliberately tiny: this slice tests the CLI shell and the validate vertical, not
/// conversion semantics, which the library suites already own.
/// </summary>
internal static class CliFixtures
{
    /// <summary>Index-bound and fully declared: resolves with or without a schema.</summary>
    public const string IndexBoundSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>
    /// Bound by header name, so it can only resolve once a schema exists — the difference
    /// between <c>validate SPEC</c> and <c>validate SPEC DATA</c>.
    /// </summary>
    public const string NameBoundSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", name = "colour" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>Name-bound to a header column the data source does not have.</summary>
    public const string MissingColumnSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", name = "not-a-column" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>Valid, but with a <c>restrict_to</c> value outside the declared domain: one Warning, no Error.</summary>
    public const string WarningOnlySpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        restrict_to = ["blue"]
        """;

    /// <summary>A scale with no discretizer: <c>AttributeScalingMissing</c>, an Error at resolve.</summary>
    public const string ErrorSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        scale = { kind = "nominal" }
        declared_domain = ["red"]
        """;

    /// <summary>Triple-shaped, index-addressed roles.</summary>
    public const string TripleSpec = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "unordered"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "colour"
        source = { kind = "predicate", name = "colour" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>A wide source whose header matches <see cref="NameBoundSpec"/>.</summary>
    public const string WideData = "colour,size\nred,1\ngreen,2\n";

    /// <summary>A triple source.</summary>
    public const string TripleData = "o1,colour,red\no2,colour,green\n";

    /// <summary>
    /// A well-formed header followed by rows that a real conversion would reject: an
    /// unparseable value under a numeric attribute, an unknown domain value, and a ragged
    /// row. Validate never reads them.
    /// </summary>
    public const string WideDataWithHostileRows = "colour,size\nnot-a-colour,x\npurple\n,,\n";

    // ---- the read-only data commands (plan / stats / fingerprint) -----------------------
    //
    // These are still deliberately tiny, but each one is chosen so that a COMPLETE expected
    // report can be authored by hand: every planned column, calibration outcome, restriction
    // entry, and count below is derivable from the spec text and the two or three data rows
    // beside it, without running the production renderer.

    /// <summary>
    /// One spec exercising every structural shape the plan report can print: data-calibrated
    /// numeric cuts with open ends, categorical cut bins with open ends, an ordinal threshold
    /// (operator + the open-end <c>all</c>), an observed domain, an authored-empty domain
    /// extended by <c>include</c>, discovered pass-through bins, and a filter-only numeric
    /// restriction carrying all three entry forms.
    /// </summary>
    public const string PlanRichSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 1, value_type = "number" }
        discretizer = { kind = "equal_width", bins = 2, range = "min_max" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "rank"
        source = { kind = "column", index = 2 }
        discretizer = { kind = "ordered_cuts", order = ["low", "mid", "high"], cuts = ["mid"], ends = "open" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "grade"
        source = { kind = "column", index = 2 }
        discretizer = { kind = "ordered_cuts", order = ["low", "mid", "high"], cuts = ["mid"], ends = "open" }
        scale = { kind = "ordinal", direction = "le" }

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "cat"
        source = { kind = "column", index = 3 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = []
        unknown_value_policy = "include"

        [[attribute]]
        name = "tag"
        source = { kind = "column", index = 4 }
        discretizer = { kind = "value_groups", unmatched = "passthrough", groups = [{ label = "G", values = ["a"] }] }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "gene"
        source = { kind = "column", index = 5, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [10.0], ends = "open" }
        scale = { kind = "nominal" }
        include = false
        restrict_to = [{ value = 30.0 }, { from = 1.0 }, {}]
        """;

    /// <summary>Six columns matching <see cref="PlanRichSpec"/>: <c>colour,age,rank,cat,tag,gene</c>.</summary>
    public const string PlanRichData = "colour,age,rank,cat,tag,gene\nred,10,low,x,a,5\ngreen,30,high,y,z,30\n";

    /// <summary>
    /// The legitimately empty calibration outcomes: an all-missing column observes nothing, a
    /// declared value under <c>include</c> adds nothing, and a fully grouped column discovers
    /// no pass-through bin. Empty <b>cuts</b> are deliberately absent — a cut outcome of the
    /// wrong length is a calibrator-contract violation, never a success (D-102).
    /// </summary>
    public const string PlanEmptyOutcomesSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "obs"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "inc"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["k"]
        unknown_value_policy = "include"

        [[attribute]]
        name = "pt"
        source = { kind = "column", index = 2 }
        discretizer = { kind = "value_groups", unmatched = "passthrough", groups = [{ label = "G", values = ["a"] }] }
        scale = { kind = "nominal" }
        """;

    /// <summary>One row whose first cell is the missing token, matching <see cref="PlanEmptyOutcomesSpec"/>.</summary>
    public const string PlanEmptyOutcomesData = "obs,inc,pt\n?,k,a\n";

    /// <summary>
    /// Triple sources plus every character class the line-oriented report must survive: a
    /// quote, a backslash, and a tab in an attribute name; a backslash and non-ASCII text in
    /// domain values; a non-ASCII predicate selector; and CR/LF inside a restriction entry —
    /// which is where a line break can legally appear, since a rendered <em>name</em>
    /// containing one is rejected at plan (§10.7).
    /// </summary>
    public const string PlanTripleEscapingSpec = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "unordered"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "q\"u\\b\ttab"
        source = { kind = "predicate", name = "péé" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["a\\b", "é中"]

        [[attribute]]
        name = "filt"
        source = { kind = "predicate", name = "f" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["k"]
        include = false
        restrict_to = ["line1\nline2\ttab"]
        """;

    /// <summary>A triple source matching <see cref="PlanTripleEscapingSpec"/>.</summary>
    public const string PlanTripleEscapingData = "o1,péé,a\\b\no2,péé,é中\no1,f,line1\n";

    /// <summary>
    /// Declared <c>red</c>/<c>blue</c> over data holding <c>red</c> and <c>green</c>: one
    /// object crosses nothing and one column is never crossed, so the two degenerate counts are
    /// both non-zero and independently checkable by eye.
    /// </summary>
    public const string StatsSparseSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "blue"]
        unknown_value_policy = "skip"
        """;

    /// <summary>Two rows, one of which carries a value outside <see cref="StatsSparseSpec"/>'s domain.</summary>
    public const string StatsSparseData = "colour,size\nred,1\ngreen,2\n";

    /// <summary>Every attribute excluded, so the plan has zero columns (<c>NoFormalAttributes</c>).</summary>
    public const string StatsNoColumnsSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red"]
        include = false
        """;

    /// <summary>A header and no data rows at all: zero objects.</summary>
    public const string HeaderOnlyWideData = "colour,size\n";

    /// <summary>
    /// A wide <c>dedupe</c> object key — the spill-capable grouping backend triple
    /// <c>unordered</c> also runs on, so a run over it actually engages the machinery
    /// <c>--temp-dir</c> configures.
    /// </summary>
    public const string DedupeSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [defaults]
        duplicate_object_policy = "dedupe"

        [binding.object_key]
        mode = "column"
        column = 0

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>Two rows sharing an object key, so <see cref="DedupeSpec"/> actually merges.</summary>
    public const string DedupeData = "id,colour\no1,red\no1,green\no2,red\n";

    /// <summary>
    /// Count-sensitive calibration over interleaved triple input: <c>equal_frequency</c> needs
    /// each distinct <c>(subject, predicate, value)</c> counted once, which under
    /// <c>ordering = "unordered"</c> forces the calibrator's grouped second pass — the
    /// <b>calibration</b> side of the machinery <c>--temp-dir</c> configures.
    /// </summary>
    public const string TripleCountSensitiveSpec = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "unordered"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "age"
        source = { kind = "predicate", name = "age", value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 2 }
        scale = { kind = "nominal" }
        """;

    /// <summary>Four subjects with one numeric observation each, matching <see cref="TripleCountSensitiveSpec"/>.</summary>
    public const string TripleCountSensitiveData = "o1,age,10\no2,age,20\no3,age,30\no4,age,40\n";
}
