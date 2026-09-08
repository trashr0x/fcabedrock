using System.Globalization;
using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The OPC rules the tool package's core-properties part must satisfy, driven through the very
/// validator <see cref="ToolPackTests"/> runs the real <c>.nupkg</c> through.
/// <para>
/// <b>Why a synthetic package.</b> Each negative here has to break exactly one rule — a
/// relationship that points elsewhere, a content type that says something else, metadata that
/// disagrees with the nuspec — and a real pack cannot be asked to produce those. Building the
/// package part by part keeps every case about one rule while still exercising the production
/// validator; the positives cover <b>both</b> legitimate producers' leaves.
/// </para>
/// </summary>
public sealed class PackageOpcTests
{
    private const string CoreProperties = PackageOpc.CorePropertiesDirectory;

    private const string Psmdcp = PackageOpc.PsmdcpExtension;

    private const string Nuspec = "FcaBedrock.Cli.nuspec";

    private const string Id = "FcaBedrock.Cli";

    private const string Version = "1.0.0";

    // A syntactically canonical GUID-N stem. It is a sample of the shape, never a pinned value.
    private const string GuidStem = "0123456789abcdef0123456789abcdef";

    [Theory]

    // The leaf NuGet emitted through SDK 10.0.302, and the hard-coded one it emits from SDK
    // 10.0.400 onwards (NuGet.Client change 5834c6b9, which fixed a deterministic-pack handle
    // leak). Both are legitimate, so both must validate — and nothing else may.
    [InlineData(GuidStem)]
    [InlineData(PackageOpc.DeterministicStem)]
    public void Validate_WhenEitherProducerNamedTheMetadata_ThenNothingIsWrong(string stem) =>
        Assert.Empty(PackageOpc.Validate(Synthetic(stem), Id, Version));

    [Theory]

    // The relationship: absent, duplicated, pointing elsewhere, dangling, external, or spelled so
    // that only a decoding resolver could turn it into a match.
    [InlineData("no-core-relationship", "relationships, not one")]
    [InlineData("two-core-relationships", "relationships, not one")]
    [InlineData("no-manifest-relationship", "relationships, not one")]
    [InlineData("relationship-elsewhere", "relationship targets")]
    [InlineData("manifest-dangling", "which the package does not hold")]
    [InlineData("relationship-external", "rather than an internal target")]
    [InlineData("relationship-traversal", "relationship targets")]
    [InlineData("relationship-encoded", "relationship targets")]
    [InlineData("duplicate-relationship-id", "repeats a relationship identifier")]

    // The content type: wrong, conflicting, or overridden to something else.
    [InlineData("wrong-content-type", "effective content type")]
    [InlineData("conflicting-content-types", "conflicting content types")]
    [InlineData("override-content-type", "effective content type")]

