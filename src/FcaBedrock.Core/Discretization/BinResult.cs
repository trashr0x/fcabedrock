using System.Diagnostics.CodeAnalysis;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// The kind of outcome <see cref="Discretizer.Discretize"/> produces for one raw
/// value. <see cref="NoBin"/> is the zero value so a default <see cref="BinResult"/>
/// is a harmless no-cross.
/// </summary>
public enum BinOutcome
{
    /// <summary>No bin: out of a closed range — object kept, no cross, silent (spec §11.2).</summary>
    NoBin,

    /// <summary>A recognized bin; its label is available via <see cref="BinResult.TryGetLabel"/>.</summary>
    Bin,

    /// <summary>A value outside the discretizer's domain — subject to <c>unknown_value_policy</c> (§10.6 / §11.8).</summary>
    Unknown,

    /// <summary>A present-but-unparseable numeric value — subject to <c>unknown_value_policy</c> (§11.5).</summary>
    Unparseable,
}

/// <summary>
/// The outcome of discretizing a single raw value. A four-way result that keeps the
/// silent no-cross case (<see cref="BinOutcome.NoBin"/>) distinct from the diagnosable
/// ones (<see cref="BinOutcome.Unknown"/>, <see cref="BinOutcome.Unparseable"/>),
/// replacing the old <c>string?</c> that collapsed all of these into "label or null"
/// (decisions.md D-059). A <c>readonly record struct</c> so the emit hot path stays
/// allocation-free (P-18) and tests get value equality.
/// </summary>
public readonly record struct BinResult
{
    private readonly string? _value;

    private BinResult(BinOutcome outcome, string? value)
    {
        Outcome = outcome;
        _value = value;
    }

    /// <summary>Which outcome this is.</summary>
    public BinOutcome Outcome { get; }

    /// <summary>
    /// The carried string: the bin label for <see cref="BinOutcome.Bin"/>; the offending
    /// raw value (the diagnostic sample) for <see cref="BinOutcome.Unknown"/> /
    /// <see cref="BinOutcome.Unparseable"/>; <see langword="null"/> for <see cref="BinOutcome.NoBin"/>.
    /// </summary>
    public string? Value => _value;

    /// <summary>A recognized bin with the given <paramref name="label"/>.</summary>
    public static BinResult Bin(string label) =>
        new(BinOutcome.Bin, label ?? throw new ArgumentNullException(nameof(label)));

    /// <summary>No bin: an out-of-range value under closed ends — kept, no cross, silent (§11.2).</summary>
    public static readonly BinResult NoBin = new(BinOutcome.NoBin, null);

    /// <summary>A value outside the discretizer's domain (§10.6 / §11.8), carrying it as the sample.</summary>
    public static BinResult Unknown(string rawValue) =>
        new(BinOutcome.Unknown, rawValue ?? throw new ArgumentNullException(nameof(rawValue)));

    /// <summary>A present-but-unparseable numeric value (§11.5), carrying it as the sample.</summary>
    public static BinResult Unparseable(string rawValue) =>
        new(BinOutcome.Unparseable, rawValue ?? throw new ArgumentNullException(nameof(rawValue)));

    /// <summary>
    /// Yields the bin label only when <see cref="Outcome"/> is <see cref="BinOutcome.Bin"/>.
    /// </summary>
    public bool TryGetLabel([MaybeNullWhen(false)] out string label)
    {
        if (Outcome == BinOutcome.Bin)
        {
            label = _value!;
            return true;
        }

        label = null;
        return false;
    }
}
