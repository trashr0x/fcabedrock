namespace FcaBedrock.Core.Spec;

/// <summary>
/// Validity of a logical <b>attribute name</b> (spec §10.1): any non-empty string that contains
/// neither CR, LF nor the TOML key-quoting character <c>"</c>. Deliberately permissive: real
/// headers look like <c>bruises?</c>, <c>feature.1</c> or <c>days@home</c>, and a spec must carry
/// them unrenamed. One authority, so the spec reader that enforces the rule and the
/// <c>probe</c> naming matrix that authors only names satisfying it cannot drift (EP-5).
/// <para>
/// This is <b>not</b> <see cref="ObjectNameValidity"/>, which governs data-derived object names
/// over a different alphabet: a whitespace-only string is a valid attribute name but an unusable
/// object name, and a control character other than CR and LF is harmless in a TOML string but
/// unusable in an object name. Two predicates, two owners, deliberately not merged.
/// </para>
/// </summary>
public static class AttributeNameValidity
{
    /// <summary>
    /// Whether <paramref name="name"/> is a valid §10.1 attribute name: non-null, non-empty, and
    /// free of CR, LF and <c>"</c>. Every other character is allowed, whitespace and other control
    /// characters included.
    /// </summary>
    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name) && name.AsSpan().IndexOfAny('\r', '\n', '"') < 0;
}
