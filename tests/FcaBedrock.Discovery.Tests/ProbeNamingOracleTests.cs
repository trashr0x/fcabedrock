using System.Globalization;
using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The probe's naming and omission decisions checked against a small, literal reference over
/// generated adversarial sources (§7.1, D-108), and every successful draft converted over the
/// full source, so actual Core planning shows that no two formal attributes share a name.
/// <para>
/// The reference keeps what the product must not: every observed value and the history of every
/// name, so it evaluates the flag exactly as stated. A pair's flag is true when any observed
/// value of the shorter attribute (retained, repeated, discarded, or containing CR/LF) starts
/// with the longer name's remainder plus a hyphen, or when the pair was formed after the shorter
/// domain truncated. Witnesses are selected together and renamed <c>probe_i_k</c> against every
/// baseline stem. The product instead walks only discarded values during the pass and decides
/// the rest at the end; this suite is what pins that the two agree.
/// </para>
/// <para>
/// Fixed seeds: the cases are the same on every run and every machine.
/// </para>
/// </summary>
public sealed class ProbeNamingOracleTests
{
    private static readonly string[] PredicatePool =
    [
        "a", "a-b", "a-b-c", "a-", "b", "b-c", "x", "predicate_0", "predicate_0-x", "predicate_0#0",
        "predicate_0#0-x", "predicate_1", "predicate_1#1", "predicate_1#1-y", "predicate_2-z", "probe_0_0",
        "probe_1_0-q", "bad\"1", "bad\"2", "bad\"0",
    ];

    private static readonly string[] HeaderPool =
        ["a", "a-b", "a-b-c", "a-", "b", "b-c", "x", "column_0", "column_0-x", "probe_0_0", "probe_1_0-q", "c-d"];

    private static readonly string[] ValuePieces =
        ["b", "c", "x", "y", "z", "q", "-", "b-", "c-", "-x", "a", "0", "#0", "n\nm", "r\rs"];

    private static readonly int[] Limits = [1, 2, 3, 100];

    [Fact]
    public async Task ProbeTriple_WhenGeneratedSourcesAreProbed_ThenEveryDecisionMatchesTheLiteralReference()
    {
        var random = new Random(20261015);
        var checkedDrafts = 0;
        for (var c = 0; c < 250; c++)
        {
            var rows = new List<(string Predicate, string? Value)>();
            var count = random.Next(2, 16);
            for (var i = 0; i < count; i++)
            {
                rows.Add((PredicatePool[random.Next(PredicatePool.Length)], random.Next(8) == 0 ? null : Value(random)));
            }

            var csv = TripleCsv(rows);
            foreach (var limit in Limits)
            {
                var expected = LiteralReference.Triple(rows, limit);
                var result = await TripleProbeFixtures.ProbeTripleCsvAsync(csv, options: Limit(limit));
                checkedDrafts += await AssertMatchesAsync(expected, result, csv, $"triple case {c}, limit {limit}");
            }
        }

        Assert.True(checkedDrafts > 500, $"Only {checkedDrafts} drafts were converted.");
    }

    [Fact]
    public async Task Probe_WhenGeneratedWideSourcesAreProbed_ThenEveryDecisionMatchesTheLiteralReference()
    {
        var random = new Random(20261016);
        var checkedDrafts = 0;
        for (var c = 0; c < 250; c++)
        {
            var header = HeaderPool.OrderBy(_ => random.Next()).Take(random.Next(2, 6)).ToArray();
            var records = new List<string?[]>();
            var count = random.Next(1, 8);
            for (var r = 0; r < count; r++)
            {
                records.Add([.. header.Select(_ => random.Next(6) == 0 ? null : Value(random))]);
            }

            var csv = WideCsv(header, records);
            foreach (var limit in Limits)
            {
                var expected = LiteralReference.Wide(header, records, limit);
                var result = await ProbeFixtures.ProbeCsvAsync(csv, options: Limit(limit));
                checkedDrafts += await AssertMatchesAsync(expected, result, csv, $"wide case {c}, limit {limit}");
            }
        }

        Assert.True(checkedDrafts > 500, $"Only {checkedDrafts} drafts were converted.");
    }

