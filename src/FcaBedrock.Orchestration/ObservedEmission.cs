using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Orchestration;

/// <summary>
/// The enumeration scope (D-141): an emit enumeration the coordinator hands to a writer or to the
/// statistics pass, observed. It counts the objects it yields for the pass the enumeration reads, and
/// brackets that pass with a scope that sees what advancing and disposing the underlying enumerator
/// throws. An exception thrown in the consumer's own loop body is never passed to an enumerator, so it
/// does not reach this scope; the operation reports it through its own outcome or exception.
/// <para>
/// Hand-written rather than an async iterator, because an iterator cannot yield inside a <c>try</c>
/// that has a <c>catch</c> or a filter. Used only when the run is observed: an unobserved run hands
/// out the original enumeration.
/// </para>
/// </summary>
internal sealed class ObservedEmission : IAsyncEnumerable<EmittedObject>
{
    private readonly IAsyncEnumerable<EmittedObject> _inner;
    private readonly InputHashTracker _tracker;
    private readonly OperationProgress _progress;
    private readonly PassKind _kind;
    private readonly WorkingStep _postInputStep;
    private readonly CancellationToken _cancellation;

    private ObservedEmission(
        IAsyncEnumerable<EmittedObject> inner,
        InputHashTracker tracker,
        OperationProgress progress,
        PassKind kind,
        WorkingStep postInputStep,
        CancellationToken cancellation)
    {
        _inner = inner;
        _tracker = tracker;
        _progress = progress;
        _kind = kind;
        _postInputStep = postInputStep;
        _cancellation = cancellation;
    }

    /// <summary>
    /// <paramref name="objects"/>, observed as a pass of <paramref name="kind"/> when
    /// <paramref name="tracker"/> reports to a progress, otherwise <paramref name="objects"/> itself.
    /// The pass's post-input step is <see cref="WorkingStep.Grouping"/> when <paramref name="plan"/>
    /// groups. <paramref name="cancellation"/> is the calling operation's own token.
    /// </summary>
    internal static IAsyncEnumerable<EmittedObject> Over(
        IAsyncEnumerable<EmittedObject> objects,
        InputHashTracker tracker,
        ConversionPlan plan,
        PassKind kind,
        CancellationToken cancellation) =>
        tracker.Progress is { } progress
            ? new ObservedEmission(objects, tracker, progress, kind, Groups(plan) ? WorkingStep.Grouping : 0, cancellation)
            : objects;

    /// <summary>
    /// Whether <paramref name="plan"/>'s emission groups its input before it yields: an unordered
    /// triple source or a wide <c>dedupe</c> key, the public plan facts the emit entrypoints route by
    /// (D-082, D-083). The grouper closes its input before it sorts, merges and replays.
    /// </summary>
    internal static bool Groups(ConversionPlan plan) =>
        plan.Execution is TripleExecution { Ordering: TripleOrdering.Unordered }
        || plan.ObjectKey is ColumnObjectKey { Policy: DuplicateObjectPolicy.Dedupe };

    /// <summary>The underlying enumerator, with the consumer's token forwarded unchanged.</summary>
    public IAsyncEnumerator<EmittedObject> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new Enumerator(_inner.GetAsyncEnumerator(cancellationToken), this);

    private sealed class Enumerator(IAsyncEnumerator<EmittedObject> inner, ObservedEmission owner)
        : IAsyncEnumerator<EmittedObject>
    {
        private ProgressScope? _scope;
        private long _count;

        public EmittedObject Current => inner.Current;

        public async ValueTask<bool> MoveNextAsync()
        {
            // The scope begins at the first advance, not at GetAsyncEnumerator: an emit enumeration
            // acquires its source, and so opens its pass, inside its first advance, and a consumer
            // whose GetAsyncEnumerator failed never disposes, which would leave a scope current.
            var scope = _scope ??= owner._tracker.EnterScope(owner._kind, owner._postInputStep, owner._cancellation);

            bool moved;
            try
            {
                moved = await inner.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (scope.Reached(exception))
            {
                // Unreachable: Reached records the exception and returns false, so nothing is caught.
                throw;
            }

            if (moved)
            {
                owner._progress.Emitted(scope.Sequence, ++_count);
            }

            return moved;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                try
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (_scope is { } scope && scope.Reached(exception))
                {
                    // Unreachable, as above.
                    throw;
                }
            }
            finally
            {
                // A consumer that never advanced opened no scope, so nothing is recorded for it.
                _scope?.Exit();
            }
        }
    }
}
