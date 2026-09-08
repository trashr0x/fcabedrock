using System.Xml.Linq;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// What the tool package actually contains (D-122 part 13 / D-123 part 1). These run on every
/// build: the installed-tool smoke proves the package works, and these prove it is the package
/// the smoke would install — the exact assembly set, the tool settings the host reads, the
/// identity the feed publishes, the readme the package page renders, and one version.
/// <para>
/// Nothing here pins whole-package bytes, the leaf NuGet gives the core-properties part, the
/// relationship part, or entry order: those differ between two packs of identical sources, so
/// pinning them would assert build noise rather than package content. That part's canonical name
/// SHAPE is still required, because a name is not noise — it is what an extractor acts on, and the
/// shape is a <b>closed two-producer set</b> rather than a wildcard (<see cref="PackageOpc"/>).
/// </para>
/// <para>
/// The OPC wiring around it is checked too, through the same bounded validator the real package
/// goes through: a metadata part nothing points at, or one the content-type map calls something
/// else, is not a discoverable core-properties part however canonical its name looks.
/// </para>
/// </summary>
[Collection(ToolPackageCollection.Name)]
public sealed class ToolPackTests(ToolPackage package)
{
    private const string ToolDirectory = "tools/net10.0/any/";

    private const string CoreProperties = "package/services/metadata/core-properties/";

    private const string PsmdcpExtension = ".psmdcp";

    // A syntactically canonical stem, used ONLY to exercise the core-properties rule in both
    // directions below. The package's own producer-chosen value is never compared to it.
    private const string CanonicalStemSample = "0123456789abcdef0123456789abcdef";

    // The eight production assemblies. Each ships its assembly, its symbols and its documentation,
    // and all three are NAMED here rather than tolerated — a stray build or test payload has
    // nowhere to hide behind "it is not a DLL".
    private static readonly string[] ProductionAssemblies =
    [
        "FcaBedrock.Cli",
        "FcaBedrock.Conversion",
        "FcaBedrock.Core",
        "FcaBedrock.Diagnostics",
        "FcaBedrock.Discovery",
        "FcaBedrock.Export",
        "FcaBedrock.Sources",
        "FcaBedrock.Spec",
    ];

    // Sep, Tomlyn, and Sep's transitive float parser — assemblies only.
    private static readonly string[] DependencyAssemblies = ["Sep.dll", "Tomlyn.dll", "csFastFloat.dll"];

    // The eleven-DLL contract as an EXACT set, so both a dropped assembly and an unannounced new
    // dependency fail.
    private static readonly string[] ExpectedAssemblies =
        [.. ProductionAssemblies.Select(assembly => assembly + ".dll").Concat(DependencyAssemblies)];

