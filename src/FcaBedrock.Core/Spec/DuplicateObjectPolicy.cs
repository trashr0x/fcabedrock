namespace FcaBedrock.Core.Spec;

/// <summary>
/// How duplicate object keys are handled. Spec §6.1. Applies only to
/// <see cref="ColumnObjectKey"/> — row-index keys are unique by construction.
/// </summary>
public enum DuplicateObjectPolicy
{
    /// <summary>A duplicate key is an error and stops conversion (default).</summary>
    Fail,

    /// <summary>Each row stays its own object; colliding names get a <c>#N</c> suffix.</summary>
    Keep,

    /// <summary>Rows sharing a key collapse to one object; crosses union onto the first.</summary>
    Dedupe,
}