    // The part itself: a name outside the closed set, two of them, a foreign root, metadata that
    // disagrees with the nuspec, and a document type that must never be processed.
    [InlineData("uncanonical-name", "is not")]
    [InlineData("two-metadata-parts", "core-properties parts, not one")]
    [InlineData("foreign-root", "no OPC coreProperties root")]
    [InlineData("identifier-mismatch", "declare identifier")]
    [InlineData("version-mismatch", "declare version")]
    [InlineData("doctype", "not well-formed XML")]
    public void Validate_WhenOneRuleIsBroken_ThenItIsReported(string mutation, string expected)
    {
        var problems = PackageOpc.Validate(Synthetic(PackageOpc.DeterministicStem, mutation), Id, Version);

        Assert.Contains(problems, problem => problem.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenAPartExceedsTheBound_ThenItIsRefusedUnread()
    {
        // The bound is a safety property, not an optimization: a part is found by NAME, so a
        // validator that read whatever was there would let a package dictate an unbounded
        // allocation before one rule had run. Over the bound is simply not one of these parts.
        var oversized = new byte[OpcArchive.MaxPartBytes + 1];
        var entries = new List<KeyValuePair<string, byte[]>>
        {
            new(PackageOpc.RelationshipsPart, oversized),
            new(PackageOpc.ContentTypesPart, oversized),
            new(CoreProperties + PackageOpc.DeterministicStem + Psmdcp, oversized),
        };

        var problems = PackageOpc.Validate(OpcArchive.Of(entries), Id, Version);

        Assert.Contains(
            problems,
            problem => problem.Contains(
                $"within {OpcArchive.MaxPartBytes} bytes", StringComparison.Ordinal));
    }

    // ---- the synthetic package the cases above build on -------------------------------------------

    // A minimal, VALID package wired exactly as both producers wire one, with a single named
    // mutation applied.
    private static OpcArchive Synthetic(string stem, string mutation = "")
    {
        var metadata = mutation is "uncanonical-name"
            ? CoreProperties + "evil" + Psmdcp
            : CoreProperties + stem + Psmdcp;

        var coreTarget = mutation switch
        {
            "relationship-elsewhere" => "/" + Nuspec,
            "relationship-traversal" => "/" + CoreProperties + "../" + stem + Psmdcp,
            "relationship-encoded" => "/package%2Fservices/metadata/core-properties/" + stem + Psmdcp,
            _ => "/" + metadata,
        };

        var relationships = new StringBuilder();
        if (mutation is not "no-manifest-relationship")
        {
            relationships.Append(Relationship(PackageOpc.ManifestRelationship, "/" + Nuspec, "R1"));
        }

        if (mutation is not "no-core-relationship")
        {
            relationships.Append(Relationship(
                PackageOpc.CorePropertiesRelationship,
                coreTarget,
                mutation is "duplicate-relationship-id" ? "R1" : "R2",
                mutation is "relationship-external" ? "External" : null));
        }

        if (mutation is "two-core-relationships")
        {
            relationships.Append(Relationship(PackageOpc.CorePropertiesRelationship, coreTarget, "R3"));
        }

        var types = new StringBuilder()
            .Append(Default("rels", "application/vnd.openxmlformats-package.relationships+xml"))
            .Append(Default(
                "psmdcp",
                mutation is "wrong-content-type"
                    ? "application/octet"
                    : PackageOpc.CorePropertiesContentType));

        if (mutation is "conflicting-content-types")
        {
            types.Append(Default("psmdcp", "application/octet"));
        }

        if (mutation is "override-content-type")
        {
            types.Append(
                CultureInfo.InvariantCulture,
                $"""<Override PartName="/{metadata}" ContentType="application/octet" />""");
        }

        var entries = new List<KeyValuePair<string, byte[]>>
        {
            Part(
                PackageOpc.RelationshipsPart,
                """<?xml version="1.0" encoding="utf-8"?>"""
                + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
                + relationships
                + "</Relationships>"),
            Part(
                PackageOpc.ContentTypesPart,
                """<?xml version="1.0" encoding="utf-8"?>"""
                + """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">"""
                + types
                + "</Types>"),
            Part(metadata, CorePropertiesDocument(mutation)),
        };

        // A relationship whose target is well formed and simply is not there: the manifest
        // relationship names the nuspec, and this package does not ship one.
        if (mutation is not "manifest-dangling")
        {
            entries.Add(Part(Nuspec, "<package />"));
        }

        if (mutation is "two-metadata-parts")
        {
            entries.Add(Part(CoreProperties + GuidStem + Psmdcp, CorePropertiesDocument(string.Empty)));
        }

        return OpcArchive.Of(entries);
    }

    private static string CorePropertiesDocument(string mutation)
    {
        var root = mutation is "foreign-root" ? "notCoreProperties" : "coreProperties";
        var identifier = mutation is "identifier-mismatch" ? "Someone.Else" : Id;
        var version = mutation is "version-mismatch" ? "9.9.9" : Version;

        // A document type declaration is never processed: the reader prohibits DTDs and supplies no
        // resolver, so this is refused as malformed rather than expanded.
        var doctype = mutation is "doctype"
            ? """<!DOCTYPE coreProperties [<!ENTITY x "y">]>"""
            : string.Empty;

        return """<?xml version="1.0" encoding="utf-8"?>"""
            + doctype
            + $"""<{root} xmlns:dc="http://purl.org/dc/elements/1.1/" """
            + """xmlns="http://schemas.openxmlformats.org/package/2006/metadata/core-properties">"""
            + $"<dc:identifier>{identifier}</dc:identifier>"
            + $"<version>{version}</version>"
            + $"</{root}>";
    }

    private static string Relationship(string type, string target, string id, string? mode = null) =>
        $"""<Relationship Type="{type}" Target="{target}" Id="{id}" """
        + (mode is null ? string.Empty : $"""TargetMode="{mode}" """)
        + "/>";

    private static string Default(string extension, string contentType) =>
        $"""<Default Extension="{extension}" ContentType="{contentType}" />""";

    private static KeyValuePair<string, byte[]> Part(string name, string content) =>
        new(name, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
}