    [Fact]
    public async Task ProbeTriple_WhenEveryDiscoveredPrefixIsProbed_ThenItsNamesEqualTheBatchPlanOverThatPrefix()
    {
        // The names kept current during the pass, checked after each newly discovered predicate:
        // probing each prefix of the rows ends the pass there, where the probe's own end-of-pass
        // check and this independent batch plan must both agree.
        string[] pool =
        [
            "bad\"0", "bad\"1", "bad\"2", "predicate_0", "predicate_0#0", "predicate_0#1", "predicate_0#2",
            "predicate_1", "predicate_1#1", "predicate_1#2", "predicate_2", "predicate_2#2", "x",
        ];
        var random = new Random(20261017);
        var displacements = 0;
        for (var c = 0; c < 400; c++)
        {
            var predicates = pool.OrderBy(_ => random.Next()).Take(random.Next(2, pool.Length)).ToList();
            for (var length = 1; length <= predicates.Count; length++)
            {
                var prefix = predicates.Take(length).ToList();
                var rows = prefix.Select((p, i) => ((string?)$"s{i}", (string?)p, (string?)"v")).ToArray();
                var draft = ProbeFixtures.Draft(await Prober.ProbeTripleAsync(
                    TripleProbeFixtures.Fake(rows), TripleProbeFixtures.TripleSettings()));

                var expected = LiteralReference.BatchNames(prefix);
                Assert.Equal(expected, draft.Attributes.Select(a => a.Name));
                if (length > 1 && !LiteralReference.BatchNames(prefix[..^1]).SequenceEqual(expected.Take(length - 1)))
                {
                    displacements++;
                }
            }
        }

        // The pool is built so that later real predicates take earlier fallbacks' names.
        Assert.True(displacements > 100, $"Only {displacements} prefixes moved a fallback.");
    }

    private static ProbeOptions Limit(int limit) => ProbeOptions.Create(valueRetentionLimit: limit);

