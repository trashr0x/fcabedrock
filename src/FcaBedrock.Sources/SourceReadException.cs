namespace FcaBedrock.Sources;

/// <summary>
/// An <b>expected</b> failure to read a source, normalized by the adapter into one
/// source-neutral type (M5-IP-008). It is the unbound seam's failure channel: the seam
/// hands back records, so a provider-specific read failure has nowhere else to surface,
/// and a consumer must be able to recognize one without knowing which adapter produced it
/// — Discovery must never catch a Sep type to learn that a CSV row was unreadable.
/// <para>
/// <b>Scope.</b> Adapter/provider read failures only, with the provider's own exception
/// retained as <see cref="Exception.InnerException"/>. This type is deliberately <em>not</em>
/// a catch-all: <see cref="OperationCanceledException"/> always propagates unwrapped and is
/// never a read failure (D-111/D-112), and programmer errors — <see cref="ArgumentException"/>,
/// <see cref="InvalidOperationException"/>, <see cref="NullReferenceException"/>, violated
/// invariants — propagate as themselves rather than being disguised as an infrastructure
/// problem (P-14).
/// </para>
/// </summary>
public sealed class SourceReadException : Exception
{
    /// <summary>Creates a read failure with no further detail.</summary>
    public SourceReadException()
    {
    }

    /// <summary>Creates a read failure described by <paramref name="message"/>.</summary>
    public SourceReadException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a read failure described by <paramref name="message"/>, retaining the
    /// provider's own <paramref name="innerException"/> as the cause.
    /// </summary>
    public SourceReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
