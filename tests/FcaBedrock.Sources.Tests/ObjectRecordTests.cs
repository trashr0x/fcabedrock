namespace FcaBedrock.Sources.Tests;

public sealed class ObjectRecordTests
{
    [Fact]
    public void Field_WhenWithinWidth_ThenReturnsValue()
    {
        var record = new ObjectRecord("0", ["x", null]);

        Assert.Equal("x", record.Field(0));
        Assert.Null(record.Field(1)); // an in-range null cell (empty / missing_token, normalized by the source)
    }

    [Fact]
    public void Field_WhenIndexAtOrBeyondWidth_ThenNull()
    {
        // Ragged short row (D-085): an absent mapped cell reads as null, never throws.
        var record = new ObjectRecord("0", ["x"]);

        Assert.Null(record.Field(1));
        Assert.Null(record.Field(99));
    }

    [Fact]
    public void Field_WhenNegativeIndex_ThenThrows()
    {
        // A negative column is a programmer error, not a "missing cell".
        var record = new ObjectRecord("0", ["x"]);

        Assert.Throws<ArgumentOutOfRangeException>(() => record.Field(-1));
    }
}
