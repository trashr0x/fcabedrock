using System.Text.RegularExpressions;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The composed document's <c>[[template]]</c> table (§9.1/§13): id → first
/// declaration, plus the two identity conditions. Built once per resolve, before
/// anything can reference a template.
/// </summary>
internal sealed class TemplateTable
{
    private readonly Dictionary<string, TemplateSection> _byId = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds the table over the <b>composed</b> template list, appending the family-1
    /// identity diagnostics in composed template order.
    /// <para>
    /// A duplicate keeps the <b>first</b> declaration as the lookup, so aggregation
    /// continues to produce useful downstream diagnostics rather than collapsing into
    /// a cascade of unknown references — the Errors still fail the resolve. An
    /// <em>invalid</em> id never reaches here from a successful parse (§9.1: a
    /// malformed id is <c>SpecFieldInvalid</c>), so the only shapes handled are
    /// "absent" and "already seen".
    /// </para>
    /// </summary>
    public static TemplateTable Build(IReadOnlyList<TemplateSection> templates, List<BedrockDiagnostic> diagnostics)
    {
        var table = new TemplateTable();
        for (var i = 0; i < templates.Count; i++)
        {
            // 1-based for humans; the ordinal is the composed declaration position, which
            // is what makes the identity of an id-less template deterministic.
            var ordinal = i + 1;
            if (templates[i].Id is not { } id)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.TemplateIdMissing, DiagnosticSeverity.Error,
                    $"[[template]] #{ordinal} declares no id; a template is referenced by name, so id is required (§9.1)."));
                continue;
            }

            if (!table._byId.TryAdd(id, templates[i]))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.TemplateIdDuplicate, DiagnosticSeverity.Error,
                    $"[[template]] #{ordinal} redeclares id \"{id}\"; template ids must be unique across the composed document (§9.1/§13)."));
            }
        }

        return table;
    }

    /// <summary>The template declaring <paramref name="id"/>, or null when none does.</summary>
    public TemplateSection? Find(string id) => _byId.GetValueOrDefault(id);
}

/// <summary>
/// A template a matcher applied to one attribute, tagged with the matcher's
/// declaration ordinal so the fold can record which matcher won a field.
/// </summary>
internal readonly record struct MatchedTemplate(int MatcherOrdinal, TemplateSection Template);

/// <summary>
/// One <c>[[matcher]]</c>'s evaluated state (§9.2): its resolved template, how many
/// attributes its selector chose, and whether any field it authors won anywhere —
/// the three facts the family-5 warning traversal needs.
/// </summary>
internal sealed class MatcherEvaluation
{
    public required int Ordinal { get; init; }

    /// <summary>The authored <c>template</c> reference, for diagnostics; null on a hand-built matcher.</summary>
    public required string? Reference { get; init; }

    /// <summary>The referenced template, or null when the reference is unknown (or absent).</summary>
    public required TemplateSection? Template { get; init; }

    /// <summary>
    /// How many declared logical attributes the selector chose. Counted even when the
    /// template reference is unknown: selection is selector-only (§9.2/D-115).
    /// </summary>
    public int SelectedCount { get; set; }

    /// <summary>Whether any field this matcher's template authors won on any selected attribute.</summary>
    public bool WonSomewhere { get; set; }
}

