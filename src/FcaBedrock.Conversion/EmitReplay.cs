using System.Collections;
using System.Runtime.CompilerServices;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion;

/// <summary>
/// The sink-aware replay entrypoint for the <c>.cxt</c> two-pass write (§18.1). The Burmeister writer
/// enumerates the emitted-object stream twice — once for names, once for the incidence matrix —
/// re-running the emit each pass and never buffering the matrix (P-16). <see cref="Begin"/> brackets one
/// conversion attempt in an <see cref="EmitReplaySession"/> that collects data diagnostics once (first
/// pass) and, crucially, <b>aggregates grouping storage failures across passes</b>, flushing one final
/// per identity at disposal — so a storage failure that can only occur in pass 2 (external spool
/// activity) is never lost, which the old first-pass-only helper could not guarantee.
/// </summary>
public static class EmitReplay
{
    /// <summary>
    /// Begins a replay session over <paramref name="emit"/> (a factory that emits objects while routing
    /// diagnostics to the sink it is handed) whose passes route to <paramref name="diagnostics"/>. Each
    /// <see cref="EmitReplaySession.Open"/> is one pass; the session <b>must be disposed</b> before
    /// <paramref name="diagnostics"/> is inspected, because the final aggregated storage diagnostics are
    /// appended at disposal.
    /// </summary>
    public static EmitReplaySession Begin(
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(emit);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new EmitReplaySession(emit, diagnostics);
    }
}

/// <summary>
/// One conversion attempt's replay session (D-082). Each <see cref="Open"/> replays the emit as a fresh
/// pass; data diagnostics reach the collector on the first pass only (later passes discard them, so the
/// data set is not double-counted), while <see cref="DiagnosticCode.GroupingStorageFailed"/> diagnostics
/// are intercepted on every pass and aggregated by stable identity across passes. <see cref="Dispose"/>
/// is the flush point: it appends one final storage diagnostic per identity, at the worst severity seen,
/// in first-occurrence order.
/// <para>
/// <b>Disposal-before-inspection is part of the contract.</b> Inspect <c>diagnostics</c> only after
/// disposal; the canonical usage is an explicit <c>using</c> <i>block</i> (not <c>using var</c>, which
/// would defer disposal to the end of the enclosing scope and let a caller read the collector before the
/// final aggregates land):
/// <code>
/// using (var session = EmitReplay.Begin(sink =&gt; Emitter.EmitAsync(plan, source, sink, ct), diagnostics))
/// {
///     await CxtWriter.WriteAsync(plan, session.Open, options, output, ct);
/// } // ONLY here are the final storage diagnostics authoritative
/// </code>
/// Passes are <b>sequential</b>: <see cref="Open"/> while a pass is active, or <see cref="Dispose"/>
/// while a pass is active, throws <see cref="InvalidOperationException"/>; <see cref="Open"/> after
/// disposal throws <see cref="ObjectDisposedException"/>. <see cref="Dispose"/> is idempotent (the first
/// call flushes once; later calls are no-ops). The session brackets one attempt <i>by contract</i>: it
/// observes stream openings, not <c>WriteAsync</c> attempts, so reusing a session for a retry is
/// documented-unsupported and not always detectable (a divergent retry that changes the object-name
/// sequence is caught by the writer's name-sequence invariant, but an identical-name replay may remain
/// indistinguishable). All state lives on the instance, so concurrent sessions are independent.
/// </para>
/// </summary>
public sealed class EmitReplaySession : IDisposable
{
    private readonly Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> _emit;
    // Lifecycle state driven by CompareExchange so enumeration-start and disposal are mutually-exclusive
    // atomic transitions — no start-vs-dispose race on the non-thread-safe storage ledger.
    private const int Idle = 0;
    private const int Active = 1;
    private const int Disposed = 2;

    private readonly ICollection<BedrockDiagnostic> _diagnostics;
    private readonly StorageFailureLedger _storage = new();
    private int _dataSinkClaimed;
    private int _state; // Idle / Active / Disposed

