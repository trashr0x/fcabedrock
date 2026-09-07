using System.Runtime.CompilerServices;
using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Tests;

/// <summary>
/// Redirects the suite's bulk-artifact root to a per-run temporary directory before any test can
/// touch it.
/// <para>
/// A module initializer, not a fixture, because <see cref="BenchmarkPaths.Root"/> resolves lazily
/// and then stays resolved for the life of the process: the redirect has to be in place before the
/// first access, and a module initializer runs before any test does. Without it these tests would
/// read and write the developer's real corpus directory — deleting prepared corpora, and letting a
/// stale one decide a test's outcome.
/// </para>
/// </summary>
internal static class BenchmarkTestRoot
{
    private static string _root = string.Empty;

    /// <summary>The temporary bulk-artifact root these tests use.</summary>
    public static string Path => _root;

    [ModuleInitializer]
    internal static void Redirect()
    {
        _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "fcabedrock-benchmarks-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(BenchmarkPaths.RootVariable, _root);
    }
}

/// <summary>A temporary directory that removes itself, for tests that need their own file tree.</summary>
internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    /// <summary>The directory's absolute path.</summary>
    public string Path { get; }

    /// <summary>Creates a fresh temporary directory.</summary>
    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "fcabedrock-benchmarks-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    /// <summary>Combines a file name onto this directory.</summary>
    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not a test failure.
        }
    }
}
