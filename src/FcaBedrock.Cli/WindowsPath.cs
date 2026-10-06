namespace FcaBedrock.Cli;

/// <summary>
/// The one spelling rule for a full path handed to a native Win32 file call in this package. All
/// three call sites that take a path (the removal open, the lifetime-reference open and the
/// rename, for both of its paths) use it, so the removal and the reference never spell one path
/// differently. Each call keeps its own access, sharing and error handling: a shared spelling does
/// not make their opens or their results the same.
/// <para>
/// In a process without long-path support, <c>CreateFileW</c> refuses an ordinary full path of
/// 260 characters or more with <c>ERROR_PATH_NOT_FOUND</c> even when the file exists, and opens
/// the same path given the extended prefix. The publication removal reads "not found" as "nothing
/// there", so a removal open spelled without the prefix, beside a reference open spelled with it,
/// could report a file removed and leave it (D-125).
/// </para>
/// </summary>
internal static class WindowsPath
{
    // The traditional path limit, which counts the terminating null: in a process without long-path
    // support, an ordinary full path of 259 characters is the longest CreateFileW opens unprefixed.
    private const int MaxShortPath = 260;

    /// <summary>
    /// <paramref name="path"/> with the extended prefix when it is fully qualified and has at least
    /// 260 characters: <c>\\?\</c>, or <c>\\?\UNC\</c> in place of a network path's leading
    /// separators. A shorter path, a path that already starts with the extended (<c>\\?\</c>) or
    /// device (<c>\\.\</c>) prefix, and a path that is not fully qualified are returned unchanged.
    /// <para>
    /// The rule adds a prefix and does nothing else: it does not normalize. A caller passes a full
    /// path from <see cref="Path.GetFullPath(string)"/> or one composed from such a directory.
    /// </para>
    /// </summary>
    public static string Extended(string path)
    {
        if (path.Length < MaxShortPath
            || path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path[2..];
        }

        return Path.IsPathFullyQualified(path) ? @"\\?\" + path : path;
    }
}
