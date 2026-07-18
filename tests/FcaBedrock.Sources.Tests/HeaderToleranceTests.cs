using System.Text;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources.Tests;

// Header tolerance for BOTH shapes (M5-IP-011). Sep's own header mode throws ArgumentException
// on a duplicate or multiply-blank header name, which made §5.3/§10.2 — where such a header is
// legal and binds by index — unreachable. The header is now consumed as the first parsed record,
// realizing that already-normative behavior. Unique-header and headerless behavior is unchanged;
// these tests pin both the expansion and the neutrality.
public sealed class HeaderToleranceTests
{
    private static Func<Stream> Opener(string text) => () => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static WideCsvSession WideSession(string text, bool hasHeader = true, string missingToken = "?") =>
        new(Opener(text), SourceReadSettings.CreateWide(hasHeader: hasHeader, missingToken: missingToken));

    private static TripleCsvSession TripleSession(string text, bool hasHeader = true, string missingToken = "?") =>
        new(Opener(text), SourceReadSettings.CreateTriple(hasHeader: hasHeader, missingToken: missingToken));

    private static Binding WideBinding(bool hasHeader, string missingToken = "?") =>
        new(SourceShape.Wide, "utf-8", ',', '"', hasHeader, "invariant", missingToken, new RowIndexObjectKey());

    private static Binding TripleBinding(bool hasHeader, TripleColumns roles, string missingToken = "?") =>
        new(SourceShape.Triple, "utf-8", ',', '"', hasHeader, "invariant", missingToken,
            new ColumnObjectKey(roles.Subject, DuplicateObjectPolicy.Fail), roles, TripleOrdering.Unordered);

    private static async Task<List<string>> RenderRecordsAsync(IAsyncEnumerable<ObjectRecord> records)
    {
        var rendered = new List<string>();
        await foreach (var record in records)
        {
            var fields = new List<string>();
            for (var i = 0; i < record.FieldCount; i++)
            {
                fields.Add(record.Field(i) ?? "<null>");
            }

            rendered.Add($"{record.Name}:[{string.Join("|", fields)}]");
        }

        return rendered;
    }

    private static async Task<List<string>> RenderRowsAsync(IAsyncEnumerable<TripleRow> rows)
    {
        var rendered = new List<string>();
        await foreach (var row in rows)
        {
            rendered.Add($"{row.RecordIndex}:{row.Subject ?? "<null>"}/{row.Predicate ?? "<null>"}/{row.Value ?? "<null>"}");
        }

        return rendered;
    }

    // --- The header matrix: every header kind × both shapes × schema/bound/unbound ---

    // Header row, and the header cells it must yield verbatim. Every case uses the same
    // three-column data body, so one matrix drives both shapes.
    public static TheoryData<string, string> HeaderKinds => new()
    {
        { "colour,size,weight", "colour|size|weight" },   // unique (must stay unchanged)
        { "a,a,b", "a|a|b" },                             // duplicate names
        { "a,b,a", "a|b|a" },                             // non-adjacent duplicates
        { "a,,c", "a||c" },                               // single blank
        { "a,,", "a||" },                                 // multiply blank
        { ",,", "||" },                                   // all blank
        { "?,size,weight", "?|size|weight" },             // a header equal to missing_token
        { "?,?,weight", "?|?|weight" },                   // missing-token-like AND duplicate
    };

    private const string DataBody = "s1,species,cat\ns2,colour,black\n";

