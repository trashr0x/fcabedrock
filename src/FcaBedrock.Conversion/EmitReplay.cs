using System.Runtime.CompilerServices;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion;

/// <summary>
/// Replay-safe diagnostic collection for the <c>.cxt</c> two-pass write (§18.1). The Burmeister
/// writer enumerates the emitted-object stream twice — once for names, once for the incidence
/// matrix — re-running the emit each pass and never buffering the matrix (P-16). Emit diagnostics
/// accrue to the caller's collector on <b>every</b> enumeration, so a naive shared collector would
/// double-count. This helper wraps a replayable emit factory so diagnostics reach the collector on
/// the <b>first full enumeration</b> only; later replays discard them. It is shape-agnostic: it
/// wraps <see cref="Emitter.EmitAsync"/> (wide) and <see cref="Emitter.EmitTripleAsync"/> (triple)
/// identically.
///
/// <para><b>Control flow is unaffected by the discard sink.</b> The discard sink is a no-op
/// collector: it changes only whether a diagnostic is <i>recorded</i>, never the emit's control
/// flow. A structural <c>yield break</c> (a fail duplicate, an invalid object key, a non-contiguous
/// subject) is driven by the data, so both passes stop at the identical record and stay mutually
/// consistent (pass-1 name count == pass-2 row count) whether or not the pass-2 diagnostic is kept.</para>
///
/// <para><b>Lifecycle contract.</b> The first enumeration to <i>begin</i> claims the diagnostic sink
/// (atomically, at first <c>MoveNext</c> — not at factory invocation), and must be enumerated to
/// completion for the aggregated end-of-stream flush to land. Later enumerations are replay-only.
/// Cancelling or disposing the first enumeration early yields <i>partial</i> diagnostics — acceptable,
/// since a cancelled conversion is discarded. The <c>CxtWriter</c> fully enumerates pass 1 before
/// pass 2, so this holds. Kept internal and narrow; a future conversion
/// run/session API (M7) can replace it with a single-emit-then-serialize orchestration.</para>
/// </summary>
internal static class EmitReplay
{
    /// <summary>
    /// Wraps <paramref name="emit"/> — a factory that emits objects while routing diagnostics to the
    /// sink it is handed — so that across repeated enumerations diagnostics reach
    /// <paramref name="diagnostics"/> exactly once. Returns a replayable object-stream factory for
    /// the <c>.cxt</c>/<c>.dat</c> writers.
    /// </summary>
    public static Func<IAsyncEnumerable<EmittedObject>> CollectDiagnosticsOnce(
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(emit);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var claimed = 0;
        // Interlocked.Exchange over the hoisted closure field: exactly one enumeration reads 0 and
        // claims the real sink; the rest read 1 and discard. Evaluated inside Pass (first MoveNext).
        return () => Pass(emit, diagnostics, () => Interlocked.Exchange(ref claimed, 1) == 0);
    }

    private static async IAsyncEnumerable<EmittedObject> Pass(
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit,
        ICollection<BedrockDiagnostic> diagnostics,
        Func<bool> tryClaim,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Deferred: this body runs only when the returned enumerable is first advanced, so the claim
        // is tied to the first actual enumeration, not to when the factory was invoked.
        var sink = tryClaim() ? diagnostics : DiscardSink.Instance;
        await foreach (var obj in emit(sink).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return obj;
        }
    }

    /// <summary>A write-only null-object collector: replay passes add to it and it keeps nothing.</summary>
    private sealed class DiscardSink : ICollection<BedrockDiagnostic>
    {
        public static readonly DiscardSink Instance = new();

        public int Count => 0;
        public bool IsReadOnly => false;
        public void Add(BedrockDiagnostic item) { }
        public void Clear() { }
        public bool Contains(BedrockDiagnostic item) => false;
        public void CopyTo(BedrockDiagnostic[] array, int arrayIndex) { }
        public bool Remove(BedrockDiagnostic item) => false;
        public IEnumerator<BedrockDiagnostic> GetEnumerator() { yield break; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
