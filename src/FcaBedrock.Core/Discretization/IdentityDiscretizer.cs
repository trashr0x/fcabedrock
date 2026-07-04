namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Pass-through discretizer: each distinct raw value is its own bin, with the
/// value itself as the label. Spec §11.1. The bin universe is the declared
/// domain, in declaration order.
/// </summary>
public sealed record IdentityDiscretizer : Discretizer
{
    public override string Kind => "identity";

    // The raw value is its own bin label; the emitter's KnownBins gate turns a value
    // outside the declared domain into an unknown (identity has no domain of its own).
    public override BinResult Discretize(string rawValue) => BinResult.Bin(rawValue);

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => declaredDomain;

    internal override bool ConsultsValueLabels => true;

    internal override bool ConsumesDeclaredDomain => true;
}
