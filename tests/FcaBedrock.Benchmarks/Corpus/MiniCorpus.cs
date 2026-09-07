using FcaBedrock.Core.Spec;
using FcaBedrock.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// One immutable v2 mini fixture, with the binding the v2 <c>.bed</c> never recorded.
/// <para>
/// The fixtures are <b>read-only evidence</b> of what v2 produced (P-9): nothing here writes,
/// copies, or normalizes them, and their expected outputs are used as byte oracles exactly as
/// checked in. The binding table is deliberately small and local — it names only the cases these
/// benchmarks use, rather than reaching into a test project.
/// </para>
/// </summary>
internal sealed record MiniCase(string Family, string Variant, BindingSection Binding)
{
    /// <summary>A variant may reuse another variant's <c>.data</c>; defaults to its own.</summary>
    public string DataVariant { get; init; } = Variant;

    /// <summary>The discrete-vs-progressive scaling mode the v2 <c>.bed</c> never recorded.</summary>
    public ScalingMode ScalingMode { get; init; } = ScalingMode.Discrete;

    /// <summary>The fixture directory.</summary>
    public string Directory => Path.Combine(BenchmarkPaths.V2FixtureRoot, Family);

    /// <summary>The v2 spec file.</summary>
    public string BedPath => Path.Combine(Directory, Variant + ".bed");

    /// <summary>The v2 data file.</summary>
    public string DataPath => Path.Combine(Directory, DataVariant + ".data");

    /// <summary>The immutable expected <c>.dat</c> bytes v2 produced.</summary>
    public string ExpectedDatPath => Path.Combine(Directory, "expected", Variant + ".dat");

    /// <summary>The immutable expected <c>.cxt</c> bytes v2 produced.</summary>
    public string ExpectedCxtPath => Path.Combine(Directory, "expected", Variant + ".cxt");

    /// <inheritdoc/>
    public override string ToString() => Variant;
}

/// <summary>The v2 mini fixtures this suite converts.</summary>
internal static class MiniCorpus
{
    /// <summary>mini-mushroom: comma-delimited, header, wide.</summary>
    public static MiniCase Mushroom { get; } =
        new("mini-mushroom", "mini-mushroom", Wide(',', hasHeader: true));

    /// <summary>mini-adult: comma-delimited, header, wide — the numeric/cut-bearing mini.</summary>
    public static MiniCase Adult { get; } =
        new("mini-adult", "mini-adult", Wide(',', hasHeader: true));

    /// <summary>Every mini case in this suite, in a stable order.</summary>
    public static IReadOnlyList<MiniCase> All { get; } = [Mushroom, Adult];

    /// <summary>True when the immutable fixture tree is present beside this checkout.</summary>
    public static bool FixturesPresent => System.IO.Directory.Exists(BenchmarkPaths.V2FixtureRoot);

    private static BindingSection Wide(char delimiter, bool hasHeader) =>
        new(SourceShape.Wide, Encoding: null, delimiter, QuoteChar: null, hasHeader,
            Locale: null, MissingToken: null, Ordering: null, Columns: null, ObjectKey: null);
}
