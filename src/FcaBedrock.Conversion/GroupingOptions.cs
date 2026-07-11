namespace FcaBedrock.Conversion;

/// <summary>
/// Immutable configuration for one grouping backend run (D-082): the in-memory budget, the merge
/// fan-in, an optional temp root, and test seams (an <see cref="ISpoolFileSystem"/> for failure
/// injection and an <see cref="IGroupingObserver"/> for resource assertions). It carries <b>no mutable
/// reporting state</b> — that is per-enumeration (<see cref="GroupingReports"/>), wired fresh by the
/// emitter — so <see cref="Default"/> is safely shared and concurrent conversions cannot
/// cross-contaminate. These are runtime knobs, never spec/fingerprint inputs: the storage strategy
/// never changes output bytes. Provisional defaults; M8 tunes them against real distributions (P-19).
/// </summary>
internal sealed class GroupingOptions
{
    /// <summary>Provisional in-memory budget before an intake spill (64 MiB); M8 tunes.</summary>
    public const long DefaultMaxBufferedBytes = 64L * 1024 * 1024;

    /// <summary>Provisional bounded merge fan-in; M8 tunes.</summary>
    public const int DefaultMaxMergeFanIn = 16;

    public GroupingOptions(
        long maxBufferedBytes = DefaultMaxBufferedBytes,
        int maxMergeFanIn = DefaultMaxMergeFanIn,
        string? tempDirectory = null,
        ISpoolFileSystem? fileSystem = null,
        IGroupingObserver? observer = null)
    {
        if (maxBufferedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBufferedBytes), maxBufferedBytes, "The buffer budget must be positive.");
        }

        if (maxMergeFanIn < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMergeFanIn), maxMergeFanIn, "The merge fan-in must be at least 2.");
        }

        MaxBufferedBytes = maxBufferedBytes;
        MaxMergeFanIn = maxMergeFanIn;
        TempDirectory = tempDirectory;
        FileSystem = fileSystem ?? SpoolFileSystem.Default;
        Observer = observer;
    }

    /// <summary>The shared default configuration (no mutable state).</summary>
    public static GroupingOptions Default { get; } = new();

    /// <summary>Resident-bytes budget before a spill is forced (P-16 bound base).</summary>
    public long MaxBufferedBytes { get; }

    /// <summary>Maximum runs merged in one pass; multi-stage above this.</summary>
    public int MaxMergeFanIn { get; }

    /// <summary>The temp root for spool workspaces; <see langword="null"/> = the OS temp path.</summary>
    public string? TempDirectory { get; }

    /// <summary>The spool filesystem (real by default; a test seam for failure injection — P-6).</summary>
    public ISpoolFileSystem FileSystem { get; }

    /// <summary>An optional observer for resource-bound assertions (a test seam; no production effect).</summary>
    public IGroupingObserver? Observer { get; }
}

/// <summary>
/// A test-only observer of the grouping backend's spool activity — run creation/open/close/delete and
/// the pre-merge-batch live-bytes accounting (D-082 resource proofs). No production behavior depends on
/// it (P-6); production runs leave it <see langword="null"/>.
/// </summary>
internal interface IGroupingObserver
{
    /// <summary>A run finished writing: <paramref name="isInitial"/> distinguishes intake spills from merge output.</summary>
    void RunWritten(string path, long sizeBytes, bool isInitial);

    /// <summary>A run was opened for reading (for peak-open-reader assertions).</summary>
    void RunOpenedForRead(string path);

    /// <summary>A run reader was closed.</summary>
    void RunClosed(string path);

    /// <summary>A run file was deleted (or a delete was attempted and reported here on success).</summary>
    void RunDeleted(string path, long sizeBytes);

    /// <summary>The total live spool bytes on disk, reported before each merge batch (peak ≤ 3T proof).</summary>
    void LiveBytes(long liveBytes);

    /// <summary>The conservative resident accounting at an intake spill (a sanity signal; the accounting is proved by the codec-component tests).</summary>
    void BufferSpilled(long residentBytes);
}
