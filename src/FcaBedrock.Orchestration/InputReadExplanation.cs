using System.Text;

namespace FcaBedrock.Orchestration;

/// <summary>Which input file an <see cref="InputReadExplanation"/> names.</summary>
public enum InputFile
{
    /// <summary>The root SPEC operand.</summary>
    Spec,

    /// <summary>The <c>.bed</c> operand of <c>migrate</c>.</summary>
    Bed,

    /// <summary>The DATA operand.</summary>
    Data,
}

/// <summary>
/// The code-less host error for an input file that could not be opened or decoded (D-122 part 2): why,
/// where the failed operation established it, and what to do next.
/// <code>
/// cannot read the spec file 'spec.toml': it does not exist. Check the path.
/// </code>
/// <para>
/// <b>The cause is the failed operation's exception type, never its message and never a later
/// look at the filesystem.</b> An open that throws <see cref="FileNotFoundException"/> or
/// <see cref="DirectoryNotFoundException"/> found nothing at the path. One that throws
/// <see cref="UnauthorizedAccessException"/> was refused; the .NET runtime reports a folder opened
/// as a file the same way on Windows and on Unix, so the advice names both possibilities. For a
/// spec or <c>.bed</c> file, <see cref="SpecTextDecoding"/> throws
/// <see cref="InvalidDataException"/> only for a UTF-16 or UTF-32 byte order mark and
/// <see cref="DecoderFallbackException"/> for bytes that are not UTF-8. DATA decoding and
/// delimited grammar are <see cref="DataReadExplanation"/>'s, which hands every other DATA failure
/// here. Every remaining failure keeps the generic sentence, so a sharing violation or a device
/// error is never guessed at, and no exception text, which can hold a path, operating-system
/// wording or data, is shown.
/// </para>
/// <para>
/// The message is plain text; the renderer escapes it once.
/// </para>
/// </summary>
public static class InputReadExplanation
{
    /// <summary>The message for <paramref name="file"/>'s <paramref name="operand"/>, which failed with <paramref name="cause"/>.</summary>
    public static string Message(InputFile file, string operand, Exception cause)
    {
        ArgumentNullException.ThrowIfNull(operand);
        ArgumentNullException.ThrowIfNull(cause);

        var detail = cause switch
        {
            FileNotFoundException or DirectoryNotFoundException => "it does not exist. Check the path.",
            UnauthorizedAccessException => "access to it was denied. Check that it is a file you can read.",
            InvalidDataException when file != InputFile.Data =>
                $"it starts with a UTF-16 or UTF-32 byte order mark, but {Rule(file)} must be UTF-8. Save it as UTF-8.",
            DecoderFallbackException when file != InputFile.Data => "it is not valid UTF-8. Save it as UTF-8.",
            _ => null,
        };

        return detail is null ? Generic(file, operand) : $"cannot read the {Noun(file)} '{operand}': {detail}";
    }

    private static string Generic(InputFile file, string operand) => file switch
    {
        InputFile.Spec => RunPipeline.SpecReadMessage(operand),
        InputFile.Data => RunPipeline.DataReadMessage(operand),
        _ => $"cannot read the {Noun(file)} '{operand}'.",
    };

    private static string Noun(InputFile file) => file switch
    {
        InputFile.Spec => "spec file",
        InputFile.Bed => ".bed file",
        InputFile.Data => "data file",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "The input file has no name."),
    };

    private static string Rule(InputFile file) => file switch
    {
        InputFile.Spec => "a spec",
        InputFile.Bed => "a .bed file",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "The input file has no decoding rule here."),
    };
}
