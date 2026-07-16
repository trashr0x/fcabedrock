using System.Collections.Immutable;
using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Auto-computed cuts at equal width (spec §11.4). <see cref="Bins"/> bins come
/// from <c>bins - 1</c> interpolated cuts over the span the <see cref="Range"/>
/// mode supplies, always with <b>open</b> ends (§11.4: the auto-discretizer's ends
/// are implicit, so data outside the calibration span still falls in the first or
/// last bin). Execution composes the shared <see cref="NumericCutBins"/> engine —
/// identical geometry, labels, structural bins, and rendering to a
/// <see cref="ManualCutsDiscretizer"/> over the same cuts, which is what makes the
/// D-088 auto/frozen byte-equivalence structural (D-093).
/// <para>
/// The range mode decides the phase (D-089): <see cref="EqualWidthRange.Manual"/>
/// is spec-determined and resolves through <see cref="CreateManual"/> at the resolve
/// seam; a data-derived range resolves through a <see cref="CalibrationPending"/>
/// carrier and lands here via <see cref="FromCalibratedCuts"/> once the calibrator
/// has read the population. Both derive their cuts from the one formula
/// (<see cref="DeriveCuts"/>), so an auto spec and its frozen form carry the same
/// numbers by construction.
/// </para>
/// <para>
/// There is no public constructor: every path validates first, so an executable
/// discretizer with a non-finite or non-ascending cut sequence is unrepresentable
/// (P-10).
/// </para>
/// </summary>
public sealed record EqualWidthDiscretizer : Discretizer
{
    private readonly NumericCutBins _bins;

    private EqualWidthDiscretizer(
        int bins,
        EqualWidthRange range,
        double? vmin,
        double? vmax,
        CutPrecision precision,
        IReadOnlyList<double> cuts,
        CultureInfo culture)
    {
        Bins = bins;
        Range = range;
        VMin = vmin;
        VMax = vmax;
        Precision = precision;
        _bins = new NumericCutBins(cuts, BinEnds.Open, culture);
    }

    /// <summary>The authored bin count (≥ 2); the discretizer produces exactly this many bins.</summary>
    public int Bins { get; }

    /// <summary>The authored range mode — preserved for the fingerprint's authored-config rule (D-094).</summary>
    public EqualWidthRange Range { get; }

    /// <summary>The authored span minimum; non-null exactly when <see cref="Range"/> is <see cref="EqualWidthRange.Manual"/>.</summary>
    public double? VMin { get; }

    /// <summary>The authored span maximum; non-null exactly when <see cref="Range"/> is <see cref="EqualWidthRange.Manual"/>.</summary>
    public double? VMax { get; }

    /// <summary>The authored (or defaulted) cut rounding (§11.4).</summary>
    public CutPrecision Precision { get; }

    /// <summary>The effective resolved cuts: <c>Bins - 1</c> values, finite and strictly ascending.</summary>
    public IReadOnlyList<double> Cuts => _bins.Cuts;

    /// <summary>The culture used to parse raw data values (never ambient — P-11).</summary>
    public CultureInfo Culture => _bins.Culture;

    /// <summary>
    /// The spec-determined <c>range = "manual"</c> form (§11.4, D-089): the cuts come
    /// from <paramref name="vmin"/>/<paramref name="vmax"/> alone, so no calibration
    /// runs. Diagnostics (P-14): <see cref="DiagnosticCode.EqualWidthRangeInvalid"/>
    /// when the authored range is non-finite or non-increasing, and
    /// <see cref="DiagnosticCode.EqualWidthCutsCollapsed"/> when the derived cuts are
    /// not finite and strictly ascending after <paramref name="precision"/> (a
    /// <c>round_to</c> collapsing two cuts onto one value). Wired into the resolve
    /// seam (D-067) — the reader owns the field shapes, including <c>bins</c>, so
    /// <paramref name="bins"/> below 2 is the P-10 programmer-error backstop.
    /// </summary>
    public static Diagnosed<EqualWidthDiscretizer> CreateManual(
        int bins, double vmin, double vmax, CutPrecision precision, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(precision);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);

