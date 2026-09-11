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
/// <para>
/// <b>And the facts are per attribute, because D-108 is.</b> Each untruncated non-all-missing
/// attribute authors its complete non-empty domain, and each truncated attribute authors its
/// retained prefix plus <c>unknown_value_policy = "include"</c>. An existential check — <em>some</em>
/// attribute has a domain, <em>some</em> attribute recovers — is satisfied by a draft in which
/// fifteen of sixteen columns discovered nothing, which is exactly the outcome these cases exist to
/// tell apart. So a caller states the exact set it expects, and every attribute of the draft is held
/// to it.
/// </para>
/// </summary>
internal static class ProbeOracle
{
    private static readonly HashSet<string> NoAttributes = new(StringComparer.Ordinal);

    /// <summary>
    /// A probe that discovered every value: a complete draft, with nothing truncated, no attribute
    /// carrying a recovery policy, and every attribute authoring its own non-empty domain.
    /// <para>
    /// <paramref name="allMissing"/> names the attributes whose every observed cell was missing —
    /// those legitimately author no <c>declared_domain</c> at all (§10.3/D-071), and the draft omits
    /// the key rather than writing <c>[]</c>. The exemption is for the domain alone: a named attribute
    /// observed nothing to truncate, so it carries no recovery policy either. Every current benchmark
    /// corpus declares <b>none</b>: each W16, T10 and long-text attribute has at least one present
    /// value by construction, so an absent domain anywhere is a defect rather than an exception.
    /// </para>
    /// </summary>
    public static void RequireComplete(
        Diagnosed<SpecDocument> result,
        int expectedAttributes,
        string what,
        IReadOnlyCollection<string>? allMissing = null)
    {
        var draft = RequireDraft(result, what);
        RequireAttributeCount(draft, expectedAttributes, what);

        if (result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.ProbeDomainTruncated))
        {
            throw new InvalidOperationException($"{what}: the draft truncated, but this case must not.");
        }

