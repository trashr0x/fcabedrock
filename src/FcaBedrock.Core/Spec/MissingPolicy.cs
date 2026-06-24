namespace FcaBedrock.Core.Spec;

/// <summary>How an attribute treats missing raw values. Spec §10.5.</summary>
public enum MissingPolicy
{
    /// <summary>Missing produces no cross (default).</summary>
    Skip,

    /// <summary>Missing produces a <c>{column}-missing</c> formal attribute.</summary>
    AsAttribute,
}
