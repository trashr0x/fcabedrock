using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The choice of each standard writer (D-122 part 3). Each stream is decided on its own: a
/// redirected one gets explicit UTF-8 with no byte order mark, and a console one gets the
/// runtime's own writer, after Windows has selected the runtime's Unicode console route. A
/// recording console stands in for the real one, so these tests prove the choice and its order,
/// and the bytes of a redirected stream. What a console window draws needs a real console.
/// </summary>
public sealed class StandardStreamsTests
{
    private const string IsOutputRedirected = nameof(IStandardConsole.IsOutputRedirected);
    private const string IsErrorRedirected = nameof(IStandardConsole.IsErrorRedirected);
    private const string UseUnicodeOutput = nameof(IStandardConsole.UseUnicodeOutput);
    private const string ConsoleOutput = nameof(IStandardConsole.ConsoleOutput);
    private const string ConsoleError = nameof(IStandardConsole.ConsoleError);
    private const string OpenStandardOutput = nameof(IStandardConsole.OpenStandardOutput);
    private const string OpenStandardError = nameof(IStandardConsole.OpenStandardError);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Open_WhenOnWindows_ThenUnicodeIsSelectedBeforeAnyConsoleWriterOnlyWhenAStreamIsAConsole(
        bool outputRedirected, bool errorRedirected)
    {
        var console = new RecordingConsole
        {
            IsWindows = true,
            OutputRedirected = outputRedirected,
            ErrorRedirected = errorRedirected,
        };
        var streams = new StandardStreams();

        streams.Open(console);

        // A console writer obtained before the selection would keep the console's code page,
        // so the selection must come first. stderr comes before stdout, so a stdout failure
        // still has a stderr to report through.
        List<string> expected = [IsOutputRedirected, IsErrorRedirected];
        if (!(outputRedirected && errorRedirected))
        {
            expected.Add(UseUnicodeOutput);
        }

        expected.Add(errorRedirected ? OpenStandardError : ConsoleError);
        expected.Add(outputRedirected ? OpenStandardOutput : ConsoleOutput);
        Assert.Equal(expected, console.Calls);
        AssertWriters(streams, console);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Open_WhenNotOnWindows_ThenEachStreamGetsItsWriterWithoutAUnicodeSelection(
        bool outputRedirected, bool errorRedirected)
    {
        var console = new RecordingConsole
        {
            IsWindows = false,
            OutputRedirected = outputRedirected,
            ErrorRedirected = errorRedirected,
        };
        var streams = new StandardStreams();

        streams.Open(console);

        Assert.Equal(
            [
                IsOutputRedirected,
                IsErrorRedirected,
                errorRedirected ? OpenStandardError : ConsoleError,
                outputRedirected ? OpenStandardOutput : ConsoleOutput,
            ],
            console.Calls);
        AssertWriters(streams, console);
    }

    [Fact]
    public void Open_WhenBothStreamsAreRedirected_ThenEachWritesExactUtf8WithNoByteOrderMarkAndOnlyTheTextsLineFeeds()
    {
        const string text = "warning X: (§7/§16.4) café_Ω_Ж_漢_😀\nsecond line\n";
        var console = new RecordingConsole { OutputRedirected = true, ErrorRedirected = true };
        var streams = new StandardStreams();
        streams.Open(console);

        streams.Output.Write(text);
        streams.Error.Write(text);
        streams.Output.Flush();
        streams.Error.Flush();

        var expected = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        Assert.Equal(expected, console.StandardOutput.ToArray());
        Assert.Equal(expected, console.StandardError.ToArray());
        Assert.DoesNotContain((byte)'\r', console.StandardOutput.ToArray());
    }

    [Fact]
    public void Open_WhenARedirectedWriteSplitsASurrogatePairAtTheBufferBoundary_ThenTheCharacterStaysWhole()
    {
        // The redirected writer buffers 1,024 characters, so the pair's high half is the last
        // character of the first buffer and is encoded only with its low half, after the
        // buffer is written out. This proves the redirected UTF-8 writer only: the console
        // writer's own buffer, and what a console draws, need a real console.
        var console = new RecordingConsole { OutputRedirected = true, ErrorRedirected = true };
        var streams = new StandardStreams();
        streams.Open(console);

        streams.Output.Write(new string('a', 1023) + "\U0001F600");
        streams.Output.Flush();

        byte[] expected = [.. Enumerable.Repeat((byte)'a', 1023), 0xF0, 0x9F, 0x98, 0x80];
        Assert.Equal(expected, console.StandardOutput.ToArray());
    }

    private static void AssertWriters(StandardStreams streams, RecordingConsole console)
    {
        // Each stream's writer must match its own route. A UTF-8 writer over a console stream
        // would send UTF-8 bytes through the Unicode console route, and the runtime's console
        // writer over a redirected stream would write UTF-16 to the file.
        AssertWriter(streams.Output, console.OutputRedirected, console.StandardOutput, console.ConsoleOutputWriter);
        AssertWriter(streams.Error, console.ErrorRedirected, console.StandardError, console.ConsoleErrorWriter);
    }

    private static void AssertWriter(TextWriter writer, bool redirected, Stream stream, TextWriter consoleWriter)
    {
        if (!redirected)
        {
            Assert.Same(consoleWriter, writer);
            return;
        }

        var streamWriter = Assert.IsType<StreamWriter>(writer);
        Assert.Same(stream, streamWriter.BaseStream);
        Assert.IsType<UTF8Encoding>(streamWriter.Encoding);
        Assert.Empty(streamWriter.Encoding.Preamble.ToArray());
        Assert.False(streamWriter.AutoFlush);
    }
}
