namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Both rule sets of the lexical <c>--out</c> check (D-122 part 4), each run on every platform
/// through the check's platform argument, so a Windows run also pins the POSIX table and the
/// reverse. Which set a real run uses is pinned separately, through the parser on each native
/// platform (<see cref="CommandLineParserTests"/>).
/// </summary>
public sealed class OutputPathTextTests
{
    private static string Problem(string text, bool windows) =>
        OutputPathText.Check(text, windows)?.ToString() ?? "none";

    [Theory]
    [InlineData("", "Blank")]
    [InlineData(" ", "Blank")]
    [InlineData("\t", "Blank")]
    [InlineData(".", "Directory")]
    [InlineData("..", "Directory")]
    [InlineData(@"out\", "Directory")]
    [InlineData("out/", "Directory")]
    [InlineData(@"sub\.", "Directory")]
    [InlineData("sub/..", "Directory")]
    [InlineData(@"\", "Directory")]
    [InlineData("/", "Directory")]
    [InlineData("C:", "Directory")]
    [InlineData("c:", "Directory")]
    [InlineData(@"C:\", "Directory")]
    [InlineData("C:/", "Directory")]
    [InlineData("C:.", "Directory")]
    [InlineData("C:..", "Directory")]
    [InlineData(@"C:\work\..", "Directory")]
    [InlineData(@"\\", "Directory")]
    [InlineData(@"\\server", "Directory")]
    [InlineData(@"\\server\", "Directory")]
    [InlineData(@"\\server\share", "Directory")]
    [InlineData(@"\\server\share\", "Directory")]
    [InlineData("//server/share", "Directory")]
    [InlineData(@"\\?\", "Directory")]
    [InlineData(@"\\?\C:", "Directory")]
    [InlineData(@"\\?\C:\", "Directory")]
    [InlineData(@"\\?\C:\work\.", "Directory")]
    [InlineData(@"\\?\C:\work\..", "Directory")]
    [InlineData(@"\\?\UNC\server\share", "Directory")]
    [InlineData(@"\\?\unc\server\share", "Directory")]
    [InlineData(@"\\?\UNC\server\share\.", "Directory")]
    [InlineData("...", "TrailingDotOrSpace")]
    [InlineData("....", "TrailingDotOrSpace")]
    [InlineData("foo.", "TrailingDotOrSpace")]
    [InlineData("foo ", "TrailingDotOrSpace")]
    [InlineData("report. ", "TrailingDotOrSpace")]
    [InlineData(@"sub\...", "TrailingDotOrSpace")]
    [InlineData("C:foo.", "TrailingDotOrSpace")]
    [InlineData(@"C:\work\draft.", "TrailingDotOrSpace")]
    [InlineData(@"\\server\share\draft ", "TrailingDotOrSpace")]
    [InlineData(@"\\.\C:\work\draft.", "TrailingDotOrSpace")]
    [InlineData("report v1.data", "none")]
    [InlineData("a..b", "none")]
    [InlineData(".hidden", "none")]
    [InlineData("-", "none")]
    [InlineData("C:driveleaf", "none")]
    [InlineData(@"C:\work\out", "none")]
    [InlineData(@"\work\out", "none")]
    [InlineData(@"back\slash", "none")]
    [InlineData(@"\\server\share\out", "none")]
    [InlineData(@"\\?\C:\work\draft.", "none")]
    [InlineData(@"\\?\C:\work\draft ", "none")]
    [InlineData(@"\\?\C:\work\...", "none")]
    [InlineData(@"\\?\UNC\server\share\draft.", "none")]
    [InlineData("CON", "none")]
    public void Check_WhenTheWindowsRulesApply_ThenTheFinalComponentDecides(string text, string expected)
    {
        // Ordinary leaves lose a final dot or space; extended (\\?\) leaves keep them. A device-
        // looking \\.\ spelling and a reserved name get the ordinary rules and their existing
        // runtime behaviour, with no namespace catalogue.
        Assert.Equal(expected, Problem(text, windows: true));
    }

    [Theory]
    [InlineData("", "Blank")]
    [InlineData(" ", "Blank")]
    [InlineData(".", "Directory")]
    [InlineData("..", "Directory")]
    [InlineData("/", "Directory")]
    [InlineData("//", "Directory")]
    [InlineData("out/", "Directory")]
    [InlineData("sub/.", "Directory")]
    [InlineData("sub/..", "Directory")]
    [InlineData("/work/..", "Directory")]
    [InlineData("foo.", "none")]
    [InlineData("foo ", "none")]
    [InlineData("...", "none")]
    [InlineData(@"back\slash", "none")]
    [InlineData(@"out\", "none")]
    [InlineData(@"sub\.", "none")]
    [InlineData(@"sub\..", "none")]
    [InlineData("C:", "none")]
    [InlineData(@"\\server\share", "none")]
    [InlineData(@"\\?\C:\work\.", "none")]
    [InlineData("report v1.data", "none")]
    public void Check_WhenThePosixRulesApply_ThenOnlySlashSeparatesAndNothingIsTrimmed(string text, string expected)
    {
        // A backslash, a final dot and a final space are ordinary file-name characters here, so
        // the Windows rules never leak into a POSIX name.
        Assert.Equal(expected, Problem(text, windows: false));
    }
}
