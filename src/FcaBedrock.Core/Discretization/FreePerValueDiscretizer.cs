using System.Globalization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// One bin per distinct observed value or declared-domain member (spec §11.3).
/// The type-flexible sibling of <see cref="IdentityDiscretizer"/> (§10.2, D-061):
/// with <see cref="SourceValueType.String"/> (the default) each raw spelling is its
/// own bin, verbatim; with <see cref="SourceValueType.Number"/> the bin identity is
/// the <b>parsed numeric value</b> — parsed with the injected
/// <see cref="CultureInfo"/> (never ambient — P-11) and rendered by the canonical
/// §14 number rule (<see cref="CanonicalNumber"/>), so <c>90</c>, <c>90.0</c>, and
/// <c>9e1</c> collapse to one bin labelled <c>90</c> and every zero spelling
/// (including <c>-0</c>) collapses to <c>0</c> (D-092/D-096). A present-but-unparseable
/// or non-finite numeric value is <see cref="BinResult.Unparseable"/> (§11.5), and
/// the emitter's <c>KnownBins</c> gate turns an otherwise-valid out-of-domain bin
/// into an unknown value (§10.6).
/// <para>
/// Like every value-bin discretizer its bin universe <b>is</b> the declared domain
/// (in declaration order); for a numeric source those domain keys are the
/// canonical numeric identities the resolve seam normalized (D-096).
/// </para>
/// </summary>
public sealed record FreePerValueDiscretizer : Discretizer
{
    /// <summary>How raw values are read: <see cref="SourceValueType.String"/> (verbatim bins) or numeric identity.</summary>
    public SourceValueType ValueType { get; }

    /// <summary>The culture used to parse raw values under <see cref="SourceValueType.Number"/> (never ambient — P-11).</summary>
    public CultureInfo Culture { get; }

    /// <summary>Creates the discretizer for <paramref name="valueType"/>, parsing numeric values with <paramref name="culture"/>.</summary>
    public FreePerValueDiscretizer(SourceValueType valueType, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ValueType = valueType;
        Culture = culture;
    }

    public override string Kind => "free_per_value";

    public override BinResult Discretize(string rawValue)
    {
        if (ValueType != SourceValueType.Number)
        {
            // String mode: the raw spelling is its own bin identity, no parsing (§11.3, D-061).
            return BinResult.Bin(rawValue);
        }

        // Number mode: the bin identity is the parsed value's canonical numeric identity
        // (§11.3/D-096) — parsed with the injected culture, zero-canonicalized, then the §14
        // shortest round-trippable form. A present-but-unparseable/non-finite value is kept,
        // no cross, diagnosable (§11.5); the KnownBins gate turns out-of-domain into unknown.
        if (!CanonicalNumber.TryParse(rawValue, Culture, out var value))
        {
            return BinResult.Unparseable(rawValue);
        }

        return BinResult.Bin(CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(value)));
    }

    // Value bins: the ordered bin universe IS the declared domain (already canonical
    // numeric keys for a numeric source, D-096). DescribeBins uses the value-bin base.
    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => declaredDomain;

    internal override bool ConsultsValueLabels => true;

    internal override bool ConsumesDeclaredDomain => true;
}
