using System.Text;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Pure TOML array rendering for the canonical writers: the inline form every
/// array uses by default, and the deterministic multiline wrapping a long array
/// uses (D-113). Items arrive <b>already rendered</b> (via
/// <see cref="TomlLiteral"/>), so string escaping, number formatting, and
/// date-time formatting keep their single owner and cannot drift between the two
/// writers that share this file.
/// <para>
/// Extracted from <see cref="SpecWriter"/> at M7 Slice E so
/// <c>FcaBedrock.Spec.Manifest.RunManifestWriter</c> reuses the one wrapping rule
/// rather than minting a second one (D-123 point 8, P-5). The extraction is
/// byte-neutral: <see cref="SpecWriter"/>'s <c>declared_domain</c> output is
/// unchanged, and <c>DeclaredDomainWrappingTests</c> is the pin.
/// </para>
/// </summary>
internal static class TomlArrays
{
    /// <summary>
    /// The single-line array form: <c>[]</c> when empty, else the rendered items
    /// joined by <c>", "</c> inside brackets. Every array uses this unless a
    /// caller explicitly opts into <see cref="Wrappable"/>.
    /// </summary>
    internal static string Inline(IReadOnlyList<string> renderedItems) =>
        renderedItems.Count == 0 ? "[]" : $"[{string.Join(", ", renderedItems)}]";

    /// <summary>
    /// The D-113 rendering for the two arrays that may wrap — the canonical
    /// writer's top-level <c>declared_domain</c> and the run manifest's non-cut
    /// calibration <c>values</c>: inline while the complete
    /// <c><paramref name="key"/> = […]</c> line fits
    /// <see cref="InlineLineLimit"/>, otherwise deterministically multiline —
    /// one rendered item per line at a two-space indent, a trailing comma on
    /// every item line, and an unindented closing bracket. A single over-long
    /// item wraps but is never split; an empty array stays inline as <c>[]</c>.
    /// Formatting only — semantics and fingerprints are untouched (§14).
    /// </summary>
    /// <param name="key">
    /// The key the value will be emitted under. It participates in the
    /// measurement, so the cutoff governs the line the writer actually emits;
    /// callers must pass the key they are about to write, never a stand-in.
    /// </param>
    /// <param name="renderedItems">The already-escaped items, in emit order.</param>
    internal static string Wrappable(string key, IReadOnlyList<string> renderedItems)
    {
        // Measured over the complete line the writer would emit — the same
        // `key = value` shape the emitters produce — so the cutoff cannot drift
        // from the rendering it governs. The transient inline string is a
        // cold-path cost.
        var inline = Inline(renderedItems);
        if ($"{key} = {inline}".Length <= InlineLineLimit)
        {
            return inline;
        }

        var wrapped = new StringBuilder("[\n");
        foreach (var item in renderedItems)
        {
            wrapped.Append("  ").Append(item).Append(",\n");
        }

        return wrapped.Append(']').ToString();
    }

    /// <summary>
    /// The length, in UTF-16 code units, of the longest complete line a
    /// wrappable array emits inline (D-113) — key, spaces, equals sign,
    /// brackets, quotes, commas, separators, and escape sequences, excluding the
    /// terminating LF. A line of this length or shorter stays inline; a longer
    /// one wraps. A private canonical-writer formatting constant, byte-pinned by
    /// test: never a spec field, probe option, CLI/UI setting, or fingerprint
    /// input. UTF-16 code units (not display cells, graphemes, or UTF-8 bytes)
    /// keep the measurement machine-independent (P-7).
    /// </summary>
    private const int InlineLineLimit = 100;
}
