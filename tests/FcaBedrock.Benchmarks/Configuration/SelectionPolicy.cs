using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;

namespace FcaBedrock.Benchmarks.Configuration;

/// <summary>
/// Which cases a bare invocation may run.
/// <para>
/// BenchmarkDotNet owns filtering; this adds exactly one policy on top of it, for two cases that
/// must never start by accident. The <see cref="BenchmarkCategories.Scale"/> cases read 7.3M and
/// 73M records, so running them unintentionally costs hours of machine time and produces results
/// nobody asked for. The <see cref="BenchmarkCategories.External"/> cases are quick, but their
/// corpus is acquired from a third-party host, so requiring them turns an unrelated outage into a
/// failure of whatever run happened to select them. Both are therefore <b>opt-in by category and
/// by nothing else</b> — a broad name filter such as <c>--filter *</c>, the case's own name, and a
/// surface category all fail to reach them — and, when no category is named at all, the default
/// selection is <see cref="BenchmarkCategories.Small"/>.
/// </para>
/// <para>
/// Opting in is not the same as skipping: a selected case whose corpus is absent is still a hard
/// failure. This decides what a run is <em>asked</em> to measure, never what it is allowed to
/// quietly not measure.
/// </para>
/// <para>
/// The policy is a pure function of the command line and a case's categories, so it is decided once
/// and tested directly rather than inferred from a run's output.
/// </para>
/// </summary>
internal sealed record SelectionPolicy(
    bool CategorySelectionPresent,
    bool ScaleRequested,
    bool WorkingRequested,
    bool ExternalRequested,
    bool JobRequested)
{
    /// <summary>
    /// True when the selection names a tier whose operations are measured in seconds or minutes
    /// rather than milliseconds — the working baseline or either target scale.
    /// <para>
    /// It decides the job shape, and only that. A Throughput job's pilot stage exists to find how
    /// many invocations fit in an interval; for an operation that already takes seconds it has
    /// nothing to find and would only multiply the run.
    /// </para>
    /// <para>
    /// <see cref="BenchmarkCategories.External"/> is deliberately absent: the acquired corpus is
    /// about 32,000 records, which is Small-sized work. It is opt-in because of where its bytes
    /// come from, not because of how long it takes, so it keeps the fresh-iteration job.
    /// </para>
    /// </summary>
    public bool LongRunRequested => ScaleRequested || WorkingRequested;

    /// <summary>BenchmarkDotNet's two category options; both are honoured as an explicit selection.</summary>
    private static readonly string[] CategoryOptions = ["--anycategories", "--allcategories"];

    private const string JobOption = "--job";

    /// <summary>Derives the policy from a raw command line.</summary>
    public static SelectionPolicy FromArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var categorySelection = false;
        var scale = false;
        var working = false;
        var external = false;
        var job = false;

        for (var i = 0; i < arguments.Count; i++)
        {
            var (name, inlineValue) = Split(arguments[i]);

            if (string.Equals(name, JobOption, StringComparison.OrdinalIgnoreCase))
            {
                job = true;
                continue;
            }

            if (!CategoryOptions.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            categorySelection = true;

            // Values may arrive inline (--anyCategories=Scale) or as the following arguments, since
            // the option is a list. Everything up to the next option is a value.
            if (inlineValue is not null)
            {
                scale |= Mentions(inlineValue, BenchmarkCategories.Scale);
                working |= Mentions(inlineValue, BenchmarkCategories.Working);
                external |= Mentions(inlineValue, BenchmarkCategories.External);
                continue;
            }

            for (var value = i + 1; value < arguments.Count && !arguments[value].StartsWith('-'); value++)
            {
                scale |= Mentions(arguments[value], BenchmarkCategories.Scale);
                working |= Mentions(arguments[value], BenchmarkCategories.Working);
                external |= Mentions(arguments[value], BenchmarkCategories.External);
            }
        }

        return new SelectionPolicy(categorySelection, scale, working, external, job);
    }

    /// <summary>Whether a case carrying <paramref name="categories"/> may run under this policy.</summary>
    public bool Includes(IReadOnlyList<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        // The two opt-in tiers are checked first and answer on their own: naming some *other*
        // category lifts the Small default, but it must never reach these. A case carries exactly
        // one tier category, so the two branches cannot both apply.
        //
        // BenchmarkDotNet compares categories case-insensitively, so this must too.
        if (Has(categories, BenchmarkCategories.Scale))
        {
            return ScaleRequested;
        }

        if (Has(categories, BenchmarkCategories.External))
        {
            return ExternalRequested;
        }

        return CategorySelectionPresent || Has(categories, BenchmarkCategories.Small);
    }

    private static bool Has(IReadOnlyList<string> categories, string category) =>
        categories.Contains(category, StringComparer.OrdinalIgnoreCase);

    // A category list value may itself be comma-separated.
    private static bool Mentions(string value, string category) =>
        value.Split(',').Any(part => string.Equals(part.Trim(), category, StringComparison.OrdinalIgnoreCase));

    private static (string Name, string? InlineValue) Split(string argument)
    {
        var separator = argument.IndexOf('=', StringComparison.Ordinal);
        return separator < 0
            ? (argument, null)
            : (argument[..separator], argument[(separator + 1)..]);
    }
}

/// <summary>Applies <see cref="SelectionPolicy"/> as an ordinary BenchmarkDotNet filter.</summary>
internal sealed class TierSelectionFilter(SelectionPolicy policy) : IFilter
{
    /// <inheritdoc/>
    public bool Predicate(BenchmarkCase benchmarkCase)
    {
        ArgumentNullException.ThrowIfNull(benchmarkCase);
        return policy.Includes(benchmarkCase.Descriptor.Categories);
    }
}
