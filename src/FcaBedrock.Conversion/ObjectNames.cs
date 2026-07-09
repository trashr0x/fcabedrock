namespace FcaBedrock.Conversion;

/// <summary>
/// Object-name usability, shared across the Conversion layer so the emit halt and the
/// <c>unordered</c> grouping boundary agree on exactly one definition (D-085).
/// </summary>
internal static class ObjectNames
{
    /// <summary>
    /// A data-derived object name (a triple subject or a wide column key) is <b>usable</b> when it
    /// is non-null, not whitespace-only, and free of control/newline characters — a newline would
    /// corrupt the line-structured <c>.cxt</c> (§5.4 / §18.1 / D-085). The source has already
    /// normalized empty / <c>missing_token</c> / too-short → <c>null</c>. An unusable name halts the
    /// conversion at its <c>ObjectKeyValueInvalid</c> emit site; the grouping layer only uses this to
    /// bound its reorder at the first such row (it never raises the diagnostic itself).
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