    internal EmitReplaySession(
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        _emit = emit;
        _diagnostics = diagnostics;
    }

    /// <summary>Opens one replay pass. Sequential only; throws if a pass is active or the session is disposed.</summary>
    public IAsyncEnumerable<EmittedObject> Open()
    {
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state == Disposed, this);
        if (state == Active)
        {
            throw new InvalidOperationException("A replay pass is already active; passes are sequential.");
        }

        return Pass(); // the real claim is the atomic transition in Pass (first MoveNext)
    }

    /// <summary>Flushes the cross-pass storage aggregates (idempotent). Throws if a pass is still active.</summary>
    public void Dispose()
    {
        var prev = Interlocked.CompareExchange(ref _state, Disposed, Idle);
        if (prev == Disposed)
        {
            return; // idempotent
        }

        if (prev == Active)
        {
            throw new InvalidOperationException("Cannot dispose the replay session while a pass is active.");
        }

        // prev == Idle → we transitioned Idle → Disposed; flush the cross-pass aggregates exactly once.
        foreach (var (failure, severity) in _storage.Aggregates())
        {
            _diagnostics.Add(GroupingStorageDiagnostics.Render(failure, severity));
        }
    }

    private async IAsyncEnumerable<EmittedObject> Pass([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Atomically claim enumeration (Idle → Active) at first MoveNext. A concurrent start or a
        // disposal loses the race — so two deferred streams can never advance at once, and a start
        // racing a dispose resolves deterministically.
        var prev = Interlocked.CompareExchange(ref _state, Active, Idle);
        ObjectDisposedException.ThrowIf(prev == Disposed, this);
        if (prev == Active)
        {
            throw new InvalidOperationException("A replay pass is already active; passes are sequential.");
        }

        // Deferred to first MoveNext: exactly one pass claims the real data sink; the rest discard data.
        // Storage diagnostics are intercepted on every pass regardless of the claim.
        var claimsData = Interlocked.Exchange(ref _dataSinkClaimed, 1) == 0;
        var collector = new SessionCollector(this, claimsData ? _diagnostics : DiscardSink.Instance);
        try
        {
            await foreach (var obj in _emit(collector).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return obj;
            }
        }
        finally
        {
            // Release Active → Idle (Dispose cannot have run: it rejects Active).
            Interlocked.CompareExchange(ref _state, Idle, Active);
        }
    }

    // Routes one diagnostic emitted during a pass: a GroupingStorageFailed (with its structured payload)
    // is captured into the cross-pass ledger and NOT appended now; anything else goes to the data sink.
    private void Route(BedrockDiagnostic diagnostic, ICollection<BedrockDiagnostic> dataSink)
    {
        if (diagnostic is { Code: DiagnosticCode.GroupingStorageFailed, Context: GroupingStorageFailure failure })
        {
            _storage.Merge(failure, diagnostic.Severity);
            return;
        }

        dataSink.Add(diagnostic);
    }

    // A per-pass collector: Add routes to the session; the rest are inert (the emitter only ever Adds).
    private sealed class SessionCollector : ICollection<BedrockDiagnostic>
    {
        private readonly EmitReplaySession _session;
        private readonly ICollection<BedrockDiagnostic> _dataSink;

        public SessionCollector(EmitReplaySession session, ICollection<BedrockDiagnostic> dataSink)
        {
            _session = session;
            _dataSink = dataSink;
        }

        public int Count => 0;
        public bool IsReadOnly => false;
        public void Add(BedrockDiagnostic item) => _session.Route(item, _dataSink);
        public void Clear() { }
        public bool Contains(BedrockDiagnostic item) => false;
        public void CopyTo(BedrockDiagnostic[] array, int arrayIndex) { }
        public bool Remove(BedrockDiagnostic item) => false;
        public IEnumerator<BedrockDiagnostic> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // A write-only null-object collector: later-pass data diagnostics add to it and it keeps nothing.
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
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
