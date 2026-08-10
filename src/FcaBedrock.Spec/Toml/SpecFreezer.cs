using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Freezes a successfully calibrated, <b>paired</b> resolved spec into an explicit,
/// fully frozen <see cref="SpecDocument"/> (D-122 part 10, D-123 point 9): the
/// library face of the <c>calibrate</c> command's freeze step (§15). It consumes the
/// retained calibration outcomes — it never reads data, recalibrates, or writes
/// fingerprints — and materializes each data-dependent result as the accepted
/// canonical mapping, written as an <b>explicit tier-5 attribute field</b> that
/// overrides any template/matcher-supplied winner while preserving every unrelated
/// authored, template-, and matcher-supplied field.
/// <para>
/// The composed document (<see cref="ResolvedDocument.Document"/>) carries the
/// attribute sections in their <em>pre-application</em> form — the resolver folds
/// templates/matchers into an effective section it discards — so a template- or
/// matcher-won base value (an <c>include</c> prefix domain, a <c>value_groups</c>
/// group set) is invisible there. Those effective values are therefore read from the
/// paired <see cref="CalibratedSpec.Spec"/>, the effective resolved state, before the
/// retained additions are appended (D-123 point 9). Templates and
/// matchers are <b>retained</b>, not stripped: the returned document is flattened only
/// in that the paired resolved snapshot is already <c>extends</c>-free (the resolver
/// rejects an un-composed document), so no root <c>extends</c> survives.
/// </para>
/// <para>
/// The <c>[spec]</c> section — including any stored fingerprint fields — is carried
/// <b>verbatim</b>: freezing is the pure mapping stage, and the accepted fully-frozen
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
    /// <returns>The explicit, fully frozen document (no stored fingerprints written — that is the write flow's step).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolved"/> or <paramref name="calibrated"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="calibrated"/> was not produced from <paramref name="resolved"/>'s
    /// resolution (<c>ReferenceEquals(resolved.Resolved, calibrated.Resolution)</c> is
    /// false) — a programmer error, mirroring the <c>SpecFingerprints.ComputeNative</c>
    /// pairing posture (P-14), not a diagnostic.
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
        // attribute name — a total key over both, because a successfully resolved spec has unique,
        // non-empty attribute names (AttributeNameDuplicate / AttributeNameMissing are resolve
        // Errors, so they cannot reach a successful calibration). The effective index is what the
        // composed document cannot supply: the template/matcher-won base values (D-123 point 9).
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
        // outcome (and every non-attribute section) is reused by reference — the sections are
        // immutable, so the returned graph exposes no new mutable backing state.
        var attributes = ImmutableArray.CreateBuilder<AttributeSection>(document.Attributes.Count);
        foreach (var section in document.Attributes)
        {
            attributes.Add(section.Name is { } name && outcomes.TryGetValue(name, out var outcome)
                ? FreezeAttribute(section, name, outcome, effective)
                : section);
        }

        return document with { Attributes = attributes.MoveToImmutable() };
    }

    // Rewrites exactly the one field the retained outcome controls (D-122 part 10), leaving every
    // other field — source, scale, restrict_to, value_labels, naming, description, include, and the
    // policies the outcome does not touch — carried verbatim, so a template/matcher still supplies
    // any it authored on re-resolve while the explicit frozen field (tier 5) overrides its winner.
    private static AttributeSection FreezeAttribute(
        AttributeSection section, string name, AttributeCalibration outcome, IReadOnlyDictionary<string, AttributeSpec> effective) =>
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
            // population, so include is folded to warn — otherwise the frozen attribute would still be
            // data-dependent (D-122 part 10).
            ObservedDomain observed => FreezeObservedDomain(section, name, observed, effective),

            // include additions → the effective final domain (prefix ++ additions, already assembled
            // once in the calibrated effective spec, D-122 part 15) with the policy rewritten to warn.
            // MissingPolicy and every other field are preserved.
            IncludeAdditions => section with
            {
                DeclaredDomain = EffectiveIncludeDomain(name, effective),
                UnknownValuePolicy = UnknownValuePolicy.Warn,
            },

            // passthrough bins → the effective pre-existing groups (read from the effective resolved
            // state, before additions) followed by one singleton group per retained bin, with unmatched
            // rewritten to skip.
            PassthroughBins bins => section with
            {
                Discretizer = FreezePassthrough(name, bins, effective),
            },

            // The union is closed (private-protected base); this is unreachable for any honest outcome.
            _ => throw new InvalidOperationException(
                $"Attribute '{name}' carries an unrecognized calibration outcome '{outcome.GetType().Name}'."),
        };

    // §10.6/D-122 part 10: an omitted consumed domain fills from the observed domain
    // (the whole population, retained first-observation order). When the paired EFFECTIVE winning
    // policy is include, that population is already complete, so include has nothing left to add and
    // is folded to warn — exactly as the IncludeAdditions path does — otherwise the frozen attribute
    // would still trip the include half of the fully-frozen gate and reject FromFullyDeclared. The
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

        var frozen = section with { DeclaredDomain = observed.Values };
        return attribute.UnknownValuePolicy == UnknownValuePolicy.Include
            ? frozen with { UnknownValuePolicy = UnknownValuePolicy.Warn }
            : frozen;
    }

    // §10.6/D-122 part 15: the frozen final domain for an include-additions attribute is the effective
    // one the calibrated spec already holds — the effective pre-existing prefix (which may itself be
    // template/matcher-won) with the retained additions appended exactly once. Reading it here (rather
    // than re-appending) is why additions are never duplicated. Unreachable-null is a corrupt pair.
    private static IReadOnlyList<string> EffectiveIncludeDomain(string name, IReadOnlyDictionary<string, AttributeSpec> effective)
    {
        if (effective.TryGetValue(name, out var attribute) && attribute.DeclaredDomain is { } domain)
        {
            return domain;
        }

        throw new InvalidOperationException(
            $"Attribute '{name}' carries an include-additions outcome, but the paired calibrated spec exposes no " +
            "effective declared domain for it; the resolution/calibration pair is inconsistent.");
    }

    // §11.6/D-122 part 10: the frozen passthrough discretizer preserves the effective pre-existing
    // groups in order (each read verbatim from the effective ValueGroup, so an omitted values list and
    // an authored values = [] stay byte-distinct, G-11), appends one singleton group { label = value,
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
