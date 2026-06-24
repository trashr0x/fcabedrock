namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Maps a raw value to a bin label (the discretizer half of the orthogonal
/// discretizer × scale model — decisions.md D-002). A closed set within Core;
/// external assemblies construct the concrete kinds but cannot derive new ones
/// (the plan-time member is internal).
/// </summary>
public abstract record Discretizer
{
    /// <summary>Stable kind identifier (e.g. <c>identity</c>).</summary>
    public abstract string Kind { get; }

    /// <summary>
    /// Emit-time: map a single non-missing raw value to a bin label, or
    /// <see langword="null"/> for "no bin" (out of range). Missing detection
    /// happens before this is called.
    /// </summary>
    public abstract string? Discretize(string rawValue);

    /// <summary>
    /// Plan-time: the ordered bin labels the scale will see for an attribute with
    /// the given declared domain. Centralizes determinism rule §17(3).
    /// </summary>
    internal abstract IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain);
}