        // The range gate runs first: DeriveCuts requires a finite increasing span, and an
        // authored one that is not is the user's error, not a collapse (D-089).
        if (!double.IsFinite(vmin) || !double.IsFinite(vmax) || vmin >= vmax)
        {
            return Diagnosed<EqualWidthDiscretizer>.Failed([
                new BedrockDiagnostic(
                    DiagnosticCode.EqualWidthRangeInvalid, DiagnosticSeverity.Error,
                    $"equal_width range = \"manual\" requires finite vmin < vmax; got vmin = {Describe(vmin)}, vmax = {Describe(vmax)} (§11.4).")
            ]);
        }

        var cuts = DeriveCuts(bins, vmin, vmax, precision);
        if (!CutValidation.AreUsableCuts(cuts))
        {
            // The message names the outcome, not a cause: rounding is the usual culprit, but a
            // span narrower than `bins - 1` representable doubles collapses under "exact" too.
            return Diagnosed<EqualWidthDiscretizer>.Failed([
                new BedrockDiagnostic(
                    DiagnosticCode.EqualWidthCutsCollapsed, DiagnosticSeverity.Error,
                    $"equal_width with bins = {bins} over [{Describe(vmin)}, {Describe(vmax)}] derives cuts that are not strictly ascending " +
                    $"([{string.Join(", ", cuts.Select(Describe))}]); the range cannot be divided into {bins} distinct bins at this precision (§11.4).")
            ]);
        }

