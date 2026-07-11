using System.Globalization;

namespace FcaBedrock.Conversion;

/// <summary>
/// Owns the per-enumeration spool directory and its run files (D-082). <b>Lazily created</b>: the
/// directory is established only when the first spill is required, so a zero-spill enumeration touches
/// no disk at all — even under an unusable temp root. Ownership is precise: a uniquely-named directory
/// is created fresh (never adopting an existing one) and only that recorded path is ever deleted.
/// In-path failures (workspace create, run write/open) throw <see cref="GroupingStorageException"/>;
/// cleanup failures (consumed-run delete, teardown) go to the non-throwing cleanup channel.
/// </summary>
internal sealed class SpoolWorkspace<TRow>
{
    private readonly GroupingOptions _options;
    private readonly IRowCodec<TRow> _codec;
    private readonly GroupingReports _reports;
    private readonly Dictionary<string, long> _liveRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _pendingDeletion = new(StringComparer.Ordinal); // consumed runs whose delete failed (path → size)
    private string? _path;
    private int _counter;

    public SpoolWorkspace(GroupingOptions options, IRowCodec<TRow> codec, GroupingReports reports)
    {
        _options = options;
        _codec = codec;
        _reports = reports;
    }

    /// <summary>Whether the workspace directory has been created (i.e. at least one spill happened).</summary>
    public bool Created => _path is not null;

    /// <summary>Total bytes of the runs currently on disk (undeleted), for the 3T degraded bound.</summary>
    public long LiveBytes { get; private set; }

    /// <summary>Writes an ordered run and records it live. Throws on write failure (in-path).</summary>
    public SpoolRunHandle WriteRun(IEnumerable<RankedRow<TRow>> entries, GroupingOperation operation)
    {
        var path = Path.Combine(EnsurePath(), string.Create(CultureInfo.InvariantCulture, $"run-{_counter++:D8}.spool"));
        try
        {
            long size;
            using (var writer = new SpoolRunWriter<TRow>(_options.FileSystem.CreateRunForWrite(path), _codec))
            {
                foreach (var entry in entries)
                {
                    writer.Write(entry);
                }

                size = writer.BytesWritten;
            }

            _liveRuns[path] = size;
            LiveBytes += size;
            _options.Observer?.RunWritten(path, size, operation == GroupingOperation.Spill);
            _options.Observer?.LiveBytes(LiveBytes); // capture the post-write peak (bounded by 3T)
            return new SpoolRunHandle(path, size);
        }
        catch (Exception ex) when (SpoolFailures.IsStorage(ex))
        {
            var kind = SpoolFailures.Classify(ex);
            _reports.RecordInPathFailure(operation, kind, path); // record at source (first-occurrence), before the throw
            throw new GroupingStorageException(operation, kind, path, $"Failed to write spool run '{path}'.", ex);
        }
    }

    /// <summary>
    /// Opens a run reader. Throws on open failure (in-path, <see cref="GroupingOperation.MergeRead"/>). If
    /// the open succeeds but reader construction faults (the ctor reads <c>stream.Length</c>), the opened
    /// stream is disposed via the cleanup channel — never leaked — with the primary MergeRead failure kept.
    /// </summary>
    public SpoolRunReader<TRow> OpenRun(SpoolRunHandle handle)
    {
        Stream? stream = null;
        try
        {
            stream = _options.FileSystem.OpenRunForRead(handle.Path);
            var reader = new SpoolRunReader<TRow>(stream, _codec, handle.Path, GroupingOperation.MergeRead);
            stream = null; // ownership transferred to the reader; the finally must not dispose it
            return reader;
        }
        catch (Exception ex) when (SpoolFailures.IsStorage(ex))
        {
            var kind = SpoolFailures.Classify(ex);
            _reports.RecordInPathFailure(GroupingOperation.MergeRead, kind, handle.Path); // primary, recorded before the finally cleanup
            throw new GroupingStorageException(GroupingOperation.MergeRead, kind, handle.Path, $"Failed to open spool run '{handle.Path}'.", ex);
        }
        finally
        {
            // Construction failed after the open (e.g. the reader ctor's stream.Length faulted): the opened
            // stream is still owned here — dispose it via the cleanup channel so it never leaks; a close
            // failure is a CleanupClose Warning recorded after the primary MergeRead, never in its place.
            if (stream is not null)
            {
                try
                {
                    stream.Dispose();
                }
                catch (Exception ex) when (SpoolFailures.IsStorage(ex))
                {
                    _reports.RecordCleanupFailure(GroupingOperation.CleanupClose, SpoolFailures.Classify(ex), handle.Path);
                }
            }
        }
    }

