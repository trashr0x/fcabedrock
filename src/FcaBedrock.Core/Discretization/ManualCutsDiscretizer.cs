using System.Globalization;
using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// User-defined numeric cut points (spec §11.2). Raw values are parsed to
/// <see cref="double"/> with the injected <see cref="CultureInfo"/> (never ambient
/// — P-12); a value that fails to parse or is non-finite gets no bin (§11.5), as
/// does an out-of-range value under <see cref="BinEnds.Closed"/> (§11.2). Cut
/// labels are invariant schema strings, not locale numbers (§14), so the same
/// spec yields the same labels everywhere.
/// </summary>
public sealed record ManualCutsDiscretizer(IReadOnlyList<double> Cuts, BinEnds Ends, CultureInfo Culture) : Discretizer
{
    private readonly IReadOnlyList<string> _cutLabels = [.. Cuts.Select(FormatCut)];
    private readonly IReadOnlyList<string> _binLabels = CutBinLabels.Build([.. Cuts.Select(FormatCut)], Ends);

    public override string Kind => "manual_cuts";

    public override string? Discretize(string rawValue)
    {
        if (!double.TryParse(rawValue, NumberStyles.Float, Culture, out var value) || !double.IsFinite(value))
        {
            return null;
        }

        return CutBinLabels.LabelFor(_binLabels, FirstCutAbove(value), Cuts.Count, Ends);
    }

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => _binLabels;

    internal override BinScheme DescribeBins(IReadOnlyList<string> declaredDomain) =>
        new(_binLabels, _cutLabels, OpenLow: Ends == BinEnds.Open, OpenHigh: Ends == BinEnds.Open);

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
}
