namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Pass-through discretizer: each distinct raw value is its own bin, with the
/// value itself as the label. Spec §11.1. The bin universe is the declared
/// domain, in declaration order.
/// </summary>
public sealed record IdentityDiscretizer : Discretizer
{
    public override string Kind => "identity";

    public override string? Discretize(string rawValue) => rawValue;

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => declaredDomain;
}
