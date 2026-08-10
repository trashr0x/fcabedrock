using System.Text;

namespace FcaBedrock.Architecture.Tests;

// Source hygiene: authored C# must not carry raw C0 control characters (or DEL) in its bytes.
// Two had crept in — a raw NUL inside a CLI cache-key literal and a raw SOH inside a Discovery
// theory datum — where the C# escape spelling was meant. Such bytes are invisible in editors and
// review tools, so the line reads as something it is not, and ripgrep classifies the whole file as
// binary, silently degrading ordinary content search over it.
//
// This is a SPELLING rule, not a semantic one: "\0" and the raw byte compile to the same string,
// so nothing here constrains behaviour. It is deliberately narrow (P-1) — only .cs under src/ and
// tests/, and only the character predicate below. It is not a Roslyn analyzer, not a universal
// file-type or asset policy, and not a line-length, file-size, or complexity rule.
public sealed class SourceHygieneTests
{
    // Repository-root marker. The walk starts at the test assembly's base directory
    // (tests/<project>/bin/<config>/net10.0), so it is short and deterministic — and independent of
    // git, the process working directory, and any injected absolute path.
    private const string RepositorySentinel = "FcaBedrock.slnx";

    // Enough to show the shape of a failure without flooding the runner; the tail counts the rest.
    private const int MaxReportedProblems = 10;

    private static readonly string[] AuthoredAreas = ["src", "tests"];

    // The repository's established strict decoder (P-5, as SpecTextDecoding and ControlText use):
    // a file that is not valid UTF-8 is itself a failure, never silently replaced.
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [Fact]
    public void AuthoredSources_ShouldContainNoRawControlCharacters()
    {
        var authored = AuthoredCsFiles(RepositoryRoot());

        // Non-vacuity: a guard that silently scanned nothing would pass forever. The count moves
        // with the repository, so only the floor is asserted, never an exact figure.
        Assert.True(
            authored.Count > 0,
            $"no authored .cs file was found under the 'src' and 'tests' areas of the repository "
                + $"located by '{RepositorySentinel}'; the scan would have passed vacuously.");

        var undecodable = new List<string>();
        var offenders = new List<Offender>();

        foreach (var (relative, fullPath) in authored)
        {
            string text;

            try
            {
                text = StrictUtf8.GetString(File.ReadAllBytes(fullPath));
            }
            catch (DecoderFallbackException)
            {
                undecodable.Add($"{relative}: invalid UTF-8.");
                continue;
            }

            offenders.AddRange(RejectedCharacters(relative, text));
        }

        Assert.True(
            undecodable.Count == 0,
            $"authored sources must be valid UTF-8; {undecodable.Count} file(s) failed to decode:\n"
                + Describe(undecodable));

        offenders.Sort(ByPathThenPosition);

        Assert.True(
            offenders.Count == 0,
            $"authored sources must not contain raw C0 control characters other than TAB, LF and CR, "
                + $"nor DEL — write the C# escape instead. {offenders.Count} occurrence(s) across "
                + $"{authored.Count} scanned file(s):\n"
                + Describe(offenders.Select(Format).ToList()));
    }

    // Where an offending character sits, in repository terms only — never an absolute machine path.
    private readonly record struct Offender(string Path, int Line, int Column, char Character);

    // Upward walk to the sentinel. Failing here is fatal and deliberate: the alternative is a scan
    // over nothing that passes.
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositorySentinel)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"no '{RepositorySentinel}' was found while walking upward from AppContext.BaseDirectory; "
                + "the repository root is unknown and no source could be scanned.");
    }

    // A filesystem enumeration, not a git one, so it sees untracked files too — including this
    // guard before the operator curates it. Sorted by normalized relative path so the diagnostics
    // are identical on every run and every machine (P-7).
    private static List<(string Relative, string FullPath)> AuthoredCsFiles(string root)
    {
        var authored = new List<(string Relative, string FullPath)>();

        foreach (var area in AuthoredAreas)
        {
            var directory = Path.Combine(root, area);

            Assert.True(
                Directory.Exists(directory),
                $"the authored-source area '{area}' is missing from the repository root, so the "
                    + "scan would be incomplete.");

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

                if (!IsGeneratedOutput(relative))
                {
                    authored.Add((relative, file));
                }
            }
        }

        authored.Sort((left, right) => string.CompareOrdinal(left.Relative, right.Relative));
        return authored;
    }

    // Generated output is not authored source. Compared by ORDINAL DIRECTORY SEGMENT, never by
    // substring, so an authored 'object' or 'obj-model' directory is not swept up; the file name
    // itself is never an exclusion reason.
    private static bool IsGeneratedOutput(string relative)
    {
        var directories = relative.Split('/')[..^1];

        return directories.Contains("bin", StringComparer.Ordinal)
            || directories.Contains("obj", StringComparer.Ordinal);
    }

    private static IEnumerable<Offender> RejectedCharacters(string relative, string text)
    {
        var line = 1;
        var column = 1;

        foreach (var character in text)
        {
            if (character == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            // CR advances neither: under CRLF the following LF owns the line break, so counting the
            // CR would double-increment the line, and a column counts a line's own characters.
            if (character == '\r')
            {
                continue;
            }

            if (IsRejected(character))
            {
                yield return new Offender(relative, line, column, character);
            }

            column++;
        }
    }

    // Exactly the C0 controls except TAB, LF and CR, plus DEL — and nothing broader. Deliberately
    // not a `< 0x09` test, which would miss VT, FF, U+000E-U+001F and DEL while claiming to be a
    // general policy. It claims no freedom from false positives; it is this predicate and no more.
    private static bool IsRejected(char character) =>
        (character < '\u0020' && character is not ('\t' or '\n' or '\r')) || character == '\u007f';

    private static int ByPathThenPosition(Offender left, Offender right)
    {
        var byPath = string.CompareOrdinal(left.Path, right.Path);

        if (byPath != 0)
        {
            return byPath;
        }

        var byLine = left.Line.CompareTo(right.Line);
        return byLine != 0 ? byLine : left.Column.CompareTo(right.Column);
    }

    private static string Format(Offender offender) =>
        $"{offender.Path}({offender.Line},{offender.Column}): U+{(int)offender.Character:X4}";

    private static string Describe(IReadOnlyList<string> problems)
    {
        var shown = problems.Take(MaxReportedProblems).ToList();
        var described = string.Join('\n', shown);

        return problems.Count > shown.Count
            ? $"{described}\n(+{problems.Count - shown.Count} more)"
            : described;
    }
}