        RequireDomains(draft, truncated: NoAttributes, Expected(allMissing, draft, what, "all-missing"), what);
    }

    /// <summary>
    /// A probe that hit the per-attribute retention limit: still a usable draft, and one that says
    /// so — <b>every</b> attribute in <paramref name="expectedTruncated"/> carries its retained
    /// prefix plus the <c>include</c> policy that lets a conversion of this draft recover the
    /// complete schema (D-108), <b>and no other attribute does</b>.
    /// <para>
    /// The exact set matters because these cases differ in it. A scale-tier wide probe truncates
    /// several columns while a dozen others stay complete, so "at least one attribute recovered"
    /// would accept a draft that lost the tail of every column but one — a different result with
    /// the same shape, and the one a benchmark row must not silently stand for.
    /// </para>
    /// </summary>
    public static void RequireTruncated(
        Diagnosed<SpecDocument> result,
        int expectedAttributes,
        IReadOnlyCollection<string> expectedTruncated,
        string what,
        IReadOnlyCollection<string>? allMissing = null)
    {
        ArgumentNullException.ThrowIfNull(expectedTruncated);

        var draft = RequireDraft(result, what);
        RequireAttributeCount(draft, expectedAttributes, what);

        if (!result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.ProbeDomainTruncated))
        {
            throw new InvalidOperationException($"{what}: nothing truncated, but this case must truncate.");
        }

        var expected = Expected(expectedTruncated, draft, what, "truncated");
        if (expected.Count == 0)
        {
            throw new InvalidOperationException(
                $"{what}: a truncating case must name the attributes it expects to truncate.");
        }

        var missing = Expected(allMissing, draft, what, "all-missing");
        foreach (var name in expected)
        {
            if (missing.Contains(name))
            {
                throw new InvalidOperationException(
                    $"{what}: '{name}' is expected to be both truncated and all-missing, which no draft can be.");
            }
        }

        // The observed set is read off the draft rather than off the diagnostic's count: only
        // truncation authors `include` (ProbeDraft), so this is D-108's recovery half checked per
        // attribute instead of a total that says nothing about which attribute it counted.
        var observed = draft.Attributes
            .Where(attribute => attribute.UnknownValuePolicy == UnknownValuePolicy.Include)
            .Select(NameOf)
            .ToHashSet(StringComparer.Ordinal);

        var absent = expected.Where(name => !observed.Contains(name)).Order(StringComparer.Ordinal).ToList();
        if (absent.Count > 0)
        {
            throw new InvalidOperationException(
                $"{what}: {Join(absent)} must truncate and recover, but the draft gives them no `include` "
                + "policy, so a conversion of it would lose their tails.");
        }

        var extra = observed.Where(name => !expected.Contains(name)).Order(StringComparer.Ordinal).ToList();
        if (extra.Count > 0)
        {
            throw new InvalidOperationException(
                $"{what}: {Join(extra)} truncated, but this case expects exactly "
                + $"{Join(expected.Order(StringComparer.Ordinal))}.");
        }

        RequireDomains(draft, expected, missing, what);
    }

    /// <summary>
    /// A probe that breached an aggregate guard: <b>no draft</b>, the breach reported, and nothing
    /// else reported. A partial draft that read as complete is precisely what the guard exists to
    /// prevent (D-110), and a run that also failed for an unrelated reason measured that failure
    /// rather than the cost of reaching the guard — so any other diagnostic is rejected.
    /// <para>
    /// The result is held to exactly what the guard path produces: the prober returns a breach
    /// through its single-diagnostic failure, which carries no value, and every guard reports one
    /// <c>ProbeLimitExceeded</c> at <c>Error</c>. So a second breach, or the right code at any other
    /// severity, is a result that path cannot produce, and is rejected like an unrelated diagnostic.
    /// </para>
    /// </summary>
    public static void RequireGuardBreach(Diagnosed<SpecDocument> result, string what)
    {
        // The value itself rather than TryGetValue: the breach is an Error, and an Error already
        // hides any value from TryGetValue, so asking through it could never see a document that
        // came back beside the breach.
        if (result.Value is not null)
        {
            throw new InvalidOperationException($"{what}: a draft was produced, but the guard must yield none.");
        }

        var diagnostics = result.Diagnostics;
        if (!diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.ProbeLimitExceeded))
        {
            throw new InvalidOperationException(
                diagnostics.Count == 0
                    ? $"{what}: no guard breach was reported: the result carries no diagnostic at all."
                    : $"{what}: no guard breach was reported: {ConversionPipeline.Describe(diagnostics)}");
        }

        // A breach is reported alone — the prober returns the single diagnostic that stopped the
        // pass — so anything beside it, a second breach included, means this run ended for a second
        // reason, and an accepted duration would be the cost of that reason rather than of the guard.
        if (diagnostics.Count != 1)
        {
            throw new InvalidOperationException(
                $"{what}: a guard breach is reported as exactly one diagnostic, but {diagnostics.Count} were "
                + "reported, so this case did not measure the guard alone: "
                + ConversionPipeline.Describe(diagnostics));
        }

        // The only diagnostic, and so the ProbeLimitExceeded found above.
        var breach = diagnostics[0];
        if (breach.Severity != DiagnosticSeverity.Error)
        {
            throw new InvalidOperationException(
                $"{what}: the guard breach was reported with severity {breach.Severity}, but a guard reports it "
                + $"with severity {DiagnosticSeverity.Error}: {ConversionPipeline.Describe(diagnostics)}");
        }
    }

    /// <summary>
    /// The W16 attributes a probe over <paramref name="records"/> rows must truncate at
    /// <paramref name="limit"/>: exactly those whose distinct cleaned non-missing values exceed it,
    /// on D-108's strictly-greater boundary.
    /// <para>
    /// Derived from the frozen generator and never from the prober — an expectation the code under
    /// measurement supplied could not disagree with it. It stays affordable at every tier because a
    /// column that exceeds the limit proves it after <c>limit + 1</c> distinct values, and a column
    /// whose values come from a bounded domain cannot exceed a limit at or above that bound, so no
    /// case walks a 73M-row tier to its end.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> W16Truncating(long records, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(records);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var truncating = new List<string>();
        for (var column = 0; column < W16Corpus.ColumnCount; column++)
        {
            var index = column;
            if (Exceeds(limit, W16DomainBound(records, index), W16Values(records, index)))
            {
                truncating.Add(W16Corpus.Columns[index]);
            }
        }

        return truncating;
    }

    /// <summary>
    /// The same derivation over the T10 triple family, whose attributes are its predicates. A
    /// subject contributes at most two distinct values to each of them, and <c>Stage</c>'s two are
    /// two <em>raw spellings</em> of one number (§5.3.1) — which is why it is the predicate that can
    /// reach the limit while the categorical ones never can.
    /// </summary>
    public static IReadOnlyList<string> T10Truncating(long records, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(records);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var subjects = T10Corpus.Subjects(records);
        var truncating = new List<string>();
        foreach (var (predicate, bound) in T10PredicateBounds(subjects))
        {
            if (Exceeds(limit, bound, T10Values(subjects, predicate)))
            {
                truncating.Add(predicate);
            }
        }

        return truncating;
    }

    /// <summary>
    /// Whether more than <paramref name="limit"/> distinct values appear in
    /// <paramref name="values"/>, given a <paramref name="bound"/> their number cannot exceed.
    /// </summary>
    private static bool Exceeds(int limit, long bound, IEnumerable<string> values)
    {
        if (bound <= limit)
        {
            return false;
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (distinct.Add(value) && distinct.Count > limit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// An upper bound on one W16 column's distinct values, read off the generator's own value
    /// definitions: <c>n_seq</c> is strictly increasing and <c>n_wide</c> draws once per row, so
    /// neither can exceed the row count; <c>n_ties</c> cycles through fifty; <c>n_skew</c> is one
    /// fixed value plus a tail of 9,973; and the categorical and binary columns draw from their
    /// fixed domains.
    /// </summary>
    private static long W16DomainBound(long records, int column) => column switch
    {
        W16Corpus.ColSeq or W16Corpus.ColWide => records,
        W16Corpus.ColTies => 50,
        W16Corpus.ColSkew => 9_974,
        >= W16Corpus.ColCategoricalFirst and < W16Corpus.ColBinaryFirst => W16Corpus.StandardDomain.Count,
        _ => 2,
    };

    private static IEnumerable<string> W16Values(long records, int column)
    {
        for (var row = 0L; row < records; row++)
        {
            if (W16Corpus.CleanedValue(row, column) is { } value)
            {
                yield return value;
            }
        }
    }

    /// <summary>
    /// Each T10 predicate with an upper bound on its distinct values: the two categorical predicates
    /// are bounded by their domains, the unmatched one carries a single fixed value, and
    /// <c>Stage</c> is bounded by two spellings per subject.
    /// </summary>
    private static IEnumerable<(string Predicate, long Bound)> T10PredicateBounds(long subjects)
    {
        yield return (T10Corpus.TissuePredicate, T10Corpus.TissueDomain.Count);
        yield return (T10Corpus.SignalPredicate, T10Corpus.SignalDomain.Count);
        yield return (T10Corpus.StagePredicate, 2 * subjects);
        yield return (T10Corpus.UnmatchedPredicate, 1);
    }

    private static IEnumerable<string> T10Values(long subjects, string predicate)
    {
        for (var subject = 0L; subject < subjects; subject++)
        {
            for (var row = 0; row < T10Corpus.RowsPerSubject; row++)
            {
                var (observed, value) = T10Corpus.Row(subject, row);
                if (string.Equals(observed, predicate, StringComparison.Ordinal))
                {
                    yield return value;
                }
            }
        }
    }

    /// <summary>
    /// Every attribute the draft carries, held to the outcome its name was given: no attribute
    /// outside the truncated set carries a recovery policy — an all-missing one included — and a
    /// truncated one authors a non-empty retained prefix, an all-missing one authors no domain at
    /// all, and every other one authors its own complete non-empty domain.
    /// </summary>
    private static void RequireDomains(
        SpecDocument draft,
        IReadOnlySet<string> truncated,
        IReadOnlySet<string> missing,
        string what)
    {
        var incomplete = new List<string>();
        var unexpectedDomain = new List<string>();
        var unexpectedRecovery = new List<string>();

        foreach (var attribute in draft.Attributes)
        {
            var name = NameOf(attribute);

            // Recovery is decided before either domain branch, so neither can skip it: the
            // all-missing exemption excuses an absent domain, never the `include` that only
            // truncation authors (ProbeDraft) — an attribute that observed nothing had nothing to
            // truncate.
            if (!truncated.Contains(name) && attribute.UnknownValuePolicy == UnknownValuePolicy.Include)
            {
                unexpectedRecovery.Add(name);
            }

            if (missing.Contains(name))
            {
                if (attribute.DeclaredDomain is not null)
                {
                    unexpectedDomain.Add(name);
                }
            }
            else if (attribute.DeclaredDomain is not { Count: > 0 })
            {
                incomplete.Add(name);
            }
        }

        if (incomplete.Count > 0)
        {
            throw new InvalidOperationException(
                $"{what}: {Join(incomplete.Order(StringComparer.Ordinal))} authored no declared domain, so a "
                + "later conversion would have to rediscover it.");
        }

        if (unexpectedDomain.Count > 0)
        {
            throw new InvalidOperationException(
                $"{what}: {Join(unexpectedDomain.Order(StringComparer.Ordinal))} authored a domain, but this "
                + "case expects them to have observed nothing but missing values.");
        }

        if (unexpectedRecovery.Count > 0)
        {
            throw new InvalidOperationException(
                $"{what}: {Join(unexpectedRecovery.Order(StringComparer.Ordinal))} carry a recovery policy "
                + "without being expected to truncate.");
        }
    }

    private static void RequireAttributeCount(SpecDocument draft, int expectedAttributes, string what)
    {
        if (draft.Attributes.Count != expectedAttributes)
        {
            throw new InvalidOperationException(
                $"{what}: the draft has {draft.Attributes.Count} attributes, expected {expectedAttributes}.");
        }
    }

    /// <summary>
    /// The expectation as a set, with every name checked against the draft: a case naming an
    /// attribute the draft does not carry is stating an expectation about nothing, and one that was
    /// only ever looked up would pass silently.
    /// </summary>
    private static IReadOnlySet<string> Expected(
        IReadOnlyCollection<string>? names, SpecDocument draft, string what, string role)
    {
        if (names is null || names.Count == 0)
        {
            return NoAttributes;
        }

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!expected.Add(name))
            {
                throw new InvalidOperationException(
                    $"{what}: '{name}' is named twice among the expected {role} attributes.");
            }
        }

        var present = draft.Attributes.Select(NameOf).ToHashSet(StringComparer.Ordinal);
        var unknown = expected.Where(name => !present.Contains(name)).Order(StringComparer.Ordinal).ToList();
        return unknown.Count == 0
            ? expected
            : throw new InvalidOperationException(
                $"{what}: the expected {role} attributes name {Join(unknown)}, which the draft does not carry.");
    }

    // A draft attribute always authors a name (ProbeDraft builds it from the naming matrix); the
    // document model allows null because a hand-authored spec can omit it, and an unnamed attribute
    // must compare as one rather than throw inside an oracle.
    private static string NameOf(AttributeSection attribute) => attribute.Name ?? string.Empty;

    private static string Join(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => $"'{name}'"));

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
