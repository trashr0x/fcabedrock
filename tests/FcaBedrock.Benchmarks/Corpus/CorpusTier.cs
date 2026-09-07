namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The input-record tiers the approved M8 corpus definition fixes for the synthetic families. A
/// tier names a <b>row count</b>, never a byte size or a duration: the plan's target is input
/// records (D-007 — roughly 7.3M and 73M against the ~732k EMAGE workload), and every derived
/// denominator in a report is computed from the prepared catalog rather than assumed.
/// </summary>
internal enum CorpusTier
{
    /// <summary>
    /// 1,000 input records. The approved lower size of the Ads-width family, whose 1,559 columns
    /// make even a thousand rows a wide-geometry case; it is a <see cref="Small"/>-category tier.
    /// </summary>
    Micro,

    /// <summary>10,000 input records. The default, hand-checkable, always-prepared tier.</summary>
    Small,

    /// <summary>730,000 input records — the chosen working baseline, not a claim about a typical user.</summary>
    Working,

    /// <summary>7,300,000 input records: 10x the motivating EMAGE workload.</summary>
    Scale7M,

    /// <summary>73,000,000 input records: 100x the motivating EMAGE workload.</summary>
    Scale73M,

    /// <summary>
    /// An externally acquired corpus whose size is a fact about the download rather than a choice.
    /// Its record count is <b>measured</b> at preparation and recorded in the catalog; nothing here
    /// projects it.
    /// </summary>
    External,
}

/// <summary>Row counts, canonical names, and the benchmark category each tier belongs to.</summary>
internal static class CorpusTiers
{
    /// <summary>Every tier, in ascending size order; <see cref="CorpusTier.External"/> last.</summary>
    public static IReadOnlyList<CorpusTier> All { get; } =
    [
        CorpusTier.Micro, CorpusTier.Small, CorpusTier.Working,
        CorpusTier.Scale7M, CorpusTier.Scale73M, CorpusTier.External,
    ];

    /// <summary>
    /// The exact number of input records a tier generates. <see cref="CorpusTier.External"/> has
    /// none — its size is measured, not declared — and asking for it is a programmer error.
    /// </summary>
    public static long Records(CorpusTier tier) => tier switch
    {
        CorpusTier.Micro => 1_000L,
        CorpusTier.Small => 10_000L,
        CorpusTier.Working => 730_000L,
        CorpusTier.Scale7M => 7_300_000L,
        CorpusTier.Scale73M => 73_000_000L,
        CorpusTier.External => throw new ArgumentOutOfRangeException(
            nameof(tier), tier, "An external corpus's record count is measured at preparation, not declared."),
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown corpus tier."),
    };

    /// <summary>The stable lowercase token used in file names, catalog keys, and CLI arguments.</summary>
    public static string Token(CorpusTier tier) => tier switch
    {
        CorpusTier.Micro => "micro",
        CorpusTier.Small => "small",
        CorpusTier.Working => "working",
        CorpusTier.Scale7M => "scale7m",
        CorpusTier.Scale73M => "scale73m",
        CorpusTier.External => "external",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown corpus tier."),
    };

    /// <summary>
    /// The benchmark tier category a case of this tier must carry.
    /// <para>
    /// <see cref="CorpusTier.Micro"/> is <c>Small</c>-category work: it is fast, generated from the
    /// same pinned arithmetic as every other synthetic case, and not a target-scale claim.
    /// <see cref="CorpusTier.External"/> is <b>not</b>, and has its own category — not because it
    /// is large (it is not) but because preparing it depends on a third-party host, so a routine
    /// run must be able to complete without it.
    /// </para>
    /// </summary>
    public static string Category(CorpusTier tier) => tier switch
    {
        CorpusTier.Micro or CorpusTier.Small => Configuration.BenchmarkCategories.Small,
        CorpusTier.Working => Configuration.BenchmarkCategories.Working,
        CorpusTier.Scale7M or CorpusTier.Scale73M => Configuration.BenchmarkCategories.Scale,
        CorpusTier.External => Configuration.BenchmarkCategories.External,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown corpus tier."),
    };

    /// <summary>Parses a <see cref="Token"/>; case-insensitive, since it arrives from a command line.</summary>
    public static bool TryParse(string token, out CorpusTier tier)
    {
        ArgumentNullException.ThrowIfNull(token);
        foreach (var candidate in All)
        {
            if (string.Equals(Token(candidate), token, StringComparison.OrdinalIgnoreCase))
            {
                tier = candidate;
                return true;
            }
        }

        tier = default;
        return false;
    }
}