    [Theory]
    [MemberData(nameof(HeaderKinds))]
    public async Task WideMatrix_SchemaBoundAndUnboundAgreeForEveryHeaderKind(string header, string expected)
    {
        var text = header + "\n" + DataBody;
        var session = WideSession(text);
        var boundSource = new WideCsvSource(Opener(text), WideBinding(hasHeader: true));

        var schema = await session.GetSchemaAsync();
        var unbound = await RenderRecordsAsync(session.ReadAsync());
        var bound = await RenderRecordsAsync(boundSource.ReadAsync());
        var boundSchema = await boundSource.GetSchemaAsync();

        // Header verbatim — never missing-normalized, never renamed or de-duplicated.
        Assert.Equal(expected, string.Join("|", schema.Header!));
        Assert.Equal(schema.Header, boundSchema.Header);
        Assert.Equal(3, schema.ColumnCount);

        // Header skipped exactly once; first data record is index 0; bound == unbound.
        Assert.Equal(["0:[s1|species|cat]", "1:[s2|colour|black]"], unbound);
        Assert.Equal(unbound, bound);
    }

    [Theory]
    [MemberData(nameof(HeaderKinds))]
    public async Task TripleMatrix_SchemaBoundAndUnboundAgreeForEveryHeaderKind(string header, string expected)
    {
        var text = header + "\n" + DataBody;
        var roles = new TripleColumns(0, 1, 2);
        var session = TripleSession(text);
        var boundSource = new TripleCsvSource(Opener(text), TripleBinding(hasHeader: true, roles));

        var schema = await session.GetSchemaAsync();
        var unbound = await RenderRowsAsync(session.ReadRowsAsync(roles));
        var bound = await RenderRowsAsync(boundSource.ReadRowsAsync());
        var boundSchema = await boundSource.GetSchemaAsync();

        Assert.Equal(expected, string.Join("|", schema.Header!));
        Assert.Equal(schema.Header, boundSchema.Header);
        Assert.Equal(3, schema.ColumnCount);

        Assert.Equal(["0:s1/species/cat", "1:s2/colour/black"], unbound);
        Assert.Equal(unbound, bound);
    }

    // The neutrality half, stated literally rather than computed: with has_header = false the very
    // same bytes yield that first row as record 0 — and, being DATA now, it IS missing-normalized,
    // which is exactly the asymmetry a header must not be subject to.
    [Theory]
    [InlineData("colour,size,weight", "0:[colour|size|weight]")]
    [InlineData("a,a,b", "0:[a|a|b]")]
    [InlineData("a,,c", "0:[a|<null>|c]")]
    [InlineData(",,", "0:[<null>|<null>|<null>]")]
    [InlineData("?,size,weight", "0:[<null>|size|weight]")]
    public async Task WideMatrix_WhenHeaderless_ThenThatRowIsOrdinaryNormalizedData(
        string firstRow, string expectedRecord)
    {
        var session = WideSession(firstRow + "\n" + DataBody, hasHeader: false);

        var schema = await session.GetSchemaAsync();
        var records = await RenderRecordsAsync(session.ReadAsync());

        Assert.Null(schema.Header);
        Assert.Equal(3, records.Count);
        Assert.Equal(expectedRecord, records[0]);
    }

    [Theory]
    [InlineData("s,p,v", "0:s/p/v")]
    [InlineData("a,a,a", "0:a/a/a")]
    [InlineData("?,p,v", "0:<null>/p/v")]
    [InlineData(",,", "0:<null>/<null>/<null>")]
    public async Task TripleMatrix_WhenHeaderless_ThenThatRowIsOrdinaryNormalizedData(
        string firstRow, string expectedRow)
    {
        var session = TripleSession(firstRow + "\n" + DataBody, hasHeader: false);

        var schema = await session.GetSchemaAsync();
        var rows = await RenderRowsAsync(session.ReadRowsAsync(new TripleColumns(0, 1, 2)));

        Assert.Null(schema.Header);
        Assert.Equal(3, rows.Count);
        Assert.Equal(expectedRow, rows[0]);
    }

    // --- A header cell equal to missing_token survives verbatim ---

    [Fact]
    public async Task Header_WhenCellEqualsMissingToken_ThenSurvivesVerbatimAndIsNotNormalized()
    {
        // Missing normalization is data-side only: "?" is a legitimate, unique, name-bindable
        // header, and must not become an empty or absent name.
        var schema = await WideSession("?,size\nred,big\n").GetSchemaAsync();

        Assert.Equal(["?", "size"], schema.Header);
    }

