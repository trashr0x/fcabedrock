using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Freezes a successfully calibrated, <b>paired</b> resolved spec into an explicit,
/// fully frozen <see cref="SpecDocument"/> (D-122 part 10, D-123 point 9): the
/// library face of the <c>calibrate</c> command's freeze step (§7). It consumes the
/// retained calibration outcomes and never reads data, recalibrates, or writes
/// fingerprints. It materializes each data-dependent result as its canonical freeze
/// mapping, written as an <b>explicit tier-5 attribute field</b> that
/// overrides any template/matcher-supplied winner while preserving every unrelated
/// authored, template-, and matcher-supplied field.
/// <para>
/// The composed document (<see cref="ResolvedDocument.Document"/>) carries the
/// attribute sections in their <em>pre-application</em> form: the resolver folds
/// templates/matchers into an effective section it discards, so a template- or
/// matcher-won base value (an <c>include</c> prefix domain, a <c>value_groups</c>
/// group set) is invisible there. A <c>value_groups</c> group set and the effective
/// policies are therefore read from the paired <see cref="CalibratedSpec.Spec"/>, the
/// effective resolved state (D-123 point 9). An <c>include</c> prefix domain is read from
/// the resolver's capture of the winning domain as authored
/// (<see cref="ResolvedDocument.AuthoredDomains"/>), because the effective numeric domain
/// holds canonical identities that cannot give back an authored spelling such as
/// <c>2.50</c>. Templates and matchers are <b>retained</b>, not stripped: the returned
/// document is flattened only
/// in that the paired resolved snapshot is already <c>extends</c>-free (the resolver
/// rejects an un-composed document), so no root <c>extends</c> survives.
/// </para>
/// <para>
/// Only <b>generated</b> domain entries (an observed domain, <c>include</c> additions) are
/// formatted. A numeric <c>free_per_value</c> entry is written for the binding locale by
/// <see cref="GeneratedNumberText"/>, and every other entry stays the retained string (§7).
/// </para>
/// <para>
/// The <c>[spec]</c> section (including any stored fingerprint fields) is carried
/// <b>verbatim</b>: freezing is the pure mapping stage, and the CLI's fully-frozen
/// write flow (re-resolve → native plan → <c>SpecFingerprints.ComputeNative</c> →
/// store the three fields via the record <c>with</c> path → canonically serialize)
/// recomputes and overwrites all three before final serialization. This method never
/// touches the filesystem, never reads a data source, never runs the calibrator, and
/// never mutates either input.
/// </para>
/// </summary>
public static class SpecFreezer
{
    /// <summary>
    /// Produces the fully frozen document for the paired <paramref name="resolved"/> /
    /// <paramref name="calibrated"/>. Every included attribute that carried a
    /// data-dependent discretizer, an omitted consumed domain, an
    /// <c>unknown_value_policy = "include"</c> domain, or a <c>value_groups</c>
    /// <c>unmatched = "passthrough"</c> policy is rewritten so the returned document
    /// re-resolves against the same schema with
    /// <see cref="CalibratedSpec.RequiresData"/> false, while every other attribute and
    /// every non-attribute section is preserved unchanged.
    /// </summary>
    /// <param name="resolved">The resolved document snapshot paired with its resolution token.</param>
    /// <param name="calibrated">The calibrated state produced from the <em>same</em> resolution.</param>
    /// <returns>The explicit, fully frozen document (no stored fingerprints written; that is the write flow's step).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolved"/> or <paramref name="calibrated"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="calibrated"/> was not produced from <paramref name="resolved"/>'s
    /// resolution (<c>ReferenceEquals(resolved.Resolved, calibrated.Resolution)</c> is
    /// false): a programmer error, mirroring the <c>SpecFingerprints.ComputeNative</c>
    /// pairing posture (EP-14), not a diagnostic.
    /// </exception>
    public static SpecDocument Freeze(ResolvedDocument resolved, CalibratedSpec calibrated)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(calibrated);

        // Pairing is by reference identity of the resolution token (D-098): the same guard
        // ComputeNative enforces, so a document can never be frozen against a calibration of
        // some other resolution. A mismatch is a programmer error, not a diagnostic.
        if (!ReferenceEquals(resolved.Resolved, calibrated.Resolution))
        {
            throw new ArgumentException(
                "The calibrated state was not produced from this resolution; Freeze requires the paired " +
                "resolved document and calibrated spec (D-123 point 9).",
                nameof(calibrated));
        }

