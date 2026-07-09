using System.Text;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources.Tests;

public sealed class TripleCsvSourceTests
{
    private static Binding Triple(
        TripleColumns? columns = null, bool hasHeader = false, string missingToken = "?", char delimiter = ',')
    {
        var map = columns ?? new TripleColumns(0, 1, 2);
        return new(SourceShape.Triple, "utf-8", delimiter, '"', hasHeader, "invariant", missingToken,
            new ColumnObjectKey(map.Subject, DuplicateObjectPolicy.Fail), map, TripleOrdering.SubjectGrouped);
    }

    private static TripleCsvSource Source(string text, Binding binding) =>
        new(() => new MemoryStream(Encoding.UTF8.GetBytes(text)), binding);

    private static async Task<List<TripleRow>> ReadAllAsync(TripleCsvSource source)
    {
        var rows = new List<TripleRow>();
        await foreach (var row in source.ReadRowsAsync())
        {
            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public async Task ReadRowsAsync_WhenDefaultColumnsNoHeader_ThenReadsRolesAndRecordIndex()
    {
        var rows = await ReadAllAsync(Source("s0,p0,v0\ns1,p1,v1", Triple()));

        Assert.Equal([0, 1], rows.Select(r => r.RecordIndex));
        Assert.Equal(new TripleRow(0, "s0", "p0", "v0"), rows[0]);
        Assert.Equal(new TripleRow(1, "s1", "p1", "v1"), rows[1]);
    }

    [Fact]
    public async Task ReadRowsAsync_WhenReorderedColumns_ThenReadsRolesByResolvedIndex()
    {
        // subject = column 2, predicate = column 0, value = column 1.
        var rows = await ReadAllAsync(Source("p,v,s", Triple(new TripleColumns(2, 0, 1))));

        var row = Assert.Single(rows);
        Assert.Equal("s", row.Subject);
        Assert.Equal("p", row.Predicate);
        Assert.Equal("v", row.Value);
    }

    [Fact]
    public async Task ReadRowsAsync_WhenRoleEqualsMissingToken_ThenNullUniformlyForEveryRole()
    {
        // §5.1 / D-082: missing_token normalizes to null uniformly — subject and predicate too,
        // not only value. The Conversion layer decides what a null role means per role.
        var rows = await ReadAllAsync(Source("?,?,?", Triple()));

        var row = Assert.Single(rows);
        Assert.Null(row.Subject);
        Assert.Null(row.Predicate);
        Assert.Null(row.Value);
    }

    [Fact]
    public async Task ReadRowsAsync_WhenEmptyOrShort_ThenNullPerRole()
    {
        // Row 0 has empty subject and empty value; row 1 is too short for predicate/value.
        var rows = await ReadAllAsync(Source(",p,\ns", Triple()));

        Assert.Null(rows[0].Subject);       // empty
        Assert.Equal("p", rows[0].Predicate);
        Assert.Null(rows[0].Value);         // empty
        Assert.Equal("s", rows[1].Subject);
        Assert.Null(rows[1].Predicate);     // absent (short row)
        Assert.Null(rows[1].Value);         // absent (short row)
    }

    [Fact]
    public async Task ReadRowsAsync_WhenQuotedWhitespace_ThenPreservedNotNull()
    {
        // A quoted whitespace field keeps its interior whitespace (§5.1); it is not "missing".
        var rows = await ReadAllAsync(Source("\" \",p,v", Triple()));

        Assert.Equal(" ", Assert.Single(rows).Subject);
    }

    [Fact]
    public async Task ReadRowsAsync_WhenHasHeader_ThenFirstRowIsConsumed()
    {
        var withHeader = await ReadAllAsync(Source("subject,predicate,value\ns0,p0,v0", Triple(hasHeader: true)));
        var noHeader = await ReadAllAsync(Source("subject,predicate,value\ns0,p0,v0", Triple(hasHeader: false)));

        Assert.Equal("s0", Assert.Single(withHeader).Subject);
        Assert.Equal(2, noHeader.Count); // the header row is data when has_header = false (the triple default)
    }

    [Fact]
    public async Task ReadRowsAsync_WhenCalledTwice_ThenReplaysFromStart()
    {
        var source = Source("s0,p0,v0\ns1,p1,v1", Triple());

        var first = await ReadAllAsync(source);
        var second = await ReadAllAsync(source);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetSchemaAsync_WhenNoHeader_ThenColumnCountFromFirstRow()
    {
        var schema = await Source("s0,p0,v0", Triple()).GetSchemaAsync();

        Assert.Equal(3, schema.ColumnCount);
    }
}
