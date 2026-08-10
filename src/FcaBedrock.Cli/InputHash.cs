using System.Security.Cryptography;

namespace FcaBedrock.Cli;

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
/// end of stream — a zero-length result for a non-empty read request. Anything that stops
/// earlier (schema acquisition on a source larger than one buffer, a failed or cancelled
/// read, disposal mid-stream) is discarded, because its digest would cover a prefix rather
/// than the input. A single genuinely completed pass is accepted as stable; a replay that
/// agrees is accepted; a completed pass whose digest differs from the first sets
/// <see cref="HasMismatch"/>, which the commands turn into a code-less host failure
/// <b>before</b> anything is written.
/// </para>
/// <para>
/// The digest is over the exact bytes consumed — a byte-order mark, the original line
/// endings, delimiters, and non-ASCII UTF-8 all included. Nothing is decoded, normalized, or
/// canonicalized, and the spec file is never hashed here.
/// </para>
/// </summary>
internal sealed class InputHashTracker
{
    private readonly Func<string, Stream> _open;
    private readonly string _path;
    private string? _first;
    private bool _mismatch;

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

    /// <summary>The first completed pass's digest — 64 lowercase hex characters — or null when none completed.</summary>
    public string? Digest => _first;

    /// <summary>
    /// Opens the input for one pass. The returned stream owns the underlying stream: disposing
    /// it disposes both, and disposal before end of stream discards the incomplete pass.
    /// </summary>
    public Stream OpenHashed() => new HashingStream(_open(_path), this);

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
        private IncrementalHash? _hash;
        private bool _completed;
        private bool _disposed;

        public HashingStream(Stream inner, InputHashTracker owner)
        {
            _inner = inner;
            _owner = owner;
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
}
