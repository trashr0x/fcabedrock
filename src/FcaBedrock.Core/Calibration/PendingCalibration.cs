using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Calibration;

/// <summary>
/// The configuration a data-reading calibration must resolve for an
/// auto-discretizer (D-093). A mechanically-closed union — the
/// <see langword="private protected"/> base constructor admits no out-of-assembly
/// variant. The concrete variants land with the slices that own their parameter
/// types (M4 slices C/D/E); slice A ships only this abstract base and the
/// <see cref="CalibrationPending"/> carrier.
/// </summary>
public abstract record PendingCalibration
{
    private protected PendingCalibration()
    {
    }

    /// <summary>The authored discretizer kind this pending calibration resolves.</summary>
    public abstract string Kind { get; }
}

/// <summary>
/// The pre-calibration carrier (D-093): a valid <see cref="Discretizer"/> the
/// resolve seam can place on a resolved attribute, but one that can never plan or
/// emit. It is the single type <c>CalibratedSpec.Create</c> must replace with an
/// executable discretizer (or throw). Its <see cref="Discretize"/> and plan-time
/// members throw <see cref="InvalidOperationException"/> — reaching them is a
/// mis-sequenced call (calibration was skipped).
/// </summary>
public sealed record CalibrationPending : Discretizer
{
    /// <summary>Creates the carrier for <paramref name="config"/>, parsing raw values with <paramref name="culture"/>.</summary>
    public CalibrationPending(PendingCalibration config, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(culture);
        Config = config;
        Culture = culture;
    }

    /// <summary>The pending calibration configuration.</summary>
    public PendingCalibration Config { get; }

    /// <summary>The culture used once the auto-discretizer resolves (never ambient — P-11).</summary>
    public CultureInfo Culture { get; }

    /// <inheritdoc/>
    public override string Kind => Config.Kind;

    /// <inheritdoc/>
    public override BinResult Discretize(string rawValue) =>
        throw new InvalidOperationException(
            $"A '{Kind}' discretizer must be calibrated before emit; Discretize was called on an unresolved CalibrationPending (D-093).");

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) =>
        throw new InvalidOperationException(
            $"A '{Kind}' discretizer must be calibrated before planning; BinLabels was called on an unresolved CalibrationPending (D-093).");

    internal override BinScheme DescribeBins(IReadOnlyList<string> declaredDomain) =>
        throw new InvalidOperationException(
            $"A '{Kind}' discretizer must be calibrated before planning; DescribeBins was called on an unresolved CalibrationPending (D-093).");
}
