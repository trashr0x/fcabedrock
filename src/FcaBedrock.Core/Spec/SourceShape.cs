namespace FcaBedrock.Core.Spec;

/// <summary>Shape of the source data. Spec §5.1.</summary>
public enum SourceShape
{
    /// <summary>One row per object (wide DSV).</summary>
    Wide,

    /// <summary>Subject-predicate-value triples.</summary>
    Triple,
}