        var document = resolved.Document;

        // Retained outcomes and the effective (post-application) Core attributes, both keyed by
        // attribute name: a total key over both, because a successfully resolved spec has unique,
        // non-empty attribute names (AttributeNameDuplicate / AttributeNameMissing are resolve
        // Errors, so they cannot reach a successful calibration). The effective index supplies what
        // the composed document cannot: template/matcher-won groups and policies, and the culture a
        // numeric domain is written for (D-123 point 9).
        var outcomes = new Dictionary<string, AttributeCalibration>(calibrated.Calibrations.Count, StringComparer.Ordinal);
        foreach (var outcome in calibrated.Calibrations)
        {
            outcomes[outcome.AttributeName] = outcome;
        }

        var effective = new Dictionary<string, AttributeSpec>(calibrated.Spec.Attributes.Count, StringComparer.Ordinal);
        foreach (var attribute in calibrated.Spec.Attributes)
        {
            effective[attribute.Name] = attribute;
        }

        // Apply outcomes by attribute identity, preserving attribute order; an attribute with no
        // outcome (and every non-attribute section) is reused by reference from the resolver's own
        // snapshot, never the caller's document. The rewritten fields are new records over
        // immutable lists, so the returned graph exposes no new mutable backing state; a reused
        // value_labels map keeps the SyncRoot-reachable backing it has in the snapshot (D-098).
        // The authored domains are paired by position: ResolvedDocument holds one per attribute.
        var authored = resolved.AuthoredDomains;
        var attributes = ImmutableArray.CreateBuilder<AttributeSection>(document.Attributes.Count);
        for (var i = 0; i < document.Attributes.Count; i++)
        {
            var section = document.Attributes[i];
            attributes.Add(section.Name is { } name && outcomes.TryGetValue(name, out var outcome)
                ? FreezeAttribute(section, name, outcome, effective, authored[i])
                : section);
        }

