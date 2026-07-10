using System.Globalization;

namespace FcaBedrock.Conversion;

/// <summary>
/// Assigns unique object names for a wide <c>column</c> object key under
/// <c>duplicate_object_policy = "keep"</c> (§6.1, D-083). Each row stays its own object; names are
/// unique by construction and assigned in emit (source-row) order. Name assignment is a converter
/// concern, not the writer's (P-15). All comparisons are ordinal (P-12); the seen-keys and
/// assigned-names sets are bounded object-name metadata (P-16), never the incidence matrix.
///
/// <para>Two independent signals drive the aggregated emit diagnostics:
/// <list type="bullet">
/// <item><c>duplicate</c> — the cleaned key was already seen (a repeated key) → <c>DuplicateObjectKey</c>.</item>
/// <item><c>disambiguated</c> — the candidate name was already assigned and had to be bumped with
/// <c>#1</c>, <c>#2</c>, … → <c>ObjectKeyNameDisambiguated</c>. This can fire for a <b>first</b>
/// occurrence whose literal key collides with a generated name, independently of <c>duplicate</c>.</item>
/// </list></para>
/// </summary>
internal sealed class ColumnKeyNamer
{
    private readonly HashSet<string> _seenKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _assigned = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the unique object name for <paramref name="cleanedKey"/> at 0-based
    /// <paramref name="recordIndex"/>. <paramref name="duplicate"/> is set when the cleaned key was
    /// already seen; <paramref name="disambiguated"/> is set when the base candidate was already
    /// assigned and required a <c>#N</c> suffix.
    /// </summary>
    public string Assign(string cleanedKey, int recordIndex, out bool duplicate, out bool disambiguated)
    {
        duplicate = !_seenKeys.Add(cleanedKey);

        // §6.1: the first occurrence of a cleaned key takes the key itself; a later occurrence takes
        // <key>#<record-index> (0-based source record index).
        var candidate = duplicate
            ? $"{cleanedKey}#{recordIndex.ToString(CultureInfo.InvariantCulture)}"
            : cleanedKey;

        // If the candidate is already assigned — a literal data key colliding with a generated name,
        // or vice versa — append #1, #2, … and take the first unused; unique by construction (§6.1).
        disambiguated = _assigned.Contains(candidate);
        if (disambiguated)
        {
            var suffix = 1;
            string escalated;
            do
            {
                escalated = $"{candidate}#{suffix.ToString(CultureInfo.InvariantCulture)}";
                suffix++;
            }
            while (!_assigned.Add(escalated));

            return escalated;
        }

        _assigned.Add(candidate);
        return candidate;
    }
}
