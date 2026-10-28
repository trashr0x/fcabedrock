namespace FcaBedrock.Orchestration;

/// <summary>Prepares runs: the one preparation path every file-backed run shares (D-122 part 10, D-140).</summary>
public static class RunPreparation
{
    /// <summary>
    /// Reads the root, composes, resolves read settings, reads the schema through the hashing
    /// wrapper, resolves, binds, calibrates when the spec needs data, plans natively, computes the
    /// three native fingerprints and checks input stability. Writes nothing. Unobserved: exactly
    /// <see cref="PrepareAsync(FileRunRequest, OrchestrationHost, OperationProgress, CancellationToken)"/>
    /// with no progress.
    /// <para>
    /// Completed: the run, with every diagnostic so far. Diagnostic failure: parse, composition,
    /// read settings, resolve, calibration or the plan failed. Host failure:
    /// <see cref="HostErrorKind.TemporaryDirectoryInvalid"/> before any open,
    /// <see cref="HostErrorKind.SpecUnreadable"/> for the root, and
    /// <see cref="HostErrorKind.DataUnreadable"/> or <see cref="HostErrorKind.DataChanged"/> for the
    /// data. Cancellation propagates as <paramref name="cancellation"/>'s own exception, and an
    /// unexpected fault propagates unchanged. The returned task completes only after the library's
    /// own cleanup for the call has run; the run holds no open stream afterwards.
    /// </para>
    /// </summary>
    public static Task<OperationOutcome<PreparedFileRun>> PrepareAsync(
        FileRunRequest request, OrchestrationHost host, CancellationToken cancellation) =>
        PrepareAsync(request, host, progress: null, cancellation);

    /// <summary>
    /// Prepares a run as the other overload does, observed by <paramref name="progress"/> (D-141):
    /// the schema pass and any calibration passes report to it, with <see cref="WorkingStep.Calibrating"/>
    /// while the calibrator works after a calibration pass's input closed, and the returned run carries
    /// it into <see cref="ContextStatistics.CountAsync"/>, <see cref="ConvertPublication.Preflight"/> and
    /// <see cref="ConvertPublication.PublishAsync"/>. A null progress is exactly the unobserved overload.
    /// <para>
    /// A cancellation that reaches a pass's scope ends that pass cancelled, unless its input had already
    /// reached its end; one observed at a phase boundary, outside every pass, leaves the last pass with
    /// the end already recorded and ends no pass cancelled. Either way the call throws
    /// <paramref name="cancellation"/>'s own exception, only after its cleanup has run.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="progress"/> is completed; thrown before any file opens.
    /// </exception>
    public static Task<OperationOutcome<PreparedFileRun>> PrepareAsync(
        FileRunRequest request, OrchestrationHost host, OperationProgress? progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(host);
        OperationProgress.ThrowIfCompleted(progress, nameof(progress));
        return RunPipeline.PrepareAsync(request, host, progress, cancellation);
    }
}
