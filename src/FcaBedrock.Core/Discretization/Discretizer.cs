using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Maps a raw value to a bin label (the discretizer half of the orthogonal
/// discretizer × scale model — decisions.md D-002). A closed set within Core;
/// external assemblies construct the concrete kinds but cannot derive new ones
/// (the plan-time members are internal).
/// </summary>
public abstract record Discretizer
{
    /// <summary>Stable kind identifier (e.g. <c>identity</c>).</summary>
    public abstract string Kind { get; }

    /// <summary>
    /// Emit-time: classify a single non-missing raw value into a <see cref="BinResult"/>
    /// — a recognized bin, no bin (out of range), an unknown value, or an unparseable
    /// numeric (decisions.md D-059). Missing detection happens before this is called.
    /// </summary>
    public abstract BinResult Discretize(string rawValue);

    /// <summary>
    /// Plan-time: the ordered bin labels the scale will see for an attribute with
    /// the given declared domain. Centralizes determinism rule §17(3).
    /// </summary>
    internal abstract IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain);

    /// <summary>
    /// Plan-time: the full bin structure a scale needs — the bins plus the cut
    /// edges and open ends an ordinal scale thresholds on. The default treats the
    /// bin labels as their own thresholds with no open ends (correct for value
    /// bins); cut discretizers override to supply real cuts. <see cref="BinLabels"/>
    /// stays the label authority.
    /// </summary>
    internal virtual BinScheme DescribeBins(IReadOnlyList<string> declaredDomain)
    {
        var labels = BinLabels(declaredDomain);
        return new BinScheme(
            labels,
            [.. labels.Select(CanonicalBin (label) => new ValueBin(label))],
            labels,
            OpenLow: false,
            OpenHigh: false);
    }

    /// <summary>
    /// Name-render time: the display form of a canonical bin label under
    /// <paramref name="style"/>. The default is style-independent (the canonical
    /// label itself); cut discretizers override for the v2-compat interior form.
    /// Identity/keys never go through here — only the rendered name (P-14, D-044).
    /// </summary>
    internal virtual string RenderBinLabel(string canonicalLabel, LabelStyle style) => canonicalLabel;

    /// <summary>
    /// Whether <c>value_labels</c> (raw value → display label, §10.8) applies to
    /// this discretizer — true only when the bin label IS the raw value
    /// (<c>identity</c>, <c>free_per_value</c>). For every other discretizer
    /// <c>value_labels</c> is dormant: ignored by both validation and name
    /// rendering, never an error (D-049). The single authority for that rule.
    /// </summary>
    internal virtual bool ConsultsValueLabels => false;

    /// <summary>
    /// Whether <see cref="BinLabels"/> reads the declared domain — true only for
    /// value-bin discretizers whose bin universe IS the domain (<c>identity</c>;
    /// <c>free_per_value</c> at M4). Cut discretizers ignore it (§10.3). The
    /// single authority for the fingerprint's effective-domain gate: an inert
    /// authored domain must not perturb output fingerprints (D-077).
    /// </summary>
    internal virtual bool ConsumesDeclaredDomain => false;
}
