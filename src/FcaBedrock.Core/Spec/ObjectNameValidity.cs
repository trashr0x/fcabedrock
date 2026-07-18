namespace FcaBedrock.Core.Spec;

/// <summary>
/// Usability of a <b>data-derived object name</b> — a triple subject, or a wide column
/// object key (§5.4 / §18.1 / D-085). One authority, so every phase that reads such a
/// name agrees on exactly one definition: Discovery's <c>probe</c>, the Conversion
/// calibrate/emit halt, and the <c>unordered</c> grouping boundary cannot drift about
/// which subjects a source admits (M5-IP-007).
/// <para>
/// This is <b>not</b> §10.1 attribute-name validity — a different predicate over a
/// different alphabet, owned elsewhere. Do not conflate the two.
/// </para>
/// </summary>
public static class ObjectNameValidity
{
    /// <summary>
    /// A data-derived object name is <b>usable</b> when it is non-null, not empty or
    /// whitespace-only, and free of control characters — a newline would corrupt the
    /// line-structured <c>.cxt</c> (§18.1). A source has already normalized empty /
    /// <c>missing_token</c> / absent cells to <see langword="null"/>, so this predicate
    /// sees only present values.
    /// </summary>
    public static bool IsUsable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var ch in name)
        {
            if (char.IsControl(ch))
            {
                return false;
            }
        }

        return true;
    }
}
