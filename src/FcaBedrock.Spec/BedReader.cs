using System.Globalization;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Spec;

/// <summary>
/// Parses the v2 <c>.bed</c> bracket-section format into a <see cref="BedDocument"/>
/// for one-way migration (decisions.md D-009/D-079). Structural problems — a
/// missing section, an entry-count shortfall, an unparseable count or convert
/// flag — are <c>BedStructureInvalid</c> (Fatal) diagnostics, aggregated where the
/// parse can continue past them (P-13); the optional file path is only a label
/// for diagnostic locations, mirroring <see cref="Toml.SpecReader"/>.
/// </summary>
public static class BedReader
{
    private static readonly string[] RequiredSections =
    [
        "Number of Attributes",
        "Attributes",
        "Attribute Categories",
        "Category Values",
        "Convert Attribute",
        "Attribute Type",
        "Restrict To Values",
    ];

    /// <summary>Parses <paramref name="text"/> (a <c>.bed</c> file's contents).</summary>
    public static Diagnosed<BedDocument> Read(string text, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var diagnostics = new List<BedrockDiagnostic>();
        var sections = ParseSections(text);

        foreach (var name in RequiredSections)
        {
            if (!sections.ContainsKey(name))
            {
                Structure(diagnostics, filePath, null, $"Missing [{name}] section in .bed file.");
            }
        }

        var count = ReadCount(diagnostics, filePath, sections);
        if (count is not { } attributeCount)
        {
            // Every other section is sized by the count; without it nothing more
            // can be checked — but the missing-section sweep above already ran.
            return Diagnosed<BedDocument>.Failed(diagnostics);
        }

        var names = Take(diagnostics, filePath, sections, "Attributes", attributeCount);
        var categories = Take(diagnostics, filePath, sections, "Attribute Categories", attributeCount);
        var values = Take(diagnostics, filePath, sections, "Category Values", attributeCount);
        var convertLines = Take(diagnostics, filePath, sections, "Convert Attribute", attributeCount);
        var types = Take(diagnostics, filePath, sections, "Attribute Type", attributeCount);
        var restrictTo = Take(diagnostics, filePath, sections, "Restrict To Values", attributeCount);

        var convert = convertLines is null ? null : ParseConvertFlags(diagnostics, filePath, sections, convertLines);
        if (names is null || categories is null || values is null || convert is null || types is null || restrictTo is null)
        {
            return Diagnosed<BedDocument>.Failed(diagnostics);
        }

        var document = new BedDocument(
            attributeCount,
            names,
            categories.Select(SplitCsv).ToList(),
            values.Select(SplitCsv).ToList(),
            convert,
            types,
            restrictTo);
        return Diagnosed<BedDocument>.Ok(document, diagnostics);
    }

    private static Dictionary<string, Section> ParseSections(string text)
    {
        var sections = new Dictionary<string, Section>(StringComparer.Ordinal);
        Section? current = null;
        var lineNumber = 0;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            lineNumber++;
            if (TryHeader(line, out var name))
            {
                current = new Section(lineNumber);
                sections[name] = current;
            }
            else
            {
                current?.Lines.Add(line);
            }
        }

        return sections;
    }

    private static bool TryHeader(string line, out string name)
    {
        var trimmed = line.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            name = trimmed[1..^1];
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static int? ReadCount(
        List<BedrockDiagnostic> diagnostics, string? filePath, Dictionary<string, Section> sections)
    {
        if (!sections.TryGetValue("Number of Attributes", out var section))
        {
            return null; // already reported by the missing-section sweep
        }

        for (var i = 0; i < section.Lines.Count; i++)
        {
            var trimmed = section.Lines[i].Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0)
            {
                return count;
            }

            Structure(diagnostics, filePath, section.HeaderLine + 1 + i,
                $"[Number of Attributes] value '{trimmed}' is not a non-negative integer.");
            return null;
        }

        Structure(diagnostics, filePath, section.HeaderLine,
            "[Number of Attributes] section has no value.");
        return null;
    }

    private static List<string>? Take(
        List<BedrockDiagnostic> diagnostics,
        string? filePath,
        Dictionary<string, Section> sections,
        string name,
        int count)
    {
        if (!sections.TryGetValue(name, out var section))
        {
            return null; // already reported by the missing-section sweep
        }

        if (section.Lines.Count < count)
        {
            Structure(diagnostics, filePath, section.HeaderLine,
                $"[{name}] declares {section.Lines.Count} entries but {count} attributes are expected.");
            return null;
        }

        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(section.Lines[i]);
        }

        return result;
    }

    private static List<bool>? ParseConvertFlags(
        List<BedrockDiagnostic> diagnostics,
        string? filePath,
        Dictionary<string, Section> sections,
        List<string> lines)
    {
        var section = sections["Convert Attribute"];
        var flags = new List<bool>(lines.Count);
        var failed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (bool.TryParse(lines[i].Trim(), out var flag))
            {
                flags.Add(flag);
            }
            else
            {
                Structure(diagnostics, filePath, section.HeaderLine + 1 + i,
                    $"[Convert Attribute] entry '{lines[i].Trim()}' is not True/False.");
                failed = true;
            }
        }

        return failed ? null : flags;
    }

    private static void Structure(
        List<BedrockDiagnostic> diagnostics, string? filePath, int? line, string message) =>
        diagnostics.Add(new BedrockDiagnostic(
            DiagnosticCode.BedStructureInvalid,
            DiagnosticSeverity.Fatal,
            message,
            new DiagnosticLocation(File: filePath, Line: line, Column: null, AttributeName: null, RecordIndex: null)));

    private static IReadOnlyList<string> SplitCsv(string line) => line.Split(',');

    private sealed record Section(int HeaderLine)
    {
        public List<string> Lines { get; } = [];
    }
}
