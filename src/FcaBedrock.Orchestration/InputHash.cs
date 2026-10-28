using System.Security.Cryptography;

namespace FcaBedrock.Orchestration;

/// <summary>
/// The one input-stability policy (D-122 part 5 / §17): every complete data
/// pass hashes the <b>raw bytes it consumes, inline</b>, and every completed pass must agree
/// with the first.
/// <para>
/// <b>Inline, never a second read.</b> The tracker hands each source pass a wrapper around
/// the stream the injected opener returned, so the hash is a by-product of the bytes the
/// pass was already reading. The CLI never opens the data a second time merely to hash it,
/// and never reads ahead to finish one.
/// </para>
/// <para>
/// <b>What counts as a pass.</b> A pass <em>completes</em> only when the wrapper observes
/// end of stream: a zero-length result for a non-empty read request. Anything that stops
/// earlier (schema acquisition on a source larger than one buffer, a failed or cancelled
/// read, disposal mid-stream) is discarded, because its digest would cover a prefix rather
/// than the input. A single genuinely completed pass is accepted as stable; a replay that
/// agrees is accepted; a completed pass whose digest differs from the first sets
/// <see cref="HasMismatch"/>, which the commands turn into a code-less host failure
/// <b>before</b> anything is written.
/// </para>
/// <para>
/// The digest is over the exact bytes consumed: a byte-order mark, the original line
/// endings, delimiters, and non-ASCII UTF-8 all included. Nothing is decoded, normalized, or
/// canonicalized, and the spec file is never hashed here.
/// </para>
/// <para>
/// <b>The leading bytes of a pass.</b> From the same reads, each pass also keeps its first four
/// bytes, so a decoding failure can be explained by the UTF-16 or UTF-32 byte order mark the
/// failing pass began with (<see cref="CurrentPassMark"/>). Nothing is read for it beyond what the
/// pass reads anyway.
/// </para>
/// <para>
/// <b>Observed passes.</b> A tracker built with an <see cref="OperationProgress"/> also reports each
/// pass to it (D-141): an open made while a scope is current starts a pass of the scope's kind, with
/// the opened stream's length when it can seek; the wrapper publishes the consumed total after each
/// read that returned data, reports end of input, and reports the input's closure at its first
/// disposal. Without a progress none of that runs: no length is read, no pass record or scope is
/// allocated, and each read gains one null check.
/// </para>
/// </summary>
internal sealed class InputHashTracker
{
    private readonly Func<string, Stream> _open;
    private readonly string _path;
    private readonly OperationProgress? _progress;
    private string? _first;
    private bool _mismatch;
    private PassPrefix? _current;
    private ProgressScope? _scope;

