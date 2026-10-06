namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The spelling rule for a full path handed to a native Win32 file call (<see cref="WindowsPath"/>),
/// as a string function. What the spelling does to a real call is proved where one runs, in
/// <see cref="PublicationLongPathTests"/>.
/// </summary>
public sealed class WindowsPathTests
{
    [Theory]
    [InlineData(17)]
    [InlineData(258)]
    [InlineData(259)]
    public void Extended_WhenALocalPathIsShorterThanTheLimit_ThenItIsUnchanged(int length)
    {
        var path = LocalPath(length);

        Assert.Equal(path, WindowsPath.Extended(path));
    }

    [Theory]
    [InlineData(260)]
    [InlineData(261)]
    public void Extended_WhenALocalPathReachesTheLimit_ThenTheExtendedPrefixIsAdded(int length)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows reads a drive-letter path as fully qualified.");
        var path = LocalPath(length);

        Assert.Equal(@"\\?\" + path, WindowsPath.Extended(path));
    }

    [Fact]
    public void Extended_WhenANetworkPathIsShorterThanTheLimit_ThenItIsUnchanged()
    {
        const string path = @"\\server\share\file.txt";

        Assert.Equal(path, WindowsPath.Extended(path));
    }

    [Fact]
    public void Extended_WhenANetworkPathReachesTheLimit_ThenItsLeadingSeparatorsBecomeTheExtendedNetworkPrefix()
    {
        var tail = new string('x', 260);

        Assert.Equal(@"\\?\UNC\server\share\" + tail, WindowsPath.Extended(@"\\server\share\" + tail));
    }

    [Theory]
    [InlineData(@"\\?\C:\")]
    [InlineData(@"\\.\C:\")]
    public void Extended_WhenThePathAlreadyCarriesTheExtendedOrDevicePrefix_ThenItIsUnchanged(string prefix)
    {
        var path = prefix + new string('x', 260);

        Assert.Equal(path, WindowsPath.Extended(path));
    }

    [Theory]
    [InlineData(@"relative\")]
    [InlineData(@"C:relative\")]
    [InlineData(@"\rooted\")]
    public void Extended_WhenThePathIsNotFullyQualified_ThenItIsUnchanged(string prefix)
    {
        var path = prefix + new string('x', 260);

        Assert.Equal(path, WindowsPath.Extended(path));
    }

    private static string LocalPath(int length) => @"C:\" + new string('x', length - 3);
}
