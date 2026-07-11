namespace FcaBedrock.Export.Tests;

public sealed class DatWriterTests
{
    [Fact]
    public async Task WriteAsync_WhenV2Compat_ThenCrlfAndTrailingSpacePerLine()
    {
        var objects = new[]
        {
            WriterFixtures.Object("0", 0, 1, 3, 5),
            WriterFixtures.Object("1", 0, 2, 3, 7),
        };

        var text = await WriterFixtures.WriteDatAsync(objects, WriterOptions.V2Compat);

        Assert.Equal("1 2 4 6 \r\n1 3 4 8 \r\n", text);
    }

    [Fact]
    public async Task WriteAsync_WhenNative_ThenLfAndNoTrailingSpace()
    {
        var objects = new[]
        {
            WriterFixtures.Object("0", 0, 1, 3, 5),
            WriterFixtures.Object("1", 0, 2, 3, 7),
        };

        var text = await WriterFixtures.WriteDatAsync(objects, WriterOptions.Native);

        Assert.Equal("1 2 4 6\n1 3 4 8\n", text);
    }

    [Fact]
    public async Task WriteAsync_WhenObjectHasNoCrosses_ThenBareEmptyLine()
    {
        var objects = new[]
        {
            WriterFixtures.Object("0", 0),
            WriterFixtures.Object("1"),
            WriterFixtures.Object("2", 1),
        };

        var text = await WriterFixtures.WriteDatAsync(objects, WriterOptions.Native);

        Assert.Equal("1\n\n2\n", text);
    }

    [Fact]
    public async Task WriteAsync_WhenTrailingNewlineDisabled_ThenOnlyFinalTerminatorOmitted()
    {
        // D-087: TrailingNewline = false suppresses ONLY the final line terminator; the
        // separators *between* lines are preserved (a bounded change, not a reflow). This is
        // what the shape-derived v2-compat triple .dat rule rides on.
        var objects = new[]
        {
            WriterFixtures.Object("0", 0, 1, 3, 5),
            WriterFixtures.Object("1", 0, 2, 3, 7),
            WriterFixtures.Object("2", 4),
        };

        var text = await WriterFixtures.WriteDatAsync(objects, WriterOptions.Native with { TrailingNewline = false });

        Assert.Equal("1 2 4 6\n1 3 4 8\n5", text);
    }

    [Fact]
    public async Task WriteAsync_WhenBaseIndexZero_ThenIdsAreZeroBased()
    {
        var text = await WriterFixtures.WriteDatAsync(
            [WriterFixtures.Object("0", 0, 3)],
            WriterOptions.Native with { BaseIndex = 0 });

        Assert.Equal("0 3\n", text);
    }
}
