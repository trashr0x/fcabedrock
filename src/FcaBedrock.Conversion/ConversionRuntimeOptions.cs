namespace FcaBedrock.Conversion;

/// <summary>
/// The public, runtime-only configuration surface for a conversion run's temporary storage (D-122
/// part 8 / D-123 point 11) — the minimal capability behind the CLI's <c>--temp-dir</c>. Its single
/// setting, <see cref="TempDirectory"/>, chooses the root under which the grouping backend's spool
/// workspace is created <b>when a spill is required</b>. It is a runtime capability, never a
/// spec/fingerprint input: it changes no calibrated state, emitted object, diagnostic, ordering,
/// output byte, fingerprint, or manifest — the storage strategy never changes bytes (D-082). The
/// memory budget and merge fan-in stay internal pending M8 measurement (P-6). Immutable and safe to
/// share between runs; it carries no mutable reporting state.
/// </summary>
public sealed record ConversionRuntimeOptions
{
    private readonly string? _tempDirectory;

    /// <summary>
    /// The temp root for spool workspaces. <see langword="null"/> (the default) means the OS temp
    /// path — the existing behaviour. A non-null value must be a non-empty, non-whitespace path; it
    /// is not required to exist and is neither created nor normalized here (the spool filesystem
    /// lazily creates the owner-restricted <c>fcabedrock-spool-*</c> workspace and maps storage
    /// failures to the established D-082 diagnostics). Validated at this public boundary: a non-null
    /// empty or whitespace-only path throws <see cref="ArgumentException"/>.
    /// </summary>
    public string? TempDirectory
    {
        get => _tempDirectory;
        init
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "The temp directory, when specified, must be a non-empty, non-whitespace path.",
                    nameof(TempDirectory));
            }

            _tempDirectory = value;
        }
    }

    /// <summary>
    /// Maps this runtime capability to the internal grouping configuration, carrying <b>only</b>
    /// <see cref="TempDirectory"/> through; the memory budget, merge fan-in, spool filesystem, and
    /// observer all take their production defaults (D-123 point 11). The one-value mapping is kept
    /// <see langword="internal"/> so it stays directly testable without widening the public surface,
    /// and so <see cref="GroupingOptions"/> stays internal.
    /// </summary>
    internal GroupingOptions ToGroupingOptions() => new(tempDirectory: TempDirectory);
}