        return document with { Attributes = attributes.MoveToImmutable() };
    }

    // Rewrites only the fields the retained outcome controls (D-122 part 10), leaving every
    // other field (source, scale, restrict_to, value_labels, naming, description, include, and the
    // policies the outcome does not touch) carried verbatim, so a template/matcher still supplies
    // any it authored on re-resolve while the explicit frozen field (tier 5) overrides its winner.
    private static AttributeSection FreezeAttribute(
        AttributeSection section,
        string name,
        AttributeCalibration outcome,
        IReadOnlyDictionary<string, AttributeSpec> effective,
        AuthoredDomain authored) =>
        outcome switch
        {
            // Automatic numeric cuts → manual_cuts over the retained cuts, with ends omitted so the
            // established open-ended default applies (byte-identical to the auto form's NumericCutBins,
            // D-088). The retained cuts are already the effective, canonicalized cut values.
            CalibratedCuts cuts => section with
            {
                Discretizer = new ManualCutsDiscretizerSection(cuts.Cuts, Ends: null),
            },

            // Observed domain → an explicit declared_domain in retained first-observation order; an
            // empty outcome becomes an authored [] (a fixed empty domain), never a re-omitted null.
            // When the effective winning policy is include, the observed domain has captured the whole
            // population, so include is folded to warn; otherwise the frozen attribute would still be
            // data-dependent (D-122 part 10).
            ObservedDomain observed => FreezeObservedDomain(section, name, observed, effective),

            // include additions → the winning authored prefix followed by the generated additions,
            // with the policy rewritten to warn. MissingPolicy and every other field are preserved.
            IncludeAdditions additions => section with
            {
                DeclaredDomain = IncludeDomain(name, additions, authored, effective),
                UnknownValuePolicy = UnknownValuePolicy.Warn,
            },

            // passthrough bins → the effective pre-existing groups (read from the effective resolved
            // state, before additions) followed by one singleton group per retained bin, with unmatched
            // rewritten to skip.
            PassthroughBins bins => section with
            {
                Discretizer = FreezePassthrough(name, bins, effective),
            },

            // Unreachable: a CalibratedSpec retains only outcomes its substitution matched,
            // which are the four types above.
            _ => throw new InvalidOperationException(
                $"Attribute '{name}' carries an unrecognized calibration outcome '{outcome.GetType().Name}'."),
        };

    // §10.6/D-122 part 10: an omitted consumed domain fills from the observed domain
    // (the whole population, retained first-observation order). When the paired EFFECTIVE winning
    // policy is include, that population is already complete, so include has nothing left to add and
    // is folded to warn, exactly as the IncludeAdditions path does. Otherwise the frozen attribute
    // would still meet RequiresData's include condition, and FromFullyDeclared would reject it. The
    // policy is read from the effective attribute, not section.UnknownValuePolicy, because the
    // include winner may be default-, template-, or matcher-supplied (D-123 point 9). Every other
    // field, including MissingPolicy, is carried verbatim.
    private static AttributeSection FreezeObservedDomain(
        AttributeSection section, string name, ObservedDomain observed, IReadOnlyDictionary<string, AttributeSpec> effective)
    {
        if (!effective.TryGetValue(name, out var attribute))
        {
            throw new InvalidOperationException(
                $"Attribute '{name}' carries an observed-domain outcome, but the paired calibrated spec exposes no " +
                "effective attribute for it; the resolution/calibration pair is inconsistent.");
        }

        // The domain was omitted, so every entry is generated.
        var frozen = section with { DeclaredDomain = Generated(attribute, observed.Values) };
        return attribute.UnknownValuePolicy == UnknownValuePolicy.Include
            ? frozen with { UnknownValuePolicy = UnknownValuePolicy.Warn }
            : frozen;
    }

    // §10.6/D-122 part 10: the frozen final domain for an include-additions attribute is the winning
    // authored prefix, exactly as written whichever tier supplied it (an authored [] included),
    // followed by the retained additions, each appended once. Membership was already decided over
    // canonical identities when the additions were retained, so an authored 2.50 and a retained 2.5
    // are never both present. A missing prefix or effective attribute is a corrupt pair.
    private static IReadOnlyList<string> IncludeDomain(
        string name, IncludeAdditions additions, AuthoredDomain authored, IReadOnlyDictionary<string, AttributeSpec> effective)
    {
        if (authored.Values is not { } prefix || !effective.TryGetValue(name, out var attribute))
        {
            throw new InvalidOperationException(
                $"Attribute '{name}' carries an include-additions outcome, but the paired resolution exposes no " +
                "authored declared domain for it; the resolution/calibration pair is inconsistent.");
        }

        var generated = Generated(attribute, additions.Values);
        var domain = ImmutableArray.CreateBuilder<string>(prefix.Count + generated.Count);
        domain.AddRange(prefix);
        domain.AddRange(generated);
        return domain.MoveToImmutable();
    }

    // §7: a generated numeric free_per_value entry is written for the binding locale, using the
    // culture the effective discretizer parses with (the resolved binding.locale). Every other
    // generated entry is already the string the domain matches verbatim (§10.3).
    private static IReadOnlyList<string> Generated(AttributeSpec attribute, IReadOnlyList<string> values) =>
        attribute.Discretizer is FreePerValueDiscretizer { ValueType: SourceValueType.Number } numeric
            ? GeneratedNumberText.Format(values, numeric.Culture)
            : values;

    // §11.6/D-122 part 10: the frozen passthrough discretizer preserves the effective pre-existing
    // groups in order (each read verbatim from the effective ValueGroup, so an omitted values list and
    // an authored values = [] stay byte-distinct, D-104), appends one singleton group { label = value,
    // values = [value] } per retained passthrough bin in first-observation order, and sets unmatched =
    // skip. The effective groups come from the calibrated spec because a template/matcher may have
    // supplied them (D-123 point 9); the bins come from the retained outcome.
    private static ValueGroupsDiscretizerSection FreezePassthrough(
        string name, PassthroughBins bins, IReadOnlyDictionary<string, AttributeSpec> effective)
    {
        if (!effective.TryGetValue(name, out var attribute) || attribute.Discretizer is not ValueGroupsDiscretizer discretizer)
        {
            throw new InvalidOperationException(
                $"Attribute '{name}' carries a passthrough-bins outcome, but the paired calibrated spec exposes no " +
                "effective value_groups discretizer for it; the resolution/calibration pair is inconsistent.");
        }

        var groups = ImmutableArray.CreateBuilder<ValueGroupSection>(discretizer.Groups.Count + bins.Values.Count);
        foreach (var group in discretizer.Groups)
        {
            groups.Add(new ValueGroupSection(group.Label, group.Values, group.Pattern));
        }

        foreach (var value in bins.Values)
        {
            groups.Add(new ValueGroupSection(value, ImmutableArray.Create(value), Pattern: null));
        }

        return new ValueGroupsDiscretizerSection(groups.MoveToImmutable(), ValueGroupsUnmatched.Skip);
    }
}
