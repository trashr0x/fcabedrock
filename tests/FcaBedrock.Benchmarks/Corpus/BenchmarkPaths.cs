namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// Where the suite reads its inputs and writes its bulk artifacts.
/// <para>
/// Generated corpora, produced outputs, and BenchmarkDotNet results are <b>bulk evidence and never
/// enter Git</b>; only generator definitions, specs, attribution, metadata contracts, and tiny
/// expectations are committed. The default root therefore sits under the repository's already
/// ignored <c>artifacts/</c> directory, and an operator can redirect it wholesale with
/// <see cref="RootVariable"/> — for a scratch volume with room for a 73M-record tier, say.
/// </para>
/// <para>
/// <b>Safety.</b> The suite only ever creates, reads, and deletes files beneath the resolved root,
/// and every generated file name is derived from a corpus case rather than from user input, so a
/// preparation run cannot overwrite an arbitrary operator file. A redirected root must be an
/// absolute path, so a relative value can never be resolved against whatever the current directory
/// happens to be.
/// </para>
/// </summary>
internal static class BenchmarkPaths
{
    /// <summary>The environment variable that redirects the bulk-artifact root.</summary>
    public const string RootVariable = "FCABEDROCK_BENCH_ROOT";

    private static readonly Lazy<string> LazyRepositoryRoot = new(FindRepositoryRoot);

    private static readonly Lazy<string> LazyRoot = new(ResolveRoot);

    /// <summary>The repository root — the directory holding <c>FcaBedrock.slnx</c>.</summary>
    public static string RepositoryRoot => LazyRepositoryRoot.Value;

    /// <summary>The immutable v2 fixture root. Read-only: nothing here is ever written (P-9).</summary>
    public static string V2FixtureRoot => Path.Combine(RepositoryRoot, "fixtures", "v2");

    /// <summary>The bulk-artifact root: <c>$FCABEDROCK_BENCH_ROOT</c>, else <c>&lt;repo&gt;/artifacts/bench</c>.</summary>
    public static string Root => LazyRoot.Value;

    /// <summary>Prepared corpus inputs (generated data and their specs).</summary>
    public static string CorpusDirectory => Path.Combine(Root, "corpus");

    /// <summary>Per-run benchmark outputs, deleted after each iteration's validation.</summary>
    public static string OutputDirectory => Path.Combine(Root, "output");

    /// <summary>BenchmarkDotNet's own artifact tree (logs, CSV, JSON, Markdown).</summary>
    public static string ResultsDirectory => Path.Combine(Root, "results");

    /// <summary>
    /// Where the grouping/calibration backend spills its sort-merge runs.
    /// <para>
    /// Production defaults this to the OS temporary directory, which on a developer machine is on
    /// the system volume — a different device from the corpora and outputs, quite possibly with far
    /// less room, and on Windows one that a security scanner watches closely. A scale run would then
    /// be measuring two volumes at once, and a spill of several gigabytes could fill the drive the
    /// operating system is running from.
    /// </para>
    /// <para>
    /// So the suite points the backend at its own root through the existing production seam
    /// (<c>GroupingOptions.TempDirectory</c>, the same one the CLI's <c>--temp-dir</c> uses). Every
    /// large byte a measured run touches — input, spool, output, and result — then lives on one
    /// identified volume, which is what makes the storage a stated condition of the measurement
    /// rather than an accident of where a temp directory happened to be.
    /// </para>
    /// </summary>
    public static string SpoolDirectory => Path.Combine(Root, "spool");

    /// <summary>Creates the bulk-artifact directories if they are absent.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(CorpusDirectory);
        Directory.CreateDirectory(OutputDirectory);
        Directory.CreateDirectory(ResultsDirectory);
        Directory.CreateDirectory(SpoolDirectory);
    }

    private static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable(RootVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(RepositoryRoot, "artifacts", "bench");
        }

        var trimmed = configured.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidOperationException(
                $"{RootVariable} must be an absolute path; it was '{trimmed}'.");
        }

        return Path.GetFullPath(trimmed);
    }

    // Never a fixed `..` hop count: the answer is wherever the solution file actually is, and a
    // failure names every directory that was probed. (The same rule the CLI test suite uses.)
    private static string FindRepositoryRoot()
    {
        var probed = new List<string>();
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            probed.Add(directory.FullName);
            if (File.Exists(Path.Combine(directory.FullName, "FcaBedrock.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"FcaBedrock.slnx was not found above '{AppContext.BaseDirectory}'; probed: {string.Join(", ", probed)}");
    }
}
