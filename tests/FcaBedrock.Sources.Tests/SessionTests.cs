using System.Collections.Immutable;
using System.Text;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources.Tests;

// The two-stage source bootstrap sessions (D-098/G-1): schema caching/retry lifecycle and binding.
public sealed class SessionTests
{
    private static SourceReadSettings WideSettings(bool hasHeader = false, string missingToken = "?") =>
        SourceReadSettings.Create(SourceShape.Wide, "utf-8", ',', '"', hasHeader, missingToken, ordering: null);

    private static Func<Stream> Opener(string text) => () => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task GetSchemaAsync_WhenCalledTwice_ThenSchemaIsCachedAndStreamOpenedOnce()
    {
        var opens = 0;
        var session = new WideCsvSession(() => { opens++; return new MemoryStream(Encoding.UTF8.GetBytes("a\nb")); }, WideSettings());

        var first = await session.GetSchemaAsync();
        var second = await session.GetSchemaAsync();

        Assert.Equal(1, first.ColumnCount);
        Assert.Equal(first, second);
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task GetSchemaAsync_WhenHeadered_ThenCachedHeaderIsImmutableAndOpenedOnce()
    {
        var opens = 0;
        var session = new WideCsvSession(
            () => { opens++; return new MemoryStream(Encoding.UTF8.GetBytes("id,age\n1,2")); },
            WideSettings(hasHeader: true));

        var first = await session.GetSchemaAsync();
        var second = await session.GetSchemaAsync();

        Assert.Equal(["id", "age"], first.Header);
        Assert.Equal(first, second);
        Assert.Equal(1, opens);
        // The cached header is immutable storage — no castable mutable array survives (D-098).
        Assert.False(first.Header is string[]);
        Assert.False(first.Header is List<string>);
        Assert.IsType<ImmutableArray<string>>(first.Header);
    }

    [Fact]
    public async Task GetSchemaAsync_WhenFirstReadFails_ThenNothingCachedAndNextRetries()
    {
        var attempts = 0;
        Func<Stream> factory = () =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("transient open failure");
            }

            return new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2"));
        };
        var session = new WideCsvSession(factory, WideSettings());

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetSchemaAsync().AsTask());
        var schema = await session.GetSchemaAsync();

        Assert.Equal(2, schema.ColumnCount);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Bind_BeforeSchemaRead_ThenThrows()
    {
        var session = new WideCsvSession(Opener("a\nb"), WideSettings());

        Assert.Throws<InvalidOperationException>(() => session.Bind(SampleResolution(new SourceSchema(1))));
    }

    [Fact]
    public async Task Bind_WhenSettingsAndSchemaMatch_ThenReturnsTokenCarryingSource()
    {
        var session = new WideCsvSession(Opener("a\nb"), WideSettings());
        var schema = await session.GetSchemaAsync();
        var resolved = SampleResolution(schema);

        var bound = session.Bind(resolved);
        var again = session.Bind(resolved);

        Assert.Equal(new TokenProvenance(resolved), bound.Provenance);
        // Repeated Bind is allowed, each producing a fresh bound source over the same stream factory.
        Assert.NotSame(bound, again);
    }

    [Fact]
    public async Task Bind_WhenSchemaDiffers_ThenThrows()
    {
        var session = new WideCsvSession(Opener("a\nb"), WideSettings());
        await session.GetSchemaAsync();

        Assert.Throws<InvalidOperationException>(() => session.Bind(SampleResolution(new SourceSchema(3))));
    }

    [Fact]
    public async Task Bind_WhenSettingsDiffer_ThenThrows()
    {
        // The session reads with missing_token "?"; the resolution was prepared with "NA".
        var session = new WideCsvSession(Opener("a\nb"), WideSettings(missingToken: "?"));
        var schema = await session.GetSchemaAsync();

        Assert.Throws<InvalidOperationException>(() => session.Bind(SampleResolution(schema, missingToken: "NA")));
    }

    [Fact]
    public async Task GetSchemaAsync_WhenCancelled_ThenNothingCachedAndNextRetries()
    {
        var opens = 0;
        var session = new WideCsvSession(() => { opens++; return new MemoryStream(Encoding.UTF8.GetBytes("a\nb")); }, WideSettings());

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.GetSchemaAsync(cts.Token).AsTask());

        var schema = await session.GetSchemaAsync();
        Assert.Equal(1, schema.ColumnCount);
    }

    // --- TripleCsvSession lifecycle (role-map binding) ---

    [Fact]
    public async Task TripleSession_BindsRoleMapAndCarriesToken()
    {
        var settings = SourceReadSettings.Create(SourceShape.Triple, "utf-8", ',', '"', false, "?", TripleOrdering.Unordered);
        var session = new TripleCsvSession(() => new MemoryStream(Encoding.UTF8.GetBytes("s,p,v")), settings);
        var schema = await session.GetSchemaAsync();
        var resolved = SampleTripleResolution(schema);

        var bound = session.Bind(resolved);

        Assert.Equal(new TokenProvenance(resolved), bound.Provenance);
    }

    [Fact]
    public void TripleSession_BindBeforeSchema_ThenThrows()
    {
        var settings = SourceReadSettings.Create(SourceShape.Triple, "utf-8", ',', '"', false, "?", TripleOrdering.Unordered);
        var session = new TripleCsvSession(() => new MemoryStream(Encoding.UTF8.GetBytes("s,p,v")), settings);

        Assert.Throws<InvalidOperationException>(() => session.Bind(SampleTripleResolution(new SourceSchema(3))));
    }

    [Fact]
    public async Task TripleSession_WhenBoundWithNonDefaultRoleMap_ThenReadsRowThroughIt()
    {
        // A non-default role map (subject=2, predicate=1, value=0) over "v0,p,s0" must extract
        // subject s0, predicate p, value v0 — proving the bound source reads through the resolved map.
        var settings = SourceReadSettings.Create(SourceShape.Triple, "utf-8", ',', '"', false, "?", TripleOrdering.Unordered);
        var session = new TripleCsvSession(() => new MemoryStream(Encoding.UTF8.GetBytes("v0,p,s0")), settings);
        var schema = await session.GetSchemaAsync();
        var bound = session.Bind(SampleTripleResolution(schema, new TripleColumns(2, 1, 0)));

        var rows = new List<TripleRow>();
        await foreach (var row in bound.ReadRowsAsync())
        {
            rows.Add(row);
        }

        var single = Assert.Single(rows);
        Assert.Equal("s0", single.Subject);
        Assert.Equal("p", single.Predicate);
        Assert.Equal("v0", single.Value);
    }

    private static ResolvedSpec SampleTripleResolution(SourceSchema schema, TripleColumns? columns = null)
    {
        var roles = columns ?? new TripleColumns(0, 1, 2);
        var binding = new Binding(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", "?",
            new ColumnObjectKey(roles.Subject, DuplicateObjectPolicy.Fail), roles, TripleOrdering.Unordered);
        var spec = new BedrockSpec(binding,
            [new AttributeSpec("g", new PredicateSource("p", SourceValueType.String), Include: true,
                new IdentityDiscretizer(), new NominalScale(), ["a"], RestrictTo: [],
                new Dictionary<string, string>(), MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        return ResolvedSpec.Create(spec, schema,
            SourceReadSettings.Create(SourceShape.Triple, "utf-8", ',', '"', false, "?", TripleOrdering.Unordered), []);
    }

    private static ResolvedSpec SampleResolution(SourceSchema schema, string missingToken = "?")
    {
        var spec = new BedrockSpec(
            new Binding(SourceShape.Wide, "utf-8", ',', '"', HasHeader: false, "invariant", missingToken, new RowIndexObjectKey()),
            [new AttributeSpec("g", new ColumnSource(0, SourceValueType.String), Include: true,
                new IdentityDiscretizer(), new NominalScale(), ["a", "b"], RestrictTo: [],
                new Dictionary<string, string>(), MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        return ResolvedSpec.Create(spec, schema, WideSettings(missingToken: missingToken), []);
    }
}
