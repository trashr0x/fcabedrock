namespace FcaBedrock.Core.Discretization;

/// <summary>
/// The rounding applied to a computed <c>equal_width</c> cut (spec §11.4
/// <c>precision</c>). A mechanically-closed union — the
/// <see langword="private protected"/> base constructor admits no out-of-assembly
/// variant, so the derivation and fingerprint switches are exhaustive. Immutable
/// and culture-free: rounding is pure binary64 arithmetic, never a locale concern
/// (P-11).
/// </summary>
public abstract record CutPrecision
{
    private protected CutPrecision()
    {
    }

    /// <summary>The <c>"exact"</c> precision (the §11.4 default): computed cuts are used unrounded.</summary>
    public static ExactPrecision Exact { get; } = new();
}

/// <summary>
/// <c>precision = "exact"</c> (§11.4): the interpolated cut is used as computed.
/// <see cref="CutPrecision.Exact"/> is the singleton convenience value; the public
/// constructor exists because every instance is equal to every other (a record
/// with no state).
/// </summary>
public sealed record ExactPrecision : CutPrecision;

/// <summary>
/// <c>precision = { round_to = r }</c> (§11.4): each computed cut is rounded to
/// the nearest multiple of <see cref="RoundTo"/>, halfway cases to even
/// (<see cref="MidpointRounding.ToEven"/> — pinned, P-11). Rounding may collapse
/// two cuts onto one value; that is diagnosed where the cuts are derived
/// (<c>EqualWidthCutsCollapsed</c> at spec validate, <c>CalibrationCutsInvalid</c>
/// at calibrate), never here.
/// </summary>
public sealed record RoundToPrecision : CutPrecision
{
    private RoundToPrecision(double roundTo) => RoundTo = roundTo;

    /// <summary>The rounding step: finite and strictly greater than zero (validated by <see cref="Create"/>).</summary>
    public double RoundTo { get; }

    /// <summary>
    /// Builds the precision for <paramref name="roundTo"/>. Throws
    /// <see cref="ArgumentOutOfRangeException"/> unless it is finite and strictly
    /// greater than zero — the reader rejects the authored form first
    /// (<c>SpecFieldInvalid</c>, §11.4); this is the P-10 backstop, so an unusable
    /// rounding step is unrepresentable.
    /// </summary>
    public static RoundToPrecision Create(double roundTo)
    {
        if (!double.IsFinite(roundTo) || roundTo <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roundTo), roundTo, "round_to must be a finite number greater than zero (§11.4).");
        }

        return new RoundToPrecision(roundTo);
    }
}
