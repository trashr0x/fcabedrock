namespace FcaBedrock.Core.Spec;

/// <summary>
/// How duplicate object keys are handled. Spec §6.1. Applies only to a wide
/// <see cref="ColumnObjectKey"/>: row-index keys are unique by construction, and
/// repeated triple subjects accumulate onto one object.
/// </summary>
public enum DuplicateObjectPolicy
{
    /// <summary>A duplicate key is an error and stops conversion (default).</summary>
    Fail,

    /// <summary>
    /// Each row stays its own object. For a key's first row, the candidate name is the key
    /// itself; for each later row, it is <c>&lt;key&gt;#&lt;record-index&gt;</c> (0-based
    /// source record index). If any candidate is already assigned, the converter appends
    /// <c>#1</c>, <c>#2</c>, … and takes the first unused (§6.1).
    /// </summary>
    Keep,

    /// <summary>Rows sharing a key collapse to one object; crosses union onto the first.</summary>
    Dedupe,
}
