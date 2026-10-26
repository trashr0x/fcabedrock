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
/// </summary>
internal sealed class InputHashTracker
{
    private readonly Func<string, Stream> _open;
    private readonly string _path;
    private string? _first;
    private bool _mismatch;
    private PassPrefix? _current;

    /// <summary>Tracks passes over <paramref name="path"/>, opened through <paramref name="open"/>.</summary>
    public InputHashTracker(Func<string, Stream> open, string path)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(path);
        _open = open;
        _path = path;
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
        return new HashingStream(_open(_path), this, prefix);
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
        private IncrementalHash? _hash;
        private bool _completed;
        private bool _disposed;

        public HashingStream(Stream inner, InputHashTracker owner, PassPrefix prefix)
        {
            _inner = inner;
            _owner = owner;
            _prefix = prefix;
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
