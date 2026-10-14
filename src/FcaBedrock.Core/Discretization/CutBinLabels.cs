using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// The one place the cut-bin label template lives, shared by
/// <see cref="ManualCutsDiscretizer"/> and <see cref="OrderedCutsDiscretizer"/> so
/// numeric and ordered cuts never drift (EP-17). Operates on already-formatted cut
/// strings: <c>&lt;{c0}</c>, <c>[{ci}, {cj})</c>, <c>&gt;={cn}</c> (spec §11.2), with
/// the v2-compat interior transform <c>[{ci}, {cj}) → {ci}to&lt;{cj}</c> (D-044) and the
/// Unicode form <c>≥{cn}</c> of the open high bin (§8). The canonical (native ASCII) form
/// is the identity-bearing key; the other forms are pure renders of it.
/// </summary>
internal static class CutBinLabels
{
    // The operator the open high bin's label starts with: the only operator in a cut-bin label
    // that has a Unicode form. The open low bin's "<" has none, and an interior label has none.
    private const string OpenHighOperator = ">=";

    /// <summary>
    /// The ordered canonical bin labels for ascending <paramref name="cuts"/> with
    /// the given <paramref name="ends"/>. Open: <c>cuts.Count + 1</c> bins; closed:
    /// <c>cuts.Count - 1</c> interior bins.
    /// </summary>
    public static IReadOnlyList<string> Build(IReadOnlyList<string> cuts, BinEnds ends)
    {
        var labels = new List<string>(cuts.Count + 1);
        if (ends == BinEnds.Open)
        {
            labels.Add($"<{cuts[0]}");
        }

        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            labels.Add(Interior(cuts[i], cuts[i + 1]));
        }

        if (ends == BinEnds.Open)
        {
            labels.Add(OpenHighOperator + cuts[^1]);
        }

        return labels;
    }

    /// <summary>The canonical interior label <c>[{lo}, {hi})</c> (spec §11.2, with the comma-space).</summary>
    public static string Interior(string lo, string hi) => $"[{lo}, {hi})";

    /// <summary>
    /// The bin label for a value whose first strictly-greater cut is at index
    /// <paramref name="firstCutAbove"/> (or <paramref name="cutCount"/> if the value
    /// exceeds every cut), or <see langword="null"/> when an end-open value falls
    /// outside a <see cref="BinEnds.Closed"/> range. Shared by both cut discretizers
    /// so numeric and ordered bin location agree with <see cref="Build"/>.
    /// </summary>
    public static string? LabelFor(IReadOnlyList<string> binLabels, int firstCutAbove, int cutCount, BinEnds ends)
    {
        if (ends == BinEnds.Open)
        {
            return binLabels[firstCutAbove]; // binLabels.Count == cutCount + 1; index 0..cutCount
        }

        if (firstCutAbove == 0 || firstCutAbove == cutCount)
        {
            return null; // below the first / at-or-above the last cut, with closed ends
        }

        return binLabels[firstCutAbove - 1]; // binLabels.Count == cutCount - 1; interior only
    }

    /// <summary>
    /// Renders one canonical label for <paramref name="style"/>. Only the interior
    /// <c>[a, b)</c> form changes (→ <c>{a}to&lt;{b}</c> under v2-compat); the open-end
    /// labels and any non-interval label (threshold cut, <c>all</c>) pass through.
    /// </summary>
    public static string Render(string canonical, LabelStyle style)
    {
        if (style != LabelStyle.V2Compat || canonical.Length < 4 || canonical[0] != '[' || canonical[^1] != ')')
        {
            return canonical;
        }

        var inner = canonical[1..^1]; // "30, 40"
        var separator = inner.IndexOf(", ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return canonical;
        }

        return $"{inner[..separator]}to<{inner[(separator + 2)..]}";
    }

    /// <summary>
    /// Renders one canonical label with the Unicode operator (§8): <c>&gt;={cn}</c> →
    /// <c>≥{cn}</c>. The <paramref name="bin"/> structure, never the label's spelling, says
    /// whether the label is an open high bin: a cut text may itself begin with <c>=</c>, so
    /// the open low bin of an <c>ordered_cuts</c> cut <c>=x</c> is <c>&lt;=x</c>, and it
    /// passes through unchanged, as does every other label and every non-cut bin.
    /// </summary>
    public static string RenderUnicode(string canonical, CanonicalBin bin)
    {
        if (bin is not (NumericCutBin { Lo: not null, Hi: null } or TextCutBin { Lo: not null, Hi: null }))
        {
            return canonical;
        }

        // Build wrote this label as the operator plus the cut, so its prefix is that operator;
        // any other label paired with an open high bin is corrupt plan state, not user text.
        if (!canonical.StartsWith(OpenHighOperator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The open high cut bin's label '{canonical}' does not start with '{OpenHighOperator}'.");
        }

        return "≥" + canonical[OpenHighOperator.Length..];
    }
}
