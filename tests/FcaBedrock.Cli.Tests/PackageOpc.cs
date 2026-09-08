using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// A package's entry names plus a <b>bounded</b> reader for the three small OPC parts, so the
/// validator below can be run against the real <c>.nupkg</c> and against a synthetic one built
/// entry by entry — the same code deciding both.
/// <para>
/// The bound is the point: an OPC part is discovered by <em>name</em>, and a validator that read
/// whatever was there would let a package dictate an unbounded allocation before a single rule had
/// run. A part larger than the bound is simply not one of the three this package ships.
/// </para>
/// </summary>
internal sealed class OpcArchive
{
    /// <summary>The largest any of the three OPC parts may be. They are hundreds of bytes.</summary>
    public const int MaxPartBytes = 64 * 1024;

    private readonly Dictionary<string, byte[]> _parts;

    private OpcArchive(IReadOnlyList<string> entries, Dictionary<string, byte[]> parts)
    {
        Entries = entries;
        _parts = parts;
    }

    /// <summary>Every entry name, in the order the archive lists them.</summary>
    public IReadOnlyList<string> Entries { get; }

    /// <summary>The real package on disk.</summary>
    public static OpcArchive OfPackage(string nupkgPath)
    {
        ArgumentNullException.ThrowIfNull(nupkgPath);

        using var archive = ZipFile.OpenRead(nupkgPath);
        var entries = new List<string>();
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var entry in archive.Entries)
        {
            entries.Add(entry.FullName);

            if (!IsOpcPart(entry.FullName) || parts.ContainsKey(entry.FullName))
            {
                continue;
            }

            // One byte past the bound, so "exactly at the bound" and "over it" are distinguishable
            // without trusting a declared length.
            using var content = entry.Open();
            using var buffer = new MemoryStream();
            var window = new byte[4096];
            var remaining = MaxPartBytes + 1;
            while (remaining > 0)
            {
                var read = content.Read(window, 0, Math.Min(window.Length, remaining));
                if (read == 0)
                {
                    break;
                }

                buffer.Write(window, 0, read);
                remaining -= read;
            }

            if (buffer.Length <= MaxPartBytes)
            {
                parts[entry.FullName] = buffer.ToArray();
            }
        }

        return new OpcArchive(entries, parts);
    }

    /// <summary>A synthetic package: exactly these entries, with these bytes for the OPC parts.</summary>
    public static OpcArchive Of(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var names = new List<string>();
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, bytes) in entries)
        {
            names.Add(name);
            if (bytes.Length <= MaxPartBytes)
            {
                parts[name] = bytes;
            }
        }

        return new OpcArchive(names, parts);
    }

    /// <summary>The part's bytes, or null when it is absent or larger than the bound.</summary>
    public byte[]? Read(string name) => _parts.GetValueOrDefault(name);

    private static bool IsOpcPart(string name) =>
        string.Equals(name, PackageOpc.RelationshipsPart, StringComparison.Ordinal)
        || string.Equals(name, PackageOpc.ContentTypesPart, StringComparison.Ordinal)
        || name.StartsWith(PackageOpc.CorePropertiesDirectory, StringComparison.Ordinal)
        || name.EndsWith(".nuspec", StringComparison.Ordinal);
}

/// <summary>
/// The bounded OPC consistency rules this package's two known producers must satisfy: which part
/// holds the core properties, that the relationship really points at it, and that the content-type
/// map really declares it as core properties.
/// <para>
/// <b>Why a shape and not a value.</b> The core-properties part's leaf is producer-chosen, and the
/// two producers this repository actually packs with choose differently: NuGet through SDK 10.0.302
/// emits a random GUID in its "N" form, and NuGet through SDK 10.0.400 hard-codes
/// <c>nuget.psmdcp</c> (upstream NuGet.Client change 5834c6b9, which fixed a deterministic-pack
/// file-handle leak). Both are legitimate; neither value may be pinned. So the rule is a
/// <b>closed two-producer set</b>, not <c>*.psmdcp</c> — accepting any leaf would admit an
/// arbitrary metadata part, <c>CON.psmdcp</c> among them, which Windows resolves to a console
/// device rather than a file when the package is extracted.
/// </para>
/// <para>
/// <b>What is deliberately not asserted.</b> Relationship identifiers, the producer's version
/// string, timestamps, archive order, the metadata bytes, and whole-package reproducibility all
/// differ between two packs of identical sources. None is pinned anywhere.
/// </para>
/// </summary>
internal static class PackageOpc
{
    /// <summary>The exact directory the core-properties part lives in.</summary>
    public const string CorePropertiesDirectory = "package/services/metadata/core-properties/";

