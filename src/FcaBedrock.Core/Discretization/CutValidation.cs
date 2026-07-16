using System.Globalization;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// The one place cut-spec validity rules live, shared by the
/// <see cref="ManualCutsDiscretizer"/> and <see cref="OrderedCutsDiscretizer"/>
/// smart factories so numeric and ordered cuts never drift (P-17, decisions.md
/// D-056). Validation runs at construction (the boundary) so an invalid cut spec
/// can never be represented as a discretizer (P-10/P-14); each rule maps to a
/// distinct <see cref="DiagnosticCode"/> (spec §11.2 / §11.8). All cut diagnostics
/// are <see cref="DiagnosticSeverity.Error"/>.
/// </summary>
internal static class CutValidation
{
    /// <summary>
    /// Validates numeric <c>manual_cuts</c> (spec §11.2): at least one cut
    /// (<see cref="DiagnosticCode.DiscretizerCutsTooFew"/>), a finite strictly-ascending
    /// sequence (<see cref="DiagnosticCode.DiscretizerCutsNotAscending"/>), and — for
    /// <see cref="BinEnds.Closed"/> — at least two cuts
    /// (<see cref="DiagnosticCode.DiscretizerEndsClosedTooFewCuts"/>).
    /// </summary>
    public static IReadOnlyList<BedrockDiagnostic> ValidateManual(IReadOnlyList<double> cuts, BinEnds ends)
    {
        var diagnostics = new List<BedrockDiagnostic>();

        if (cuts.Count < 1)
        {
            diagnostics.Add(TooFew("manual_cuts"));
        }
        else if (ends == BinEnds.Closed && cuts.Count < 2)
        {
            diagnostics.Add(ClosedTooFew("manual_cuts"));
        }

        // The cuts must be a finite, strictly-ascending sequence: a single contract. NaN/±∞ are
        // not finite cut points — they produce nonsense bin edges (<NaN, >=Infinity) and break
        // the order — so a non-finite cut fails here even when numerically "ascending" (e.g.
        // [1, +∞]) or single (e.g. [NaN], which the pairwise ascending check would not catch).
        if (cuts.Count >= 1 && (cuts.Any(c => !double.IsFinite(c)) || !StrictlyAscending(cuts)))
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.DiscretizerCutsNotAscending,
                DiagnosticSeverity.Error,
                $"manual_cuts cuts must be finite and strictly ascending; got [{string.Join(", ", cuts.Select(FormatInvariant))}]."));
        }

        return diagnostics;
    }

    /// <summary>
    /// Validates <c>ordered_cuts</c> (spec §11.8): <see cref="DiagnosticCode.OrderDomainInvalid"/>
    /// (order entries distinct and non-empty), <see cref="DiagnosticCode.DiscretizerCutsTooFew"/>
    /// / <see cref="DiagnosticCode.DiscretizerEndsClosedTooFewCuts"/> (the §11.2 cut-count rules),
    /// <see cref="DiagnosticCode.OrderedCutsCutNotInDomain"/> (each cut a member of order), and
    /// <see cref="DiagnosticCode.OrderedCutsNotAscending"/> (cuts strictly ascending by order
    /// position). Independent problems are reported together so the author sees them at once.
    /// </summary>
    public static IReadOnlyList<BedrockDiagnostic> ValidateOrdered(
        IReadOnlyList<string> order, IReadOnlyList<string> cuts, BinEnds ends)
    {
        var diagnostics = new List<BedrockDiagnostic>();

        var position = new Dictionary<string, int>(order.Count, StringComparer.Ordinal);
        var orderValid = true;
        for (var i = 0; i < order.Count; i++)
        {
            // First occurrence wins; a duplicate or empty entry invalidates the domain.
            if (order[i].Length == 0 || !position.TryAdd(order[i], i))
            {
                orderValid = false;
            }
        }

        if (!orderValid)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.OrderDomainInvalid,
                DiagnosticSeverity.Error,
                "ordered_cuts order entries must be distinct and non-empty."));
        }

        if (cuts.Count < 1)
        {
            diagnostics.Add(TooFew("ordered_cuts"));
        }
        else if (ends == BinEnds.Closed && cuts.Count < 2)
        {
            diagnostics.Add(ClosedTooFew("ordered_cuts"));
        }

        // Membership against order; ascending is only well-defined for cuts that have a position.
        var positions = new List<int>(cuts.Count);
        foreach (var cut in cuts)
        {
            if (position.TryGetValue(cut, out var index))
            {
                positions.Add(index);
            }
            else
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.OrderedCutsCutNotInDomain,
                    DiagnosticSeverity.Error,
                    $"ordered_cuts cut '{cut}' is not a member of order."));
            }
        }

        if (positions.Count == cuts.Count && cuts.Count >= 2 && !StrictlyAscending(positions))
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.OrderedCutsNotAscending,
                DiagnosticSeverity.Error,
                "ordered_cuts cuts must be strictly ascending by order position."));
        }

        return diagnostics;
    }

    /// <summary>
    /// Whether <paramref name="cuts"/> is a usable numeric cut sequence: non-empty,
    /// every value finite, strictly ascending. The one shared post-derivation validity
    /// check for <b>computed</b> <c>equal_width</c> cuts (§11.4/§9.1) — the predicate is
    /// the same wherever they came from; only the diagnostic differs by phase
    /// (<see cref="DiagnosticCode.EqualWidthCutsCollapsed"/> at spec validate for an
    /// authored manual range, <see cref="DiagnosticCode.CalibrationCutsInvalid"/> at
    /// calibrate for a data-derived one, D-088/D-089).
    /// </summary>
    public static bool AreUsableCuts(IReadOnlyList<double> cuts) =>
        cuts.Count >= 1 && cuts.All(double.IsFinite) && StrictlyAscending(cuts);

    private static BedrockDiagnostic TooFew(string kind) =>
        new(DiagnosticCode.DiscretizerCutsTooFew, DiagnosticSeverity.Error,
            $"{kind} requires at least one cut.");

    private static BedrockDiagnostic ClosedTooFew(string kind) =>
        new(DiagnosticCode.DiscretizerEndsClosedTooFewCuts, DiagnosticSeverity.Error,
            $"{kind} with ends = \"closed\" requires at least two cuts.");

    private static bool StrictlyAscending(IReadOnlyList<double> values)
    {
        for (var i = 0; i + 1 < values.Count; i++)
        {
            if (!(values[i] < values[i + 1]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StrictlyAscending(IReadOnlyList<int> values)
    {
        for (var i = 0; i + 1 < values.Count; i++)
        {
            if (values[i] >= values[i + 1])
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatInvariant(double value) => value.ToString(CultureInfo.InvariantCulture);
}
