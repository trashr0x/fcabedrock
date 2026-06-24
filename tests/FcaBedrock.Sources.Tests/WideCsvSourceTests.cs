using System.Text;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources.Tests;

public sealed class WideCsvSourceTests
{
    private static Binding Wide(char delimiter = ',', bool hasHeader = true, string missingToken = "?") =>
        new(SourceShape.Wide, delimiter, '"', hasHeader, "invariant", missingToken, new RowIndexObjectKey());

    private static WideCsvSource Source(string text, Binding binding) =>
        new(() => new MemoryStream(Encoding.UTF8.GetBytes(text)), binding);

    private static async Task<List<ObjectRecord>> ReadAllAsync(WideCsvSource source)
    {
        var records = new List<ObjectRecord>();
        await foreach (var record in source.ReadAsync())
        {
            records.Add(record);
        }

        return records;
    }

    [Fact]
    public async Task ReadAsync_WhenCommaWithHeader_ThenSkipsHeaderAndNamesByRowIndex()
    {
        var records = await ReadAllAsync(Source("a,b\nx,y\np,q", Wide()));

        Assert.Equal(2, records.Count);
        Assert.Equal(["0", "1"], records.Select(r => r.Name));
        Assert.Equal("x", records[0].Field(0));
        Assert.Equal("q", records[1].Field(1));
    }

    [Fact]
    public async Task ReadAsync_WhenTabNoHeader_ThenEveryRowIsData()
    {
        var records = await ReadAllAsync(Source("x\ty\np\tq", Wide('\t', hasHeader: false)));

        Assert.Equal(2, records.Count);
        Assert.Equal("x", records[0].Field(0));
        Assert.Equal("y", records[0].Field(1));
    }

    [Fact]
    public async Task ReadAsync_WhenMissingTokenOrEmpty_ThenFieldIsNull()
    {
        var records = await ReadAllAsync(Source("a,b,c\n?,,z", Wide()));

        Assert.Null(records[0].Field(0)); // missing token "?"
        Assert.Null(records[0].Field(1)); // empty cell
        Assert.Equal("z", records[0].Field(2));
    }

    [Fact]
    public async Task ReadAsync_WhenQuotedFieldContainsDelimiter_ThenFieldIsOneValue()
    {
        var records = await ReadAllAsync(Source("a,b\n\"x,y\",z", Wide()));

        Assert.Equal("x,y", records[0].Field(0));
        Assert.Equal("z", records[0].Field(1));
    }

    [Fact]
    public async Task GetSchemaAsync_WhenHeader_ThenReturnsHeaderNamesAndCount()
    {
        var schema = await Source("a,b,c\n1,2,3", Wide()).GetSchemaAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, schema.ColumnCount);
        Assert.Equal(["a", "b", "c"], schema.Header);
    }

    [Fact]
    public async Task GetSchemaAsync_WhenNoHeader_ThenColumnCountFromFirstRowAndNoHeader()
    {
        var schema = await Source("1,2,3", Wide(hasHeader: false)).GetSchemaAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, schema.ColumnCount);
        Assert.Null(schema.Header);
    }

    [Fact]
    public async Task ReadAsync_WhenCalledTwice_ThenReplaysFromStart()
    {
        var source = Source("a\nx\ny", Wide());

        var first = await ReadAllAsync(source);
        var second = await ReadAllAsync(source);

        Assert.Equal(2, second.Count);
        Assert.Equal(first.Select(r => r.Field(0)), second.Select(r => r.Field(0)));
    }
}
