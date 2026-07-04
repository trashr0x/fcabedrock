using System.Globalization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// User-defined numeric cut points (spec §11.2). Raw values are parsed to
/// <see cref="double"/> with the injected <see cref="CultureInfo"/> (never ambient
/// — P-12); a value that fails to parse or is non-finite gets no bin (§11.5), as
/// does an out-of-range value under <see cref="BinEnds.Closed"/> (§11.2). Cut
/// labels are invariant schema strings, not locale numbers (§14), so the same
/// spec yields the same labels everywhere.
/// <para>
/// Constructed only through <see cref="Create"/>, which validates the cut spec
/// (<see cref="CutValidation.ValidateManual"/>) so an invalid one is unrepresentable
/// (P-10, D-056). The private constructor trusts its already-validated inputs.
/// </para>
/// </summary>
public sealed record ManualCutsDiscretizer : Discretizer
{
    /// <summary>The cut points, strictly ascending (validated by <see cref="Create"/>).</summary>
    public IReadOnlyList<double> Cuts { get; }

    /// <summary>Whether the outer bins extend to ±∞ (<see cref="BinEnds.Open"/>) or are dropped.</summary>
    public BinEnds Ends { get; }

    /// <summary>The culture used to parse raw data values (never ambient — P-12).</summary>
    public CultureInfo Culture { get; }

    private readonly IReadOnlyList<string> _cutLabels;
    private readonly IReadOnlyList<string> _binLabels;
    private readonly IReadOnlyList<CanonicalBin> _structuralBins;

    private ManualCutsDiscretizer(IReadOnlyList<double> cuts, BinEnds ends, CultureInfo culture)
    {
        // Snapshot the caller's list: a mutable input must not desync Cuts from the cached
        // labels after construction (P-10 — the validated invariants stay true for life).
        Cuts = [.. cuts];
        Ends = ends;
        Culture = culture;
        _cutLabels = [.. Cuts.Select(FormatCut)];
        _binLabels = CutBinLabels.Build(_cutLabels, ends);
        _structuralBins = BuildStructuralBins(Cuts, ends);
    }

    /// <summary>
    /// Validates the cut spec and, if valid, builds the discretizer (spec §11.2,
    /// D-056). On any problem returns <see cref="Diagnosed{T}.Failed"/> with the
    /// cut diagnostics and never constructs — so the discretizer's invariants
    /// (non-empty / ascending / closed-ends ≥ 2) always hold. Wired into
    /// <c>BedToSpec</c> now; reused by the M2 TOML reader.
    /// </summary>
    public static Diagnosed<ManualCutsDiscretizer> Create(
        IReadOnlyList<double> cuts, BinEnds ends, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(cuts);
        ArgumentNullException.ThrowIfNull(culture);

        var diagnostics = CutValidation.ValidateManual(cuts, ends);
        return diagnostics.Count == 0
            ? Diagnosed<ManualCutsDiscretizer>.Ok(new ManualCutsDiscretizer(cuts, ends, culture))
            : Diagnosed<ManualCutsDiscretizer>.Failed(diagnostics);
    }

    public override string Kind => "manual_cuts";

    public override BinResult Discretize(string rawValue)
    {
        if (!double.TryParse(rawValue, NumberStyles.Float, Culture, out var value) || !double.IsFinite(value))
        {
            return BinResult.Unparseable(rawValue); // §11.5 / D-050: kept, no cross, diagnosable
        }

        var label = CutBinLabels.LabelFor(_binLabels, FirstCutAbove(value), Cuts.Count, Ends);
        return label is null ? BinResult.NoBin : BinResult.Bin(label); // null ⇒ out of a closed range (§11.2)
    }

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => _binLabels;

    internal override BinScheme DescribeBins(IReadOnlyList<string> declaredDomain) =>
        new(_binLabels, _structuralBins, _cutLabels, OpenLow: Ends == BinEnds.Open, OpenHigh: Ends == BinEnds.Open);

    internal override string RenderBinLabel(string canonicalLabel, LabelStyle style) =>
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

    private static string FormatCut(double cut) => cut.ToString(CultureInfo.InvariantCulture);

    // Index-aligned with CutBinLabels.Build: open ends add the two unbounded outer
    // bins around the interiors; closed ends keep interiors only. A null bound is
    // the unbounded (±∞) end — never interval inclusivity (D-077).
    private static IReadOnlyList<CanonicalBin> BuildStructuralBins(IReadOnlyList<double> cuts, BinEnds ends)
    {
        var bins = new List<CanonicalBin>(cuts.Count + 1);
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

        return bins;
    }
}
