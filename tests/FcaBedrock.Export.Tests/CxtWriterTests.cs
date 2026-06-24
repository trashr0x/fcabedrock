using FcaBedrock.Core.Planning;

namespace FcaBedrock.Export.Tests;

public sealed class CxtWriterTests
{
    private static readonly EmittedObject[] Objects =
    [
        WriterFixtures.Object("0", 0),
        WriterFixtures.Object("1", 1),
        WriterFixtures.Object("2"),
    ];

    [Fact]
    public async Task WriteAsync_WhenNative_ThenBurmeisterLayoutWithLf()
    {
        var text = await WriterFixtures.WriteCxtAsync(WriterFixtures.TwoColumnPlan(), Objects, WriterOptions.Native);

        Assert.Equal("B\n\n3\n2\n\n0\n1\n2\na-x\na-y\nX.\n.X\n..\n", text);
    }

    [Fact]
    public async Task WriteAsync_WhenV2Compat_ThenSameLayoutWithCrlf()
    {
        var text = await WriterFixtures.WriteCxtAsync(WriterFixtures.TwoColumnPlan(), Objects, WriterOptions.V2Compat);

        Assert.Equal("B\r\n\r\n3\r\n2\r\n\r\n0\r\n1\r\n2\r\na-x\r\na-y\r\nX.\r\n.X\r\n..\r\n", text);
    }

    [Fact]
    public async Task WriteAsync_WhenTrailingNewlineDisabled_ThenNoFinalLineEnding()
    {
        var text = await WriterFixtures.WriteCxtAsync(
            WriterFixtures.TwoColumnPlan(), Objects, WriterOptions.Native with { TrailingNewline = false });

        Assert.EndsWith("X.\n.X\n..", text);
        Assert.False(text.EndsWith("..\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WriteAsync_WhenCalledTwice_ThenProducesIdenticalBytes()
    {
        var plan = WriterFixtures.TwoColumnPlan();

        var first = await WriterFixtures.WriteCxtBytesAsync(plan, Objects, WriterOptions.V2Compat);
        var second = await WriterFixtures.WriteCxtBytesAsync(plan, Objects, WriterOptions.V2Compat);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task WriteAsync_WhenNoBom_ThenFirstByteIsB()
    {
        var bytes = await WriterFixtures.WriteCxtBytesAsync(
            WriterFixtures.TwoColumnPlan(), Objects, WriterOptions.Native);

        Assert.Equal((byte)'B', bytes[0]);
    }
}
