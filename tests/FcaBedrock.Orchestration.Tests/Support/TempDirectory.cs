using System.Text;

namespace FcaBedrock.TestSupport;

/// <summary>A disposable temporary directory for tests that need real files on disk.</summary>
internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    /// <summary>The directory's full path.</summary>
    public string Path { get; }

    /// <summary>Creates a fresh, uniquely named temporary directory.</summary>
    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "fcabedrock-cli-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    /// <summary>Writes <paramref name="content"/> as UTF-8 without a byte-order mark and returns its full path.</summary>
    public string Write(string name, string content)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>The full path <paramref name="name"/> would have, without creating anything.</summary>
    public string Resolve(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
