namespace FcaBedrock.Orchestration;

/// <summary>
/// A run whose root spec is read from a file: what <see cref="RunPreparation"/> prepares.
/// The two paths are the spellings §15 records, kept as given.
/// </summary>
public sealed record FileRunRequest
{
    /// <summary>The root spec, as the host spelled it.</summary>
    public required string SpecPath { get; init; }

    /// <summary>The data file, as the host spelled it.</summary>
    public required string DataPath { get; init; }

    /// <summary>
    /// Where Conversion places its temporary spill files, or null for the platform default. A
    /// runtime placement only: it changes no output byte (D-122 part 8). A value that is empty or
    /// whitespace is refused with <see cref="HostErrorKind.TemporaryDirectoryInvalid"/> before any
    /// file is opened.
    /// </summary>
    public string? TempDirectory { get; init; }
}
