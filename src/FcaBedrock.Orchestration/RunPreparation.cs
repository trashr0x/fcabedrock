namespace FcaBedrock.Orchestration;

/// <summary>Prepares runs: the one preparation path every file-backed run shares (D-122 part 10, D-140).</summary>
public static class RunPreparation
{
    /// <summary>
    /// Reads the root, composes, resolves read settings, reads the schema through the hashing
    /// wrapper, resolves, binds, calibrates when the spec needs data, plans natively, computes the
    /// three native fingerprints and checks input stability. Writes nothing.
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
        FileRunRequest request, OrchestrationHost host, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(host);
        return RunPipeline.PrepareAsync(request, host, cancellation);
    }
}
