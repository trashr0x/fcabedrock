using System.Runtime.ExceptionServices;
using System.Text;
using nietras.SeparatedValues;

namespace FcaBedrock.Sources;

/// <summary>
/// The three resources one delimited read owns from the moment its stream factory returns: the
/// stream, the <see cref="StreamReader"/> that decodes it, and the Sep reader, which reads the
/// decoder through a <see cref="WholeSpanTextReader"/> (D-137). It advances the provider, keeps the
/// operation's cancellation checks and the provider-limit rule in one place, records the
/// operation's primary failure, and closes the resources in a fixed order (D-041).
/// <para>
/// <b>Closing.</b> <see cref="Close"/> disposes the Sep reader, then the decoder, then the stream,
/// each exactly once, and still attempts the later ones after an earlier one fails. A recorded
/// primary failure always wins: the cleanup failures ride on it, in attempt order, under
/// <see cref="SecondaryCleanupFailuresKey"/> in its <see cref="Exception.Data"/>. With no primary,
/// the first cleanup failure in attempt order is thrown as itself, carrying the later ones. When
/// <c>Sep.From</c> throws it has already disposed the state it built, so only the decoder and the
/// stream remain to be closed.
/// </para>
/// </summary>
internal sealed class DelimitedSourceReader
{
    /// <summary>The <see cref="Exception.Data"/> key under which cleanup failures ride on a primary failure.</summary>
    public const string SecondaryCleanupFailuresKey = "FcaBedrock.Sources.SecondaryCleanupFailures";

    /// <summary>The read failure for the provider's row and buffer limit (spec §5.1.1).</summary>
    public const string LimitMessage =
        "The source could not be read: a record exceeded the reader's row and buffer limit of about "
        + "16 million UTF-16 code units. A quoted field that is never closed can run into this limit.";

    private readonly Stream _stream;
    private readonly CancellationToken _cancellationToken;
    private StreamReader? _decoder;
    private SepReader? _sep;
    private Exception? _primary;
    private bool _closed;

    private DelimitedSourceReader(Stream stream, CancellationToken cancellationToken)
    {
        _stream = stream;
        _cancellationToken = cancellationToken;
    }

    /// <summary>The provider, valid between a successful <see cref="Open"/> and <see cref="Close"/>.</summary>
    public SepReader Reader => _sep ?? throw new InvalidOperationException("The reader is not open.");

    /// <summary>The operation's cancellation token, which the whole-span reader and the work budgets also check.</summary>
    public CancellationToken CancellationToken => _cancellationToken;

    /// <summary>
    /// Builds the provider options before the stream factory runs, so a provider refusal can never
    /// follow acquisition. Sep is opened headerless and raw: it splits candidate records and
    /// returns each field's untouched span, and the reading rules are applied by the caller.
    /// </summary>
    public static SepReaderOptions ProviderOptions(char delimiter) =>
        Sep.New(delimiter).Reader(o => o with
        {
            HasHeader = false, Unescape = false, Trim = SepTrim.None, DisableColCountCheck = true,
        });

    /// <summary>
    /// Acquires and wires the chain. A factory exception leaves nothing owned. Any later failure,
    /// including cancellation observed after Sep's initialization read, closes what was built and
    /// rethrows the primary.
    /// </summary>
    public static DelimitedSourceReader Open(Func<Stream> openStream, char delimiter, CancellationToken cancellationToken)
    {
        var options = ProviderOptions(delimiter);
        var stream = openStream();
        ArgumentNullException.ThrowIfNull(stream, "openStream()");
        var owner = new DelimitedSourceReader(stream, cancellationToken);
        try
        {
            // The decoder is configured exactly as Sep's own From(Stream) configures it, so decoding
            // is unchanged: UTF-8, byte order mark detection, the default buffer, replacement of
            // invalid bytes. leaveOpen keeps the stream's single disposal with this owner.
            owner._decoder = new StreamReader(
                stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: true);
            owner._sep = options.From(new WholeSpanTextReader(owner._decoder, cancellationToken), leaveOpen: true);
            owner.ThrowIfCancellationRequested(); // Sep read and parsed the first candidate while it was built
            return owner;
        }
        catch (NotSupportedException ex) when (IsProviderLimit(ex))
        {
            var wrapped = new SourceReadException(LimitMessage, ex);
            owner.Fail(wrapped);
            owner.Close();
            throw wrapped;
        }
        catch (Exception ex)
        {
            owner.Fail(ex);
            owner.Close();
            throw;
        }
    }

    /// <summary>
    /// Advances the provider one candidate. Cancellation is checked before the advance and again
    /// after it, whether or not it moved, so a token cancelled during a fill is observed before the
    /// candidate is examined. The provider limit becomes a <see cref="SourceReadException"/>; nothing
    /// else is wrapped.
    /// </summary>
    public bool MoveNext()
    {
        ThrowIfCancellationRequested();
        bool moved;
        try
        {
            moved = Reader.MoveNext();
        }
        catch (NotSupportedException ex) when (IsProviderLimit(ex))
        {
            var wrapped = new SourceReadException(LimitMessage, ex);
            Fail(wrapped);
            throw wrapped;
        }
        catch (Exception ex)
        {
            Fail(ex);
            throw;
        }

        ThrowIfCancellationRequested();
        return moved;
    }

    /// <summary>Records the operation's primary failure; the first one recorded wins.</summary>
    public void Fail(Exception exception) => _primary ??= exception;

    public void ThrowIfCancellationRequested()
    {
        if (_cancellationToken.IsCancellationRequested)
        {
            var canceled = new OperationCanceledException(_cancellationToken);
            Fail(canceled);
            throw canceled;
        }
    }

    /// <summary>Closes the owned resources once, in the fixed order (see the type remarks).</summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        CloseInOrder([_sep, _decoder, _stream], _primary);
    }

    /// <summary>
    /// Disposes each resource once, in order, skipping the ones never acquired and continuing after
    /// a failure. With a <paramref name="primary"/>, every cleanup failure rides on it and nothing
    /// is thrown here; without one, the first cleanup failure is thrown as itself and the later ones
    /// ride on it. Cleanup failures are recorded, never reclassified.
    /// </summary>
    internal static void CloseInOrder(ReadOnlySpan<IDisposable?> resources, Exception? primary)
    {
        List<Exception>? failures = null;
        foreach (var resource in resources)
        {
            if (resource is null)
            {
                continue;
            }

            try
            {
                resource.Dispose();
            }
            catch (Exception ex) // cleanup only: kept as detail, never a reason to skip a later resource
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is null)
        {
            return;
        }

        if (primary is not null)
        {
            AttachSecondary(primary, failures);
            return;
        }

        var first = failures[0];
        AttachSecondary(first, failures);
        ExceptionDispatchInfo.Throw(first);
    }

    // Origin, not message text: only a NotSupportedException thrown from within Sep itself is the
    // limit. Sep reads through the caller's stream, so a stream whose Read throws
    // NotSupportedException surfaces through the very same call, and wrapping it would disguise a
    // contract error as an expected read failure. Verified against pinned Sep 0.15.0: the limit's
    // TargetSite is a Sep throw helper (assembly "Sep"), a throwing stream's is its own Read.
    private static bool IsProviderLimit(NotSupportedException ex) =>
        ex.TargetSite?.DeclaringType?.Assembly == typeof(Sep).Assembly;

    private static void AttachSecondary(Exception target, List<Exception> failures)
    {
        var secondary = failures.Where(e => !ReferenceEquals(e, target)).ToArray();
        if (secondary.Length > 0)
        {
            target.Data[SecondaryCleanupFailuresKey] = secondary;
        }
    }
}