/// <summary>
/// §9.2 template/matcher application: selector evaluation and the five-tier,
/// field-wise, presence-based merge, run entirely inside
/// <see cref="SpecResolver.Resolve"/> after composition and source addressing and
/// before effective-attribute validation (D-118).
/// <para>
/// The whole design rests on one idea (D-114): applying a template is
/// <b>deterministic syntactic sugar for ordinary per-attribute configuration</b>.
/// So application produces an effective <see cref="AttributeSection"/> — the same
/// document type a flat spec produces — and every existing validation owner then
/// runs over it unchanged. Provenance falls out for free rather than needing a
/// parallel model: a template-won <c>boundary</c> simply <em>is</em> a non-null
/// <c>Scale.Boundary</c> on the effective section, which is exactly what
/// "authored" already means to <c>ValidateOrdinalOverCuts</c>, while
/// <c>[defaults]</c> fills stay below the fold and stay defaulted.
/// </para>
/// <para>
/// Nothing here reaches Core: template ids, selectors, compiled regexes, ranges,
/// and precedence syntax all die at this seam (D-118).
/// </para>
/// </summary>
internal static class TemplateApplication
{
    /// <summary>
    /// Evaluates every matcher in declaration order against the declared attributes,
    /// emitting the family-2 diagnostics (unknown reference, selector/shape
    /// incompatibility) and filling <paramref name="matching"/> — per attribute, the
    /// templates that apply to it, in matcher declaration order.
    /// <para>
    /// Selection reads only composed configuration plus the already-computed
    /// addressing table: no source is opened and no data row is read (§7 phase 1,
    /// D-118). Each <c>name_regex</c> is compiled once and reused across all
    /// attribute names.
    /// </para>
    /// </summary>
    public static MatcherEvaluation[] Evaluate(
        IReadOnlyList<MatcherSection> matchers,
        IReadOnlyList<AttributeSection> attributes,
        AddressedAttribute[] addressed,
        TemplateTable templates,
        SourceShape shape,
        List<MatchedTemplate>[] matching,
        List<BedrockDiagnostic> diagnostics)
    {
        var evaluations = new MatcherEvaluation[matchers.Count];
        for (var m = 0; m < matchers.Count; m++)
        {
            var matcher = matchers[m];
            var ordinal = m + 1; // 1-based for humans

            var template = matcher.Template is { } reference ? templates.Find(reference) : null;
            if (matcher.Template is { } unresolved && template is null)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.TemplateReferenceUnknown, DiagnosticSeverity.Error,
                    $"[[matcher]] #{ordinal} references template \"{unresolved}\", which no [[template]] declares (§9.2)."));
            }

            var evaluation = new MatcherEvaluation
            {
                Ordinal = ordinal,
                Reference = matcher.Template,
                Template = template,
            };
            evaluations[m] = evaluation;

            // §9.2/D-115: a range addresses a physical wide column, which a predicate
            // source does not have. Reported per incompatible matcher; the selector then
            // simply selects nothing (no attribute under triple carries a column index),
            // which is also why it still reaches the family-5 zero-match warning.
            if (matcher.Match?.SourceIndexRange is not null && shape == SourceShape.Triple)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.MatcherSelectorInvalidForShape, DiagnosticSeverity.Error,
                    $"{Identify(evaluation)} authors source_index_range, which is incompatible with shape = \"triple\"; " +
                    "a predicate source has no column index (§9.2)."));
            }

            Select(matcher, attributes, addressed, evaluation, template, matching);
        }

        return evaluations;
    }

    private static void Select(
        MatcherSection matcher,
        IReadOnlyList<AttributeSection> attributes,
        AddressedAttribute[] addressed,
        MatcherEvaluation evaluation,
        TemplateSection? template,
        List<MatchedTemplate>[] matching)
    {
        if (matcher.Match is not { } match)
        {
            return; // no selector: parse rejects this, so only a hand-built document reaches here
        }

        Regex? regex = null;
        if (match.NameRegex is { } pattern && !MatcherSelectors.TryCompileWholeName(pattern, out regex, out _))
        {
            return; // likewise unreachable from a successful parse, which gates compilability
        }

        for (var a = 0; a < attributes.Count; a++)
        {
            if (!Selects(regex, match.SourceIndexRange, attributes[a], addressed[a]))
            {
                continue;
            }

            evaluation.SelectedCount++;
            if (template is not null)
            {
                // An unknown reference contributes no configuration, but its selection
                // still counted above — the zero-match warning is selector-only.
                matching[a].Add(new MatchedTemplate(evaluation.Ordinal, template));
            }
        }
    }

    private static bool Selects(Regex? regex, IReadOnlyList<long>? range, AttributeSection attribute, AddressedAttribute addressed)
    {
        if (regex is not null)
        {
            // §9.2/D-115: the selector reads the complete logical attribute name — never a
            // source header, predicate text, display_name, or a rendered formal name. An
            // attribute with no usable name has no logical name to test, and is already an
            // AttributeNameMissing Error, so it is never selected.
            return attribute.Name is { Length: > 0 } name && regex.IsMatch(name);
        }

        // §9.2/D-115: inclusive, zero-based, over the RESOLVED physical column index — so
        // several logical attributes bound to one column all match, a name-bound source
        // participates once the schema resolved it, and an endpoint beyond the source
        // width is legal over-coverage with simply nothing left to match. A source that
        // could not be addressed is not selected: its own binding diagnostic (already
        // recorded exactly once) is the sufficient report.
        return range is { Count: 2 }
            && addressed.Source is AddressedColumn column
            && range[0] <= column.Index && column.Index <= range[1];
    }

    /// <summary>
    /// Folds tiers 3–5 into one effective <see cref="AttributeSection"/> — matching
    /// matcher templates in declaration order, then the directly named template, then
    /// the attribute's own explicit fields — and records, per matcher, whether any
    /// field it authors won.
    /// <para>
    /// Tiers 1–2 (built-ins and <c>[defaults]</c>) deliberately stay <b>below</b> this
    /// fold, where the resolver already applies them: materializing them into the
    /// effective section would turn defaulted values into authored ones and, for
    /// <c>ordinal_boundary</c>, silently convert a legal defaulted boundary into an
    /// <c>OrdinalBoundaryIncompatibleWithCuts</c> Error (§6/§12.3/D-114).
    /// </para>
    /// </summary>
    public static AttributeSection Apply(
        AttributeSection attribute,
        TemplateSection? named,
        IReadOnlyList<MatchedTemplate> matching,
        MatcherEvaluation[] matchers)
    {
        if (named is null && matching.Count == 0)
        {
            return attribute; // the flat path: byte-for-byte the pre-M6 document
        }

        var effective = attribute;

        // The ten §9.1 eligible fields, each folded independently. Presence — not value —
        // decides every one of them (D-114), so an explicit `false`, an authored `[]`, and
        // a value equal to its own default all override a lower tier, while an omission
        // inherits and can never erase. Compounds (discretizer, scale, declared_domain,
        // restrict_to, value_labels, and the two naming fields) are replaced ENTIRE: a
        // half-merged scale is exactly the incoherent hybrid §9.2 rejects.
        if (Winner(attribute.Include is not null, named, matching, static t => t.Include is not null, matchers) is { } include)
        {
            effective = effective with { Include = include.Include };
        }

        if (Winner(attribute.Discretizer is not null, named, matching, static t => t.Discretizer is not null, matchers) is { } discretizer)
        {
            effective = effective with { Discretizer = discretizer.Discretizer };
        }

        if (Winner(attribute.Scale is not null, named, matching, static t => t.Scale is not null, matchers) is { } scale)
        {
            effective = effective with { Scale = scale.Scale };
        }

        if (Winner(attribute.DeclaredDomain is not null, named, matching, static t => t.DeclaredDomain is not null, matchers) is { } domain)
        {
            effective = effective with { DeclaredDomain = domain.DeclaredDomain };
        }

        if (Winner(attribute.RestrictTo is not null, named, matching, static t => t.RestrictTo is not null, matchers) is { } restrictTo)
        {
            effective = effective with { RestrictTo = restrictTo.RestrictTo };
        }

        if (Winner(attribute.ValueLabels is not null, named, matching, static t => t.ValueLabels is not null, matchers) is { } labels)
        {
            effective = effective with { ValueLabels = labels.ValueLabels };
        }

        if (Winner(attribute.MissingPolicy is not null, named, matching, static t => t.MissingPolicy is not null, matchers) is { } missing)
        {
            effective = effective with { MissingPolicy = missing.MissingPolicy };
        }

        if (Winner(attribute.UnknownValuePolicy is not null, named, matching, static t => t.UnknownValuePolicy is not null, matchers) is { } unknown)
        {
            effective = effective with { UnknownValuePolicy = unknown.UnknownValuePolicy };
        }

        if (Winner(attribute.DisplayName is not null, named, matching, static t => t.DisplayName is not null, matchers) is { } displayName)
        {
            effective = effective with { DisplayName = displayName.DisplayName };
        }

        if (Winner(attribute.FormalAttributeFormat is not null, named, matching, static t => t.FormalAttributeFormat is not null, matchers) is { } format)
        {
            effective = effective with { FormalAttributeFormat = format.FormalAttributeFormat };
        }

        return effective;
    }

    /// <summary>
    /// The precedence walk for one field: the explicit attribute field wins outright
    /// (tier 5, nothing to copy); otherwise the directly named template (tier 4);
    /// otherwise the <b>last</b> matching template that authors the field, in matcher
    /// declaration order (tier 3's field-wise last-author-wins).
    /// <para>
    /// Recording the matcher win here rather than in a second pass is deliberate: the
    /// winner is known exactly once, at the moment it is selected, so the shadow map
    /// cannot drift from the merge it describes.
    /// </para>
    /// </summary>
    private static TemplateSection? Winner(
        bool explicitlyAuthored,
        TemplateSection? named,
        IReadOnlyList<MatchedTemplate> matching,
        Func<TemplateSection, bool> authors,
        MatcherEvaluation[] matchers)
    {
        if (explicitlyAuthored)
        {
            return null; // tier 5
        }

        if (named is not null && authors(named))
        {
            return named; // tier 4 — a direct reference beats every pattern
        }

        for (var i = matching.Count - 1; i >= 0; i--)
        {
            if (authors(matching[i].Template))
            {
                matchers[matching[i].MatcherOrdinal - 1].WonSomewhere = true;
                return matching[i].Template;
            }
        }

        return null;
    }

    /// <summary>
    /// Family 5 (§16.4): the two matcher warnings, produced by <b>one</b> traversal in
    /// matcher declaration order — so they interleave by matcher rather than grouping
    /// by code, and a matcher qualifies for at most one.
    /// <para>
    /// Zero-match is selector-only: it fires whether or not the referenced template
    /// resolved, because over-covering is the condition it catches. Fully-shadowed is a
    /// <b>merge-level</b> determination and is deliberately independent of
    /// <c>include = false</c> dormancy (§9.2/D-116) — a field that wins on an excluded
    /// attribute means the matcher is doing something, even though the winning
    /// configuration is dormant while the attribute is excluded.
    /// </para>
    /// </summary>
    public static void AddWarnings(MatcherEvaluation[] matchers, List<BedrockDiagnostic> diagnostics)
    {
        foreach (var matcher in matchers)
        {
            if (matcher.SelectedCount == 0)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.MatcherSelectsNoAttributes, DiagnosticSeverity.Warning,
                    $"{Identify(matcher)} selects no attributes; its selector matches nothing declared in this spec (§9.2)."));
            }
            else if (matcher.Template is not null && !matcher.WonSomewhere)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.MatcherFullyShadowed, DiagnosticSeverity.Warning,
                    $"{Identify(matcher)} is fully shadowed: every field its template authors is overridden by a " +
                    $"higher-precedence source on all {matcher.SelectedCount} attribute(s) it selects, so it changes nothing (§9.2)."));
            }
        }
    }

    /// <summary>
    /// How a matcher-scoped diagnostic names itself (§16.4): its <b>declaration
    /// ordinal</b> and its <b>template reference</b>, both deterministic. One helper for
    /// every such message, so the identity cannot drift between them.
    /// <para>
    /// The reference is null only on a hand-built document — parse requires a matcher's
    /// <c>template</c> key — so that branch says so rather than rendering an empty
    /// quoted string. <c>TemplateReferenceUnknown</c> does not use this helper: its own
    /// prose already names both, and prefixing it would repeat the reference twice.
    /// </para>
    /// </summary>
    private static string Identify(MatcherEvaluation matcher) =>
        matcher.Reference is { } reference
            ? $"[[matcher]] #{matcher.Ordinal} (template \"{reference}\")"
            : $"[[matcher]] #{matcher.Ordinal} (no template reference)";
}
