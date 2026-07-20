using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// §9.2's five-tier, field-wise, presence-based merge (D-114/D-121), asserted on
/// the <b>resolved</b> attribute rather than on any intermediate structure — the
/// contract is "a template behaves exactly as though its fields had been written
/// on the attribute", so what must be pinned is what resolution actually produces.
/// <para>
/// The tiers, lowest to highest: built-in defaults &lt; <c>[defaults]</c> &lt;
/// matching matcher templates in declaration order &lt; the directly named
/// template &lt; explicit attribute fields.
/// </para>
/// </summary>
public sealed class TemplateApplicationTests
{
    private static Diagnosed<BedrockSpec> Resolve(SpecDocument document)
    {
        var resolved = SpecResolver.Resolve(document);
        return resolved.TryGetValue(out var doc)
            ? Diagnosed<BedrockSpec>.Ok(doc.Resolved.Spec, resolved.Diagnostics)
            : Diagnosed<BedrockSpec>.Failed(resolved.Diagnostics);
    }

    private static AttributeSpec ResolveSingle(SpecDocument document)
    {
        var result = Resolve(document);
        Assert.True(result.TryGetValue(out var spec), Describe(result.Diagnostics));
        return Assert.Single(spec.Attributes);
    }

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    // A scalable-by-default attribute the tests layer templates onto: name + source only,
    // so every scaling field below arrives from whichever tier the case is exercising.
    private static AttributeSection Bare(string name = "a") =>
        DocumentFixtures.Attribute(name, DocumentFixtures.Column(0));

    private static SpecDocument With(
        AttributeSection attribute,
        IReadOnlyList<TemplateSection> templates,
        IReadOnlyList<MatcherSection> matchers,
        DefaultsSection? defaults = null) =>
        DocumentFixtures.Document([attribute], defaults: defaults, templates: templates, matchers: matchers);

    // Matches whatever single attribute the case declares, so selection is never the
    // variable under test — precedence is.
    private static MatcherSection MatchAll(string template = "t") =>
        DocumentFixtures.Matcher(nameRegex: ".*", template: template);

    // --- Every adjacent tier pair ---

    [Fact]
    public void Apply_WhenDefaultsAuthorsAField_ThenItBeatsTheBuiltInDefault()
    {
        // Tier 1 vs 2, the pre-M6 behaviour, asserted here as the merge's floor.
        var attribute = ResolveSingle(DocumentFixtures.Document(
            [DocumentFixtures.Nominal("a", 0, ["x"])],
            defaults: new DefaultsSection(null, MissingPolicy.AsAttribute, null, null, null, null)));

        Assert.Equal(MissingPolicy.AsAttribute, attribute.MissingPolicy);
    }

    [Fact]
    public void Apply_WhenAMatcherTemplateAuthorsAField_ThenItBeatsDefaults()
    {
        // Tier 2 vs 3.
        var attribute = ResolveSingle(With(
            DocumentFixtures.Nominal("a", 0, ["x"]),
            [DocumentFixtures.Template("t", missingPolicy: MissingPolicy.Skip)],
            [MatchAll()],
            defaults: new DefaultsSection(null, MissingPolicy.AsAttribute, null, null, null, null)));

        Assert.Equal(MissingPolicy.Skip, attribute.MissingPolicy);
    }

    [Fact]
    public void Apply_WhenTwoMatchingTemplatesAuthorAField_ThenTheLastDeclaredWins()
    {
        // Tier 3 internal ordering: field-wise LAST-author-wins across matchers, in
        // declaration order (§9.2). The diagnostic consequence — the defeated earlier
        // matcher is fully shadowed and the winner is silent — is asserted at the
        // resolver level in SpecResolverTests, since it is a diagnostic contract rather
        // than a merge one.
        var attribute = ResolveSingle(With(
            DocumentFixtures.Nominal("a", 0, ["x"]),
            [
                DocumentFixtures.Template("first", missingPolicy: MissingPolicy.AsAttribute),
                DocumentFixtures.Template("second", missingPolicy: MissingPolicy.Skip),
            ],
            [MatchAll("first"), MatchAll("second")]));

        Assert.Equal(MissingPolicy.Skip, attribute.MissingPolicy);
    }

