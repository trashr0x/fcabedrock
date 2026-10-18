using System.Text;

namespace FcaBedrock.Cli;

/// <summary>
/// The process's standard output and error before the CLI holds a writer for either.
/// <see cref="SystemConsole"/> is the real one; tests substitute a recording console, so the
/// choice of writer and its order are proven without touching the test host's own console.
/// </summary>
internal interface IStandardConsole
{
    /// <summary>Whether this process runs on Windows.</summary>
    bool IsWindows { get; }

    /// <summary>Whether stdout is a file or pipe rather than a console.</summary>
    bool IsOutputRedirected { get; }

    /// <summary>Whether stderr is a file or pipe rather than a console.</summary>
    bool IsErrorRedirected { get; }

    /// <summary>Makes this process's console writers write UTF-16 (Windows only).</summary>
    void UseUnicodeOutput();

    /// <summary>The runtime's writer for a console stdout.</summary>
    TextWriter ConsoleOutput();

    /// <summary>The runtime's writer for a console stderr.</summary>
    TextWriter ConsoleError();

    /// <summary>The raw stdout stream.</summary>
    Stream OpenStandardOutput();

    /// <summary>The raw stderr stream.</summary>
    Stream OpenStandardError();
}

/// <summary>The real standard streams, through <see cref="Console"/>.</summary>
internal sealed class SystemConsole : IStandardConsole
{
    private SystemConsole()
    {
    }

    /// <summary>The shared instance.</summary>
    public static SystemConsole Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsWindows => OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public bool IsOutputRedirected => Console.IsOutputRedirected;

    /// <inheritdoc/>
    public bool IsErrorRedirected => Console.IsErrorRedirected;

    /// <inheritdoc/>
    public void UseUnicodeOutput()
    {
        // StandardStreams calls this only on Windows. The check says so to the platform
        // analyzer, because the setter is unsupported on some other platforms.
        if (OperatingSystem.IsWindows())
        {
            Console.OutputEncoding = Encoding.Unicode;
        }
    }

    /// <inheritdoc/>
    public TextWriter ConsoleOutput() => Console.Out;

    /// <inheritdoc/>
    public TextWriter ConsoleError() => Console.Error;

    /// <inheritdoc/>
    public Stream OpenStandardOutput() => Console.OpenStandardOutput();

    /// <inheritdoc/>
    public Stream OpenStandardError() => Console.OpenStandardError();
}

/// <summary>
/// The CLI's stdout and stderr writers, each chosen for its own stream (D-122 part 3). A
/// redirected stream gets explicit UTF-8 with no byte order mark, so a file or pipe receives
/// exactly the bytes the renderer produced. A console stream gets the runtime's own console
/// writer; on Windows the process first selects the runtime's Unicode console route, so text
/// reaches the console without being converted to its code page, which loses every character
/// the code page lacks.
/// <para>
/// Neither writer is disposed: each wraps a standard handle the process owns, and
/// <see cref="CliHost.Complete"/> flushes both.
/// </para>
/// </summary>
internal sealed class StandardStreams
{
    /// <summary>The stdout writer; the null writer until it is built.</summary>
    public TextWriter Output { get; private set; } = TextWriter.Null;

    /// <summary>The stderr writer; the null writer until it is built.</summary>
    public TextWriter Error { get; private set; } = TextWriter.Null;

    /// <summary>
    /// Builds stderr, then stdout. Each property is set as soon as its writer exists, so after a
    /// failure here <see cref="Error"/> is either a stderr that can still carry the report or the
    /// null writer.
    /// </summary>
    public void Open(IStandardConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        // The selection must come before any console writer exists: a writer obtained earlier
        // keeps converting to the console's code page after the encoding changes. It is made
        // only when a stream is a console, so fully redirected output keeps its route. It sets
        // no native code page, mode or input setting: for UTF-16 the runtime skips its native
        // code-page call and writes a console stream through WriteConsoleW (.NET runtime source,
        // v10.0.12 and v10.0.13).
        var outputRedirected = console.IsOutputRedirected;
        var errorRedirected = console.IsErrorRedirected;
        if (console.IsWindows && !(outputRedirected && errorRedirected))
        {
            console.UseUnicodeOutput();
        }

        // stderr first, so a stdout that cannot be set up can still be reported. Each writer
        // must match its own stream: after the selection the console writer would write UTF-16
        // into a file, and the UTF-8 writer would send UTF-8 bytes through the UTF-16 console
        // route, so a console writer is obtained only for a console stream.
        Error = errorRedirected ? CreateRedirected(console.OpenStandardError()) : console.ConsoleError();
        Output = outputRedirected ? CreateRedirected(console.OpenStandardOutput()) : console.ConsoleOutput();
    }

    // Line endings never come from here: every rendered string carries its own LF, so output
    // is identical on every platform.
    private static StreamWriter CreateRedirected(Stream stream) =>
        new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false,
        };
}
