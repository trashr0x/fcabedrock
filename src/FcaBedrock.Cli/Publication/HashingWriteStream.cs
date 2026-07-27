using System.Security.Cryptography;

namespace FcaBedrock.Cli.Publication;

/// <summary>
/// Marks a failure as having come from the <b>output</b> side (CX-M7H-006).
/// <para>
/// An exporter writes a staged artifact while enumerating the data source, so one
/// <see cref="IOException"/> escaping that call could equally mean "the source could not be read"
/// or "the stage could not be written" — and the two get opposite messages, one naming the DATA
/// operand and one naming the output. Tagging the failure where it happens is what keeps a full
/// disk from being reported as a broken input file.
/// </para>
/// <para>It never escapes the CLI: publication converts it to a sanitized code-less host error.</para>
/// </summary>
internal sealed class PublicationStreamException : Exception
{
    /// <summary>Wraps <paramref name="inner"/>, the failure the output stream actually raised.</summary>
    public PublicationStreamException(Exception inner)
        : base("The publication output stream failed.", inner)
    {
    }
}

/// <summary>
/// Marks a failure as a <b>contract or state defect at a publication boundary</b> (CX-M7H-034/035).
/// <para>
/// An <see cref="ObjectDisposedException"/>, <see cref="ArgumentException"/>, or
/// <see cref="NotSupportedException"/> raised by an already-open publication stream — or by an
/// internal residue read — is a product bug, not an environment failure, and belongs on the
/// sanitized unexpected-fault exit. Two of those types would otherwise be indistinguishable from
/// something else: the host maps a bare <see cref="ObjectDisposedException"/> to "cannot write to
/// standard output", and preflight maps a bare <see cref="ArgumentException"/> to "the output
/// operand is not a usable path". Wrapping at the origin is what keeps both of those readings for
/// the cases they are actually about.
/// </para>
/// <para>It never escapes the CLI as itself: the host renders one fixed sanitized line.</para>
/// </summary>
internal sealed class PublicationFaultException : Exception
{
    /// <summary>Wraps <paramref name="inner"/>, the contract defect the boundary actually raised.</summary>
    public PublicationFaultException(Exception inner)
        : base("A publication boundary violated its contract.", inner)
    {
    }
}

/// <summary>
/// A write-only pass-through that feeds every byte an exporter emits into one incremental
/// SHA-256, so a staged artifact's hash is a by-product of writing it (D-122 part 5).
/// <para>
/// <b>Inline, never a second pass.</b> The stage is never seeked, re-opened, or re-read to be
/// hashed: by the time the exporter returns, the digest of exactly the bytes that reached the
/// file is already complete.
/// </para>
/// <para>
/// <b>Hash after the write succeeds.</b> Each override delegates to the inner stream <em>first</em>
/// and appends only once that call returns, so a failed or refused write contributes nothing.
/// Every write entry point is overridden and each one delegates to the inner stream's matching
/// method — never to another override — so a byte cannot be counted twice by a base-class
/// implementation quietly routing one API through another.
/// </para>
/// <para>
/// <b>A digest exists only for a completed write.</b> <see cref="Complete"/> is an explicit step
/// the transaction takes after the exporter finished and the bytes were flushed to disk;
/// disposing without it leaves <see cref="Digest"/> null, which is what keeps an abandoned or
/// failed stage from ever presenting a hash.
/// </para>
/// </summary>
internal sealed class HashingWriteStream : Stream
{
    private const string ReadOnlyMessage = "The hashing output stream does not support reading.";
    private const string SeekMessage = "The hashing output stream does not support seeking.";

    private readonly Stream _inner;
    private IncrementalHash? _hash;
    private bool _disposed;

    /// <summary>Wraps <paramref name="inner"/>; the wrapper does <b>not</b> own it.</summary>
    public HashingWriteStream(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    /// <summary>The prefixed digest of everything written, or null until <see cref="Complete"/> runs.</summary>
    public string? Digest { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => !_disposed && _inner.CanWrite;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException(SeekMessage);

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException(SeekMessage);
        set => throw new NotSupportedException(SeekMessage);
    }

    /// <summary>
    /// Finalizes the digest of everything written so far and returns it. Idempotent: a second
    /// call returns the same value rather than a digest of nothing.
    /// </summary>
    public string Complete()
    {
        if (Digest is null)
        {
            ObjectDisposedException.ThrowIf(_hash is null, this);
            Digest = ContentHash.Format(Convert.ToHexStringLower(_hash.GetCurrentHash()));
        }

        return Digest;
    }

    /// <inheritdoc/>
    public override void Flush() => Guard(_inner.Flush);

    /// <inheritdoc/>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (PublicationTransaction.IsEnvironmentFailure(exception))
        {
            throw new PublicationStreamException(exception);
        }
        catch (Exception exception) when (PublicationTransaction.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(SeekMessage);

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException(SeekMessage);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(ReadOnlyMessage);

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => throw new NotSupportedException(ReadOnlyMessage);

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Guard(() => _inner.Write(buffer, offset, count));
        Observe(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // The span cannot cross a lambda boundary, so the guard is written out here.
        try
        {
            _inner.Write(buffer);
        }
        catch (Exception exception) when (PublicationTransaction.IsEnvironmentFailure(exception))
        {
            throw new PublicationStreamException(exception);
        }
        catch (Exception exception) when (PublicationTransaction.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }

        Observe(buffer);
    }

    /// <inheritdoc/>
    public override void WriteByte(byte value)
    {
        Guard(() => _inner.WriteByte(value));
        Observe([value]);
    }

    /// <inheritdoc/>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (PublicationTransaction.IsEnvironmentFailure(exception))
        {
            throw new PublicationStreamException(exception);
        }
        catch (Exception exception) when (PublicationTransaction.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }

        Observe(buffer.Span);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                // The hash state goes away with the wrapper; the inner stream belongs to the
                // transaction, which closes it in its own order.
                _hash?.Dispose();
                _hash = null;
            }
        }

        base.Dispose(disposing);
    }

    private void Observe(ReadOnlySpan<byte> data)
    {
        if (_hash is { } hash && data.Length > 0)
        {
            hash.AppendData(data);
        }
    }

    // Every call into the destination is tagged at its origin, so a failure to WRITE the output
    // can never be mistaken downstream for a failure to READ the source.
    private static void Guard(Action write)
    {
        try
        {
            write();
        }
        catch (Exception exception) when (PublicationTransaction.IsEnvironmentFailure(exception))
        {
            throw new PublicationStreamException(exception);
        }
        catch (Exception exception) when (PublicationTransaction.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }
    }
}
