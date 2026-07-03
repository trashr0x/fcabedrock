using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// An authored attribute <c>discretizer</c> (§11). This slice models the three
/// M2-executable kinds only (D-070 tier 1); the deferred kinds
/// (<c>free_per_value</c>, <c>equal_width</c>, <c>equal_frequency</c>,
/// <c>value_groups</c>) are recognized-and-rejected by the reader slice
/// (D-070 tier 2) and gain no document carrier here.
/// </summary>
public abstract record DiscretizerSection;

/// <summary>The <c>identity</c> discretizer (§11.1): each raw value is its own bin.</summary>
public sealed record IdentityDiscretizerSection : DiscretizerSection;

/// <summary>The <c>manual_cuts</c> discretizer (§11.2): numeric bins between authored cuts.</summary>
/// <param name="Cuts">Ascending cut points; null when not authored (diagnosed via the D-056 factory).</param>
/// <param name="Ends">Whether the outer bins are open or closed (§11.2).</param>
public sealed record ManualCutsDiscretizerSection(IReadOnlyList<double>? Cuts, BinEnds? Ends) : DiscretizerSection;

/// <summary>The <c>ordered_cuts</c> discretizer (§11.8): categorical bins cut over an ordered domain.</summary>
/// <param name="Order">The ordered domain; null when not authored.</param>
/// <param name="Cuts">Cut members of <paramref name="Order"/>; null when not authored.</param>
/// <param name="Ends">Whether the outer bins are open or closed (§11.8).</param>
public sealed record OrderedCutsDiscretizerSection(
    IReadOnlyList<string>? Order,
    IReadOnlyList<string>? Cuts,
    BinEnds? Ends) : DiscretizerSection;
