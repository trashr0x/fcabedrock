namespace FcaBedrock.Golden.Tests;

// Locates the v2 fixtures that the csproj copies next to the test assembly, and
// enumerates the golden output files the harness compares against. Returns
// nothing (so tests skip) when fixtures have not been added yet.
internal static class FixturePaths
{
    public static string V2Root { get; } =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "v2");

    public static bool V2RootExists => Directory.Exists(V2Root);

    public static IEnumerable<string> EnumerateExpectedOutputs()
    {
        if (!V2RootExists)
        {
            yield break;
        }

        foreach (var exampleDir in Directory.EnumerateDirectories(V2Root))
        {
            var expectedDir = Path.Combine(exampleDir, "expected");
            if (!Directory.Exists(expectedDir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(expectedDir))
            {
                var ext = Path.GetExtension(file);
                if (ext is ".cxt" or ".dat")
                {
                    yield return file;
                }
            }
        }
    }
}
