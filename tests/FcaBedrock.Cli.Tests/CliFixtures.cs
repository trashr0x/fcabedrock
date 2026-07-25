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
}
