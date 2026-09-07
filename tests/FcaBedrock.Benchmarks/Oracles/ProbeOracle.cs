using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// What each probe outcome must actually be.
/// <para>
/// The three outcomes are checked by their <em>distinguishing</em> facts rather than by "it did not
/// throw", because the difference between them is the whole point: a complete draft, a truncated
/// draft that carries the recovery policy making it still usable, and a guard breach that yields no
/// draft at all. A benchmark that timed all three without telling them apart could report a
/// throughput for a probe that had silently stopped discovering anything.
/// </para>
/// </summary>
internal static class ProbeOracle
{
    /// <summary>A probe that discovered every value: a complete draft, with nothing truncated.</summary>
    public static void RequireComplete(Diagnosed<SpecDocument> result, int expectedAttributes, string what)
    {
        var draft = RequireDraft(result, what);

        if (draft.Attributes.Count != expectedAttributes)
        {
            throw new InvalidOperationException(
                $"{what}: the draft has {draft.Attributes.Count} attributes, expected {expectedAttributes}.");
        }

        if (result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.ProbeDomainTruncated))
        {
            throw new InvalidOperationException($"{what}: the draft truncated, but this case must not.");
        }

        // A complete draft authors its domains; leaving one absent would silently ask a later
        // conversion to rediscover it.
        if (!draft.Attributes.Any(attribute => attribute.DeclaredDomain is { Count: > 0 }))
        {
            throw new InvalidOperationException($"{what}: the draft authored no declared domain.");
        }
    }

    /// <summary>
    /// A probe that hit the per-attribute retention limit: still a usable draft, and one that says
    /// so — the truncated attribute carries its retained prefix plus the <c>include</c> policy that
    /// lets a conversion of this draft recover the complete schema (D-108).
    /// </summary>
    public static void RequireTruncated(Diagnosed<SpecDocument> result, int expectedAttributes, string what)
    {
        var draft = RequireDraft(result, what);

        if (draft.Attributes.Count != expectedAttributes)
        {
            throw new InvalidOperationException(
                $"{what}: the draft has {draft.Attributes.Count} attributes, expected {expectedAttributes}.");
        }

        if (!result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.ProbeDomainTruncated))
        {
            throw new InvalidOperationException($"{what}: nothing truncated, but this case must truncate.");
        }

        var recovering = draft.Attributes.Count(
            attribute => attribute.UnknownValuePolicy == UnknownValuePolicy.Include);
        if (recovering == 0)
        {
            throw new InvalidOperationException(
                $"{what}: a truncated attribute carries no recovery policy, so its draft would lose the tail.");
        }
    }

    /// <summary>
    /// A probe that breached an aggregate guard: <b>no draft</b>, and the breach reported. A partial
    /// draft that read as complete is precisely what the guard exists to prevent (D-110).
    /// </summary>
    public static void RequireGuardBreach(Diagnosed<SpecDocument> result, string what)
    {
        if (result.TryGetValue(out _))
        {
            throw new InvalidOperationException($"{what}: a draft was produced, but the guard must yield none.");
        }

        if (!result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.ProbeLimitExceeded))
        {
            throw new InvalidOperationException(
                $"{what}: no guard breach was reported: {ConversionPipeline.Describe(result.Diagnostics)}");
        }
    }

    private static SpecDocument RequireDraft(Diagnosed<SpecDocument> result, string what)
    {
        if (!result.TryGetValue(out var draft))
        {
            throw new InvalidOperationException(
                $"{what}: no draft was produced: {ConversionPipeline.Describe(result.Diagnostics)}");
        }

        var blocking = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            .ToList();
        return blocking.Count == 0
            ? draft
            : throw new InvalidOperationException($"{what}: {ConversionPipeline.Describe(blocking)}");
    }
}