    [Fact]
    public async Task Header_WhenCellEqualsCustomMissingToken_ThenStillSurvivesVerbatim()
    {
        var schema = await WideSession("NA,size\nred,big\n", missingToken: "NA").GetSchemaAsync();

        Assert.Equal(["NA", "size"], schema.Header);
    }

    [Fact]
    public async Task Header_WhenCellEqualsMissingToken_ThenTheSameTokenStillNormalizesInData()
    {
        var session = WideSession("?,size\n?,big\n");

        var schema = await session.GetSchemaAsync();
        var records = await RenderRecordsAsync(session.ReadAsync());

        Assert.Equal(["?", "size"], schema.Header);
        Assert.Equal(["0:[<null>|big]"], records);
    }

    // --- Unchanged: unique headers and headerless sources ---

    [Fact]
    public async Task WideSchema_WhenHeaderUnique_ThenUnchanged()
    {
        var schema = await WideSession("colour,size\nred,big\n").GetSchemaAsync();

        Assert.Equal(2, schema.ColumnCount);
        Assert.Equal(["colour", "size"], schema.Header);
    }

    [Fact]
    public async Task WideSchema_WhenHeaderless_ThenColumnCountFromFirstDataRecordAndNoHeader()
    {
        var schema = await WideSession("red,big\nblue,small\n", hasHeader: false).GetSchemaAsync();

        Assert.Equal(2, schema.ColumnCount);
        Assert.Null(schema.Header);
    }

    [Fact]
    public async Task WideRecords_WhenHeaderless_ThenFirstPhysicalRowIsData()
    {
        var session = WideSession("red,big\nblue,small\n", hasHeader: false);

        Assert.Equal(["0:[red|big]", "1:[blue|small]"], await RenderRecordsAsync(session.ReadAsync()));
    }

    [Fact]
    public async Task TripleSchema_WhenHeaderless_ThenColumnCountFromFirstDataRecordAndNoHeader()
    {
        var schema = await TripleSession("s1,p,v\n", hasHeader: false).GetSchemaAsync();

        Assert.Equal(3, schema.ColumnCount);
        Assert.Null(schema.Header);
    }

    [Fact]
    public async Task Header_WhenPresent_ThenCellsKeepSection51TrimAndUnescape()
    {
        // Header cells are the post-Sep trim/unescape text verbatim: an unquoted cell loses its
        // surrounding whitespace, a quoted one keeps its interior, and "" unescapes to a quote.
        var schema = await WideSession("  a  ,\" b \",\"x\"\"y\"\n1,2,3\n").GetSchemaAsync();

        Assert.Equal(["a", " b ", "x\"y"], schema.Header);
    }

    // --- Degenerate inputs keep their existing schema shape ---

    [Fact]
    public async Task Schema_WhenHeaderedButSourceEmpty_ThenNoHeaderAndZeroColumns()
    {
        // An empty source has no record to take a header from, so the schema stays header-LESS
        // rather than gaining an empty header — matching what Sep itself reported before.
        var schema = await WideSession("").GetSchemaAsync();

        Assert.Equal(0, schema.ColumnCount);
        Assert.Null(schema.Header);
    }

    [Fact]
    public async Task Schema_WhenHeaderOnly_ThenHeaderReadAndNoRecords()
    {
        var session = WideSession("colour,size\n");

        var schema = await session.GetSchemaAsync();
        var records = await RenderRecordsAsync(session.ReadAsync());

        Assert.Equal(["colour", "size"], schema.Header);
        Assert.Empty(records);
    }

    [Fact]
    public async Task Schema_WhenSingleEmptyLineHeader_ThenOneBlankHeaderCell()
    {
        var schema = await WideSession("\n").GetSchemaAsync();

        Assert.Equal(1, schema.ColumnCount);
        Assert.Equal([""], schema.Header);
    }

}
