namespace FcaBedrock.Conversion;

/// <summary>
/// How a consumer ends its enumeration of an upstream row stream, so that a result the consumer
/// has already selected is never replaced by a failure to close that stream. A selected result is
/// a halting diagnostic, a storage failure, or an exception already in flight (a read failure,
/// cancellation, or an engine bug). Without one, a close failure is the operation's own failure:
/// after the upstream reported its end, after a deliberate early stop such as a truncation, and
/// when this consumer is itself disposed while suspended at a <c>yield return</c>, where its own
/// consumer decides the precedence.
/// </summary>
internal static class SourceEnumeration
{
    /// <summary>Closes the upstream after a selected result: a close failure cannot replace it.</summary>
    public static async ValueTask CloseAfterSelectedResultAsync<T>(IAsyncEnumerator<T> upstream)
    {
        try
        {
            await upstream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The selected result is the operation's outcome; the close failure is not reported.
        }
    }

    /// <summary>Ends an iterator's upstream enumeration according to how the iterator is leaving.</summary>
    public static ValueTask EndAsync<T>(IAsyncEnumerator<T> upstream, SourceExit exit) =>
        exit is SourceExit.Running or SourceExit.Selected
            ? CloseAfterSelectedResultAsync(upstream)
            : upstream.DisposeAsync();
}

/// <summary>How an iterator that consumes an upstream row stream is leaving it.</summary>
internal enum SourceExit
{
    /// <summary>Executing its own code: leaving now means an exception is in flight.</summary>
    Running,

    /// <summary>Suspended at a <c>yield return</c>: its own consumer is disposing it.</summary>
    Suspended,

    /// <summary>The upstream reported its end.</summary>
    Completed,

    /// <summary>It stopped reading on purpose without selecting a result (a truncation).</summary>
    Stopped,

    /// <summary>It selected a result: a halting diagnostic or a storage failure.</summary>
    Selected,
}
