namespace FcaBedrock.Core.Spec;

/// <summary>How an attribute treats missing raw values. Spec §10.5.</summary>
public enum MissingPolicy
{
    /// <summary>Missing produces no cross (default).</summary>
    Skip,

    /// <summary>
    /// Missing produces one extra formal attribute after the scale's columns, named
    /// <c>{column}-missing</c> unless an explicit <c>formal_attribute_format</c> renders it
    /// (§10.5/§10.7, D-074/D-117).
    /// </summary>
    AsAttribute,
}
