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

    // ---- calibrate (the freeze vertical) --------------------------------------------------
    //
    // Every discovery-class calibration mode warns whenever it executes, zero discoveries
    // included, so a COMBINED fixture could never produce an empty-stderr or single-warning
    // row. Each mapping fixture below is therefore deliberately ISOLATED to one attribute, so
    // each row's stderr is an independent literal rather than a filtered subset.

    /// <summary>
    /// The one wide source the calibrate suite shares. Column 0 holds three distinct strings in
    /// first-observation order <c>red</c>, <c>green</c>, <c>blue</c>; column 1 holds three finite
    /// numerics with min 10, max 30, and non-zero spread.
    /// </summary>
    public const string CalibrateWideData = "colour,age\nred,10\ngreen,20\nblue,30\n";

    /// <summary>
    /// One numeric attribute whose cuts are calibrated from the data. A successful
    /// <c>min_max</c> calibration emits <b>no</b> diagnostic at all, so this is the fixture a
    /// row needs when its stderr oracle is "empty".
    /// </summary>
    public const string CalibrateCutsSpec = """
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
        """;

    /// <summary>An omitted <c>declared_domain</c> under a consuming discretizer: one <c>ObservedDomainUsed</c>.</summary>
    public const string CalibrateObservedSpec = """
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
        """;

    /// <summary>An authored domain extended by <c>include</c>: one <c>UnknownValuePolicyInclude</c>.</summary>
    public const string CalibrateIncludeSpec = """
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
        unknown_value_policy = "include"
        """;

    /// <summary>Pass-through discovery: one <c>ValueGroupsPassthroughDataDependent</c>.</summary>
    public const string CalibratePassthroughSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "value_groups", unmatched = "passthrough", groups = [{ label = "G", values = ["red"] }] }
        scale = { kind = "nominal" }
        """;

    /// <summary>
    /// An observed domain over an all-missing column, so the outcome is legitimately empty. The
    /// <c>missing_policy = "as_attribute"</c> is deliberate: it guarantees exactly one planned
    /// column, so the row's only stderr line is the calibration Warning and not also
    /// <c>NoFormalAttributes</c>.
    /// </summary>
    public const string CalibrateEmptyObservedSpec = """
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
        missing_policy = "as_attribute"
        """;

    /// <summary>An <c>include</c> attribute whose only observed value is already declared: no additions.</summary>
    public const string CalibrateEmptyIncludeSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "inc"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["k"]
        unknown_value_policy = "include"
        """;

    /// <summary>A pass-through attribute whose only observed value is already grouped: no bins discovered.</summary>
    public const string CalibrateEmptyPassthroughSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "pt"
        source = { kind = "column", index = 2 }
        discretizer = { kind = "value_groups", unmatched = "passthrough", groups = [{ label = "G", values = ["a"] }] }
        scale = { kind = "nominal" }
        """;

    /// <summary>
    /// One row producing three legitimately empty outcomes: an all-missing first cell observes
    /// nothing, a declared value under <c>include</c> adds nothing, and a fully grouped value
    /// discovers no pass-through bin. A distinct constant from
    /// <see cref="PlanEmptyOutcomesData"/> so the plan-report locks stay untouched.
    /// </summary>
    public const string CalibrateEmptyOutcomesData = "obs,inc,pt\n?,k,a\n";

    /// <summary>
    /// One attribute with an authored <b>empty</b> domain and the two policies the 4 × 2 matrix
    /// varies. An authored <c>[]</c> is complete (D-122 part 15), so <c>warn</c>/<c>skip</c>/
    /// <c>fail</c> read no rows at all and only <c>include</c> is data-dependent.
    /// </summary>
    public static string CalibrateAuthoredEmptySpec(string unknown, string missing) => $$"""
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
        declared_domain = []
        unknown_value_policy = "{{unknown}}"
        missing_policy = "{{missing}}"
        """;

    /// <summary>Two objects: one carrying a value the empty domain does not declare, one missing.</summary>
    public const string CalibrateAuthoredEmptyData = "colour\nred\n?\n";

    /// <summary>
    /// The root of a two-file chain: it authors <c>[binding]</c> and the <c>extends</c>
    /// reference, and no attribute of its own — so flattening is observable, and the base is a
    /// genuine publication input rather than an incidental one.
    /// </summary>
    public const string CalibrateChainRootSpec = """
        [spec]
        version = 1
        extends = "base.toml"

        [binding]
        shape = "wide"
        has_header = true
        """;

    /// <summary>The base of that chain: it holds the chain's only data-dependent attribute.</summary>
    public const string CalibrateChainBaseSpec = """
        [spec]
        version = 1

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        """;

    /// <summary>
    /// <see cref="CalibrateCutsSpec"/> with three deliberately wrong, well-formed stored values.
    /// Its calibration is a successful <c>min_max</c> and therefore silent, and it can produce no
    /// resolve diagnostic — so the three stale warnings are the run's <b>only</b> stderr.
    /// </summary>
    public const string CalibrateStaleHashesSpec = """
        [spec]
        version = 1
        schema_fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
        cxt_output_fingerprint = "sha256:1111111111111111111111111111111111111111111111111111111111111111"
        dat_output_fingerprint = "sha256:2222222222222222222222222222222222222222222222222222222222222222"

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 1, value_type = "number" }
        discretizer = { kind = "equal_width", bins = 2, range = "min_max" }
        scale = { kind = "nominal" }
        """;

    /// <summary>
    /// A matcher that wins exactly one field, on an attribute that authors no discretizer of its
    /// own. <b>Before</b> the freeze the matcher supplies <c>discretizer</c>, so it won somewhere
    /// and stays silent; the calibration is a successful <c>min_max</c> and is silent too.
    /// <b>After</b> the freeze an explicit <c>manual_cuts</c> shadows the template's only field,
    /// so the re-resolve newly emits exactly one <c>MatcherFullyShadowed</c> — the post-freeze
    /// diagnostic the composition must retain.
    /// </summary>
    public const string CalibrateShadowedMatcherSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[template]]
        id = "cuts"
        discretizer = { kind = "equal_width", bins = 2, range = "min_max" }

        [[matcher]]
        match = { name_regex = "age" }
        template = "cuts"

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 1, value_type = "number" }
        scale = { kind = "nominal" }
        """;

    // ---- fingerprint --write ------------------------------------------------------------------

    /// <summary>
    /// Fully frozen and completely silent: explicit <c>manual_cuts</c> on the numeric attribute,
    /// an explicit non-empty <c>declared_domain</c> on the string one, no <c>include</c>, no
    /// pass-through, no <c>restrict_to</c>, and no stored fingerprints. Nothing here can produce a
    /// diagnostic, so a row asserting empty stderr is asserting the fixture's whole behaviour.
    /// </summary>
    public const string FrozenSpec = """
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

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 1, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [20.0], ends = "open" }
        scale = { kind = "nominal" }
        """;

    /// <summary>Columns matching <see cref="FrozenSpec"/>.</summary>
    public const string FrozenData = "colour,age\nred,10\ngreen,30\n";

    /// <summary><see cref="FrozenSpec"/> with three deliberately wrong stored values: the report reads stale ×3.</summary>
    public const string FrozenStaleSpec = """
        [spec]
        version = 1
        schema_fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
        cxt_output_fingerprint = "sha256:1111111111111111111111111111111111111111111111111111111111111111"
        dat_output_fingerprint = "sha256:2222222222222222222222222222222222222222222222222222222222222222"

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 1, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [20.0], ends = "open" }
        scale = { kind = "nominal" }
        """;

    /// <summary>
    /// One spec per fully-frozen-gate reason (§14), each with exactly <b>one</b> included
    /// attribute so each row's stderr is a single independent literal. The set mirrors the
    /// library gate matrix; every row must <em>calibrate successfully</em> to reach the gate at
    /// all, which is what <see cref="GateData"/> is sized for.
    /// </summary>
    public static string NotFullyFrozenSpec(string reason) => $$"""
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        {{NotFullyFrozenAttribute(reason)}}
        """;

    private static string NotFullyFrozenAttribute(string reason) => reason switch
    {
        // A consuming discretizer with an OMITTED domain: ObservedDomainUsed.
        "omitted-domain" => """
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            """,

        // Still ObservedDomainUsed, NOT UnknownValuePolicyInclude: the omitted-domain
        // calibration wins, because isInclude is `!absentDomain && include`.
        "omitted-domain-include" => """
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            unknown_value_policy = "include"
            """,

        "authored-domain-include" => """
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["red"]
            unknown_value_policy = "include"
            """,

        "passthrough" => """
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "value_groups", unmatched = "passthrough", groups = [{ label = "G", values = ["red"] }] }
            scale = { kind = "nominal" }
            """,

        // The three cut modes: each calibrates SUCCESSFULLY over GateData, and a successful cut
        // calibration is silent — so these rows render the host error and nothing else.
        "equal-frequency" => """
            name = "age"
            source = { kind = "column", index = 1, value_type = "number" }
            discretizer = { kind = "equal_frequency", bins = 2 }
            scale = { kind = "nominal" }
            """,

        "equal-width-min-max" => """
            name = "age"
            source = { kind = "column", index = 1, value_type = "number" }
            discretizer = { kind = "equal_width", bins = 2, range = "min_max" }
            scale = { kind = "nominal" }
            """,

        "equal-width-percentile" => """
            name = "age"
            source = { kind = "column", index = 1, value_type = "number" }
            discretizer = { kind = "equal_width", bins = 2, range = "percentile_p1_p99" }
            scale = { kind = "nominal" }
            """,

        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown gate reason."),
    };

    /// <summary>
    /// Five rows: three distinct strings for the discovery rows, and five distinct finite
    /// numerics with non-zero spread and distinct 1st/99th percentiles — so the min/max,
    /// distinct-count, and percentile-span guards are all cleared and every cut row reaches the
    /// gate instead of failing calibration first.
    /// </summary>
    public const string GateData = "colour,age\nred,10\ngreen,20\nblue,30\nred,40\ngreen,50\n";

    /// <summary>
    /// A fully-frozen root that authors <c>extends</c> plus <c>[provenance]</c>,
    /// <c>[defaults]</c>, and <c>[output]</c> — so a write can be shown to preserve the root's
    /// <c>extends</c> and every non-<c>[spec]</c> section, and so the base is a collision input.
    /// </summary>
    public const string FrozenChainRootSpec = """
        [spec]
        version = 1
        extends = "frozen-base.toml"

        [provenance]
        author = "slice-j"
        notes = "root of a two-file chain"

        [binding]
        shape = "wide"
        has_header = true

        [defaults]
        unknown_value_policy = "skip"

        [output]
        bin_label_unicode = false
        """;

    /// <summary>The base of that chain: it contributes the chain's only attribute.</summary>
    public const string FrozenChainBaseSpec = """
        [spec]
        version = 1

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;
}