        return Diagnosed<EqualWidthDiscretizer>.Ok(
            new EqualWidthDiscretizer(bins, EqualWidthRange.Manual, vmin, vmax, precision, cuts, culture));
    }

    /// <summary>
    /// The data-derived form: the executable discretizer for <paramref name="config"/>
    /// over the cuts the calibrator derived from the observed span. Called only by
    /// <c>CalibratedSpec.Create</c> (same assembly), which owns the pending →
    /// executable substitution (D-093). Diagnostics (P-14):
    /// <see cref="DiagnosticCode.CalibrationCutsInvalid"/> when the calibrated cuts are
    /// not finite and strictly ascending — the calibrate-phase twin of
    /// <see cref="DiagnosticCode.EqualWidthCutsCollapsed"/> (D-088/D-089).
    /// </summary>
    internal static Diagnosed<EqualWidthDiscretizer> FromCalibratedCuts(
        PendingEqualWidth config, IReadOnlyList<double> cuts, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(cuts);
        ArgumentNullException.ThrowIfNull(culture);

        // §11.4: `bins` bins come from exactly `bins - 1` cuts. A wrong-sized outcome is a
        // calibrator-contract violation, not a data-derived failure: it would build a discretizer
        // whose Bins disagrees with its own geometry — the fingerprint would encode "bins":4 beside
        // a two-bin schema array. That state must be unrepresentable, so it throws rather than
        // diagnosing (the D-093 programmer-error posture, P-10). Cut *validity* below stays a
        // diagnostic: correctly-sized cuts that the data could not make ascending are expected.
        if (cuts.Count != config.Bins - 1)
        {
            throw new ArgumentException(
                $"equal_width with bins = {config.Bins} requires exactly {config.Bins - 1} calibrated cuts, but {cuts.Count} were supplied (§11.4/D-093).",
                nameof(cuts));
        }

        if (!CutValidation.AreUsableCuts(cuts))
        {
            return Diagnosed<EqualWidthDiscretizer>.Failed([
                new BedrockDiagnostic(
                    DiagnosticCode.CalibrationCutsInvalid, DiagnosticSeverity.Error,
                    $"equal_width calibration derived cuts that are not finite and strictly ascending " +
                    $"([{string.Join(", ", cuts.Select(Describe))}]); the calibrated span and precision cannot produce {config.Bins} bins (§11.4).")
            ]);
        }

        // A data-derived range authors no vmin/vmax: they stay null, so the fingerprint
        // omits them (D-094) and the authored config round-trips exactly.
        return Diagnosed<EqualWidthDiscretizer>.Ok(
            new EqualWidthDiscretizer(config.Bins, config.Range, vmin: null, vmax: null, config.Precision, cuts, culture));
    }

    /// <summary>
    /// The one <c>equal_width</c> cut formula (spec §11.4; the G-5 pinned expression
    /// order). Internal: <see cref="CreateManual"/> is the single boundary through
    /// which cuts are derived — the Conversion calibrator invokes that factory over
    /// the <b>calibrated</b> span rather than re-deriving, so auto and frozen cuts are
    /// the same numbers by construction (D-088) with no second copy of the formula and
    /// no public surface beyond the approved inventory (P-4).
    /// <para>
    /// For <c>i = 1 .. bins - 1</c> with <c>t = (double)i / bins</c>, the interpolation
    /// is <b>sign-aware</b> so that no finite increasing span can overflow: a same-sign
    /// span (or one with a zero bound) uses <c>vmin + (vmax - vmin) * t</c>, whose
    /// difference is bounded by the larger magnitude; a span crossing zero uses the
    /// convex combination <c>vmin * (1 - t) + vmax * t</c>, whose terms are each
    /// bounded by their own operand — so a range like
    /// <c>[-1.7e308, 1.7e308]</c> derives finite cuts rather than an overflow
    /// (G-7: every finite increasing range is accepted; the derived-cut check is
    /// defense in depth). <paramref name="precision"/> then rounds
    /// (<see cref="MidpointRounding.ToEven"/>, pinned), and every cut is
    /// positive-zero canonicalized so a computed <c>-0</c> never reaches a bin
    /// identity, label, or hash (G-6/D-096).
    /// </para>
    /// <para>
    /// Callers gate the range first — the manual factory with
    /// <see cref="DiagnosticCode.EqualWidthRangeInvalid"/>, the calibrator with
    /// <see cref="DiagnosticCode.CalibrationDataInsufficient"/> — so a non-finite or
    /// non-increasing span here is programmer error
    /// (<see cref="ArgumentOutOfRangeException"/>, P-10). The returned cuts may still
    /// be unusable after rounding; the caller validates them
    /// (<see cref="CutValidation.AreUsableCuts"/>).
    /// </para>
    /// </summary>
    internal static IReadOnlyList<double> DeriveCuts(int bins, double vmin, double vmax, CutPrecision precision)
    {
        ArgumentNullException.ThrowIfNull(precision);
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);
        if (!double.IsFinite(vmin) || !double.IsFinite(vmax) || vmin >= vmax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(vmin), $"equal_width cut derivation requires a finite increasing span; got [{Describe(vmin)}, {Describe(vmax)}].");
        }

        var crossesZero = vmin < 0.0 && vmax > 0.0;
        var cuts = ImmutableArray.CreateBuilder<double>(bins - 1);
        for (var i = 1; i < bins; i++)
        {
            var t = (double)i / bins;

            // The two branches are the pinned expression order (G-5); do not reorder or
            // fold them into a single "always vmax - vmin" form, which overflows on a
            // wide opposite-sign span.
            var cut = crossesZero
                ? (vmin * (1.0 - t)) + (vmax * t)
                : vmin + ((vmax - vmin) * t);

            if (precision is RoundToPrecision roundTo)
            {
                cut = Math.Round(cut / roundTo.RoundTo, MidpointRounding.ToEven) * roundTo.RoundTo;
            }

            cuts.Add(CanonicalNumber.CanonicalizeZero(cut));
        }

        return cuts.MoveToImmutable();
    }

    /// <summary>
    /// Rebuilds <paramref name="source"/> over <paramref name="culture"/>, reusing its
    /// already-validated state. The <c>ResolvedSpec</c> trust boundary's recursive-immutable
    /// snapshot uses this to re-home the discretizer on a read-only culture clone (D-098),
    /// without re-deriving cuts — the retained cuts are the identity.
    /// </summary>
    internal static EqualWidthDiscretizer Rebuild(EqualWidthDiscretizer source, CultureInfo culture) =>
        new(source.Bins, source.Range, source.VMin, source.VMax, source.Precision, source.Cuts, culture);

    public override string Kind => "equal_width";

    public override BinResult Discretize(string rawValue) => _bins.Discretize(rawValue);

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => _bins.BinLabels;

    internal override BinScheme DescribeBins(IReadOnlyList<string> declaredDomain) => _bins.DescribeBins();

    internal override string RenderBinLabel(string canonicalLabel, LabelStyle style) =>
        NumericCutBins.RenderBinLabel(canonicalLabel, style);

    // Diagnostic text only: the values here may be non-finite (that is what is being
    // reported), so CanonicalNumber.Format — which refuses them — cannot be used.
    private static string Describe(double value) => value.ToString(CultureInfo.InvariantCulture);
}
