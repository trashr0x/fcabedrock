namespace FcaBedrock.Cli;

/// <summary>Why an <c>--out</c> spelling cannot name the file a command would write.</summary>
internal enum OutputPathProblem
{
    /// <summary>Empty or whitespace-only.</summary>
    Blank,

    /// <summary>
    /// The final component names a directory: nothing after a root or a terminal separator, or a
    /// final <c>.</c> or <c>..</c>.
    /// </summary>
    Directory,

    /// <summary>An ordinary Windows file name ending in a dot or a space, which Windows removes.</summary>
    TrailingDotOrSpace,
}

/// <summary>
/// The lexical check on an <c>--out</c> spelling (D-122 part 4): does its final component name a
/// file? It reads the text and nothing else. It resolves no path, queries no filesystem and calls
/// no normalizer: <see cref="Path.GetFullPath(string)"/> can expand short 8.3 names through
/// filesystem queries on Windows, and the target's existence, identity and permissions belong to
/// publication, not to parsing.
/// <para>
/// <b>The separators and roots are the platform's own.</b> On Windows <c>\</c> and <c>/</c> both
/// separate, a drive (<c>C:</c>, <c>C:\</c>), a share (<c>\\server\share</c>) and the explicit
/// extended forms' roots (<c>\\?\C:</c>, <c>\\?\UNC\server\share</c>) are roots, and an ordinary
/// final component ending in a dot or a space is refused because Windows removes those characters
/// when it opens the file. The extended namespace keeps them, so an explicit extended path is
/// refused only for the directory forms. Elsewhere only <c>/</c> separates and roots, and a
/// backslash, a final dot and a final space are ordinary file-name characters.
/// </para>
/// <para>
/// No other namespace is recognized. A device-looking spelling gets the ordinary rules and keeps
/// the runtime behaviour of whatever it names, as does a reserved device name.
/// </para>
/// </summary>
internal static class OutputPathText
{
    private const string ExtendedPrefix = @"\\?\";

    /// <summary>
    /// The problem with <paramref name="text"/> under the Windows rules when
    /// <paramref name="windows"/> is true and the POSIX rules otherwise, or null when its final
    /// component names a file. The caller handles the stdout dash first.
    /// </summary>
    public static OutputPathProblem? Check(string text, bool windows)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return OutputPathProblem.Blank;
        }

        return windows ? CheckWindows(text) : CheckPosix(text);
    }

    private static OutputPathProblem? CheckPosix(string text) =>
        NamesDirectory(text.AsSpan(text.LastIndexOf('/') + 1)) ? OutputPathProblem.Directory : null;

    private static OutputPathProblem? CheckWindows(string text)
    {
        var extended = text.StartsWith(ExtendedPrefix, StringComparison.Ordinal);
        var root = extended ? ExtendedRootLength(text) : OrdinaryRootLength(text);

        // The final component starts after the root and after the last separator, so a bare root,
        // a drive with nothing after it and a terminal separator all leave it empty.
        var leaf = text.AsSpan(Math.Max(root, text.AsSpan().LastIndexOfAny('\\', '/') + 1));

        if (NamesDirectory(leaf))
        {
            return OutputPathProblem.Directory;
        }

        return !extended && leaf[^1] is '.' or ' ' ? OutputPathProblem.TrailingDotOrSpace : null;
    }

    private static bool NamesDirectory(ReadOnlySpan<char> leaf) => leaf is "" or "." or "..";

    private static bool IsSeparator(char value) => value is '\\' or '/';

    // C: (drive-relative), \\server\share (a share root) or neither. A rooted "\name" needs no
    // root length: its leaf already starts after the last separator.
    private static int OrdinaryRootLength(string text)
    {
        if (text.Length >= 2 && IsSeparator(text[0]) && IsSeparator(text[1]))
        {
            return ComponentsEnd(text, start: 2, count: 2);
        }

        return text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':' ? 2 : 0;
    }

    // \\?\ then one volume component (C:, or another volume name), extended by the server and the
    // share for \\?\UNC\server\share. UNC matches in any letter case: counting a share as part of
    // the root can only refuse a spelling that names no file.
    private static int ExtendedRootLength(string text)
    {
        var volumeEnd = ComponentsEnd(text, ExtendedPrefix.Length, count: 1);
        var volume = text.AsSpan(ExtendedPrefix.Length, volumeEnd - ExtendedPrefix.Length);

        return volume.Equals("UNC", StringComparison.OrdinalIgnoreCase)
            ? ComponentsEnd(text, volumeEnd + 1, count: 2)
            : volumeEnd;
    }

    // The index just past the count-th component from start, or the text's length when it runs
    // out first, so an incomplete root (\\server, \\?\UNC\server) is the whole spelling.
    private static int ComponentsEnd(string text, int start, int count)
    {
        var position = start;
        for (var component = 0; component < count; component++)
        {
            if (position >= text.Length)
            {
                return text.Length;
            }

            var next = text.AsSpan(position).IndexOfAny('\\', '/');
            if (next < 0)
            {
                return text.Length;
            }

            position += next + (component + 1 < count ? 1 : 0);
        }

        return position;
    }
}
