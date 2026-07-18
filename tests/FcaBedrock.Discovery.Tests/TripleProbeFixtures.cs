using System.Runtime.CompilerServices;
using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

// The triple counterparts of ProbeFixtures: the real CSV session, the in-memory one that proves
// the engine has no delimited-source coupling, and the small builders every triple suite repeats.
internal static class TripleProbeFixtures
{
    public static SourceReadSettings TripleSettings(
        char delimiter = ',',
        bool hasHeader = false,
        string missingToken = "?",
        TripleOrdering ordering = TripleOrdering.Unordered) =>
        SourceReadSettings.CreateTriple(
            delimiter: delimiter, hasHeader: hasHeader, missingToken: missingToken, ordering: ordering);

    /// <summary>Probes a triple CSV string with the settings that produced it (the ordinary caller shape).</summary>
    public static async Task<Diagnosed<SpecDocument>> ProbeTripleCsvAsync(
        string text,
        char delimiter = ',',
        bool hasHeader = false,
        string missingToken = "?",
        TripleOrdering ordering = TripleOrdering.Unordered,
        TripleColumnsSection? columns = null,
        ProbeOptions? options = null)
    {
        var settings = TripleSettings(delimiter, hasHeader, missingToken, ordering);
        var session = new TripleCsvSession(ProbeFixtures.Bytes(text), settings);
        return await Prober.ProbeTripleAsync(session, settings, columns, options);
    }

    /// <summary>The complete index-addressed role map, spelled out for tests that supply one.</summary>
    public static TripleColumnsSection Indexes(int subject, int predicate, int value) =>
        new(new IndexColumnRef(subject), new IndexColumnRef(predicate), new IndexColumnRef(value));

    /// <summary>The complete name-addressed role map (§5.3 — requires <c>has_header = true</c>).</summary>
    public static TripleColumnsSection Names(string subject, string predicate, string value) =>
        new(new NameColumnRef(subject), new NameColumnRef(predicate), new NameColumnRef(value));

    public static PredicateSourceSection SourceOf(SpecDocument draft, string name) =>
        (PredicateSourceSection)draft.Attributes.Single(a => a.Name == name).Source!;

    public static IReadOnlyList<TripleRow> Rows(params (string? Subject, string? Predicate, string? Value)[] rows)
    {
        var built = new List<TripleRow>(rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            built.Add(new TripleRow(i, rows[i].Subject, rows[i].Predicate, rows[i].Value));
        }

        return built;
    }

    public static FakeTripleSession Fake(params (string? Subject, string? Predicate, string? Value)[] rows) =>
        new(new SourceSchema(3), Rows(rows));

    /// <summary>
    /// A triple session over in-memory rows: no stream, file, delimiter, encoding, or Sep
    /// anywhere. Records the role map it was read under and counts enumerations, so "exactly one
    /// pass, through the resolved roles" is assertable rather than assumed.
    /// </summary>
    internal sealed class FakeTripleSession(
        SourceSchema schema,
        IReadOnlyList<TripleRow> rows,
        Func<Exception>? schemaFailure = null,
        Func<Exception>? rowFailure = null,
        int failAfterRows = 0,
        Action? beforeEachRow = null) : ITripleSourceSession
    {
        public int SchemaReads { get; private set; }

        public int RowEnumerations { get; private set; }

        public int RowsYielded { get; private set; }

        /// <summary>Every role map this session was read under, in call order.</summary>
        public List<TripleColumns> RolesRead { get; } = [];

        public SourceShape Shape { get; init; } = SourceShape.Triple;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
        {
            SchemaReads++;
            cancellationToken.ThrowIfCancellationRequested();
            if (schemaFailure is not null)
            {
                throw schemaFailure();
            }

            return ValueTask.FromResult(schema);
        }

        public async IAsyncEnumerable<TripleRow> ReadRowsAsync(
            TripleColumns columns,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RowEnumerations++;
            RolesRead.Add(columns);
            await Task.Yield();
            var index = 0;
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                beforeEachRow?.Invoke();
                if (rowFailure is not null && index == failAfterRows)
                {
                    throw rowFailure();
                }

                index++;
                RowsYielded++;
                yield return row;
            }

            if (rowFailure is not null && index == failAfterRows)
            {
                throw rowFailure();
            }
        }
    }

    /// <summary>UTF-8 bytes for a triple CSV, for callers that need the factory twice.</summary>
    public static Func<Stream> Bytes(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return () => new MemoryStream(bytes);
    }
}
