using System.Globalization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// User-defined numeric cut points (spec §11.2). Composes the shared
/// <see cref="NumericCutBins"/> engine, which owns the parsing, classification,
/// labelling, and rendering every numeric-cut discretizer shares (P-17, D-093):
/// raw values are parsed to <see cref="double"/> with the injected
/// <see cref="CultureInfo"/> (never ambient — P-11); a value that fails to parse or
/// is non-finite gets no bin (§11.5), as does an out-of-range value under
/// <see cref="BinEnds.Closed"/> (§11.2). Cut labels are invariant schema strings,
/// not locale numbers (§14), so the same spec yields the same labels everywhere.
/// <para>
/// Constructed only through <see cref="Create"/>, which validates the cut spec
/// (<see cref="CutValidation.ValidateManual"/>) so an invalid one is unrepresentable
/// (P-10, D-056). The private constructor trusts its already-validated inputs.
/// </para>
/// </summary>
public sealed record ManualCutsDiscretizer : Discretizer
{
    private readonly NumericCutBins _bins;

    private ManualCutsDiscretizer(IReadOnlyList<double> cuts, BinEnds ends, CultureInfo culture) =>
        _bins = new NumericCutBins(cuts, ends, culture);

    /// <summary>The cut points, strictly ascending (validated by <see cref="Create"/>).</summary>
    public IReadOnlyList<double> Cuts => _bins.Cuts;

    /// <summary>Whether the outer bins extend to ±∞ (<see cref="BinEnds.Open"/>) or are dropped.</summary>
    public BinEnds Ends => _bins.Ends;

    /// <summary>The culture used to parse raw data values (never ambient — P-11).</summary>
    public CultureInfo Culture => _bins.Culture;

    /// <summary>
    /// Validates the cut spec and, if valid, builds the discretizer (spec §11.2,
    /// D-056). On any problem returns <see cref="Diagnosed{T}.Failed"/> with the
    /// cut diagnostics and never constructs — so the discretizer's invariants
    /// (non-empty / ascending / closed-ends ≥ 2) always hold. Wired into the
    /// resolve seam (D-067), the one path both TOML and migrated specs take.
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

    public override BinResult Discretize(string rawValue) => _bins.Discretize(rawValue);

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => _bins.BinLabels;

    internal override BinScheme DescribeBins(IReadOnlyList<string> declaredDomain) => _bins.DescribeBins();

    internal override string RenderBinLabel(string canonicalLabel, LabelStyle style) =>
        NumericCutBins.RenderBinLabel(canonicalLabel, style);
}
