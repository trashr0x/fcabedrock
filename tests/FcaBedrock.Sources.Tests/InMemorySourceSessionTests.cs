using System.Runtime.CompilerServices;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources.Tests;

// The seam's source-neutrality, proven structurally: these sessions are implemented over
// in-memory record lists with no stream, file, delimiter, encoding, or Sep anywhere. If the
// seam ever grew a delimited-source concept, this file would stop compiling — which is the
// point (D-109/M5-IP-002). It is also the shape a future SQL/SPARQL adapter would take, and
// the shape Slice 3's probe tests will drive the engine with.
public sealed class InMemorySourceSessionTests
{
    private sealed class InMemoryWideSession(SourceSchema schema, IReadOnlyList<ObjectRecord> records)
        : IWideSourceSession
    {
        public int Enumerations { get; private set; }

        public SourceShape Shape => SourceShape.Wide;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(schema);
        }

        public async IAsyncEnumerable<ObjectRecord> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Enumerations++;
            await Task.Yield();
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
            }
        }
    }

    private sealed class InMemoryTripleSession(SourceSchema schema, IReadOnlyList<string?[]> rows)
        : ITripleSourceSession
    {
        public SourceShape Shape => SourceShape.Triple;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(schema);
        }

        public async IAsyncEnumerable<TripleRow> ReadRowsAsync(
            TripleColumns columns, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            for (var i = 0; i < rows.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = rows[i];
                yield return new TripleRow(
                    i,
                    columns.Subject < row.Length ? row[columns.Subject] : null,
                    columns.Predicate < row.Length ? row[columns.Predicate] : null,
                    columns.Value < row.Length ? row[columns.Value] : null);
            }
        }
    }

    [Fact]
    public async Task InMemoryWideSession_SatisfiesTheSeamWithoutAnyDelimitedSourceConcept()
    {
        ISourceSession session = new InMemoryWideSession(
            new SourceSchema(2, ["colour", "size"]),
            [new ObjectRecord("0", ["red", "big"]), new ObjectRecord("1", ["blue", null])]);

        var schema = await session.GetSchemaAsync();
        var records = new List<string>();
        await foreach (var record in ((IWideSourceSession)session).ReadAsync())
        {
            records.Add($"{record.Name}:{record.Field(0)}/{record.Field(1) ?? "<null>"}");
        }

        Assert.Equal(SourceShape.Wide, session.Shape);
        Assert.Equal(["colour", "size"], schema.Header);
        Assert.Equal(["0:red/big", "1:blue/<null>"], records);
    }

    [Fact]
    public async Task InMemoryTripleSession_ReadsThroughThePerReadRoleMap()
    {
        ITripleSourceSession session = new InMemoryTripleSession(
            new SourceSchema(3),
            [["v0", "p", "s0"]]);

        var natural = new List<string>();
        await foreach (var row in session.ReadRowsAsync(new TripleColumns(0, 1, 2)))
        {
            natural.Add($"{row.Subject}/{row.Predicate}/{row.Value}");
        }

        var reversed = new List<string>();
        await foreach (var row in session.ReadRowsAsync(new TripleColumns(2, 1, 0)))
        {
            reversed.Add($"{row.Subject}/{row.Predicate}/{row.Value}");
        }

        Assert.Equal(["v0/p/s0"], natural);
        Assert.Equal(["s0/p/v0"], reversed);
    }

    [Fact]
    public async Task InMemoryWideSession_IsReplayableAndCountsRecordEnumerations()
    {
        // Probe's "one data-record pass" (D-106) is a claim about record ENUMERATIONS, which a
        // session can count. Slice 3 asserts the probe engine makes exactly one.
        var session = new InMemoryWideSession(new SourceSchema(1), [new ObjectRecord("0", ["x"])]);

        await foreach (var _ in session.ReadAsync())
        {
        }

        await foreach (var _ in session.ReadAsync())
        {
        }

        Assert.Equal(2, session.Enumerations);
    }

    [Fact]
    public async Task InMemorySession_CancellationPropagatesUnwrapped()
    {
        var session = new InMemoryWideSession(new SourceSchema(1), [new ObjectRecord("0", ["x"])]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in session.ReadAsync(cts.Token))
            {
            }
        });
    }
}
