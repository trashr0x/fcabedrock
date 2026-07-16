namespace FcaBedrock.Conversion.Tests;

// A decorating spool filesystem for failure injection (P-6 test seam). Each hook returns a non-null
// exception to fail that operation; otherwise it delegates to the real filesystem. A delete hook that
// throws leaves the file on disk, so live bytes accumulate (exercising the degraded-cleanup escalation).
internal sealed class FakeSpoolFileSystem : ISpoolFileSystem
{
    private readonly ISpoolFileSystem _inner;

    public FakeSpoolFileSystem(ISpoolFileSystem? inner = null) => _inner = inner ?? SpoolFileSystem.Default;

    public Func<Exception?>? OnCreateWorkspace { get; set; }
    public Func<string, Exception?>? OnCreateRun { get; set; }
    public Func<string, Exception?>? OnOpenRun { get; set; }
    public Func<string, Exception?>? OnDeleteRun { get; set; }
    public Func<string, Exception?>? OnDeleteWorkspace { get; set; }
    public Func<string, Stream, Stream>? WrapReadStream { get; set; }

    public string CreateWorkspace(string root)
    {
        if (OnCreateWorkspace?.Invoke() is { } ex)
        {
            throw ex;
        }

        return _inner.CreateWorkspace(root);
    }

    public Stream CreateRunForWrite(string path)
    {
        if (OnCreateRun?.Invoke(path) is { } ex)
        {
            throw ex;
        }

        return _inner.CreateRunForWrite(path);
    }

    public Stream OpenRunForRead(string path)
    {
        if (OnOpenRun?.Invoke(path) is { } ex)
        {
            throw ex;
        }

        var stream = _inner.OpenRunForRead(path);
        return WrapReadStream is null ? stream : WrapReadStream(path, stream);
    }

    public void DeleteRun(string path)
    {
        if (OnDeleteRun?.Invoke(path) is { } ex)
        {
            throw ex; // leaves the file on disk — live bytes accumulate
        }

        _inner.DeleteRun(path);
    }

    public void DeleteWorkspace(string path)
    {
        if (OnDeleteWorkspace?.Invoke(path) is { } ex)
        {
            throw ex;
        }

        _inner.DeleteWorkspace(path);
    }
}

// Records the backend's observable spool activity for resource-bound assertions.
internal sealed class RecordingObserver : IGroupingObserver
{
    private int _openReaders;

    public int PeakOpenReaders { get; private set; }
    public long PeakLiveBytes { get; private set; }
    public long PeakResidentBytes { get; private set; }
    public int PeakPendingDeletions { get; private set; }
    public List<(string Path, long Size, bool Initial)> Written { get; } = [];
    public List<(string Path, long Size)> Deleted { get; } = [];

    public void RunWritten(string path, long sizeBytes, bool isInitial) => Written.Add((path, sizeBytes, isInitial));

    public void RunOpenedForRead(string path)
    {
        _openReaders++;
        PeakOpenReaders = Math.Max(PeakOpenReaders, _openReaders);
    }

    public void RunClosed(string path) => _openReaders--;

    public void RunDeleted(string path, long sizeBytes) => Deleted.Add((path, sizeBytes));

    public void LiveBytes(long liveBytes) => PeakLiveBytes = Math.Max(PeakLiveBytes, liveBytes);

    public void PendingDeletions(int count) => PeakPendingDeletions = Math.Max(PeakPendingDeletions, count);

    public void BufferSpilled(long residentBytes) => PeakResidentBytes = Math.Max(PeakResidentBytes, residentBytes);
}

// Extends the spool recording with the calibration engine's own tier-1 (byte-exact accumulator
// model) and tier-2 (run-catalog) signals, so one object watches both tiers of the D-095/D-103
// resource contract.
internal sealed class RecordingCalibrationObserver : ICalibrationObserver
{
    private readonly RecordingObserver _spool = new();
    private int _openReaders;