    /// <summary>Tracks passes over <paramref name="path"/>, opened through <paramref name="open"/>.</summary>
    public InputHashTracker(Func<string, Stream> open, string path)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(path);
        _open = open;
        _path = path;
    }

    /// <summary>
    /// Tracks passes over <paramref name="path"/> and reports them to <paramref name="progress"/>;
    /// a null progress is exactly the unobserved tracker.
    /// </summary>
    public InputHashTracker(Func<string, Stream> open, string path, OperationProgress? progress)
        : this(open, path)
    {
        _progress = progress;
    }

    /// <summary>The progress this tracker reports to, or null when it is not observed; a run carries it into its later passes.</summary>
    internal OperationProgress? Progress => _progress;

    /// <summary>
    /// How many opens succeeded on the observed tracker; never counted without a progress. Every open
    /// is meant to be made under a scope, and an unscoped open records no pass, so tests compare this
    /// count with the passes the progress recorded: an open outside every scope becomes visible
    /// instead of silently missing from the display.
    /// </summary>
    internal int ObservedOpens { get; private set; }

    /// <summary>
    /// Begins the coordinator's bracket around the consumer that will read the next pass (D-141): the
    /// pass takes <paramref name="kind"/>, and its closure names <paramref name="postInputStep"/>, when
    /// set, as the work that runs after the input closed. <paramref name="cancellation"/> is the
    /// call's own token, which decides whether an operation-cancelled exception that reaches the scope
    /// is the call's cancellation. Unobserved, it returns the shared no-op scope.
    /// </summary>
    internal ProgressScope EnterScope(PassKind kind, WorkingStep postInputStep, CancellationToken cancellation)
    {
        if (_progress is null)
        {
            return ProgressScope.None;
        }

        var scope = new ProgressScope(this, _progress, kind, postInputStep, _progress.CurrentStep, cancellation);
        _scope = scope;
        return scope;
    }

    /// <summary>Forgets <paramref name="scope"/> as the current scope, when it still is.</summary>
    internal void ScopeExited(ProgressScope scope)
    {
        if (ReferenceEquals(_scope, scope))
        {
            _scope = null;
        }
    }

    /// <summary>True once a completed pass disagreed with the first completed pass.</summary>
    public bool HasMismatch => _mismatch;

    /// <summary>How many passes reached end of stream. Incomplete passes never count.</summary>
    public int CompletedPasses { get; private set; }

    /// <summary>The first completed pass's digest (64 lowercase hex characters) or null when none completed.</summary>
    public string? Digest => _first;

    /// <summary>
    /// The byte order mark the most recently opened pass begins with, judged only from the bytes
    /// that pass has consumed, whether or not it reached the end of the input. Passes run one at a
    /// time, so after a read failure this is the failing pass's mark. It stays available after the
    /// pass's stream is disposed, and opening a pass resets it.
    /// </summary>
    public DataByteOrderMark CurrentPassMark => _current?.Mark ?? DataByteOrderMark.None;

    /// <summary>
    /// Opens the input for one pass. The returned stream owns the underlying stream: disposing
    /// it disposes both, and disposal before end of stream discards the incomplete pass.
    /// </summary>
    public Stream OpenHashed()
    {
        // Reset before the open: a pass whose open fails, or that reads nothing, has no mark, and
        // an earlier pass's bytes never stand in for it.
        var prefix = new PassPrefix();
        _current = prefix;
        var inner = _open(_path);
        return new HashingStream(inner, this, prefix, _progress is null ? null : StartPass(inner));
    }

    // Observed only. A pass whose open throws never gets here, so it never starts.
    private ObservedPass? StartPass(Stream inner)
    {
        ObservedOpens++;
        if (_scope is not { } scope)
        {
            return null;
        }

        // Read once, before the wrapper, which reports no seek. The length is a display denominator,
        // so a stream that cannot answer leaves it unknown rather than failing the read.
        long? length = null;
        try
        {
            if (inner.CanSeek)
            {
                length = inner.Length;
            }
        }
        catch (Exception)
        {
            length = null;
        }

        var sequence = _progress!.PassStarted(scope.Kind, length);
        scope.Started(sequence);
        return new ObservedPass(_progress, sequence, scope.PostInputStep);
    }

    /// <summary>
    /// Classifies a pass's leading bytes. A four-byte UTF-32 mark is checked before the two-byte
    /// UTF-16 mark it overlaps; when only <c>FF FE</c>, or <c>FF FE 00</c>, has been consumed, the
    /// two cannot be told apart and the answer is <see cref="DataByteOrderMark.Utf16OrUtf32"/>.
    /// Bytes that begin with no such mark are <see cref="DataByteOrderMark.None"/>; no encoding is
    /// guessed from them.
    /// </summary>
    internal static DataByteOrderMark Classify(ReadOnlySpan<byte> prefix) => prefix switch
    {
        [0xFF, 0xFE, 0x00, 0x00, ..] => DataByteOrderMark.Utf32LittleEndian,
        [0x00, 0x00, 0xFE, 0xFF, ..] => DataByteOrderMark.Utf32BigEndian,
        [0xFF, 0xFE] or [0xFF, 0xFE, 0x00] => DataByteOrderMark.Utf16OrUtf32,
        [0xFF, 0xFE, ..] => DataByteOrderMark.Utf16LittleEndian,
        [0xFE, 0xFF, ..] => DataByteOrderMark.Utf16BigEndian,
        _ => DataByteOrderMark.None,
    };

    private void Complete(string digest)
    {
        CompletedPasses++;

        if (_first is null)
        {
            _first = digest;
            return;
        }

        if (!string.Equals(_first, digest, StringComparison.Ordinal))
        {
            _mismatch = true;
        }
    }

    /// <summary>
    /// A read-only pass-through that feeds every byte it hands out into one incremental
    /// SHA-256. It reports <see cref="CanSeek"/> as false and refuses to seek: a consumer that
    /// repositioned the stream would skip or repeat bytes, and the digest would then describe
    /// something other than what the pass consumed.
    /// </summary>
    private sealed class HashingStream : Stream
    {
        private readonly Stream _inner;
        private readonly InputHashTracker _owner;
        private readonly PassPrefix _prefix;
        private readonly ObservedPass? _pass;
        private IncrementalHash? _hash;
        private bool _completed;
        private bool _disposed;

        public HashingStream(Stream inner, InputHashTracker owner, PassPrefix prefix, ObservedPass? pass)
        {
            _inner = inner;
            _owner = owner;
            _prefix = prefix;
            _pass = pass;
            _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        public override bool CanRead => !_disposed && _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException("The hashing input stream does not support seeking.");

        public override long Position
        {
            get => throw new NotSupportedException("The hashing input stream does not support seeking.");
            set => throw new NotSupportedException("The hashing input stream does not support seeking.");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("The hashing input stream does not support seeking.");

        public override void SetLength(long value) =>
            throw new NotSupportedException("The hashing input stream is read-only.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("The hashing input stream is read-only.");

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            var read = _inner.Read(buffer, offset, count);
            Observe(count, buffer.AsSpan(offset, read));
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            Observe(buffer.Length, buffer[..read]);
            return read;
        }

        public override int ReadByte()
        {
            Span<byte> one = stackalloc byte[1];
            return Read(one) == 0 ? -1 : one[0];
        }

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            Observe(count, buffer.AsSpan(offset, read));
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Observe(buffer.Length, buffer.Span[..read]);
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    // The input's closure, once, before the inner stream goes.
                    _pass?.Close();

                    // An unfinished pass is simply dropped: the hash state goes away with it and
                    // the tracker never sees a digest for a prefix.
                    _hash?.Dispose();
                    _hash = null;
                    _inner.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _pass?.Close();
                _hash?.Dispose();
                _hash = null;
                await _inner.DisposeAsync().ConfigureAwait(false);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        // `requested` is what makes end of stream unambiguous: a caller asking for zero bytes
        // also gets zero back, and that is not the end of anything.
        private void Observe(int requested, ReadOnlySpan<byte> data)
        {
            _prefix.Observe(data);
            _pass?.Observe(requested, data.Length);
            if (_hash is not { } hash || _completed)
            {
                return;
            }

            if (data.Length > 0)
            {
                hash.AppendData(data);
                return;
            }

            if (requested <= 0)
            {
                return;
            }

            _completed = true;
            _owner.Complete(Convert.ToHexString(hash.GetCurrentHash()).ToLowerInvariant());
        }
    }

    /// <summary>The first four bytes one pass has consumed, kept from its own reads.</summary>
    private sealed class PassPrefix
    {
        private readonly byte[] _bytes = new byte[4];
        private int _length;

        public DataByteOrderMark Mark => Classify(_bytes.AsSpan(0, _length));

        public void Observe(ReadOnlySpan<byte> data)
        {
            var take = Math.Min(_bytes.Length - _length, data.Length);
            data[..take].CopyTo(_bytes.AsSpan(_length));
            _length += take;
        }
    }

    /// <summary>
    /// One observed pass, held by its wrapper: the running total it publishes, from the same reads that
    /// are hashed, and the facts its wrapper reports. Every report names the pass's sequence, so the
    /// progress ignores it once a newer pass is current.
    /// </summary>
    private sealed class ObservedPass(OperationProgress progress, int sequence, WorkingStep postInputStep)
    {
        private long _consumed;
        private bool _endOfInput;

        // At most one report per read call: the total after a read with data, or end of input after a
        // zero-length result for a non-empty request. A zero-length request reports nothing.
        public void Observe(int requested, int read)
        {
            if (read > 0)
            {
                _consumed += read;
                progress.Consumed(sequence, _consumed);
            }
            else if (requested > 0 && !_endOfInput)
            {
                _endOfInput = true;
                progress.EndOfInput(sequence);
            }
        }

        public void Close() => progress.Closed(sequence, postInputStep);
    }
}

/// <summary>
/// The coordinator's bracket around the consumer that reads one pass (D-141). A <em>call scope</em>
/// wraps one awaited library call; an <em>enumeration scope</em> wraps an enumeration the coordinator
/// hands out. It learns what reached it through a non-catching exception filter
/// (<see cref="Reached"/>), and its <see cref="Exit"/> decides a pending end from that and restores
/// the step that was current when it began, in one locked event.
/// <para>
/// Unobserved runs use the shared <see cref="None"/>, which records nothing and holds no state.
/// </para>
/// </summary>
internal sealed class ProgressScope
{
    /// <summary>The no-op scope of an unobserved tracker.</summary>
    internal static readonly ProgressScope None = new(null, null, 0, 0, 0, CancellationToken.None);

    private readonly InputHashTracker? _tracker;
    private readonly OperationProgress? _progress;
    private readonly WorkingStep _outerStep;
    private readonly CancellationToken _cancellation;
    private Exception? _first;
    private bool _exited;
    private int _sequence;

    internal ProgressScope(
        InputHashTracker? tracker,
        OperationProgress? progress,
        PassKind kind,
        WorkingStep postInputStep,
        WorkingStep outerStep,
        CancellationToken cancellation)
    {
        _tracker = tracker;
        _progress = progress;
        Kind = kind;
        PostInputStep = postInputStep;
        _outerStep = outerStep;
        _cancellation = cancellation;
    }

    /// <summary>The kind every pass this scope starts takes.</summary>
    internal PassKind Kind { get; }

    /// <summary>The step a closure of this scope's pass names; 0 when the work after closure has no name.</summary>
    internal WorkingStep PostInputStep { get; }

    /// <summary>The latest pass this scope started; 0 while it has started none.</summary>
    internal int Sequence => _sequence;

    /// <summary>
    /// Records the first exception that reached this scope and returns false, so the filter that calls
    /// it catches nothing: the same exception object propagates, unwrapped and with its stack, tag and
    /// token as they were. A field write only, so it cannot itself throw.
    /// </summary>
    internal bool Reached(Exception exception)
    {
        if (_progress is not null)
        {
            _first ??= exception;
        }

        return false;
    }

    /// <summary>A pass started under this scope: later reports and the exit name it.</summary>
    internal void Started(int sequence) => _sequence = sequence;

    /// <summary>
    /// Ends this scope, once: the pass it started, when that pass is still current and has no end, ends
    /// with end of input when its wrapper observed it, otherwise with the call's own cancellation, any
    /// other exception that reached the scope (failed), or nothing (closed before end of input); then
    /// the outer step is restored. Runs in a <c>finally</c>, so it takes no catch and only the
    /// progress's lock.
    /// </summary>
    internal void Exit()
    {
        if (_progress is null || _exited)
        {
            return;
        }

        _exited = true;
        _tracker!.ScopeExited(this);
        _progress.ScopeExit(_sequence, PendingReason(), _outerStep);
    }

    // The call's own cancellation is decided by the exception's token alone, by equality with the
    // call's token (D-141): no request state is read. The CLI's exit 3 also requires a request, but a
    // pass end describes what reached the scope, not the operation, so the two need not agree.
    private PassEnd PendingReason() => _first switch
    {
        null => PassEnd.ClosedBeforeEndOfInput,
        OperationCanceledException cancelled when cancelled.CancellationToken == _cancellation => PassEnd.Cancelled,
        _ => PassEnd.Failed,
    };
}

/// <summary>
/// The UTF-16 or UTF-32 byte order mark a DATA pass begins with, which explains why its bytes are
/// not UTF-8. A UTF-8 mark is not listed: it is valid data input, and the reader consumes it.
/// </summary>
internal enum DataByteOrderMark
{
    /// <summary>The consumed bytes begin with none of the marks below.</summary>
    None = 0,

    /// <summary><c>FF FE</c>, followed by bytes that rule out the UTF-32 mark.</summary>
    Utf16LittleEndian,

    /// <summary><c>FE FF</c>.</summary>
    Utf16BigEndian,

    /// <summary><c>FF FE 00 00</c>.</summary>
    Utf32LittleEndian,

    /// <summary><c>00 00 FE FF</c>.</summary>
    Utf32BigEndian,

    /// <summary>Only <c>FF FE</c> or <c>FF FE 00</c> was consumed, which begins both little-endian marks.</summary>
    Utf16OrUtf32,
}