    [Fact]
    public void Apply_WhenTheNamedTemplateAuthorsAField_ThenItBeatsEveryMatcher()
    {
        // Tier 3 vs 4 — §9.2's correction of the old four-term shorthand: a DIRECT
        // reference is more specific than a pattern, so it wins.
        var attribute = ResolveSingle(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), template: "named",
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]),
            [
                DocumentFixtures.Template("named", missingPolicy: MissingPolicy.AsAttribute),
                DocumentFixtures.Template("matched", missingPolicy: MissingPolicy.Skip),
            ],
            [MatchAll("matched")]));

        Assert.Equal(MissingPolicy.AsAttribute, attribute.MissingPolicy);
    }

    [Fact]
    public void Apply_WhenTheAttributeAuthorsAField_ThenItBeatsTheNamedTemplate()
    {
        // Tier 4 vs 5.
        var attribute = ResolveSingle(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), template: "named",
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["x"], missingPolicy: MissingPolicy.Skip),
            [DocumentFixtures.Template("named", missingPolicy: MissingPolicy.AsAttribute)],
            []));

        Assert.Equal(MissingPolicy.Skip, attribute.MissingPolicy);
    }

    // --- Layering, not selection ---

    [Fact]
    public void Apply_WhenTwoMatchingTemplatesAuthorDifferentFields_ThenBothContribute()
    {
        // §9.2 example 1 and the reason layering (not whole-template selection) is the
        // contract: a scaling template and a policy template COMPOSE into a valid
        // two-column context. Under whole-template selection this would instead fail
        // AttributeScalingMissing, which is exactly the divergence D-114 settled.
        var attribute = ResolveSingle(With(
            Bare(),
            [
                DocumentFixtures.Template("yn", discretizer: new IdentityDiscretizerSection(),
                    scale: new DichotomicScaleSection("y"), declaredDomain: ["y", "n"]),
                DocumentFixtures.Template("miss", missingPolicy: MissingPolicy.AsAttribute),
            ],
            [MatchAll("yn"), MatchAll("miss")]));

        Assert.IsType<IdentityDiscretizer>(attribute.Discretizer);
        Assert.IsType<DichotomicScale>(attribute.Scale);
        Assert.Equal(["y", "n"], attribute.DeclaredDomain);
        Assert.Equal(MissingPolicy.AsAttribute, attribute.MissingPolicy);
    }

    [Fact]
    public void Apply_WhenALaterTemplateOmitsAField_ThenItNeverErasesAnEarlierValue()
    {
        // §9.2: omission INHERITS. No presence state can express "erase", and letting it
        // would make declaration order destructive.
        var attribute = ResolveSingle(With(
            DocumentFixtures.Nominal("a", 0, ["x"]),
            [
                DocumentFixtures.Template("first", missingPolicy: MissingPolicy.AsAttribute),
                DocumentFixtures.Template("second", unknownValuePolicy: UnknownValuePolicy.Skip),
            ],
            [MatchAll("first"), MatchAll("second")]));

        Assert.Equal(MissingPolicy.AsAttribute, attribute.MissingPolicy);
        Assert.Equal(UnknownValuePolicy.Skip, attribute.UnknownValuePolicy);
    }

    // --- Presence, not value, drives the override ---

    [Fact]
    public void Apply_WhenAHigherTierAuthorsFalse_ThenItOverridesAnEarlierTrue()
    {
        // An explicit `false` is a VALUE, and presence is what the merge reads — so it
        // overrides, exactly as a `true` would.
        var attribute = ResolveSingle(With(
            Bare(),
            [
                DocumentFixtures.Template("on", include: true, discretizer: new IdentityDiscretizerSection(),
                    scale: new NominalScaleSection(), declaredDomain: ["x"]),
                DocumentFixtures.Template("off", include: false),
            ],
            [MatchAll("on"), MatchAll("off")]));

        Assert.False(attribute.Include);
    }

    [Fact]
    public void Apply_WhenAHigherTierAuthorsAnEmptyDomain_ThenItOverridesAPopulatedOne()
    {
        // §10.3's authored `[]` is a presence state distinct from omission (D-071): it
        // overrides the earlier domain, and then RESOLVES as absent (calibrated later) —
        // which is why the resolved domain is empty rather than ["x"].
        var attribute = ResolveSingle(With(
            Bare(),
            [
                DocumentFixtures.Template("a", discretizer: new IdentityDiscretizerSection(),
                    scale: new NominalScaleSection(), declaredDomain: ["x"]),
                DocumentFixtures.Template("b", declaredDomain: []),
            ],
            [MatchAll("a"), MatchAll("b")]));

        Assert.Empty(attribute.DeclaredDomain);
    }

    [Fact]
    public void Apply_WhenAHigherTierAuthorsItsOwnDefaultValue_ThenItStillOverrides()
    {
        // Authored-equals-default still overrides: the merge cannot see values, only
        // whether a field was authored. `skip` IS the hard default, and authoring it
        // must beat a lower tier's `as_attribute`.
        var attribute = ResolveSingle(With(
            DocumentFixtures.Nominal("a", 0, ["x"]),
            [
                DocumentFixtures.Template("first", missingPolicy: MissingPolicy.AsAttribute),
                DocumentFixtures.Template("second", missingPolicy: MissingPolicy.Skip),
            ],
            [MatchAll("first"), MatchAll("second")]));

        Assert.Equal(MissingPolicy.Skip, attribute.MissingPolicy);
    }

    // --- Compound fields are whole values ---

    [Fact]
    public void Apply_WhenAnExplicitScaleWinsOverATemplateScale_ThenNoHybridIsComposed()
    {
        // §9.2 example 3: `scale` is replaced ENTIRE, never deep-merged. The explicit
        // scale authors direction only, so the template's `order` must NOT survive into
        // it — and the resulting orderless value-bin ordinal is then a legitimate
        // OrdinalOrderMissing at plan, not a silently-repaired hybrid.
        var attribute = ResolveSingle(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(),
                scale: new OrdinalScaleSection(OrdinalDirection.Le, null, null, null),
                declaredDomain: ["p", "q"]),
            [DocumentFixtures.Template("t",
                scale: new OrdinalScaleSection(OrdinalDirection.Ge, OrdinalBoundary.Strict, ["p", "q"], null))],
            [MatchAll()]));

        var ordinal = Assert.IsType<OrdinalScale>(attribute.Scale);
        Assert.Equal(OrdinalDirection.Le, ordinal.Direction);
        Assert.Null(ordinal.Order);
        // The template's boundary is gone too — the whole value lost, not just `order`.
        Assert.Equal(OrdinalBoundary.Inclusive, ordinal.Boundary);
    }

    [Fact]
    public void Apply_WhenValueLabelsAreReplaced_ThenNoMapEntriesAreMerged()
    {
        // A compound map is a whole value: the winning template's labels replace the
        // loser's entirely, rather than unioning per key.
        var attribute = ResolveSingle(With(
            Bare(),
            [
                DocumentFixtures.Template("a", discretizer: new IdentityDiscretizerSection(),
                    scale: new NominalScaleSection(), declaredDomain: ["x", "y"],
                    valueLabels: new Dictionary<string, string> { ["x"] = "ex" }),
                DocumentFixtures.Template("b", valueLabels: new Dictionary<string, string> { ["y"] = "why" }),
            ],
            [MatchAll("a"), MatchAll("b")]));

        Assert.Equal(new Dictionary<string, string> { ["y"] = "why" }, attribute.ValueLabels);
    }

    [Fact]
    public void Apply_WhenDiscretizerAndDomainComeFromDifferentTemplates_ThenEachIsWholeValued()
    {
        var attribute = ResolveSingle(With(
            Bare(),
            [
                DocumentFixtures.Template("shape", discretizer: new IdentityDiscretizerSection(),
                    scale: new NominalScaleSection(), declaredDomain: ["x"]),
                DocumentFixtures.Template("domain", declaredDomain: ["p", "q"]),
            ],
            [MatchAll("shape"), MatchAll("domain")]));

        Assert.IsType<IdentityDiscretizer>(attribute.Discretizer);
        Assert.Equal(["p", "q"], attribute.DeclaredDomain);
    }

    // --- Authored provenance: a winning template field counts as explicitly authored ---

    [Fact]
    public void Apply_WhenATemplateSuppliesAStraddlingBoundary_ThenOrdinalOverCutsRejects()
    {
        // D-114's provenance rule, and the sharpest test of it: over cut bins the
        // geometry fixes the operator, so an EXPLICITLY authored straddling boundary
        // (ge + strict) is invalid. A template-supplied one must trip the same check —
        // "configuration applied through a template behaves as though written on the
        // attribute" (§12.3).
        var result = Resolve(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: new ManualCutsDiscretizerSection([30d], null)),
            [DocumentFixtures.Template("t",
                scale: new OrdinalScaleSection(OrdinalDirection.Ge, OrdinalBoundary.Strict, null, null))],
            [MatchAll()]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts);
    }

    [Fact]
    public void Apply_WhenDefaultsSuppliesTheSameBoundary_ThenItStaysDefaultedAndSilent()
    {
        // The other half of the same contract, and why the merge must NOT materialize
        // [defaults] into the effective section: a boundary filled from
        // [defaults].ordinal_boundary AFTER the winning scale is selected keeps DEFAULTED
        // provenance, never selects the operator over cut bins, and never trips the check
        // (§6/§12.3/D-060(c)).
        var result = Resolve(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: new ManualCutsDiscretizerSection([30d], null)),
            [DocumentFixtures.Template("t", scale: new OrdinalScaleSection(OrdinalDirection.Ge, null, null, null))],
            [MatchAll()],
            defaults: new DefaultsSection(null, null, null, null, null, OrdinalBoundary.Strict)));

        Assert.True(result.TryGetValue(out var spec), Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts);

        // The default still FILLED — it is defaulted, not ignored.
        var ordinal = Assert.IsType<OrdinalScale>(Assert.Single(spec.Attributes).Scale);
        Assert.Equal(OrdinalBoundary.Strict, ordinal.Boundary);
    }

    [Fact]
    public void Apply_WhenOrdinalDefaultsFill_ThenTheyFillAfterTheWinningScale()
    {
        // Ordering matters: the direction/boundary defaults fill the scale the merge
        // SELECTED, not some earlier candidate. Here the winning (template) scale omits
        // both, so both come from [defaults].
        var attribute = ResolveSingle(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), declaredDomain: ["p", "q"]),
            [DocumentFixtures.Template("t", scale: new OrdinalScaleSection(null, null, ["p", "q"], null))],
            [MatchAll()],
            defaults: new DefaultsSection(null, null, null, null, OrdinalDirection.Le, OrdinalBoundary.Strict)));

        var ordinal = Assert.IsType<OrdinalScale>(attribute.Scale);
        Assert.Equal(OrdinalDirection.Le, ordinal.Direction);
        Assert.Equal(OrdinalBoundary.Strict, ordinal.Boundary);
        Assert.Equal(["p", "q"], ordinal.Order);
    }

    // --- Effective value type is derived AFTER application ---

    [Fact]
    public void Apply_WhenATemplateSuppliesANumericCutDiscretizer_ThenRestrictToIsTypedByIt()
    {
        // D-061 over the EFFECTIVE discretizer (D-121): the attribute authors a bare-string
        // restrict_to and no discretizer; the template supplies manual_cuts, which is
        // number-fixing — so the string entry must be rejected exactly as it would be if
        // the discretizer had been written on the attribute. This is the case that fails
        // if value typing is computed before application rather than after.
        var result = Resolve(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), restrictTo: [new RestrictToValue("x")]),
            [DocumentFixtures.Template("t",
                discretizer: new ManualCutsDiscretizerSection([30d], null), scale: new NominalScaleSection())],
            [MatchAll()]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.RestrictToNumericEntryRequired);
    }

    [Fact]
    public void Apply_WhenTheFlatEquivalentIsWritten_ThenItProducesTheSameDiagnostic()
    {
        // The other side of the equivalence, built independently: writing the same
        // discretizer directly on the attribute must produce the same condition. Proving
        // both halves is what makes the claim "as though written on the attribute"
        // rather than merely "also fails".
        var result = Resolve(DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: new ManualCutsDiscretizerSection([30d], null), scale: new NominalScaleSection(),
                restrictTo: [new RestrictToValue("x")])]));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.RestrictToNumericEntryRequired);
    }

    // --- Excluded attributes: application still happens, shaping stays dormant ---

    [Fact]
    public void Apply_WhenTheAttributeIsExcluded_ThenShapingIsDormantButRestrictToIsLive()
    {
        // §9.2/D-049: application covers EVERY attribute. On an excluded one the emitted
        // shaping a template supplies is retained-but-dormant (no scaling validation
        // fires, and no discretizer/scale is resolved), while a template-supplied
        // restrict_to is live — the filter-only pattern (§10.4).
        var attribute = ResolveSingle(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), include: false),
            [DocumentFixtures.Template("t", restrictTo: [new RestrictToValue("keep")])],
            [MatchAll()]));

        Assert.False(attribute.Include);
        Assert.Null(attribute.Discretizer);
        Assert.Null(attribute.Scale);
        Assert.Equal([new RestrictToValue("keep")], attribute.RestrictTo);
    }

    [Fact]
    public void Apply_WhenAnExcludedAttributeGetsNoScaling_ThenNoScalingErrorFires()
    {
        // Dormancy proven negatively too: an excluded attribute with neither its own nor
        // a template's discretizer/scale resolves cleanly (§10.9).
        var result = Resolve(With(
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0), include: false),
            [DocumentFixtures.Template("t", missingPolicy: MissingPolicy.AsAttribute)],
            [MatchAll()]));

        Assert.True(result.TryGetValue(out _), Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.AttributeScalingMissing);
    }

    // --- Naming fields participate as ordinary compounds ---

    [Fact]
    public void Apply_WhenATemplateAuthorsAFormat_ThenItBeatsDefaults()
    {
        var attribute = ResolveSingle(With(
            DocumentFixtures.Nominal("a", 0, ["x"]),
            [DocumentFixtures.Template("t", formalAttributeFormat: "{value}")],
            [MatchAll()],
            defaults: new DefaultsSection(null, null, null, null, null, null) { FormalAttributeFormat = "{name}" }));

        Assert.Equal("{value}", attribute.NameFormat?.Text);
    }

    [Fact]
    public void Apply_WhenTheAttributeAuthorsNaming_ThenItBeatsTheTemplate()
    {
        var attribute = ResolveSingle(With(
            DocumentFixtures.Nominal("a", 0, ["x"]) with
            {
                DisplayName = "Explicit",
                FormalAttributeFormat = "{display_name}",
            },
            [DocumentFixtures.Template("t", displayName: "FromTemplate", formalAttributeFormat: "{value}")],
            [MatchAll()]));

        Assert.Equal("Explicit", attribute.DisplayName);
        Assert.Equal("{display_name}", attribute.NameFormat?.Text);
    }

    [Fact]
    public void Apply_WhenOnlyATemplateAuthorsNaming_ThenTheAttributeInheritsIt()
    {
        // Template naming was carried-but-inert at Slice A; application is what makes it
        // live (D-120/D-121).
        var attribute = ResolveSingle(With(
            DocumentFixtures.Nominal("a", 0, ["x"]),
            [DocumentFixtures.Template("t", displayName: "Alpha", formalAttributeFormat: "{display_name}-{value}")],
            [MatchAll()]));

        Assert.Equal("Alpha", attribute.DisplayName);
        Assert.Equal("{display_name}-{value}", attribute.NameFormat?.Text);
    }

    // --- Granularity: per effective attribute, never per template ---

    [Fact]
    public void Apply_WhenOneInvalidTemplateReachesThreeAttributes_ThenEachAttributeReports()
    {
        // §16.4/D-116: an effective attribute may draw on several templates plus higher
        // tiers, so the ATTRIBUTE is the only sound owner — one diagnostic per affected
        // effective attribute, in attribute declaration order, never one per template.
        var document = DocumentFixtures.Document(
            [Bare("a"), Bare("b"), Bare("c")],
            templates: [DocumentFixtures.Template("t", discretizer: new IdentityDiscretizerSection())],
            matchers: [MatchAll()]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        var scaling = result.Diagnostics.Where(d => d.Code == DiagnosticCode.AttributeScalingMissing).ToList();
        Assert.Equal(["a", "b", "c"], scaling.Select(d => d.Location?.AttributeName));
    }
}
