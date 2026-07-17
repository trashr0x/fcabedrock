using System.Collections.Immutable;
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
/// The <c>equal_width</c> configuration a data-derived range must resolve (§11.4,
/// D-089): the authored bin count, range mode, and precision carried into
/// calibration, so the calibrator reads the population and
/// <c>CalibratedSpec.Create</c> substitutes the executable
/// <see cref="EqualWidthDiscretizer"/> over the derived cuts (D-093). Holds only
/// immutable values.
/// <para>
/// <see cref="EqualWidthRange.Manual"/> never pends — it is spec-determined and
/// resolves straight to an executable discretizer (§7) — so it is rejected here
/// (P-10: the mis-sequenced state is unrepresentable rather than merely diagnosed).
/// </para>
/// </summary>
public sealed record PendingEqualWidth : PendingCalibration
{
    /// <summary>Carries <paramref name="bins"/>/<paramref name="range"/>/<paramref name="precision"/> into calibration.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="range"/> is <see cref="EqualWidthRange.Manual"/> (spec-determined,
    /// never pending) or undefined, or <paramref name="bins"/> is below 2 — the reader
    /// owns the authored forms (<c>SpecFieldInvalid</c>, §11.4); these are the P-10 backstops.
    /// </exception>
    public PendingEqualWidth(int bins, EqualWidthRange range, CutPrecision precision)
    {
        ArgumentNullException.ThrowIfNull(precision);
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);
        if (!Enum.IsDefined(range))
        {
            throw new ArgumentOutOfRangeException(nameof(range), range, "equal_width range holds an undefined enum value.");
        }

        if (range == EqualWidthRange.Manual)
        {
            throw new ArgumentOutOfRangeException(
                nameof(range), range,
                "equal_width range = \"manual\" is spec-determined and never pends calibration; build it with EqualWidthDiscretizer.CreateManual (§11.4/D-089).");
        }

        Bins = bins;
        Range = range;
        Precision = precision;
    }

    /// <summary>The authored bin count (≥ 2).</summary>
    public int Bins { get; }

    /// <summary>The data-derived range mode the calibrator must resolve.</summary>
    public EqualWidthRange Range { get; }

    /// <summary>The authored (or defaulted) cut rounding, applied to the derived cuts.</summary>
    public CutPrecision Precision { get; }

    /// <inheritdoc/>
    public override string Kind => "equal_width";
}

/// <summary>
/// The <c>equal_frequency</c> configuration calibration must resolve (§11.5, D-088):
/// the authored bin count, tie policy, and cut placement carried into calibration, so
/// the calibrator reads the population and <c>CalibratedSpec.Create</c> substitutes the
/// executable <see cref="EqualFrequencyDiscretizer"/> over the selected cuts (D-093).
/// Holds only immutable values.
/// <para>
/// Unlike <see cref="PendingEqualWidth"/> there is no mode that escapes calibration:
/// every <c>equal_frequency</c> spec is data-dependent (§7), so this carrier has no
/// spec-determined counterpart to reject.
/// </para>
/// </summary>
public sealed record PendingEqualFrequency : PendingCalibration
{
    /// <summary>Carries <paramref name="bins"/>/<paramref name="tiePolicy"/>/<paramref name="cutPlacement"/> into calibration.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bins"/> is below 2, or <paramref name="tiePolicy"/> /
    /// <paramref name="cutPlacement"/> is undefined — the reader owns the authored forms
    /// (<c>SpecFieldInvalid</c>, §11.5); these are the P-10 backstops.
    /// </exception>
    public PendingEqualFrequency(int bins, TiePolicy tiePolicy, CutPlacement cutPlacement)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);
        if (!Enum.IsDefined(tiePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(tiePolicy), tiePolicy, "equal_frequency tie_policy holds an undefined enum value.");
        }

        if (!Enum.IsDefined(cutPlacement))
        {
            throw new ArgumentOutOfRangeException(nameof(cutPlacement), cutPlacement, "equal_frequency cut_placement holds an undefined enum value.");
        }

        Bins = bins;
        TiePolicy = tiePolicy;
        CutPlacement = cutPlacement;
    }

    /// <summary>The authored bin count (≥ 2).</summary>
    public int Bins { get; }

    /// <summary>The authored (or defaulted) tie policy the gap selection applies (§11.5).</summary>
    public TiePolicy TiePolicy { get; }

    /// <summary>The authored (or defaulted) cut placement within each selected gap (§11.5).</summary>
    public CutPlacement CutPlacement { get; }

    /// <inheritdoc/>
    public override string Kind => "equal_frequency";
}

/// <summary>
/// The <c>value_groups</c> <c>unmatched = "passthrough"</c> configuration calibration must
/// resolve (§11.6, D-055/D-090): the authored groups carried into calibration, so the
/// calibrator discovers the ungrouped raw values and <c>CalibratedSpec.Create</c> substitutes
/// the executable <see cref="ValueGroupsDiscretizer"/> over the retained
/// <see cref="PassthroughBins"/> (D-093).
/// <para>
/// Recursively immutable: this constructor snapshots the group list, and each
/// <see cref="ValueGroup"/> already snapshots its own authored values — so no caller-owned
/// list survives on the graph. Unlike the numeric carriers there is no bin count or policy to
/// validate; a group's own validity is <see cref="ValueGroup.Create"/>'s contract, and label
/// distinctness is checked where the executable form is built (and re-checked at the
/// <c>ResolvedSpec</c> trust boundary, since this carrier is freely constructible).
/// </para>
/// </summary>
public sealed record PendingValueGroupsPassthrough : PendingCalibration
{
    private readonly ImmutableArray<ValueGroup> _groups;

    /// <summary>Carries the authored <paramref name="groups"/> into passthrough calibration.</summary>
    public PendingValueGroupsPassthrough(IReadOnlyList<ValueGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        _groups = groups.ToImmutableArray();
        foreach (var group in _groups)
        {
            ArgumentNullException.ThrowIfNull(group, nameof(groups));
        }
    }

    /// <summary>The authored groups in declaration order (first match wins, §11.6).</summary>
    public IReadOnlyList<ValueGroup> Groups => _groups;

    /// <inheritdoc/>
    public override string Kind => "value_groups";
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