    private static string Value(Random random)
    {
        var builder = new StringBuilder();
        var pieces = random.Next(1, 4);
        for (var i = 0; i < pieces; i++)
        {
            builder.Append(ValuePieces[random.Next(ValuePieces.Length)]);
        }

        return builder.ToString();
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string TripleCsv(List<(string Predicate, string? Value)> rows)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"s{i % 4},{Quote(rows[i].Predicate)},");
            builder.Append(rows[i].Value is { } value ? Quote(value) : string.Empty).Append('\n');
        }

        return builder.ToString();
    }

    private static string WideCsv(string[] header, List<string?[]> records)
    {
        var builder = new StringBuilder();
        builder.AppendJoin(',', header.Select(Quote)).Append('\n');
        foreach (var record in records)
        {
            builder.AppendJoin(',', record.Select(v => v is null ? string.Empty : Quote(v))).Append('\n');
        }

        return builder.ToString();
    }

    // Returns 1 when a draft was produced and converted, 0 for an expected refusal.
    private static async Task<int> AssertMatchesAsync(
        LiteralReference.Outcome expected, Diagnosed<SpecDocument> result, string csv, string label)
    {
        if (expected.Refused)
        {
            Assert.False(result.TryGetValue(out _), label);
            Assert.Equal(DiagnosticCode.ProbeUnsafeTruncatedDomain, Assert.Single(result.Diagnostics).Code);
            return 0;
        }

        Assert.True(result.TryGetValue(out var draft), $"{label}: {ProbeFixtures.Describe(result.Diagnostics)}");
        Assert.Equal(expected.Names, draft.Attributes.Select(a => a.Name));
        for (var i = 0; i < expected.Names.Count; i++)
        {
            Assert.Equal(expected.Domains[i], draft.Attributes[i].DeclaredDomain);
            Assert.Equal(expected.Policies[i], draft.Attributes[i].UnknownValuePolicy);
        }

        var converted = await ProbeConversion.ConvertAsync(
            draft, draft.Binding!.Shape == SourceShape.Triple ? TripleProbeFixtures.Bytes(csv) : ProbeFixtures.Bytes(csv));
        Assert.Equal(expected.Columns, converted.Columns);
        return 1;
    }

    /// <summary>The literal reference: full histories, no shortcuts.</summary>
    private static class LiteralReference
    {
        public sealed record Outcome(
            bool Refused,
            IReadOnlyList<string> Names,
            IReadOnlyList<IReadOnlyList<string>?> Domains,
            IReadOnlyList<UnknownValuePolicy?> Policies,
            IReadOnlyList<string> Columns);

        private static bool Valid(string name) =>
            name.Length > 0 && !name.Contains('\r', StringComparison.Ordinal) && !name.Contains('\n', StringComparison.Ordinal)
            && !name.Contains('"', StringComparison.Ordinal);

        private static bool Unsafe(string value) =>
            value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal);

        // The D-107 triple matrix over a predicate list, as the spec states it.
        public static List<string> BatchNames(IReadOnlyList<string> predicates)
        {
            var used = new HashSet<string>(predicates.Where(Valid), StringComparer.Ordinal);
            var names = new List<string>();
            for (var i = 0; i < predicates.Count; i++)
            {
                if (Valid(predicates[i]))
                {
                    names.Add(predicates[i]);
                    continue;
                }

                var fallback = $"predicate_{i}";
                var name = !used.Contains(fallback) ? fallback
                    : !used.Contains($"{fallback}#{i}") ? $"{fallback}#{i}"
                    : Enumerable.Range(1, int.MaxValue - 1).Select(k => $"{fallback}#{k}").First(n => !used.Contains(n));
                used.Add(name);
                names.Add(name);
            }

            return names;
        }

        public static Outcome Triple(List<(string Predicate, string? Value)> rows, int limit)
        {
            var predicates = new List<string>();
            var observations = new List<(int Attribute, string? Value)>();
            var starts = new Dictionary<(int, int), int>();
            for (var t = 0; t < rows.Count; t++)
            {
                var attribute = predicates.IndexOf(rows[t].Predicate);
                if (attribute < 0)
                {
                    predicates.Add(rows[t].Predicate);
                    attribute = predicates.Count - 1;
                    Track(BatchNames(predicates), starts, t);
                }

                observations.Add((attribute, rows[t].Value));
            }

            return Decide(BatchNames(predicates), observations, starts, limit);
        }

        public static Outcome Wide(string[] header, List<string?[]> records, int limit)
        {
            var starts = new Dictionary<(int, int), int>();
            Track([.. header], starts, -1);
            var observations = new List<(int, string?)>();
            foreach (var record in records)
            {
                for (var i = 0; i < header.Length; i++)
                {
                    observations.Add((i, record[i]));
                }
            }

            return Decide([.. header], observations, starts, limit);
        }

        // Keeps, for every current pair, the time its current form appeared.
        private static void Track(List<string> names, Dictionary<(int, int), int> starts, int time)
        {
            var current = new HashSet<(int, int)>();
            for (var s = 0; s < names.Count; s++)
            {
                for (var l = 0; l < names.Count; l++)
                {
                    if (s != l && names[l].StartsWith(names[s] + "-", StringComparison.Ordinal))
                    {
                        current.Add((s, l));
                    }
                }
            }

            foreach (var gone in starts.Keys.Where(k => !current.Contains(k)).ToList())
            {
                starts.Remove(gone);
            }

            foreach (var pair in current)
            {
                starts.TryAdd(pair, time);
            }
        }

        private static Outcome Decide(
            List<string> names, List<(int Attribute, string? Value)> observations, Dictionary<(int, int), int> starts, int limit)
        {
            var count = names.Count;
            var distinct = Enumerable.Range(0, count).Select(_ => new List<string>()).ToArray();
            var observed = Enumerable.Range(0, count).Select(_ => new List<string>()).ToArray();
            var truncatedAt = Enumerable.Repeat(int.MaxValue, count).ToArray();

            // Observation times: one per observation; a triple's time is its row, which the
            // starts above use too. Wide pairs exist from the start.
            for (var t = 0; t < observations.Count; t++)
            {
                var (attribute, value) = observations[t];
                if (value is null)
                {
                    continue;
                }

                observed[attribute].Add(value);
                if (!distinct[attribute].Contains(value))
                {
                    if (distinct[attribute].Count == limit && truncatedAt[attribute] == int.MaxValue)
                    {
                        truncatedAt[attribute] = t;
                    }

                    distinct[attribute].Add(value);
                }
            }

            var truncated = distinct.Select(d => d.Count > limit).ToArray();
            if (Enumerable.Range(0, count).Any(i => truncated[i] && observed[i].Any(Unsafe)))
            {
                return new Outcome(true, [], [], [], []);
            }

            var selected = new bool[count];
            foreach (var ((s, l), start) in starts)
            {
                var q = names[l][(names[s].Length + 1)..] + "-";
                var flag = (truncated[s] && truncatedAt[s] < start) || observed[s].Any(v => v.StartsWith(q, StringComparison.Ordinal));
                if ((truncated[s] || truncated[l]) && flag)
                {
                    selected[Math.Max(s, l)] = true;
                }

                if (!truncated[s] && !truncated[l] && distinct[s].Any(v => !Unsafe(v)
                    && v.StartsWith(q, StringComparison.Ordinal) && distinct[l].Contains(v[q.Length..])))
                {
                    selected[Math.Max(s, l)] = true;
                }
            }

            var stems = names.Select(n => n.Contains('-', StringComparison.Ordinal) ? n[..n.IndexOf('-', StringComparison.Ordinal)] : n)
                .ToHashSet(StringComparer.Ordinal);
            var final = names.Select((n, i) => selected[i]
                ? Enumerable.Range(0, count + 1).Select(k => $"probe_{i}_{k}").First(c => !stems.Contains(c))
                : n).ToList();

            var domains = new List<IReadOnlyList<string>?>();
            var policies = new List<UnknownValuePolicy?>();
            var columns = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if (truncated[i])
                {
                    domains.Add(distinct[i].Take(limit).ToList());
                    policies.Add(UnknownValuePolicy.Include);
                }
                else
                {
                    domains.Add(distinct[i].Count == 0 ? null : distinct[i].Where(v => !Unsafe(v)).ToList());
                    policies.Add(distinct[i].Any(Unsafe) ? UnknownValuePolicy.Warn : null);
                }

                columns.AddRange(distinct[i].Where(v => !Unsafe(v)).Select(v => final[i] + "-" + v));
            }

            return new Outcome(false, final, domains, policies, columns);
        }
    }
}
