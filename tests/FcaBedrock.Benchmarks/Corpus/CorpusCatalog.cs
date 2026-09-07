using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>One prepared file's identity: its name, its exact byte length, and its SHA-256.</summary>
internal sealed record CorpusFile(string FileName, long ByteLength, string Sha256);

/// <summary>
/// The recorded identity of one prepared corpus case. Everything a later reader needs in order to
/// decide whether the bytes on disk are still the bytes a measurement was stated against: the
/// generator revision that produced them, the exact geometry, and a digest per file.
/// </summary>
internal sealed record CorpusEntry(
    string Id,
    string Family,
    string Tier,
    int GeneratorRevision,
    long Records,
    int Columns,
    CorpusFile Data,
    CorpusFile Spec)
{
    /// <summary>
    /// True when <see cref="Records"/> was fixed by the case's tier rather than measured after
    /// acquisition. It is a property of the <em>expectation</em>, not of the stored entry: a
    /// written catalog always carries a real count, and this only decides whether a re-read one is
    /// required to equal a number known in advance. It is therefore deliberately not serialized.
    /// </summary>
    public bool RecordsDeclared { get; init; } = true;
}

/// <summary>
/// Reads and writes the corpus catalog: one small fixed-shape text file per prepared case.
/// <para>
/// The format is a hand-written ordered <c>key = value</c> list rather than a serializer's output,
/// for the same reason the spec writer is hand-rolled (D-075): a metadata contract that decides
/// whether cached inputs are accepted must have exact, stable bytes that cannot drift with a
/// library upgrade. It is written <b>last</b>, after the files it describes are complete on disk,
/// so an interrupted preparation leaves no entry and is therefore refused rather than reused.
/// </para>
/// </summary>
internal static class CorpusCatalog
{
    private const string FormatKey = "catalog_format";
    private const int FormatVersion = 1;

    /// <summary>The directory holding the catalog entries.</summary>
    public static string Directory => Path.Combine(BenchmarkPaths.CorpusDirectory, "catalog");

    /// <summary>The catalog file for <paramref name="id"/>.</summary>
    public static string PathFor(string id) => Path.Combine(Directory, id + ".txt");

    /// <summary>Serializes <paramref name="entry"/> to its canonical text.</summary>
    public static string Render(CorpusEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var text = new StringBuilder();
        Append(text, FormatKey, FormatVersion.ToString(CultureInfo.InvariantCulture));
        Append(text, "id", entry.Id);
        Append(text, "family", entry.Family);
        Append(text, "tier", entry.Tier);
        Append(text, "generator_revision", entry.GeneratorRevision.ToString(CultureInfo.InvariantCulture));
        Append(text, "records", entry.Records.ToString(CultureInfo.InvariantCulture));
        Append(text, "columns", entry.Columns.ToString(CultureInfo.InvariantCulture));
        Append(text, "data_file", entry.Data.FileName);
        Append(text, "data_bytes", entry.Data.ByteLength.ToString(CultureInfo.InvariantCulture));
        Append(text, "data_sha256", entry.Data.Sha256);
        Append(text, "spec_file", entry.Spec.FileName);
        Append(text, "spec_bytes", entry.Spec.ByteLength.ToString(CultureInfo.InvariantCulture));
        Append(text, "spec_sha256", entry.Spec.Sha256);
        return text.ToString();
    }

    /// <summary>
    /// Parses catalog text. Returns <see langword="null"/> for anything this reader does not fully
    /// understand — an unknown format version, a missing key, an unparseable number. A catalog we
    /// cannot read is treated exactly like an absent one: the corpus is re-prepared, never guessed at.
    /// </summary>
    public static CorpusEntry? TryParse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                continue;
            }

            var separator = trimmed.IndexOf(" = ", StringComparison.Ordinal);
            if (separator < 0)
            {
                return null;
            }

            fields[trimmed[..separator]] = trimmed[(separator + 3)..];
        }

        if (!TryInt(fields, FormatKey, out var format) || format != FormatVersion)
        {
            return null;
        }

        if (!fields.TryGetValue("id", out var id)
            || !fields.TryGetValue("family", out var family)
            || !fields.TryGetValue("tier", out var tier)
            || !fields.TryGetValue("data_file", out var dataFile)
            || !fields.TryGetValue("data_sha256", out var dataHash)
            || !fields.TryGetValue("spec_file", out var specFile)
            || !fields.TryGetValue("spec_sha256", out var specHash)
            || !TryInt(fields, "generator_revision", out var revision)
            || !TryInt(fields, "columns", out var columns)
            || !TryLong(fields, "records", out var records)
            || !TryLong(fields, "data_bytes", out var dataBytes)
            || !TryLong(fields, "spec_bytes", out var specBytes))
        {
            return null;
        }

        return new CorpusEntry(
            id,
            family,
            tier,
            revision,
            records,
            columns,
            new CorpusFile(dataFile, dataBytes, dataHash),
            new CorpusFile(specFile, specBytes, specHash));
    }

    /// <summary>Reads the entry for <paramref name="id"/>, or <see langword="null"/> when absent or unreadable.</summary>
    public static CorpusEntry? Read(string id)
    {
        var path = PathFor(id);
        return File.Exists(path) ? TryParse(File.ReadAllText(path)) : null;
    }

    /// <summary>Writes <paramref name="entry"/>. Called only after every file it describes is complete.</summary>
    public static void Write(CorpusEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllBytes(PathFor(entry.Id), Encoding.UTF8.GetBytes(Render(entry)));
    }

    /// <summary>The lowercase hexadecimal SHA-256 of a file, computed by streaming it.</summary>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>The lowercase hexadecimal SHA-256 of <paramref name="bytes"/>.</summary>
    public static string HashBytes(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void Append(StringBuilder text, string key, string value) =>
        text.Append(key).Append(" = ").Append(value).Append('\n');

    private static bool TryInt(Dictionary<string, string> fields, string key, out int value)
    {
        value = 0;
        return fields.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryLong(Dictionary<string, string> fields, string key, out long value)
    {
        value = 0;
        return fields.TryGetValue(key, out var text)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
