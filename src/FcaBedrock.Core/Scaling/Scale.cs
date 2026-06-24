namespace FcaBedrock.Core.Scaling;

/// <summary>
/// Maps bin labels to zero or more formal attributes (the scale half of the
/// orthogonal discretizer × scale model — decisions.md D-002). A closed set
/// within Core; external assemblies construct the concrete kinds but cannot
/// derive new ones (the plan-time member is internal).
/// </summary>
public abstract record Scale
{
    /// <summary>Stable kind identifier (e.g. <c>nominal</c>); part of the canonical identity.</summary>
    public abstract string Kind { get; }

    /// <summary>
    /// Plan-time: describe the formal attributes this scale produces over the
    /// given ordered bin labels, in canonical enumeration order (§17 rule 2).
    /// </summary>
    internal abstract IReadOnlyList<FormalAttributeShape> BuildShapes(IReadOnlyList<string> binLabels);
}
