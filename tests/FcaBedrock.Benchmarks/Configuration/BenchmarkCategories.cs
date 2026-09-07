namespace FcaBedrock.Benchmarks.Configuration;

/// <summary>
/// The BenchmarkDotNet categories the suite is selected through.
/// <para>
/// Two axes, deliberately separate. A <b>tier</b> category says how much data a case reads, and is
/// what the default-selection and opt-in rules act on. A <b>surface</b> category says which
/// production path it measures, so a reader can ask for "every source-drain case" without knowing
/// class names. Filtering itself is BenchmarkDotNet's job — these are just the vocabulary.
/// </para>
/// </summary>
internal static class BenchmarkCategories
{
    // ---- tiers -----------------------------------------------------------------------------

    /// <summary>10,000-record cases. The default selection: fast, hand-checkable, always prepared.</summary>
    public const string Small = "Small";

    /// <summary>730,000-record cases — the working baseline. Opt in by naming this category.</summary>
    public const string Working = "Working";

    /// <summary>
    /// 7.3M and 73M-record cases. Never selected implicitly, however broad the name filter: a run
    /// of these costs hours and needs prepared corpora and controlled hardware.
    /// </summary>
    public const string Scale = "Scale";

    // ---- surfaces --------------------------------------------------------------------------

    /// <summary>Source-session drain: schema plus every cleaned record, no binding or conversion.</summary>
    public const string Source = "Source";

    /// <summary>Emission and export of a fixed plan to a real artifact.</summary>
    public const string Convert = "Convert";

    /// <summary>Conversion of the immutable v2 mini fixtures, validated against their v2 bytes.</summary>
    public const string Mini = "Mini";

    /// <summary>Triple (subject-predicate-value) input, in either physical layout.</summary>
    public const string Triple = "Triple";

    /// <summary>Data-reading calibration: observed domains, min/max, and count-sensitive quantiles.</summary>
    public const string Calibrate = "Calibrate";

    /// <summary>The pure planner: no data I/O at all.</summary>
    public const string Plan = "Plan";

    /// <summary>Discovery: one set-based observation pass producing a draft spec.</summary>
    public const string Probe = "Probe";

    /// <summary>The shared grouping/spool backend, and the two internal knobs M8 may tune.</summary>
    public const string Grouping = "Grouping";

    /// <summary>
    /// The in-process CLI host: one complete command at the argv boundary, through the real
    /// publication transaction. Never installed-command latency.
    /// </summary>
    public const string Cli = "Cli";

    /// <summary>
    /// The input and output hashing wrappers, measured as wrapped/unwrapped pairs. These are
    /// component experiments, not a product switch: neither hash can be turned off.
    /// </summary>
    public const string Hash = "Hash";

    /// <summary>Every tier category, so the selection policy can recognize one.</summary>
    public static IReadOnlyList<string> Tiers { get; } = [Small, Working, Scale];
}
