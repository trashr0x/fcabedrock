namespace FcaBedrock.Core.Discretization;

/// <summary>
/// The one place the cut-bin label template lives, shared by
/// <see cref="ManualCutsDiscretizer"/> and <see cref="OrderedCutsDiscretizer"/> so
/// numeric and ordered cuts never drift (P-17). Operates on already-formatted cut
/// strings: <c>&lt;{c0}</c>, <c>[{ci}, {cj})</c>, <c>&gt;={cn}</c> (spec §11.2), with
/// the v2-compat interior transform <c>[{ci}, {cj}) → {ci}to&lt;{cj}</c> (D-044). The
/// canonical (native) form is the identity-bearing key; the v2 form is a pure
/// render of it.
/// </summary>
internal static class CutBinLabels
{
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
            labels.Add($">={cuts[^1]}");
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
}
