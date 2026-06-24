using System.Globalization;

namespace FcaBedrock.Spec;

/// <summary>
/// Parses the v2 <c>.bed</c> bracket-section format into a <see cref="BedDocument"/>
/// for one-way migration (decisions.md D-009). Structural problems throw
/// <see cref="FormatException"/>: the <c>.bed</c> is a trusted migration input, and
/// a user-facing <c>migrate</c> command (M7) will wrap such failures as diagnostics.
/// </summary>
public static class BedReader
{
    /// <summary>Parses <paramref name="text"/> (a <c>.bed</c> file's contents).</summary>
    public static BedDocument Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sections = ParseSections(text);
        var count = int.Parse(FirstNonEmpty(Section(sections, "Number of Attributes")), CultureInfo.InvariantCulture);

        var names = Take(Section(sections, "Attributes"), count);
        var categories = Take(Section(sections, "Attribute Categories"), count).Select(SplitCsv).ToList();
        var values = Take(Section(sections, "Category Values"), count).Select(SplitCsv).ToList();
        var convert = Take(Section(sections, "Convert Attribute"), count).Select(ParseBool).ToList();
        var types = Take(Section(sections, "Attribute Type"), count);
        var restrictTo = Take(Section(sections, "Restrict To Values"), count);

        return new BedDocument(count, names, categories, values, convert, types, restrictTo);
    }

    private static Dictionary<string, List<string>> ParseSections(string text)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (TryHeader(line, out var name))
            {
                current = [];
                sections[name] = current;
            }
            else
            {
                current?.Add(line);
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

    private static List<string> Section(Dictionary<string, List<string>> sections, string name) =>
        sections.TryGetValue(name, out var lines)
            ? lines
            : throw new FormatException($"Missing [{name}] section in .bed file.");

    private static List<string> Take(List<string> lines, int count)
    {
        if (lines.Count < count)
        {
            throw new FormatException($"Expected {count} entries but found {lines.Count}.");
        }

        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(lines[i]);
        }

        return result;
    }

    private static IReadOnlyList<string> SplitCsv(string line) => line.Split(',');

    private static bool ParseBool(string line) => bool.Parse(line.Trim());

    private static string FirstNonEmpty(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        throw new FormatException("Expected a value but the section was empty.");
    }
}