    [Fact]
    public void Package_WhenPacked_ThenItShipsExactlyTheExpectedAssemblies()
    {
        using var archive = package.OpenArchive();
        var entries = archive.Entries.Select(entry => entry.FullName).ToList();

        // Every name is validated BEFORE it is trusted as a key or reduced to a file name, and the
        // package infrastructure is validated with the rest: a boundary that inspects only the
        // DLLs is not a boundary.
        foreach (var entry in entries)
        {
            Check(IsSafeEntryName(entry), $"the package holds an unsafe entry name '{Printable(entry)}'.");
        }

        // A ZIP may repeat a name and package paths alias case-insensitively, so a second entry for
        // a name is a defect — never something to silently take the first or the last of.
        var aliased = entries
            .GroupBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Check(aliased.Count == 0, $"the package repeats or aliases entry names: {string.Join(", ", aliased)}.");

        // The three parts two packs of identical sources disagree on are accounted for by SHAPE and
        // never by content: no byte of the nupkg, of the producer-named core-properties part, or of
        // the relationship part is pinned anywhere. The partition stays deliberately LOOSE — anything
        // under the core-properties directory is classified here — so that a rogue metadata part is
        // caught by the exact rule below with a message that names it, instead of slipping into the
        // payload comparison as an anonymous "unexpected" entry.
        var infrastructure = entries.Where(IsInfrastructure).ToList();
        var coreProperties = infrastructure
            .Where(name => name.StartsWith(CoreProperties, StringComparison.Ordinal))
            .ToList();

        Check(
            infrastructure.Count == 3
            && coreProperties.Count == 1
            && infrastructure.Contains("_rels/.rels", StringComparer.Ordinal)
            && infrastructure.Contains("[Content_Types].xml", StringComparer.Ordinal),
            "the package infrastructure is not exactly one core-properties part, '_rels/.rels' and "
            + $"'[Content_Types].xml': {string.Join(", ", infrastructure)}.");

        // The sole producer-named entry must be one of NuGet's two canonical core-properties
        // leaves. Its VALUE is not pinned; its shape is. Accepting any '*.psmdcp' leaf would admit
        // an arbitrary metadata part — 'CON.psmdcp' among them, which Windows resolves to a console
        // device rather than a file when the package is extracted.
        Check(
            IsCorePropertiesPart(coreProperties[0]),
            $"the core-properties part '{Printable(coreProperties[0])}' is not "
            + $"'{CoreProperties}<32 lowercase hexadecimal digits|{PackageOpc.DeterministicStem}>"
            + $"{PsmdcpExtension}'.");

        // Everything else is the COMPLETE allowed surface, compared exactly.
        var shipped = entries.Where(name => !IsInfrastructure(name)).Order(StringComparer.Ordinal).ToList();
        var expected = ExpectedSurface().Order(StringComparer.Ordinal).ToList();

        Check(
            expected.SequenceEqual(shipped, StringComparer.Ordinal),
            $"the package does not ship exactly the expected {expected.Count} entries. "
            + $"missing: {string.Join(", ", expected.Except(shipped, StringComparer.Ordinal))}; "
            + $"unexpected: {string.Join(", ", shipped.Except(expected, StringComparer.Ordinal))}.");

        // The eleven-DLL contract, spelled separately so its failure names the assemblies.
        var assemblies = shipped
            .Where(name => name.StartsWith(ToolDirectory, StringComparison.Ordinal))
            .Select(name => name[ToolDirectory.Length..])
            .Where(name => name.EndsWith(".dll", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        var expectedAssemblies = ExpectedAssemblies.Order(StringComparer.Ordinal).ToList();

        Check(
            expectedAssemblies.SequenceEqual(assemblies, StringComparer.Ordinal),
            $"the package does not ship exactly the expected {expectedAssemblies.Count} assemblies. "
            + $"expected: {string.Join(", ", expectedAssemblies)}; shipped: {string.Join(", ", assemblies)}.");

        // The whole archive reconciles: the exact fixed surface, the two fixed infrastructure
        // parts, and exactly ONE dynamically named part. Stated as a total so that a future entry
        // cannot arrive unaccounted for by widening one of the sets above.
        Check(
            entries.Count == expected.Count + 3,
            $"the package holds {entries.Count} entries, not {expected.Count + 3} "
            + $"({expected.Count} fixed, '_rels/.rels', '[Content_Types].xml' and one core-properties part).");

        // The rule the fact just applied is asserted in BOTH directions, so a later loosening back
        // to a bare '*.psmdcp' pattern cannot pass unnoticed. None of these is compared against the
        // package's own name; they exercise the predicate, and the canonical stem is a sample.
        //
        // The accepted set is exactly two, because exactly two producers write it: the GUID-N leaf
        // NuGet emitted through SDK 10.0.302, and the hard-coded 'nuget' leaf it emits from
        // SDK 10.0.400 onwards (NuGet.Client change 5834c6b9).
        foreach (var accepted in (string[])
                 [
                     CoreProperties + CanonicalStemSample + PsmdcpExtension,
                     CoreProperties + PackageOpc.DeterministicStem + PsmdcpExtension,
                 ])
        {
            Check(
                IsCorePropertiesPart(accepted),
                $"the core-properties rule rejects the canonical part '{Printable(accepted)}'.");
        }

        foreach (var alias in (string[])
                 [
                     CoreProperties + "evil" + PsmdcpExtension,
                     CoreProperties + "CON" + PsmdcpExtension,
                     CoreProperties + "con" + PsmdcpExtension,
                     CoreProperties + CanonicalStemSample.ToUpperInvariant() + PsmdcpExtension,
                     CoreProperties + "01234567-89ab-cdef-0123-456789abcdef" + PsmdcpExtension,
                     CoreProperties + CanonicalStemSample + ".PSMDCP",
                     CoreProperties + CanonicalStemSample + PsmdcpExtension + ".bak",
                     CoreProperties + "x" + CanonicalStemSample + PsmdcpExtension,
                     CoreProperties + CanonicalStemSample + "0" + PsmdcpExtension,
                     CoreProperties + "nested/" + CanonicalStemSample + PsmdcpExtension,
                     "package\\services\\metadata\\core-properties\\" + CanonicalStemSample + PsmdcpExtension,

                     // The deterministic stem is an ORDINAL literal, so an altered case, any
                     // padding, and any nesting are all somebody else's part.
                     CoreProperties + "NuGet" + PsmdcpExtension,
                     CoreProperties + "NUGET" + PsmdcpExtension,
                     CoreProperties + "nuget0" + PsmdcpExtension,
                     CoreProperties + "0nuget" + PsmdcpExtension,
                     CoreProperties + " nuget" + PsmdcpExtension,
                     CoreProperties + "nuget " + PsmdcpExtension,
                     CoreProperties + "nuget" + PsmdcpExtension + PsmdcpExtension,
                     CoreProperties + "nested/" + PackageOpc.DeterministicStem + PsmdcpExtension,
                     CoreProperties + PsmdcpExtension,
                 ])
        {
            Check(!IsCorePropertiesPart(alias), $"the core-properties rule accepts '{Printable(alias)}'.");
        }

        // And the device-name refusal is a property of EVERY entry name, not of the metadata part
        // alone, so it is asserted on the payload shape too.
        foreach (var device in (string[])
                 [ToolDirectory + "CON.dll", ToolDirectory + "prn.xml", ToolDirectory + "LPT1", "NUL.md"])
        {
            Check(!IsSafeEntryName(device), $"the entry-name rule accepts the device name '{device}'.");
        }
    }

    [Fact]
    public void Package_WhenPacked_ThenItShipsTheToolSettingsAndRuntimeFiles()
    {
        var names = package.EntryNames();
        foreach (var required in (string[])
                 ["DotnetToolSettings.xml", "FcaBedrock.Cli.deps.json", "FcaBedrock.Cli.runtimeconfig.json"])
        {
            Check(
                names.Contains(ToolDirectory + required, StringComparer.Ordinal),
                $"the package has no '{ToolDirectory + required}'.");
        }

        // The settings file is what `dotnet tool install` reads to create the shim, so it is
        // parsed rather than pattern-matched: a well-formed document declaring exactly one command.
        var settings = XDocument.Parse(package.ReadEntryText(ToolDirectory + "DotnetToolSettings.xml"));
        var commands = settings.Descendants("Command").ToList();

        Check(commands.Count == 1, $"the tool settings declare {commands.Count} commands, not one.");
        Same("fcabedrock", (string?)commands[0].Attribute("Name"), "the tool command name");
        Same("FcaBedrock.Cli.dll", (string?)commands[0].Attribute("EntryPoint"), "the tool entry point");
        Same("dotnet", (string?)commands[0].Attribute("Runner"), "the tool runner");
    }

    [Fact]
    public void Package_WhenPacked_ThenTheNuspecDeclaresTheToolIdentity()
    {
        var metadata = Metadata();

        Same("FcaBedrock.Cli", Value(metadata, "id"), "the package id");
        Same(package.Version, Value(metadata, "version"), "the nuspec version");
        Same("Constantinos Orphanides", Value(metadata, "authors"), "the package authors");
        Same(
            "Formal Concept Analysis preprocessing - the fcabedrock command.",
            Value(metadata, "description"),
            "the package description");

        var types = Element(metadata, "packageTypes").Elements()
            .Where(element => element.Name.LocalName == "packageType")
            .ToList();

        Check(types.Count == 1, $"the nuspec declares {types.Count} package types, not one.");
        Same("DotnetTool", (string?)types[0].Attribute("name"), "the package type");

        var license = Element(metadata, "license");
        Same("expression", (string?)license.Attribute("type"), "the license kind");
        Same("MIT", license.Value, "the license expression");

        // The commit is the checkout's ACTUAL HEAD, captured beside the pack. Both values are
        // printed, so a stale or all-zero SHA is reported as the regression it is.
        var repository = Element(metadata, "repository");
        Same("git", (string?)repository.Attribute("type"), "the repository kind");

        var commit = (string?)repository.Attribute("commit") ?? string.Empty;
        Check(
            ToolPackage.IsCommit(commit),
            $"the nuspec repository commit '{commit}' is not a lowercase 40-character hexadecimal commit id.");
        Check(
            string.Equals(commit, package.HeadCommit, StringComparison.Ordinal),
            $"the packaged commit does not match the checkout: nuspec '{commit}', HEAD '{package.HeadCommit}'.");
    }

    [Fact]
    public void Package_WhenPacked_ThenTheNuspecReferencesTheShippedPackageReadme()
    {
        Same("README.md", Value(Metadata(), "readme"), "the nuspec readme element");
        Check(
            package.EntryNames().Contains("README.md", StringComparer.Ordinal),
            "the package has no root 'README.md' entry.");

        var packed = package.ReadEntryBytes("README.md");
        var authored = File.ReadAllBytes(package.SourceReadmePath);

        Check(
            packed.AsSpan().SequenceEqual(authored),
            $"the packed README ({packed.Length} bytes) is not '{package.SourceReadmePath}' ({authored.Length} bytes).");
        Check(
            package.ReadEntryText("README.md")
                .Contains("dotnet tool install --global FcaBedrock.Cli", StringComparison.Ordinal),
            "the packed README does not document the global-tool install command.");
    }

    [Fact]
    public void Package_WhenPacked_ThenTheCorePropertiesPartIsReallyDiscoverable()
    {
        // A canonical NAME is not discoverability. OPC finds the core-properties part through a
        // package relationship and the part's effective content type, so the REAL package goes
        // through the bounded validator that decides exactly that. The rule matrix in
        // `PackageOpcTests` drives the same validator, which is what makes a green result here
        // evidence about this package rather than about a helper.
        var problems = PackageOpc.Validate(
            OpcArchive.OfPackage(package.NupkgPath), "FcaBedrock.Cli", package.Version);

        Check(problems.Count == 0, $"the packed OPC metadata is inconsistent: {string.Join(" ", problems)}");
    }

    [Fact]
    public void Package_WhenPacked_ThenThePackageVersionMatchesTheToolVersion()
    {
        // Three spellings of one fact: the packed file name, the nuspec, and the string the
        // installed tool prints. They move together or this fails.
        var nuspecVersion = Value(Metadata(), "version");

        Same(package.Version, nuspecVersion, "the nuspec version against the packed file name");
        Same(ToolVersion.Prefix + nuspecVersion, ToolVersion.Current, "the tool version against the package version");
    }

    private XElement Metadata()
    {
        var nuspecs = package.EntryNames()
            .Where(name => name.EndsWith(".nuspec", StringComparison.Ordinal))
            .Where(name => !name.Contains('/', StringComparison.Ordinal))
            .ToList();

        Check(nuspecs.Count == 1, $"the package holds {nuspecs.Count} root nuspec files, not one.");
        var document = XDocument.Parse(package.ReadEntryText(nuspecs[0]));

        Check(document.Root is not null, "the nuspec has no root element.");
        return Element(document.Root!, "metadata");
    }

    private XElement Element(XElement parent, string localName)
    {
        var elements = parent.Elements().Where(element => element.Name.LocalName == localName).ToList();

        Check(elements.Count == 1, $"the nuspec holds {elements.Count} '{localName}' elements, not one.");
        return elements[0];
    }

    private string Value(XElement parent, string localName) => Element(parent, localName).Value;

    private void Same(string? expected, string? actual, string what) =>
        Check(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{what}: expected '{expected}', found '{actual}'.");

    // Every failure carries the whole package description, so a red assertion is diagnosable
    // without re-running the pack.
    private void Check(bool condition, string message) => Assert.True(condition, $"{message} {package.Describe()}");

    // The complete allowed archive surface, minus only the three infrastructure parts above.
    private static IEnumerable<string> ExpectedSurface()
    {
        yield return "FcaBedrock.Cli.nuspec";
        yield return "README.md";
        yield return ToolDirectory + "DotnetToolSettings.xml";
        yield return ToolDirectory + "FcaBedrock.Cli.deps.json";
        yield return ToolDirectory + "FcaBedrock.Cli.runtimeconfig.json";

        foreach (var assembly in ProductionAssemblies)
        {
            yield return ToolDirectory + assembly + ".dll";
            yield return ToolDirectory + assembly + ".pdb";
            yield return ToolDirectory + assembly + ".xml";
        }

        foreach (var assembly in DependencyAssemblies)
        {
            yield return ToolDirectory + assembly;
        }
    }

    // The one entry the producer names. ONE rule, shared with the bounded OPC validator, so the
    // name predicate and the consistency check can never disagree about which part this is.
    private static bool IsCorePropertiesPart(string name) => PackageOpc.IsCorePropertiesPart(name);

    private static bool IsInfrastructure(string name) =>
        name.StartsWith(CoreProperties, StringComparison.Ordinal)
        || string.Equals(name, "_rels/.rels", StringComparison.Ordinal)
        || string.Equals(name, "[Content_Types].xml", StringComparison.Ordinal);

    // Windows resolves these to devices whatever extension follows, so an entry spelled
    // 'CON.psmdcp' or 'tools/net10.0/any/PRN.dll' is an extraction alias rather than a file. The
    // comparison is case-insensitive because the device rule is.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    // A package entry name is a RELATIVE, forward-slash, canonical path. Anything else — rooted,
    // backslashed, drive- or URI-spelled, empty, a `.`/`..` or empty segment, a control character,
    // a reserved device name, or a segment Windows would alias by trimming a trailing space or
    // dot — is refused before it can be trusted as a lookup key or reduced to a file name.
    private static bool IsSafeEntryName(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] == '/' || Path.IsPathRooted(name))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (char.IsControl(character) || character is '\\' or ':')
            {
                return false;
            }
        }

        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0
                || segment is "." or ".."
                || segment[0] == ' '
                || segment[^1] is ' ' or '.')
            {
                return false;
            }

            // The device name is whatever precedes the first dot, so 'CON', 'CON.psmdcp' and
            // 'con.dll' are all refused.
            var dot = segment.IndexOf('.');

            if (ReservedDeviceNames.Contains(dot < 0 ? segment : segment[..dot], StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // A control character must never reach an assertion message verbatim.
    private static string Printable(string name) =>
        new(name.Select(character => char.IsControl(character) ? '?' : character).ToArray());
}