    public int PeakOpenReaders { get; private set; }
    public long PeakLiveBytes => _spool.PeakLiveBytes;
    public int PeakPendingDeletions { get; private set; }
    public List<(string Path, long Size, bool Initial)> Written => _spool.Written;
    public List<(string Path, long Size)> Deleted => _spool.Deleted;

    // Tier 1: the accepted capacity + modeled bytes per attribute, and every aggregate report.
    public List<(string Attribute, int Capacity, long ModeledBytes)> Sized { get; } = [];
    public List<long> Aggregates { get; } = [];

    // Tier 2: the live-run catalog per attribute, and the peak across all of them.
    public Dictionary<string, int> LiveRuns { get; } = new(StringComparer.Ordinal);
    public int PeakLiveRuns { get; private set; }

    public void AccumulatorSized(string attribute, int capacity, long modeledBytes) =>
        Sized.Add((attribute, capacity, modeledBytes));

    public void AggregateResident(long modeledBytes) => Aggregates.Add(modeledBytes);

    public void RunCatalog(string attribute, int liveRuns)
    {
        LiveRuns[attribute] = liveRuns;
        PeakLiveRuns = Math.Max(PeakLiveRuns, liveRuns);
    }

    public void RunWritten(string path, long sizeBytes, bool isInitial) => _spool.RunWritten(path, sizeBytes, isInitial);

    public void RunOpenedForRead(string path)
    {
        _openReaders++;
        PeakOpenReaders = Math.Max(PeakOpenReaders, _openReaders);
        _spool.RunOpenedForRead(path);
    }

    public void RunClosed(string path)
    {
        _openReaders--;
        _spool.RunClosed(path);
    }

    public void RunDeleted(string path, long sizeBytes) => _spool.RunDeleted(path, sizeBytes);

    public void LiveBytes(long liveBytes) => _spool.LiveBytes(liveBytes);

    public void PendingDeletions(int count) => PeakPendingDeletions = Math.Max(PeakPendingDeletions, count);

    public void BufferSpilled(long residentBytes) => _spool.BufferSpilled(residentBytes);
}

// Common synthetic-exception factories mapping to the classifier's kinds.
internal static class StorageFaults
{
    public static IOException DiskFull() => new("simulated disk full") { HResult = unchecked((int)0x80070070) };

    public static UnauthorizedAccessException AccessDenied() => new("simulated access denied");
}

// A readable stream with a non-zero length whose Read faults — a device/read error after a run opens.
internal sealed class ThrowingReadStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => 100;
    public override long Position { get => 0; set { } }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("simulated read fault");
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) { }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Delegates reads to an inner stream but faults on Dispose — a close-time device error. The inner (real)
// handle is released first, so the run file can still be deleted; only the synthetic close failure is
// raised, which the merge routes to the CleanupClose cleanup channel (a Warning, never escaping disposal).
internal sealed class ThrowOnDisposeStream : Stream
{
    private readonly Stream _inner;

    public ThrowOnDisposeStream(Stream inner) => _inner = inner;

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        throw StorageFaults.AccessDenied(); // simulated close-time device error
    }
}

// A read stream whose Length faults (a device error observed while the SpoolRunReader ctor reads
// stream.Length), tracking disposal and optionally faulting on Dispose too — for the OpenRun
// construction-failure handle-cleanup paths.
internal sealed class ThrowOnLengthStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _throwOnDispose;

    public ThrowOnLengthStream(Stream inner, bool throwOnDispose)
    {
        _inner = inner;
        _throwOnDispose = throwOnDispose;
    }

    public bool Disposed { get; private set; }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => throw StorageFaults.DiskFull(); // faults the reader ctor's stream.Length read
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        if (disposing)
        {
            _inner.Dispose();
        }

        if (_throwOnDispose)
        {
            throw StorageFaults.AccessDenied();
        }
    }
}
