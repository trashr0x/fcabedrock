using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Core.Spec;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The Adult record count, which every Adult rate is divided by, follows the reader's own blank
/// rule (spec §5.1.1): the published file's empty final line is a blank record and is not counted.
/// Checked on local bytes shaped like the published file; no corpus is fetched.
/// </summary>
public sealed class AdultCorpusTests
{
    // Two census-shaped rows, a whitespace-only line between them, and the doubled newline the
    // published file ends with.
    private const string AdultShaped =
        "39, State-gov, 77516, Bachelors\n \t\n50, Self-emp-not-inc, 83311, Bachelors\n\n";

    [Fact]
    public void CountRecords_WhenTheFileEndsWithADoubledNewline_ThenTheEmptyLastLineIsNotCounted()
    {
        using var temp = TempDirectory.Create();
        var path = temp.File("adult.data");
        File.WriteAllText(path, AdultShaped);

        Assert.Equal(2, AdultCorpus.CountRecords(path));
    }

    [Fact]
    public async Task CountRecords_WhenComparedWithTheReader_ThenBothCountTheSameRecords()
    {
        using var temp = TempDirectory.Create();
        var path = temp.File("adult.data");
        await File.WriteAllTextAsync(path, AdultShaped, TestContext.Current.CancellationToken);
        var session = new WideCsvSession(() => File.OpenRead(path), SourceReadSettings.CreateWide(hasHeader: false));

        var read = 0L;
        await foreach (var _ in session.ReadAsync(TestContext.Current.CancellationToken))
        {
            read++;
        }

        Assert.Equal(read, AdultCorpus.CountRecords(path));
    }
}
