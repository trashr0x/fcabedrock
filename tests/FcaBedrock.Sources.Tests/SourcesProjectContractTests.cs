using System.Reflection;
using System.Runtime.CompilerServices;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The Sources package's friend-access contract. The only grant is to this test assembly, so the
/// reading grammar's in-work cancellation checkpoints can be tested directly; no production
/// package is granted friend access (D-098). The grant is asserted against the real project file,
/// which this test project copies to its output, and against the compiled assembly.
/// </summary>
public sealed class SourcesProjectContractTests
{
    private const string Grant = "<InternalsVisibleTo Include=\"FcaBedrock.Sources.Tests\" />";

    private static string ProjectFile() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "project", "FcaBedrock.Sources.csproj.txt"));

    [Fact]
    public void Project_ShouldGrantFriendAccessToExactlyItsOwnTestAssembly()
    {
        // Exactly one grant, once, in the project text AND in the compiled assembly, compared as a
        // set so declaration order can neither satisfy nor break it. A presence-only check would let
        // a second grant appear unnoticed.
        var project = ProjectFile();

        Assert.Equal(1, CountOccurrences(project, Grant));
        Assert.Equal(1, CountOccurrences(project, "<InternalsVisibleTo"));

        var granted = typeof(SourceReadException).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(new HashSet<string>(["FcaBedrock.Sources.Tests"], StringComparer.Ordinal), granted);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