    /// <summary>The package relationships part.</summary>
    public const string RelationshipsPart = "_rels/.rels";

    /// <summary>The content-type map.</summary>
    public const string ContentTypesPart = "[Content_Types].xml";

    /// <summary>The lowercase extension both accepted producers use.</summary>
    public const string PsmdcpExtension = ".psmdcp";

    /// <summary>The one leaf stem the deterministic producer emits, compared ordinally.</summary>
    public const string DeterministicStem = "nuget";

    /// <summary>The effective content type the core-properties part must carry.</summary>
    public const string CorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";

    /// <summary>The OPC relationship type that points at the core-properties part.</summary>
    public const string CorePropertiesRelationship =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

    /// <summary>The relationship type that points at the NuGet manifest.</summary>
    public const string ManifestRelationship = "http://schemas.microsoft.com/packaging/2010/07/manifest";

    private static readonly XNamespace RelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XNamespace ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly XNamespace CorePropertiesNamespace =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary>
    /// Whether <paramref name="name"/> is a core-properties part either accepted producer emits:
    /// the exact directory in forward-slash archive syntax, one leaf with no nested component, a
    /// stem that is either a lowercase 32-digit GUID-N or the ordinal literal <c>nuget</c>, and a
    /// lowercase <c>.psmdcp</c>.
    /// </summary>
    public static bool IsCorePropertiesPart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length <= CorePropertiesDirectory.Length + PsmdcpExtension.Length
            || !name.StartsWith(CorePropertiesDirectory, StringComparison.Ordinal)
            || !name.EndsWith(PsmdcpExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var stem = name[CorePropertiesDirectory.Length..^PsmdcpExtension.Length];
        if (stem.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        // The ordinal literal, so 'NuGet', 'nuget0' and ' nuget' are all refused.
        if (string.Equals(stem, DeterministicStem, StringComparison.Ordinal))
        {
            return true;
        }

        // `Guid.TryParseExact(..., "N", ...)` accepts EITHER case, so the ordinal round-trip
        // against `ToString("N")` — lowercase by definition — is what pins the canonical spelling.
        return Guid.TryParseExact(stem, "N", out var identifier)
            && string.Equals(
                stem, identifier.ToString("N", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every way <paramref name="archive"/>'s OPC wiring fails to hold together, as printable
    /// problems; an empty list is a package whose core-properties part is discoverable exactly the
    /// way OPC says it is. <paramref name="packageId"/> and <paramref name="packageVersion"/> come
    /// from the nuspec, so the metadata is checked for agreement rather than for exact bytes.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        OpcArchive archive, string packageId, string packageVersion)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var problems = new List<string>();

        var metadataParts = archive.Entries
            .Where(name => name.StartsWith(CorePropertiesDirectory, StringComparison.Ordinal))
            .ToList();

        if (metadataParts.Count != 1)
        {
            problems.Add($"the package holds {metadataParts.Count} core-properties parts, not one.");
            return problems;
        }

        var metadata = metadataParts[0];
        if (!IsCorePropertiesPart(metadata))
        {
            problems.Add(
                $"the core-properties part '{metadata}' is not '{CorePropertiesDirectory}"
                + $"<32 lowercase hexadecimal digits|{DeterministicStem}>{PsmdcpExtension}'.");
        }

        ValidateRelationships(archive, metadata, problems);
        ValidateContentType(archive, metadata, problems);
        ValidateCoreProperties(archive, metadata, packageId, packageVersion, problems);
        return problems;
    }

    private static void ValidateRelationships(OpcArchive archive, string metadata, List<string> problems)
    {
        if (Parse(archive, RelationshipsPart, problems) is not { } document)
        {
            return;
        }

        if (document.Root?.Name != RelationshipsNamespace + "Relationships")
        {
            problems.Add($"'{RelationshipsPart}' has no OPC Relationships root.");
            return;
        }

        var relationships = document.Root.Elements(RelationshipsNamespace + "Relationship").ToList();

        var identifiers = relationships
            .Select(relationship => (string?)relationship.Attribute("Id") ?? string.Empty)
            .ToList();

        // The VALUES are producer-generated and are never pinned; their uniqueness is a rule.
        if (identifiers.Distinct(StringComparer.Ordinal).Count() != identifiers.Count)
        {
            problems.Add($"'{RelationshipsPart}' repeats a relationship identifier.");
        }

        Require(relationships, CorePropertiesRelationship, metadata, problems);
        Require(relationships, ManifestRelationship, "FcaBedrock.Cli.nuspec", problems);

        void Require(List<XElement> all, string type, string expectedTarget, List<string> into)
        {
            var matching = all
                .Where(relationship =>
                    string.Equals((string?)relationship.Attribute("Type"), type, StringComparison.Ordinal))
                .ToList();

            if (matching.Count != 1)
            {
                into.Add($"'{RelationshipsPart}' declares {matching.Count} '{type}' relationships, not one.");
                return;
            }

            var mode = (string?)matching[0].Attribute("TargetMode");
            if (mode is not null && !string.Equals(mode, "Internal", StringComparison.Ordinal))
            {
                into.Add($"the '{type}' relationship is '{mode}' rather than an internal target.");
                return;
            }

            var target = (string?)matching[0].Attribute("Target") ?? string.Empty;
            if (!Resolves(target, expectedTarget))
            {
                into.Add($"the '{type}' relationship targets '{target}', not '{expectedTarget}'.");
                return;
            }

            if (!archive.Entries.Contains(expectedTarget, StringComparer.Ordinal))
            {
                into.Add($"the '{type}' relationship targets '{expectedTarget}', which the package does not hold.");
            }
        }
    }

    // Both known producers write an absolute part name, so this is exactly what has to be
    // understood: an optional single leading slash, then the already-validated entry name compared
    // ordinally. A traversal, a backslash, or a percent-encoded alias resolves to nothing rather
    // than being decoded into a match — no general URI resolver is needed for two known producers.
    private static bool Resolves(string target, string entryName)
    {
        if (target.Length == 0
            || target.Contains('\\', StringComparison.Ordinal)
            || target.Contains('%', StringComparison.Ordinal)
            || target.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var trimmed = target[0] == '/' ? target[1..] : target;
        return string.Equals(trimmed, entryName, StringComparison.Ordinal);
    }

    private static void ValidateContentType(OpcArchive archive, string metadata, List<string> problems)
    {
        if (Parse(archive, ContentTypesPart, problems) is not { } document)
        {
            return;
        }

        if (document.Root?.Name != ContentTypesNamespace + "Types")
        {
            problems.Add($"'{ContentTypesPart}' has no OPC Types root.");
            return;
        }

        // An Override wins over a Default for the same part, so both are read and the EFFECTIVE
        // type is what is checked — and a conflicting pair is a defect rather than a tie to break.
        var extension = metadata[(metadata.LastIndexOf('.') + 1)..];

        var defaults = document.Root
            .Elements(ContentTypesNamespace + "Default")
            .Where(element => string.Equals(
                (string?)element.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase))
            .Select(element => (string?)element.Attribute("ContentType") ?? string.Empty)
            .ToList();

        var overrides = document.Root
            .Elements(ContentTypesNamespace + "Override")
            .Where(element => Resolves((string?)element.Attribute("PartName") ?? string.Empty, metadata))
            .Select(element => (string?)element.Attribute("ContentType") ?? string.Empty)
            .ToList();

        if (defaults.Distinct(StringComparer.Ordinal).Count() > 1 || overrides.Count > 1)
        {
            problems.Add($"'{ContentTypesPart}' declares conflicting content types for '{metadata}'.");
            return;
        }

        var effective = overrides.Count == 1 ? overrides[0] : defaults.FirstOrDefault();
        if (!string.Equals(effective, CorePropertiesContentType, StringComparison.Ordinal))
        {
            problems.Add(
                $"'{metadata}' has effective content type '{effective ?? "(none)"}', "
                + $"not '{CorePropertiesContentType}'.");
        }
    }

    private static void ValidateCoreProperties(
        OpcArchive archive, string metadata, string packageId, string packageVersion, List<string> problems)
    {
        if (Parse(archive, metadata, problems) is not { } document)
        {
            return;
        }

        if (document.Root?.Name != CorePropertiesNamespace + "coreProperties")
        {
            problems.Add($"'{metadata}' has no OPC coreProperties root.");
            return;
        }

        Same("identifier", (string?)document.Root.Element(DublinCore + "identifier"), packageId);
        Same("version", (string?)document.Root.Element(CorePropertiesNamespace + "version"), packageVersion);

        void Same(string what, string? actual, string expected)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                problems.Add($"the core properties declare {what} '{actual}', not '{expected}'.");
            }
        }
    }

    // DTD processing is prohibited and no resolver is supplied, so neither a document type nor an
    // external entity can be fetched or expanded while reading a package's own metadata.
    private static XDocument? Parse(OpcArchive archive, string name, List<string> problems)
    {
        var bytes = archive.Read(name);
        if (bytes is null)
        {
            problems.Add($"the package has no readable '{name}' within {OpcArchive.MaxPartBytes} bytes.");
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(
                stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            return XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            problems.Add($"'{name}' is not well-formed XML: {exception.Message}");
            return null;
        }
    }
}
