using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Auto-computed cuts placed for approximately equal counts per bin (spec §11.5).
/// <see cref="Bins"/> bins come from <c>bins - 1</c> calibrated cuts, always with
/// <b>open</b> ends (§11.5 shares §11.4's implicit auto-discretizer geometry, so data
/// outside the calibration span still falls in the first or last bin). Execution
/// composes the shared <see cref="NumericCutBins"/> engine — identical geometry,
/// labels, structural bins, and rendering to a <see cref="ManualCutsDiscretizer"/>
/// over the same cuts, which is what makes the D-088 auto/frozen byte-equivalence
/// structural rather than a property two code paths maintain (D-093).
/// <para>
/// Unlike <see cref="EqualWidthDiscretizer"/> there is no spec-determined form:
/// equal-frequency cuts are always drawn from the calibration population, so the
/// only route here is <see cref="FromCalibratedCuts"/> via the
/// <see cref="CalibrationPending"/> carrier the Calibrate phase replaces. There is no
/// public constructor: an executable discretizer with a non-finite or non-ascending
/// cut sequence, or one whose cut count contradicts its own <see cref="Bins"/>, is
/// unrepresentable (P-10).
/// </para>
/// <para>
/// <see cref="TiePolicy"/> and <see cref="CutPlacement"/> are retained
/// <b>authored configuration</b> — they shaped the cuts during calibration and are
/// spent by the time this type exists (§11.5: Emit does no tie handling of its own).
/// They survive only for the fingerprint's authored-config rule (D-094).
/// </para>
/// </summary>
public sealed record EqualFrequencyDiscretizer : Discretizer
{
    private readonly NumericCutBins _bins;

    private EqualFrequencyDiscretizer(
        int bins,
        TiePolicy tiePolicy,
        CutPlacement cutPlacement,
        IReadOnlyList<double> cuts,
        CultureInfo culture)
    {
        Bins = bins;
        TiePolicy = tiePolicy;
        CutPlacement = cutPlacement;
        _bins = new NumericCutBins(cuts, BinEnds.Open, culture);
    }

    /// <summary>The authored bin count (≥ 2); the discretizer produces exactly this many bins.</summary>
    public int Bins { get; }

    /// <summary>The authored (or defaulted) tie policy — preserved for the fingerprint's authored-config rule (D-094).</summary>
    public TiePolicy TiePolicy { get; }

    /// <summary>The authored (or defaulted) cut placement — preserved for the fingerprint's authored-config rule (D-094).</summary>
    public CutPlacement CutPlacement { get; }

    /// <summary>The effective calibrated cuts: <c>Bins - 1</c> values, finite and strictly ascending.</summary>
    public IReadOnlyList<double> Cuts => _bins.Cuts;

    /// <summary>The culture used to parse raw data values (never ambient — P-11).</summary>
    public CultureInfo Culture => _bins.Culture;

    /// <summary>
    /// The executable discretizer for <paramref name="config"/> over the cuts the
    /// calibrator selected from the population. Called only by
    /// <c>CalibratedSpec.Create</c> (same assembly), which owns the pending →
    /// executable substitution (D-093). Diagnostics (P-14):
    /// <see cref="DiagnosticCode.CalibrationCutsInvalid"/> when the calibrated cuts are
    /// not finite and strictly ascending.
    /// </summary>
    internal static Diagnosed<EqualFrequencyDiscretizer> FromCalibratedCuts(
        PendingEqualFrequency config, IReadOnlyList<double> cuts, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(cuts);
        ArgumentNullException.ThrowIfNull(culture);

        // §11.5: `bins` bins come from exactly `bins - 1` cuts. A wrong-sized outcome is a
        // calibrator-contract violation, not a data-derived failure: it would build a discretizer
        // whose Bins disagrees with its own geometry — the fingerprint would encode "bins":4 beside
        // a schema array of another width. That state must be unrepresentable, so it throws rather
        // than diagnosing (the D-093 programmer-error posture, P-10). Cut *validity* below stays a
        // diagnostic: correctly-sized cuts the data could not make ascending are expected.
        if (cuts.Count != config.Bins - 1)
        {
            throw new ArgumentException(
                $"equal_frequency with bins = {config.Bins} requires exactly {config.Bins - 1} calibrated cuts, but {cuts.Count} were supplied (§11.5/D-093).",
                nameof(cuts));
        }

        if (!CutValidation.AreUsableCuts(cuts))
        {
            return Diagnosed<EqualFrequencyDiscretizer>.Failed([
                new BedrockDiagnostic(
                    DiagnosticCode.CalibrationCutsInvalid, DiagnosticSeverity.Error,
                    $"equal_frequency calibration derived cuts that are not finite and strictly ascending " +
                    $"([{string.Join(", ", cuts.Select(Describe))}]); the calibrated population cannot produce {config.Bins} bins (§11.5).")
            ]);
        }

        return Diagnosed<EqualFrequencyDiscretizer>.Ok(
            new EqualFrequencyDiscretizer(config.Bins, config.TiePolicy, config.CutPlacement, cuts, culture));
    }

    /// <summary>
    /// Rebuilds <paramref name="source"/> over <paramref name="culture"/>, reusing its
    /// already-validated state. The <c>ResolvedSpec</c> trust boundary's recursive-immutable
    /// snapshot uses this to re-home the discretizer on a read-only culture clone (D-098),
    /// without re-selecting cuts — the retained cuts are the identity (there is no data here
    /// to re-select from, which is exactly why they must be carried, not recomputed).
    /// </summary>
    internal static EqualFrequencyDiscretizer Rebuild(EqualFrequencyDiscretizer source, CultureInfo culture) =>
        new(source.Bins, source.TiePolicy, source.CutPlacement, source.Cuts, culture);

    public override string Kind => "equal_frequency";

    public override BinResult Discretize(string rawValue) => _bins.Discretize(rawValue);

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => _bins.BinLabels;

    internal override BinScheme DescribeBins(IReadOnlyList<string> declaredDomain) => _bins.DescribeBins();

    internal override string RenderBinLabel(string canonicalLabel, LabelStyle style) =>
        NumericCutBins.RenderBinLabel(canonicalLabel, style);

    // Diagnostic text only: the values here may be non-finite (that is what is being
    // reported), so CanonicalNumber.Format — which refuses them — cannot be used.
    private static string Describe(double value) => value.ToString(CultureInfo.InvariantCulture);
}
