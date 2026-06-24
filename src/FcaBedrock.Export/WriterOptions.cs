namespace FcaBedrock.Export;

/// <summary>
/// Byte-level formatting knobs for the writers (spec §8). The defaults are the
/// vNext-native output; <see cref="V2Compat"/> reproduces v2's exact bytes. These
/// are the writers' only choices — all semantics are decided before export (P-14).
/// </summary>
public sealed record WriterOptions
{
    /// <summary>Line terminator. <c>"\n"</c> native; <c>"\r\n"</c> under v2-compat.</summary>
    public string LineEnding { get; init; } = "\n";

    /// <summary>Whether the final line is terminated (spec §18.1). Default true.</summary>
    public bool TrailingNewline { get; init; } = true;

    /// <summary><c>.dat</c> id base. 1 (FIMI/v2) by default; 0 for ML conventions (§18.2).</summary>
    public int BaseIndex { get; init; } = 1;

    /// <summary><c>.dat</c>: trailing space after the last id on a non-empty line. v2-ism.</summary>
    public bool NonemptyLineTrailingSpace { get; init; }

    /// <summary><c>.dat</c>: trailing space on a crossless object's empty line.</summary>
    public bool EmptyLineTrailingSpace { get; init; }

    /// <summary>vNext-native defaults: LF, trailing newline, 1-based, no trailing spaces.</summary>
    public static WriterOptions Native { get; } = new();

    /// <summary>v2 byte conventions: CRLF and a trailing space on non-empty <c>.dat</c> lines (§8, D-011).</summary>
    public static WriterOptions V2Compat { get; } = new()
    {
        LineEnding = "\r\n",
        NonemptyLineTrailingSpace = true,
    };
}
