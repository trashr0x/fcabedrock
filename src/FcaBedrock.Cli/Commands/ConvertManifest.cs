using FcaBedrock.Core.Calibration;
using FcaBedrock.Spec.Manifest;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// Composes the §15 run manifest from one prepared run's own facts.
/// <para>
/// <b>It composes; it never formats.</b> The canonical bytes belong to
/// <see cref="RunManifestWriter"/> in <c>FcaBedrock.Spec</c>, which shares the spec writer's
/// literal, array, and wrapping machinery — so the CLI writes no TOML and there is no second
/// canonical emitter (D-123 point 8, P-5).
/// </para>
/// <para>
/// <b>Nothing here is re-derived.</b> The fingerprints, hashes, paths, and calibration outcomes
/// are all carried in from where they were produced (D-093): the clock is read once, the audit
/// argv is passed through verbatim, and the retained
/// <see cref="AttributeCalibration"/> outcomes are handed to the model as they are.
/// </para>
/// </summary>
internal static class ConvertManifest
{
    /// <summary>Assembles the manifest for <paramref name="run"/>.</summary>
    /// <param name="run">The prepared run — the owner of every reproduction fact.</param>
    /// <param name="environment">The injected clock, version, and audit argv.</param>
    /// <param name="outputs">The staged artifacts, in canonical format order.</param>
    /// <param name="cxtFingerprint">The <b>effective</b> <c>.cxt</c> fingerprint, or null when no <c>.cxt</c> was written.</param>
    /// <param name="datFingerprint">The <b>effective</b> <c>.dat</c> fingerprint, or null when no <c>.dat</c> was written.</param>
    public static RunManifest Compose(
        PreparedRun run,
        CliEnvironment environment,
        IReadOnlyList<RunOutput> outputs,
        string? cxtFingerprint,
        string? datFingerprint)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(outputs);

        var chain = run.SpecChain;
        var section = new RunSection(
            environment.ToolVersion,
            WholeSecondUtc(environment.Clock.UtcNow),
            environment.AuditArgv,
            run.SpecPath,
            chain[0].Hash,

            // The schema fingerprint is the NATIVE paired plan's: it is a property of the spec,
            // not of a CLI byte override, and §14 keeps it style-independent. Only the two output
            // fingerprints are the effective ones (§15).
            run.Fingerprints.SchemaFingerprint,
            cxtFingerprint,
            datFingerprint,
            run.DataPath,
            ContentHash.Format(run.Input.Digest!));

        return new RunManifest(section, outputs, SpecFiles(chain), Calibrations(run));
    }

    /// <summary>
    /// §15's whole-second RFC 3339 UTC instant, produced at the host boundary from one clock
    /// read. Truncation happens here rather than in the model, and never through text, so no
    /// culture participates and the audit value is not silently rewritten downstream.
    /// </summary>
    internal static DateTimeOffset WholeSecondUtc(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    // Present only for an extends chain (§15): a single spec file leaves the whole family absent.
    // Otherwise every file in load order — root first, then bases — with its authored spelling.
    private static List<SpecFileEntry> SpecFiles(IReadOnlyList<SpecChainFile> chain)
    {
        var entries = new List<SpecFileEntry>();
        if (chain.Count < 2)
        {
            return entries;
        }

        foreach (var file in chain)
        {
            entries.Add(new SpecFileEntry(file.Spelling, file.Hash));
        }

        return entries;
    }

    // One entry per retained outcome, in CalibratedSpec.Calibrations order (spec-attribute
    // order). A legitimate zero-discovery observed-domain, include-additions, or passthrough-bins
    // outcome is passed through as it is, so it serializes as an explicit `values = []`.
    private static List<RunCalibration> Calibrations(PreparedRun run)
    {
        var entries = new List<RunCalibration>();
        foreach (var outcome in run.Calibrated.Calibrations)
        {
            entries.Add(new RunCalibration(
                outcome, outcome is CalibratedCuts ? AuthoredCutsKind(run, outcome.AttributeName) : null));
        }

        return entries;
    }

    /// <summary>
    /// The authored discretizer kind behind a cuts outcome — <c>equal_width</c> or
    /// <c>equal_frequency</c> — read from the <b>pre-calibration</b> resolved spec's
    /// <see cref="CalibrationPending"/> carrier for that attribute.
    /// <para>
    /// This is the only place it can honestly come from. The retained outcome carries the cut
    /// values alone, and the effective spec has already had the pending carrier replaced by an
    /// executable discretizer — so guessing from the cut values, from the substituted
    /// discretizer, or from anything else would be inventing an audit fact.
    /// </para>
    /// </summary>
    private static string AuthoredCutsKind(PreparedRun run, string attributeName)
    {
        foreach (var attribute in run.Resolved.Resolved.Spec.Attributes)
        {
            if (string.Equals(attribute.Name, attributeName, StringComparison.Ordinal)
                && attribute.Discretizer is CalibrationPending pending)
            {
                return pending.Config.Kind;
            }
        }

        // Unreachable: a cuts outcome is produced only for an attribute the calibrator found
        // pending. Reaching it would mean the retained state and the resolution disagree.
        throw new InvalidOperationException(
            $"Attribute '{attributeName}' has a calibrated-cuts outcome but no pending auto-discretizer.");
    }
}
