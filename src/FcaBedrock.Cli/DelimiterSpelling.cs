using FcaBedrock.Core.Spec;

namespace FcaBedrock.Cli;

/// <summary>
/// The command-line spellings of a §5.1.1 delimiter, decoded in one place for the parser and for
/// both commands that take <c>--delimiter</c>, so a spelling the parser accepts always means the
/// same character to the command that uses it.
/// <para>
/// The word <c>tab</c>, in any letter case, means TAB (U+0009): cmd.exe completes file names on
/// the Tab key, so a tab cannot be typed there. Any other spelling is exactly one character of the
/// v1 alphabet other than the quote, an actual tab included. The word is a command-line spelling
/// only: a spec's <c>delimiter</c> is still one character, and a backslash followed by <c>t</c> is
/// two characters, not a tab (D-122 part 10).
/// </para>
/// </summary>
internal static class DelimiterSpelling
{
    /// <summary>
    /// Decodes <paramref name="spelling"/>; false when it spells no usable delimiter. Accepted
    /// spellings are not rewritten anywhere else: the parser keeps the text as given.
    /// </summary>
    public static bool TryDecode(string spelling, out char delimiter)
    {
        ArgumentNullException.ThrowIfNull(spelling);

        // ASCII letters only, so no culture or Unicode case mapping decides what spells the word.
        if (spelling is ['t' or 'T', 'a' or 'A', 'b' or 'B'])
        {
            delimiter = '\t';
            return true;
        }

        // One UTF-16 unit, because the resolved setting is a char; the quote is excluded because
        // v1 fixes the quote character at '"' (§5.1).
        if (spelling.Length == 1 && SourceReadSettings.IsInDelimiterAlphabet(spelling[0]) && spelling[0] != '"')
        {
            delimiter = spelling[0];
            return true;
        }

        delimiter = default;
        return false;
    }

    /// <summary>
    /// The delimiter <paramref name="spelling"/> means. Only a spelling the parser accepted reaches
    /// a command, so an undecodable one here is a defect in this code, not a usage mistake.
    /// </summary>
    public static char Decode(string spelling) =>
        TryDecode(spelling, out var delimiter)
            ? delimiter
            : throw new ArgumentException("The delimiter spelling was not validated by the parser.", nameof(spelling));
}
