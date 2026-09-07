namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The two data-dependent calibration kinds the observed-domain spec does not cover: <b>include</b>
/// recovery and <b>value-groups pass-through</b>.
/// <para>
/// All three discover their column set from the data, and all three are the kinds a real spec
/// reaches for when the author does not know every value in advance — but they discover it in
/// different ways, and the difference is what these specs isolate. An <em>observed</em> domain starts
/// empty and takes what it finds; <b>include</b> starts from a declared prefix and appends the rest,
/// so the retained order is the declared values followed by the newly discovered ones in
/// first-observation order; <b>pass-through</b> keeps its authored groups and adds one bin per
/// unmatched distinct raw value, which is the case that always reports
/// <c>ValueGroupsPassthroughDataDependent</c> because the column set depends on this input whatever
/// it happens to contain.
/// </para>
/// <para>
/// Both run over <c>c0</c>, an eight-value domain, so the outcome is small and hand-checkable while
/// the <em>pass</em> is a full working-tier read. That is deliberate: the cost being measured is the
/// pass, not the size of what it discovers.
/// </para>
/// </summary>
internal static class W16RecoverySpecs
{
    /// <summary>
    /// <c>include</c> recovery: one declared value, and every other value in the column joins the
    /// domain during calibration.
    /// </summary>
    public static string Include { get; } =
        $$"""
        # W16 include: the declared prefix is deliberately one value, so calibration has to recover
        # the other seven. The retained domain is the declared values followed by the discovered
        # ones in first-observation order.
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true
        delimiter = ","
        missing_token = "?"

        [[attribute]]
        name = "c0"
        source = { kind = "column", index = {{W16Corpus.ColCategoricalFirst}} }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["{{FirstDeclared}}"]
        unknown_value_policy = "include"

        """;

    /// <summary>
    /// <c>value_groups</c> with <c>unmatched = "passthrough"</c>: one authored group, and every
    /// unmatched distinct raw value becomes its own bin.
    /// </summary>
    public static string Passthrough { get; } =
        $$"""
        # W16 pass-through: one authored group over two values, and the remaining six values each
        # become their own bin. The column set therefore depends on the data, which is exactly what
        # ValueGroupsPassthroughDataDependent reports on every run of this kind.
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true
        delimiter = ","
        missing_token = "?"

        [[attribute]]
        name = "c0"
        source = { kind = "column", index = {{W16Corpus.ColCategoricalFirst}} }
        discretizer = { kind = "value_groups", unmatched = "passthrough", groups = [{ label = "pair", values = ["{{FirstDeclared}}", "{{SecondDeclared}}"] }] }
        scale = { kind = "nominal" }

        """;

    /// <summary>The one value the include spec declares up front.</summary>
    public static string FirstDeclared => W16Corpus.StandardDomain[0];

    /// <summary>The second value the pass-through spec's authored group carries.</summary>
    public static string SecondDeclared => W16Corpus.StandardDomain[1];
}
