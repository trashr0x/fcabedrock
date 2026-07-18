using System.Globalization;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Discovery;

/// <summary>
/// One discovered wide attribute: which physical column it observes, the logical name it
/// authors, how its <c>source</c> selects the column, and whether reaching that name
/// required an adjustment worth warning about.
/// </summary>
/// <param name="Index">The 0-based physical column, and the selector when <paramref name="BindByName"/> is null.</param>
/// <param name="Name">The logical attribute name — §10.1-valid and ordinal-unique across the draft.</param>
/// <param name="BindByName">The header cell the <c>source</c> selects by name, or null to select by index.</param>
/// <param name="NameAdjusted">Whether this column's <em>name</em> was synthesized from an unusable header or disambiguated (D-111: routine headerless <c>column_N</c> synthesis is not an adjustment).</param>
internal sealed record DiscoveredColumn(int Index, string Name, string? BindByName, bool NameAdjusted);

/// <summary>
/// The §7.1 / D-107 wide naming and binding matrix, computed over the ordered schema before
/// any record is read.
/// <para>
/// <b>The invariant that drives every branch: no source selector is ever silently changed.</b>
/// A name that must be adjusted is adjusted; the column it reads is not. That is why a
/// duplicate, blank, unusable, or headerless column binds by <em>physical index</em> — a
/// duplicate or blank header does not resolve to exactly one column (<c>SourceBindingInvalid</c>,
/// §10.2), so binding it by name would produce a draft that fails its own reread/resolve
/// guarantee (D-107). A unique usable header still binds by name, which keeps the reorder
/// protection wide name-binding exists for.
/// </para>
/// </summary>
internal static class AttributeNaming
{
    /// <summary>
    /// §10.1 attribute-name validity: any non-empty string containing no newline and no
    /// <c>"</c> (the TOML key-quoting character). Deliberately permissive — real headers look
    /// like <c>bruises?</c>, <c>feature.1</c>, <c>days@home</c>, and a draft must round-trip
    /// them unrenamed.
    /// <para>
    /// This is <b>not</b> <see cref="ObjectNameValidity"/>, which governs data-derived object
    /// names (subjects, wide key cells) over a different alphabet: whitespace-only is a
    /// <em>usable</em> attribute name but an unusable object name, and a control character
    /// other than CR/LF is unusable as an object name but harmless in a TOML string. Two
    /// predicates, two owners, deliberately not merged. Discovery owns this one because the
    /// resolve seam does not enforce it — <c>SpecResolver</c> checks only missing and
    /// duplicate names — so probe's naming matrix is its sole enforcement point.
    /// </para>
    /// </summary>
    public static bool IsUsableName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var ch in name)
        {
            if (ch is '\n' or '\r' or '"')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Plans one attribute per physical schema column, in schema order.
    /// </summary>
    public static IReadOnlyList<DiscoveredColumn> Plan(SourceSchema schema)
    {
        var count = schema.ColumnCount;
        var cells = new string?[count];
        var usableOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            // A header shorter than the column count is only reachable from a hand-built schema
            // (the CSV adapter derives the count FROM the header), and such a column is simply
            // headerless — the same case as a source with no header row at all.
            var cell = schema.Header is { } header && i < header.Count ? header[i] : null;
            cells[i] = cell;
            if (IsUsableName(cell))
            {
                usableOccurrences[cell!] = usableOccurrences.GetValueOrDefault(cell!) + 1;
            }
        }

        var byName = new bool[count];
        var candidates = new string[count];
        var fallbackAdjusted = new bool[count];

        for (var i = 0; i < count; i++)
        {
            var cell = cells[i];
            if (IsUsableName(cell) && usableOccurrences[cell!] == 1)
            {
                byName[i] = true;
                candidates[i] = cell!;
            }
            else if (cell is null)
            {
                // Headerless: routine synthesis, not an adjustment (D-111). There was no
                // authored name to depart from.
                candidates[i] = FallbackName(i);
            }
            else if (IsUsableName(cell))
            {
                // A duplicate usable header keeps its own text as the candidate. The first
                // column to claim it keeps it verbatim — its NAME is untouched, so it is not a
                // name adjustment even though its binding fell back to the index; later
                // claimants are disambiguated below, and those are.
                candidates[i] = cell;
            }
            else
            {
                // Blank or §10.1-unusable: the authored header cannot be the name, so the
                // fallback IS a departure from what was written, and is warned.
                candidates[i] = FallbackName(i);
                fallbackAdjusted[i] = true;
            }
        }

        // Reserve every by-name column's logical name BEFORE assigning any fallback or
        // disambiguated one. Those names are fixed — a name-bound column's name is its
        // selector's text — so resolving collisions against the complete set is what keeps an
        // adversarial header (one that already spells a fallback or a `#k` suffix) from
        // colliding with a name synthesized later in the same pass.
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if (byName[i])
            {
                used.Add(candidates[i]);
            }
        }

        var planned = new DiscoveredColumn[count];
        for (var i = 0; i < count; i++)
        {
            if (byName[i])
            {
                planned[i] = new DiscoveredColumn(i, candidates[i], candidates[i], NameAdjusted: false);
                continue;
            }

            var name = FirstUnused(candidates[i], i, used);
            used.Add(name);
            planned[i] = new DiscoveredColumn(
                i, name, BindByName: null,
                NameAdjusted: fallbackAdjusted[i] || !string.Equals(name, candidates[i], StringComparison.Ordinal));
        }

        return planned;
    }

    private static string FallbackName(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"column_{index}");

    // The D-107 ladder: the candidate itself, then `#<source-index>`, then ordinal `#1`, `#2`, …
    // to the first unused. Escalation is bounded — at most one more step than there are assigned
    // names — and depends only on the schema, so the same schema always yields the same names.
    private static string FirstUnused(string candidate, int sourceIndex, HashSet<string> used)
    {
        if (!used.Contains(candidate))
        {
            return candidate;
        }

        var indexed = string.Create(CultureInfo.InvariantCulture, $"{candidate}#{sourceIndex}");
        if (!used.Contains(indexed))
        {
            return indexed;
        }

        for (var k = 1; ; k++)
        {
            var escalated = string.Create(CultureInfo.InvariantCulture, $"{candidate}#{k}");
            if (!used.Contains(escalated))
            {
                return escalated;
            }
        }
    }
}
