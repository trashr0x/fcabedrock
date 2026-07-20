using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The one construction site for a <c>[[matcher]]</c> <c>name_regex</c> (§9.2,
/// D-115). The parse gate and the resolver's selector evaluation both compile
/// through here, so a pattern can never parse successfully and then fail — or,
/// worse, match differently — when it is actually evaluated (P-5).
/// </summary>
internal static class MatcherSelectors
{
    /// <summary>
    /// Compiles <paramref name="pattern"/> in the <b>exact</b> form that executes.
    /// <para>
    /// Three construction choices are load-bearing and none is incidental:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>\A(?:…)\z</c> makes matching <b>whole-name</b> (§9.2) without rewriting the
    /// author's pattern. The group is <b>non-capturing</b>, so it shifts no capture
    /// number a backreference might use, and it is <b>required</b> rather than
    /// cosmetic: bare anchors around an alternation would bind as
    /// <c>\Aa|b\z</c> — "starts with a, or ends with b" — instead of the intended
    /// whole-name "a or b". An author's own redundant anchors stay legal.
    /// </description></item>
    /// <item><description>
    /// <see cref="RegexOptions.CultureInvariant"/>, matching <c>value_groups</c>
    /// (D-090/D-104): culture-aware casing is ICU/NLS-version dependent, so the same
    /// spec could select different attributes on two machines (P-12). Case sensitivity
    /// is therefore the .NET default, and an authored inline <c>(?i)</c> still applies
    /// — it sits at the start of the enclosing group and so covers the whole pattern.
    /// </description></item>
    /// <item><description>
    /// <see cref="Regex.InfiniteMatchTimeout"/> passed <b>explicitly</b>: the overloads
    /// that omit it inherit the host's ambient <c>REGEX_DEFAULT_MATCH_TIMEOUT</c>, which
    /// would make the same spec host-dependent on the exception channel (P-7/P-14).
    /// <c>RegexOptions.NonBacktracking</c> is deliberately not adopted — it would
    /// silently narrow the regex language relative to <c>value_groups</c> (D-115).
    /// </description></item>
    /// </list>
    /// <para>
    /// An empty pattern is rejected here rather than at the call sites, so "non-empty
    /// and compilable" (§9.2) is one rule in one place.
    /// </para>
    /// </summary>
    public static bool TryCompileWholeName(
        string pattern,
        [NotNullWhen(true)] out Regex? regex,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern.Length == 0)
        {
            regex = null;
            error = "the pattern is empty";
            return false;
        }

        try
        {
            regex = new Regex(@"\A(?:" + pattern + @")\z", RegexOptions.CultureInvariant, Regex.InfiniteMatchTimeout);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            regex = null;
            error = ex.Message;
            return false;
        }
    }
}
