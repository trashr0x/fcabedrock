namespace FcaBedrock.Cli.Publication;

/// <summary>
/// Why a publication step failed, where the step that failed established it (D-122 part 2).
/// <para>
/// A cause is read from the failed operation's own exception, or from the object the transaction
/// already holds; never from a later look at the directory, and never from an exception's message.
/// Where neither says why, the cause is <see cref="Unknown"/> and the message stays generic.
/// </para>
/// </summary>
internal enum PublicationCause
{
    /// <summary>Nothing established why.</summary>
    Unknown,

    /// <summary>Creating a file found no folder at the output location.</summary>
    FolderMissing,

    /// <summary>The output folder refused to be listed or to have a file created in it.</summary>
    FolderDenied,

    /// <summary>The object refused the operation's access.</summary>
    AccessDenied,

    /// <summary>Windows: the operation met a sharing violation.</summary>
    InUse,

    /// <summary>Windows: the held object carries the read-only attribute.</summary>
    ReadOnly,

    /// <summary>An existing entry could not be opened and held, so nothing about it was decided.</summary>
    NotHeld,

    /// <summary>The entry is not an object the transaction can prove it created, so it was left untouched.</summary>
    NotProved,
}

/// <summary>Which cleanup operation a <see cref="CleanupFact"/> describes.</summary>
internal enum CleanupStep
{
    /// <summary>Removing a private entry, or a published one a rollback owns.</summary>
    Remove,

    /// <summary>Renaming a backup home to the target it was taken from.</summary>
    Restore,
}

/// <summary>
/// The first cleanup step that did not complete: what it tried, why where that is known, and the
/// entry it acted on, by file name only. The entry sits beside the output base, so its name locates
/// it; the name can carry a transaction token, and naming it proves nothing about who owns it.
/// </summary>
internal sealed record CleanupFact(CleanupStep Step, PublicationCause Cause, string EntryName);

/// <summary>Reads a <see cref="PublicationCause"/> from a failed operation's exception type and code.</summary>
internal static class PublicationCauses
{
    // Windows error codes, as the native seam reports them raw and managed I/O reports them inside
    // an HRESULT (0x8007xxxx).
    private const int WindowsAccessDenied = 5;
    private const int WindowsSharingViolation = 32;

    /// <summary>
    /// The cause of a failure to list the output folder or to create a file in it. The runtime maps
    /// a missing folder to <see cref="DirectoryNotFoundException"/> on every platform (a create-new
    /// in a missing folder on Unix included) and a refusal to <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static PublicationCause OfLocation(Exception exception) => exception switch
    {
        DirectoryNotFoundException => PublicationCause.FolderMissing,
        UnauthorizedAccessException => PublicationCause.FolderDenied,
        _ => PublicationCause.Unknown,
    };

    /// <summary>
    /// The cause of a failed rename or removal of an existing object. Only Windows codes are read:
    /// a Unix errno with the same number means something else, and Unix refusals already arrive as
    /// <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static PublicationCause OfObject(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return PublicationCause.AccessDenied;
        }

        if (!OperatingSystem.IsWindows() || exception is not IOException)
        {
            return PublicationCause.Unknown;
        }

        var code = WindowsCode(exception.HResult);
        return code switch
        {
            WindowsAccessDenied => PublicationCause.AccessDenied,
            WindowsSharingViolation => PublicationCause.InUse,
            _ => PublicationCause.Unknown,
        };
    }

    // A raw Win32 code, or one wrapped as HRESULT_FROM_WIN32.
    private static int WindowsCode(int result) =>
        (result & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? result & 0xFFFF : result;
}
