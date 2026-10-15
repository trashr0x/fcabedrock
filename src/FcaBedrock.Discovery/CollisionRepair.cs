using System.Globalization;

namespace FcaBedrock.Discovery;

/// <summary>
/// Renames the attributes whose baseline names could make two formal attributes render the
/// same name on the probed source, and proves the renamed set cannot (§7.1, D-108).
/// <para>
/// <b>Which pairs are witnesses.</b> For final baseline names <c>N</c> and <c>N-r</c>
/// (<see cref="NameRelations"/> explains the pair and its flag):
/// </para>
/// <list type="bullet">
/// <item>both domains complete: a witness only when a saved category of the shorter attribute
/// renders exactly the same name as a saved category of the longer one;</item>
/// <item>either domain truncated: a witness when the flag is true, because a value recovered at
/// conversion could collide;</item>
/// <item>flag false: never a witness, truncated or not, because no observed value of the shorter
/// attribute starts with <c>r + "-"</c>.</item>
/// </list>
/// <para>
/// <b>Simultaneous selection.</b> Every witness marks its later ordinal, and all marks are taken
/// before any name changes, so each renamed attribute has a witness under the baseline names and
/// one pair's repair never decides another's. A selected attribute at ordinal <c>i</c> takes the
/// first <c>probe_&lt;i&gt;_&lt;k&gt;</c>, for <c>k</c> from 0 to the attribute count, that is not
/// the text before the first hyphen of any baseline name. Such a name has no hyphen, so its
/// prefix overlaps another name's only if it equals that name's stem, which is excluded; the
/// families of different ordinals share no string; and at most as many stems exist as
/// attributes, so a free candidate always exists. The choice reads only the baseline names, so an
/// attribute selected at two retention limits gets the same name at both. Source selectors never
/// change.
/// </para>
/// <para>
/// <b>The check.</b> After renaming, the same traversals run under the final names and must find
/// no witness among unrenamed pairs and no pair at all with a renamed attribute. A failure is a
/// defect in this code, never a property of the data, so it throws.
/// </para>
/// </summary>
internal static class CollisionRepair
{
    /// <summary>
    /// Selects, renames and checks, leaving the final names current in <paramref name="names"/>,
    /// and returns which ordinals were renamed. Every loop here checks cancellation, and the
    /// name walks it calls check it at every character.
    /// </summary>
    public static bool[] Apply(
        NameRelations names, IReadOnlyList<RetainedDomain> domains, CancellationToken cancellationToken)
    {
        var count = names.Count;
        var renamed = new bool[count];
        var pairs = new List<(int, int)>();
        Visit(names, domains, renamed, verify: false, pairs, cancellationToken);

        var stems = new HashSet<string>(count, StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stems.Add(Stem(names.NameOf(ordinal)));
        }

        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (renamed[ordinal])
            {
                names.Replace(ordinal, RepairName(ordinal, count, stems, cancellationToken));
            }
        }

        Visit(names, domains, renamed, verify: true, pairs, cancellationToken);
        return renamed;
    }

    // Walks every pair the current names form. Retained histories give the exact check for
    // complete pairs and the retained part of the flag; the name list gives every pair, for the
    // forced and streamed parts of the flag, which only a truncated pair can have.
    private static void Visit(
        NameRelations names,
        IReadOnlyList<RetainedDomain> domains,
        bool[] renamed,
        bool verify,
        List<(int, int)> pairs,
        CancellationToken cancellationToken)
    {
        for (var shorter = 0; shorter < domains.Count; shorter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = names.DescendantStart(shorter);
            if (start < 0)
            {
                continue;
            }

            var shorterDomain = domains[shorter];
            foreach (var value in shorterDomain.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pairs.Clear();
                names.Walk(start, value, pairs);
                foreach (var (longer, offset) in pairs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // The value starts with r + "-", so the flag is true. For a complete pair the
                    // remainder after that hyphen is the only longer category that can collide.
                    // It is safe whenever the value is, so the full retained set answers for the
                    // saved one.
                    var longerDomain = domains[longer];
                    var witness = shorterDomain.Truncated || longerDomain.Truncated
                        || (!RetainedDomain.ContainsLineBreak(value)
                            && longerDomain.ContainsRetained(value.AsSpan(offset + 1)));
                    Record(renamed, verify, shorter, longer, witness);
                }
            }
        }

        for (var longer = 0; longer < domains.Count; longer++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pairs.Clear();
            names.Shorter(longer, pairs);
            foreach (var (shorter, _) in pairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var witness = (domains[shorter].Truncated || domains[longer].Truncated)
                    && (names.Forced(shorter, longer) || names.Hit(shorter, longer));
                Record(renamed, verify, shorter, longer, witness);
            }
        }
    }

    private static void Record(bool[] renamed, bool verify, int shorter, int longer, bool witness)
    {
        if (!verify)
        {
            if (witness)
            {
                renamed[Math.Max(shorter, longer)] = true;
            }

            return;
        }

        if (renamed[shorter] || renamed[longer])
        {
            throw new InvalidOperationException(
                $"A repaired probe name still forms a name pair (ordinals {shorter} and {longer}).");
        }

        if (witness)
        {
            throw new InvalidOperationException(
                $"Probe names that can still collide were not repaired (ordinals {shorter} and {longer}).");
        }
    }

    // The bound is the attribute count: at most that many stems can block the count + 1
    // candidates. The counter is a long so the bound itself cannot overflow it.
    private static string RepairName(
        int ordinal, int count, HashSet<string> stems, CancellationToken cancellationToken)
    {
        for (var k = 0L; k <= count; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = string.Create(CultureInfo.InvariantCulture, $"probe_{ordinal}_{k}");
            if (!stems.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"No repair name is free for probe attribute {ordinal} among {count} baseline names.");
    }

    private static string Stem(string name)
    {
        var hyphen = name.IndexOf('-', StringComparison.Ordinal);
        return hyphen < 0 ? name : name[..hyphen];
    }
}
