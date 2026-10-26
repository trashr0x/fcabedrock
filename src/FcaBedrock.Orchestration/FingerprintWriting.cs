using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Orchestration;

/// <summary>
/// Writes a fully frozen run's computed fingerprints into its root document (D-122 part 10), the
/// rewrite of the CLI's <c>fingerprint --write</c>.
/// </summary>
public static class FingerprintWriting
{
    /// <summary>
    /// The <b>root</b> document as authored, never the composed one, with its three stored
    /// fingerprint fields set to the computed values. Every other section survives verbatim and the
    /// chain is not flattened (§13 rule 8): deliberately the opposite of
    /// <see cref="SpecFreezing.Freeze"/>, which writes the flattened composed document. It freezes,
    /// re-resolves and replans nothing.
    /// <para>
    /// The root's authored <c>extends</c> is kept. A host that writes the result to a file decides
    /// that file's <c>extends</c> through <see cref="SpecRelocation"/>; one that writes it to a
    /// stream with no folder of its own writes it as authored.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="run"/> is not fully frozen (<see cref="PreparedRun.IsFullyFrozen"/>, §14): the
    /// same rule the CLI's <c>fingerprint --write</c> applies before it opens any output, so no host
    /// has a second reading of when fingerprints can be written.
    /// </exception>
    public static SpecDocument Corrected(PreparedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!run.IsFullyFrozen)
        {
            throw new ArgumentException("A run whose spec is not fully frozen has no fingerprints to write.", nameof(run));
        }

        // Everything else the record carries is untouched, extends included.
        var spec = run.RootDocument.Spec! with
        {
            SchemaFingerprint = run.Fingerprints.SchemaFingerprint,
            CxtOutputFingerprint = run.Fingerprints.CxtOutputFingerprint,
            DatOutputFingerprint = run.Fingerprints.DatOutputFingerprint,
        };

        return run.RootDocument with { Spec = spec };
    }
}
