using System.Collections.Immutable;
using System.Globalization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// The one execution engine for <b>numeric</b> cut bins, composed by every
/// discretizer whose bins are numeric cut intervals — <see cref="ManualCutsDiscretizer"/>
/// (authored cuts, §11.2) and <see cref="EqualWidthDiscretizer"/> (computed cuts,
/// always open-ended, §11.4). It owns numeric parsing, finite-only classification,
/// cut membership, the cut and bin labels, the structural interval bins, and the
/// native/v2-compat rendering, so those formulas exist once (P-17).
/// <para>
/// This is what makes the D-088 auto/frozen byte-equivalence <b>structural</b>
/// rather than a property two code paths must independently maintain (D-093): the
/// same effective cuts through this engine give the same bins, labels, canonical
/// identities, and crosses, whether they were authored or calibrated.
/// </para>
/// <para>
/// The constructor trusts its inputs: the owning discretizer's smart factory has
/// already validated the cuts as finite and strictly ascending (and, for
/// <see cref="BinEnds.Closed"/>, at least two), so labelling never sees a NaN/∞
/// edge (P-10).
/// </para>
/// </summary>
internal sealed class NumericCutBins
{
    private readonly ImmutableArray<string> _cutLabels;
    private readonly ImmutableArray<string> _binLabels;
    private readonly ImmutableArray<CanonicalBin> _structuralBins;

    /// <summary>Builds the engine over already-validated <paramref name="cuts"/>.</summary>
    public NumericCutBins(IReadOnlyList<double> cuts, BinEnds ends, CultureInfo culture)
    {
        // Snapshot into immutable storage: a mutable caller list must not desync the cuts
        // from the cached labels after construction, and no castable mutable backing array
        // may survive on the resolved/planned graph (P-10, D-098 recursive immutability).
        Cuts = ImmutableArray.CreateRange(cuts);
        Ends = ends;
        Culture = culture;
        _cutLabels = [.. Cuts.Select(FormatCut)];
        _binLabels = [.. CutBinLabels.Build(_cutLabels, ends)];
        _structuralBins = BuildStructuralBins(Cuts, ends);
    }

    /// <summary>The cut points, strictly ascending and finite.</summary>
    public IReadOnlyList<double> Cuts { get; }

    /// <summary>Whether the outer bins extend to ±∞ (<see cref="BinEnds.Open"/>) or are dropped.</summary>
    public BinEnds Ends { get; }

    /// <summary>The culture raw values parse under (never ambient — P-11).</summary>
    public CultureInfo Culture { get; }

    /// <summary>The canonical cut labels — invariant schema strings, not locale numbers (§14).</summary>
    public IReadOnlyList<string> CutLabels => _cutLabels;

    /// <summary>The ordered canonical bin labels (§11.2).</summary>
    public IReadOnlyList<string> BinLabels => _binLabels;

    /// <summary>
    /// Classifies one non-missing raw value: parsed under <see cref="Culture"/>,
    /// finite-only. A value that fails to parse or is non-finite is
    /// <see cref="BinResult.Unparseable"/> — kept, no cross, diagnosable (§11.5 /
    /// D-050); a value outside a <see cref="BinEnds.Closed"/> range gets no bin
    /// (§11.2).
    /// </summary>
    public BinResult Discretize(string rawValue)
    {
        if (!CanonicalNumber.TryParse(rawValue, Culture, out var value))
        {
            return BinResult.Unparseable(rawValue);
        }

        var label = CutBinLabels.LabelFor(_binLabels, FirstCutAbove(value), Cuts.Count, Ends);
        return label is null ? BinResult.NoBin : BinResult.Bin(label);
    }

    /// <summary>The full cut-bin structure a scale thresholds on (§12.3).</summary>
    public BinScheme DescribeBins() =>
        new(_binLabels, _structuralBins, _cutLabels,
            OpenLow: Ends == BinEnds.Open, OpenHigh: Ends == BinEnds.Open, CutBins: true);

    /// <summary>Renders one canonical bin label for <paramref name="style"/> (the v2-compat interior form, D-044).</summary>
    public static string RenderBinLabel(string canonicalLabel, LabelStyle style) =>
        CutBinLabels.Render(canonicalLabel, style);

    // Index of the first cut strictly greater than value (Cuts.Count if none).
    private int FirstCutAbove(double value)
    {
        for (var i = 0; i < Cuts.Count; i++)
        {
            if (value < Cuts[i])
            {
                return i;
            }
        }

        return Cuts.Count;
    }

    // The §14 canonical number rule (D-092/D-096): invariant, shortest round-trippable, so a
    // cut labels identically everywhere. The factories validate finiteness before construction,
    // so Format never sees a non-finite cut.
    private static string FormatCut(double cut) => CanonicalNumber.Format(cut);

    // Index-aligned with CutBinLabels.Build: open ends add the two unbounded outer
    // bins around the interiors; closed ends keep interiors only. A null bound is
    // the unbounded (±∞) end — never interval inclusivity (D-077).
    private static ImmutableArray<CanonicalBin> BuildStructuralBins(IReadOnlyList<double> cuts, BinEnds ends)
    {
        var bins = ImmutableArray.CreateBuilder<CanonicalBin>(cuts.Count + 1);
        if (ends == BinEnds.Open)
        {
            bins.Add(new NumericCutBin(Lo: null, Hi: cuts[0]));
        }

        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            bins.Add(new NumericCutBin(cuts[i], cuts[i + 1]));
        }

        if (ends == BinEnds.Open)
        {
            bins.Add(new NumericCutBin(Lo: cuts[^1], Hi: null));
        }

        return bins.ToImmutable();
    }
}
