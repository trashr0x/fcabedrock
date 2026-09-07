using FcaBedrock.Conversion;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The grouping options every measured conversion in this suite runs under.
/// <para>
/// It exists for one reason: <b>no path may forget the spool directory</b>. The backend's default is
/// the OS temporary directory, so a case that built its own <see cref="GroupingOptions"/> to vary a
/// budget would silently spill to a different volume from the one every other byte of the run lives
/// on — and the comparison between that case and its neighbours would then include a difference
/// nobody chose. Routing every construction through here makes that impossible to get wrong once,
/// rather than possible to get wrong per case.
/// </para>
/// <para>
/// Nothing else is changed from production. The budget and fan-in default to the shipped values, and
/// a case that varies one of them varies exactly that one.
/// </para>
/// </summary>
internal static class BenchmarkGrouping
{
    /// <summary>The production defaults, with the suite's spool directory.</summary>
    public static GroupingOptions Default { get; } = Create();

    /// <summary>
    /// Options with an optional budget, fan-in, and observer, always spooling under
    /// <see cref="BenchmarkPaths.SpoolDirectory"/>.
    /// </summary>
    /// <param name="maxBufferedBytes">The in-memory budget before an intake spill; null keeps the shipped default.</param>
    /// <param name="maxMergeFanIn">The merge fan-in; null keeps the shipped default.</param>
    /// <param name="observer">
    /// The resource observer. Production leaves it null (P-6), and so does every <em>timed</em> case:
    /// an observer recording every spill while the clock runs would be measuring itself. It is
    /// supplied only by the separate, untimed resource checks.
    /// </param>
    public static GroupingOptions Create(
        long? maxBufferedBytes = null, int? maxMergeFanIn = null, IGroupingObserver? observer = null)
    {
        BenchmarkPaths.EnsureDirectories();

        return new GroupingOptions(
            maxBufferedBytes ?? GroupingOptions.DefaultMaxBufferedBytes,
            maxMergeFanIn ?? GroupingOptions.DefaultMaxMergeFanIn,
            tempDirectory: BenchmarkPaths.SpoolDirectory,
            fileSystem: null,
            observer: observer);
    }
}
