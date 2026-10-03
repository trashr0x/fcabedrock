namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// A counting <see cref="ISpoolFileSystem"/> decorator. It wraps the existing <b>managed</b> stream
/// boundary and changes nothing about it: no buffering is added, removed or replaced, no byte is
/// altered, no read is coalesced or split, and every failure propagates verbatim, so the
/// two-channel storage-failure model and the first-occurrence ledger are untouched.
/// <para>
/// Runs are keyed by the <b>workspace-local ordinal</b> the production workspace already assigns
/// (<c>run-NNNNNNNN.spool</c>), never by the randomly named workspace directory, so run identities
/// are stable across runs and machines and carry no path outside the workspace.
/// </para>
/// <para>
/// It makes <b>no OS-operation claim</b>. A managed read is not a device read: the underlying
/// <c>FileStream</c> owns its own refill policy, which cannot be observed from here. Every counter
/// below is a managed-boundary count, and is labelled as one.
/// </para>
/// </summary>
internal sealed class CountingSpoolFileSystem : ISpoolFileSystem
{
    private readonly ISpoolFileSystem _inner;
    private readonly Dictionary<string, SpoolRunCounters> _runs = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    public CountingSpoolFileSystem(ISpoolFileSystem? inner = null) => _inner = inner ?? SpoolFileSystem.Default;

    /// <summary>The peak number of simultaneously open readers observed at this seam.</summary>
    public int PeakOpenReaders { get; private set; }

    /// <summary>The peak number of simultaneously open writers observed at this seam.</summary>
    public int PeakOpenWriters { get; private set; }

    private int OpenReaders { get; set; }

    private int OpenWriters { get; set; }

    /// <summary>The counters for one run ordinal, created on first sight.</summary>
    public SpoolRunCounters Counters(string ordinal)
    {
        if (!_runs.TryGetValue(ordinal, out var counters))
        {
            counters = new SpoolRunCounters(ordinal);
            _runs[ordinal] = counters;
            _order.Add(ordinal);
        }

        return counters;
    }

    /// <summary>Every run's counters, in creation order.</summary>
    public IReadOnlyList<SpoolRunCounters> All => [.. _order.Select(ordinal => _runs[ordinal])];

    public string CreateWorkspace(string root) => _inner.CreateWorkspace(root);

    public Stream CreateRunForWrite(string path)
    {
        var ordinal = Ordinal(path);
        var counters = Counters(ordinal);
        var stream = _inner.CreateRunForWrite(path);
        counters.Created = true;
        OpenWriters++;
        PeakOpenWriters = Math.Max(PeakOpenWriters, OpenWriters);
        return new CountingSpoolStream(stream, counters, reading: false, () => OpenWriters--);
    }

    public Stream OpenRunForRead(string path)
    {
        var ordinal = Ordinal(path);
        var counters = Counters(ordinal);
        var stream = _inner.OpenRunForRead(path);
        OpenReaders++;
        PeakOpenReaders = Math.Max(PeakOpenReaders, OpenReaders);
        return new CountingSpoolStream(stream, counters, reading: true, () => OpenReaders--);
    }

    public void DeleteRun(string path)
    {
        var ordinal = Ordinal(path);
        var counters = Counters(ordinal);

        // Observe the size the way an independent auditor would — from the file itself, before it
        // is removed — rather than trusting the handle the product carries.
        long observed;
        try
        {
            observed = new FileInfo(path).Length;
        }
        catch (IOException)
        {
            observed = -1;
        }
        catch (UnauthorizedAccessException)
        {
            observed = -1;
        }

        _inner.DeleteRun(path);
        counters.Deleted = true;
        counters.DeletedBytes = observed;
    }

    public void DeleteWorkspace(string path) => _inner.DeleteWorkspace(path);

    private static string Ordinal(string path) => Path.GetFileName(path);
}

/// <summary>
/// One run's managed-boundary counters. <see cref="CompletedPasses"/> counts reader closes at which
/// the bytes returned during that open equalled the length captured when it opened. On the
/// forward-only reads these tests drive, a pass cancelled or faulted part way is therefore never
/// counted; it is not a general certificate that decoding or downstream enumeration succeeded.
/// </summary>
internal sealed class SpoolRunCounters(string ordinal)
{
    /// <summary>The workspace-local run ordinal, e.g. <c>run-00000003.spool</c>.</summary>
    public string Ordinal { get; } = ordinal;

    /// <summary>Whether the run file was created.</summary>
    public bool Created { get; set; }

    /// <summary>Whether a delete of this run succeeded.</summary>
    public bool Deleted { get; set; }

    /// <summary>The size observed on disk immediately before the successful delete, or -1.</summary>
    public long DeletedBytes { get; set; } = -1;

    /// <summary>Managed bytes requested to be written.</summary>
    public long WriteBytesRequested { get; set; }

    /// <summary>Managed bytes returned by reads.</summary>
    public long ReadBytesReturned { get; set; }

    /// <summary>Reader opens that returned exactly the run's length before closing.</summary>
    public int CompletedPasses { get; set; }
}

// Forwards every member verbatim and records what crossed. It buffers nothing: a Read of n bytes
// is one inner Read of n bytes, and the inner FileStream's own strategy is untouched.
//
// A completed read pass is "this open consumed exactly the file's length", measured per open. The
// reader stops at its cached length rather than reading zero bytes at EOF, so an end-of-file marker
// would never fire; a byte-for-byte accounting is both available and stricter, and it cannot report
// a completed pass for an enumeration that cancelled or faulted part way.
internal sealed class CountingSpoolStream(Stream inner, SpoolRunCounters counters, bool reading, Action onClose) : Stream
{
    private readonly long _lengthAtOpen = reading ? inner.Length : -1;
    private long _readThisOpen;
    private bool _closed;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        counters.ReadBytesReturned += read;
        _readThisOpen += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        counters.WriteBytesRequested += buffer.Length;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            if (reading && _readThisOpen == _lengthAtOpen)
            {
                counters.CompletedPasses++;
            }

            onClose();
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