    /// <summary>
    /// Closes a merge reader via the non-throwing cleanup channel: a close/stream-dispose failure records a
    /// <see cref="GroupingOperation.CleanupClose"/> Warning rather than escaping disposal (D-082), so it
    /// never replaces the primary result, cancellation, or in-path failure. No-op for a null reader.
    /// </summary>
    public void CloseRun(SpoolRunReader<TRow>? reader)
    {
        if (reader is null)
        {
            return;
        }

        try
        {
            reader.Dispose();
        }
        catch (Exception ex) when (SpoolFailures.IsStorage(ex))
        {
            _reports.RecordCleanupFailure(GroupingOperation.CleanupClose, SpoolFailures.Classify(ex), reader.Path);
        }
    }

    /// <summary>
    /// Records an in-path storage failure into the per-enumeration ledger at its first-occurrence position,
    /// for the merger (whose reader-read faults and 3T escalation are detected outside this class). The
    /// caller still throws <see cref="GroupingStorageException"/> to halt.
    /// </summary>
    public void RecordInPathFailure(GroupingOperation operation, SpoolFailureKind kind, string? pathSample) =>
        _reports.RecordInPathFailure(operation, kind, pathSample);

    /// <summary>Deletes a consumed run via the non-throwing cleanup channel; a failed delete is retained
    /// for retry at the next batch boundary (a transient failure must not permanently retain the run).</summary>
    public void DeleteRun(SpoolRunHandle handle)
    {
        if (TryDelete(handle.Path, handle.SizeBytes))
        {
            _pendingDeletion.Remove(handle.Path);
        }
        else
        {
            _pendingDeletion[handle.Path] = handle.SizeBytes; // retained for RetryPendingDeletions
        }
    }

    /// <summary>Re-attempts every retained failed deletion (called before each merge batch). A now-
    /// succeeding delete drops its run; a still-failing one is counted again (every attempt aggregates).</summary>
    public void RetryPendingDeletions()
    {
        if (_pendingDeletion.Count == 0)
        {
            return;
        }

        foreach (var (path, size) in _pendingDeletion.ToArray())
        {
            if (TryDelete(path, size))
            {
                _pendingDeletion.Remove(path);
            }
        }
    }

    /// <summary>
    /// Final teardown via the cleanup channel: deletes any remaining runs then the recorded workspace
    /// directory (never the root, a pre-existing directory, or a partial path). Non-throwing.
    /// </summary>
    public void Cleanup()
    {
        foreach (var (path, size) in _liveRuns.ToArray())
        {
            TryDelete(path, size);
        }

        _pendingDeletion.Clear(); // any file still on disk is removed by the recursive workspace delete below

        if (_path is not null)
        {
            try
            {
                _options.FileSystem.DeleteWorkspace(_path);
            }
            catch (Exception ex) when (SpoolFailures.IsStorage(ex))
            {
                _reports.RecordCleanupFailure(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, _path);
            }
        }
    }

    // Attempts one run delete. Success updates live accounting; failure records a cleanup Warning
    // (every failed attempt increments the (CleanupDelete, DeleteFailed) aggregate — samples stay capped).
    private bool TryDelete(string path, long size)
    {
        try
        {
            _options.FileSystem.DeleteRun(path);
            if (_liveRuns.Remove(path))
            {
                LiveBytes -= size;
            }

            _options.Observer?.RunDeleted(path, size);
            return true;
        }
        catch (Exception ex) when (SpoolFailures.IsStorage(ex))
        {
            _reports.RecordCleanupFailure(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, path);
            return false;
        }
    }

    private string EnsurePath()
    {
        if (_path is null)
        {
            var root = _options.TempDirectory ?? Path.GetTempPath();
            try
            {
                _path = _options.FileSystem.CreateWorkspace(root);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not OperationCanceledException)
            {
                // Any failure to establish the owner-restricted workspace halts — never proceed
                // unprotected (D-082 confidentiality). Includes ACL-establishment failure.
                _reports.RecordInPathFailure(GroupingOperation.Workspace, SpoolFailureKind.WorkspaceCreation, root);
                throw new GroupingStorageException(
                    GroupingOperation.Workspace, SpoolFailureKind.WorkspaceCreation, root, $"Failed to create an owner-restricted spool workspace under '{root}'.", ex);
            }
        }

        return _path;
    }
}
