using System.Runtime.CompilerServices;
using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

// Shared builders for the probe suites: the two session flavours (a real CSV session, and the
// in-memory one that proves the engine has no delimited-source coupling) plus the small
// assertion helpers every suite repeats.
internal static class ProbeFixtures
{
    public static SourceReadSettings WideSettings(
        char delimiter = ',', bool hasHeader = true, string missingToken = "?") =>
        SourceReadSettings.CreateWide(delimiter: delimiter, hasHeader: hasHeader, missingToken: missingToken);

    public static WideCsvSession Csv(
        string text, char delimiter = ',', bool hasHeader = true, string missingToken = "?") =>
        new(() => new MemoryStream(Encoding.UTF8.GetBytes(text)),
            WideSettings(delimiter, hasHeader, missingToken));

    public static Func<Stream> Bytes(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return () => new MemoryStream(bytes);
    }

    /// <summary>Probes a CSV string with the settings that produced it (the ordinary caller shape).</summary>
    public static async Task<Diagnosed<SpecDocument>> ProbeCsvAsync(
        string text, char delimiter = ',', bool hasHeader = true, string missingToken = "?",
        ProbeOptions? options = null)
    {
        var settings = WideSettings(delimiter, hasHeader, missingToken);
        var session = new WideCsvSession(Bytes(text), settings);
        return await Prober.ProbeAsync(session, settings, options);
    }

    public static SpecDocument Draft(Diagnosed<SpecDocument> result)
    {
        Assert.True(result.TryGetValue(out var document), Describe(result.Diagnostics));
        return document;
    }

    public static string Toml(Diagnosed<SpecDocument> result) => SpecWriter.Write(Draft(result));

    public static IReadOnlyList<string>? DomainOf(SpecDocument draft, string name) =>
        draft.Attributes.Single(a => a.Name == name).DeclaredDomain;

    public static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        diagnostics.Count == 0
            ? "<none>"
            : string.Join("; ", diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));

    /// <summary>
    /// A wide session over in-memory records: no stream, file, delimiter, encoding, or Sep
    /// anywhere. Proves the engine consumes only the D-109 seam, counts record enumerations so
    /// "one cleaned pass" is assertable, and can be told to fail or stall at a chosen point.
    /// </summary>
    internal sealed class FakeWideSession(
        SourceSchema schema,
        IReadOnlyList<ObjectRecord> records,
        Func<Exception>? schemaFailure = null,
        Func<Exception>? recordFailure = null,
        int failAfterRecords = 0,
        Action? beforeEachRecord = null) : IWideSourceSession
    {
        public int SchemaReads { get; private set; }

        public int RecordEnumerations { get; private set; }

        public int RecordsYielded { get; private set; }

        public SourceShape Shape { get; init; } = SourceShape.Wide;

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

        public async IAsyncEnumerable<ObjectRecord> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RecordEnumerations++;
            await Task.Yield();
            var index = 0;
            foreach (var record in records)
            {
                // Mirrors every real adapter: the token is honoured before each record, so a
                // cancellation mid-pass surfaces from the seam exactly as the CSV session's does.
                cancellationToken.ThrowIfCancellationRequested();
                beforeEachRecord?.Invoke();
                if (recordFailure is not null && index == failAfterRecords)
                {
                    throw recordFailure();
                }

                index++;
                RecordsYielded++;
                yield return record;
            }

            if (recordFailure is not null && index == failAfterRecords)
            {
                throw recordFailure();
            }
        }
    }

    public static FakeWideSession Fake(SourceSchema schema, params string?[][] rows) =>
        new(schema, Records(rows));

    public static IReadOnlyList<ObjectRecord> Records(params string?[][] rows)
    {
        var records = new List<ObjectRecord>(rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            records.Add(new ObjectRecord(i.ToString(System.Globalization.CultureInfo.InvariantCulture), rows[i]));
        }

        return records;
    }
}
