using FcaBedrock.Diagnostics;

namespace FcaBedrock.Orchestration;

/// <summary>
/// The five counted facts of a prepared run's native context (D-122 part 10). Hosts render them;
/// the density is derived from them by each host's own rule.
/// </summary>
public sealed record ContextStatistics
{
    internal ContextStatistics(
        long objects, long formalAttributes, long crosses, long crosslessObjects, long emptyAttributes)
    {
        Objects = objects;
        FormalAttributes = formalAttributes;
        Crosses = crosses;
        CrosslessObjects = crosslessObjects;
        EmptyAttributes = emptyAttributes;
    }

    /// <summary>The emitted objects.</summary>
    public long Objects { get; }

    /// <summary>The planned formal attributes.</summary>
    public long FormalAttributes { get; }

    /// <summary>The crosses over every emitted object.</summary>
    public long Crosses { get; }

    /// <summary>The emitted objects with no cross.</summary>
    public long CrosslessObjects { get; }

    /// <summary>The formal attributes no emitted object crosses.</summary>
    public long EmptyAttributes { get; }

    /// <summary>
    /// One bounded counting pass over <paramref name="run"/>'s native emit enumeration, then the
    /// input-stability check.
    /// <para>
    /// <b>Bounded by the column count, not the context.</b> Counting keeps four integers and one
    /// <c>bool</c> per planned column; no emitted object, no row's cross list, and no incidence
    /// cell is retained (EP-16). There is no second pass: exactly one bounded emit-counting pass
    /// is allowed (D-122 part 10), so <c>EmitReplay</c> is deliberately not used.
    /// </para>
    /// <para>
    /// <paramref name="diagnostics"/> is the run's complete prior list. The returned list is that
    /// list followed by the emit diagnostics, which become authoritative when the single
    /// enumeration completes, the single-pass boundary, as for <c>.dat</c> (§16.2/D-105). Completed:
    /// the counts, with a list that may hold an emit Error; the host decides what to present. Host
    /// failure: <see cref="HostErrorKind.DataUnreadable"/> or <see cref="HostErrorKind.DataChanged"/>.
    /// The result can represent a diagnostic failure, but this operation never produces one.
    /// </para>
    /// <para>
    /// When <paramref name="run"/> carries an <see cref="OperationProgress"/>, the counting pass reports
    /// to it as a <see cref="PassKind.Statistics"/> pass with its objects, and with
    /// <see cref="WorkingStep.Grouping"/> once its input closed when the plan groups (D-141).
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The progress <paramref name="run"/> carries is completed; thrown before any file opens.
    /// </exception>
    public static async Task<Result<Diagnosed<ContextStatistics>, HostFailure>> CountAsync(
        PreparedRun run, IReadOnlyList<BedrockDiagnostic> diagnostics, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(diagnostics);
        OperationProgress.ThrowIfCompleted(run.Input.Progress, nameof(run));

        var collected = new List<BedrockDiagnostic>(diagnostics);

        ContextStatistics counts;
        try
        {
            counts = await CountObjectsAsync(run, collected, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (RunPipeline.IsDataReadFailure(exception))
        {
            return MixedOutcome<ContextStatistics>.HostFailed(
                RunPipeline.DataUnreadable(
                    run.DataPath, DataReadExplanation.Message(run.DataPath, exception, run.Input.CurrentPassMark)),
                collected);
        }

        // The counting pass may follow earlier complete passes, so stability is re-checked here,
        // before any count is returned.
        if (run.Input.HasMismatch)
        {
            return MixedOutcome<ContextStatistics>.HostFailed(RunPipeline.DataChanged(run.DataPath), collected);
        }

        return MixedOutcome<ContextStatistics>.Completed(counts, collected);
    }

    // One enumeration, bounded state: the per-column "was ever crossed" flags are the only
    // collection, and their size is the planned column count.
    private static async Task<ContextStatistics> CountObjectsAsync(
        PreparedRun run, List<BedrockDiagnostic> diagnostics, CancellationToken cancellation)
    {
        var formalAttributes = run.Plan.FormalAttributes.Count;
        var crossed = new bool[formalAttributes];
        long objects = 0;
        long crosses = 0;
        long crosslessObjects = 0;

        var enumeration = ObservedEmission.Over(
            run.EmitWith(run.Plan, diagnostics, cancellation), run.Input, run.Plan, PassKind.Statistics, cancellation);
        await foreach (var emitted in enumeration.WithCancellation(cancellation).ConfigureAwait(false))
        {
            objects++;

            var ids = emitted.CrossedFormalAttributeIds;
            if (ids.Count == 0)
            {
                crosslessObjects++;
            }

            crosses += ids.Count;
            for (var i = 0; i < ids.Count; i++)
            {
                crossed[ids[i]] = true;
            }
        }

        long empty = 0;
        foreach (var seen in crossed)
        {
            if (!seen)
            {
                empty++;
            }
        }

        return new ContextStatistics(objects, formalAttributes, crosses, crosslessObjects, empty);
    }
}
