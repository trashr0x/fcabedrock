using System.Reflection;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

// SpecFreezer (M7 Slice D / S4, D-122 part 10, D-123 point 9). The focused suite drives the
// library face Core-only: it builds the paired ResolvedDocument + CalibratedSpec through
// SpecResolver.Resolve + CalibratedSpec.Create over hand-built retained outcomes (no data pass),
// freezes, and asserts the four mappings, effective template/matcher preservation, the
// fully-frozen gate, canonical/idempotent output, and the three-fingerprint write flow. The
// cross-package .cxt/.dat byte equivalence lives in FcaBedrock.Golden.Tests (this project cannot
// emit output bytes).
public sealed class SpecFreezerTests
{
    // ---- API, pairing, purity, preservation ------------------------------------------------

    [Fact]
    public void Freeze_ExposesExactlyOnePublicFreezeMethodAndNoOtherKnobs()
    {
        var type = typeof(SpecFreezer);
        Assert.True(type is { IsAbstract: true, IsSealed: true, IsPublic: true }); // C# static class

        var publicMethods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();
        var freeze = Assert.Single(publicMethods);
        Assert.Equal(nameof(SpecFreezer.Freeze), freeze.Name);
        Assert.Equal(typeof(SpecDocument), freeze.ReturnType);
        Assert.Equal(
            [typeof(ResolvedDocument), typeof(CalibratedSpec)],
            freeze.GetParameters().Select(p => p.ParameterType).ToArray());

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(type.GetNestedTypes(BindingFlags.Public));
    }

    [Fact]
    public void Freeze_WhenResolvedNull_ThenThrows()
    {
        var (_, calibrated) = CalibrateCuts();
        Assert.Throws<ArgumentNullException>(() => SpecFreezer.Freeze(null!, calibrated));
    }

    [Fact]
    public void Freeze_WhenCalibratedNull_ThenThrows()
    {
        var (resolved, _) = CalibrateCuts();
        Assert.Throws<ArgumentNullException>(() => SpecFreezer.Freeze(resolved, null!));
    }

    [Fact]
    public void Freeze_WhenResolutionMismatched_ThenThrowsArgumentException()
    {
        var (resolvedA, _) = CalibrateCuts();
        // A second, independent resolution/calibration of an equivalent document: a different token,
        // so pairing must reject it even though the documents are structurally equal.
        var (_, calibratedB) = CalibrateCuts();
        Assert.NotSame(resolvedA.Resolved, calibratedB.Resolution);

        var ex = Assert.Throws<ArgumentException>(() => SpecFreezer.Freeze(resolvedA, calibratedB));
        Assert.Equal("calibrated", ex.ParamName);
    }

    [Fact]
    public void Freeze_WhenNoCalibration_ThenPreservesSemanticsOrderAndUnrelatedState()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("first", 0, ["a", "b"]),
            DocumentFixtures.Attribute("second", DocumentFixtures.Column(1), include: false),
            DocumentFixtures.Nominal("third", 2, ["x"]),
        ]);
        var resolved = Resolve(document, new SourceSchema(3));
        var calibrated = CalibratedSpec.FromFullyDeclared(resolved.Resolved);

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        // The freezer works over the resolver's immutable snapshot (ResolvedDocument.Document),
        // not the caller's original document, so unchanged sections are reference-identical to the
        // snapshot's — nothing was rebuilt.
        var snapshot = resolved.Document;
        Assert.Equal(["first", "second", "third"], frozen.Attributes.Select(a => a.Name));
        for (var i = 0; i < snapshot.Attributes.Count; i++)
        {
            Assert.Same(snapshot.Attributes[i], frozen.Attributes[i]);
        }

        Assert.Same(snapshot.Binding, frozen.Binding);
        Assert.Same(snapshot.Spec, frozen.Spec);
        Assert.Same(snapshot.Provenance, frozen.Provenance);
        Assert.Same(snapshot.Output, frozen.Output);
        Assert.Same(snapshot.Defaults, frozen.Defaults);
    }

    [Fact]
    public void Freeze_DoesNotMutateEitherInput()
    {
        var document = CutsDocument();
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new CalibratedCuts("score", [25, 50, 75]));

        // Snapshot the observable input state.
        var originalDiscretizer = document.Attributes[0].Discretizer;
        var originalOutcomeCount = calibrated.Calibrations.Count;
        var originalEffectiveDiscretizer = calibrated.Spec.Attributes[0].Discretizer;

        _ = SpecFreezer.Freeze(resolved, calibrated);

        // Freeze takes no source and is synchronous, so it can perform no data read; and both inputs
        // are immutable records, so nothing observable moved.
        Assert.Same(originalDiscretizer, document.Attributes[0].Discretizer);
        Assert.IsType<EqualFrequencyDiscretizerSection>(document.Attributes[0].Discretizer);
        Assert.Equal(originalOutcomeCount, calibrated.Calibrations.Count);
        Assert.Same(originalEffectiveDiscretizer, calibrated.Spec.Attributes[0].Discretizer);
    }

    [Fact]
    public void Freeze_ReturnedCollectionsAreImmutable()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection()),
        ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new ObservedDomain("color", ["red", "green"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        // Not a caller-mutable list: the returned graph exposes no new mutable backing state.
        Assert.IsNotType<List<string>>(frozen.Attributes[0].DeclaredDomain);
    }

    // ---- The four mappings -----------------------------------------------------------------

    [Fact]
    public void Freeze_WhenCalibratedCuts_ThenManualCutsWithOmittedEnds()
    {
        var document = CutsDocument();
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new CalibratedCuts("score", [25, 50, 75]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        var manual = Assert.IsType<ManualCutsDiscretizerSection>(frozen.Attributes[0].Discretizer);
        Assert.Equal([25.0, 50.0, 75.0], manual.Cuts!);
        Assert.Null(manual.Ends); // open-ended default applies; the writer omits `ends`
        // The scale (unrelated) is carried verbatim.
        Assert.Same(document.Attributes[0].Scale, frozen.Attributes[0].Scale);
    }

    [Fact]
    public void Freeze_WhenObservedDomain_ThenExplicitOrderedDomain()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection()),
        ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new ObservedDomain("color", ["red", "green", "blue"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(["red", "green", "blue"], frozen.Attributes[0].DeclaredDomain!);
    }

    [Fact]
    public void Freeze_WhenObservedDomainEmpty_ThenAuthoredEmptyArray()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection()),
        ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new ObservedDomain("color", []));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        var domain = frozen.Attributes[0].DeclaredDomain;
        Assert.NotNull(domain);            // authored [], never re-omitted
        Assert.Empty(domain);
    }

    // An omitted domain plus include calibrates to an ObservedDomain outcome (the
    // omitted-domain branch wins), so the freeze must also fold include → warn — read from the
    // EFFECTIVE policy, whatever tier supplied it — or the frozen attribute stays data-dependent.

    [Fact]
    public void Freeze_WhenOmittedDomainWithExplicitInclude_ThenObservedDomainAndWarnAndFullyFrozen()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                unknownValuePolicy: UnknownValuePolicy.Include),
        ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new ObservedDomain("color", ["red", "blue"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(["red", "blue"], frozen.Attributes[0].DeclaredDomain!);
        Assert.Equal(UnknownValuePolicy.Warn, frozen.Attributes[0].UnknownValuePolicy);
        AssertFullyFrozen(frozen, new SourceSchema(1));
    }

    [Fact]
    public void Freeze_WhenOmittedDomainWithIncludeFromDefaults_ThenFoldedToWarnAndFullyFrozen()
    {
        // include arrives from [defaults], so section.UnknownValuePolicy is null — the fold must read
        // the effective policy, not the raw section field.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection())],
            defaults: new DefaultsSection(null, null, UnknownValuePolicy.Include, null, null, null));
        var resolved = Resolve(document, new SourceSchema(1));
        Assert.Null(document.Attributes[0].UnknownValuePolicy); // the raw section carries no policy
        var calibrated = Create(resolved, new ObservedDomain("color", ["red", "blue"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(UnknownValuePolicy.Warn, frozen.Attributes[0].UnknownValuePolicy);
        AssertFullyFrozen(frozen, new SourceSchema(1));
    }

    [Fact]
    public void Freeze_WhenOmittedDomainWithIncludeFromTemplate_ThenFoldedToWarnAndFullyFrozen()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), template: "t")],
            templates: [DocumentFixtures.Template("t", unknownValuePolicy: UnknownValuePolicy.Include)]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new ObservedDomain("color", ["red", "blue"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(UnknownValuePolicy.Warn, frozen.Attributes[0].UnknownValuePolicy);
        AssertFullyFrozen(frozen, new SourceSchema(1));
    }

    [Fact]
    public void Freeze_WhenObservedDomainWithoutInclude_ThenPolicyUnchanged()
    {
        // A non-include effective policy is preserved: the fold applies only to include.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                unknownValuePolicy: UnknownValuePolicy.Skip),
        ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new ObservedDomain("color", ["red"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(UnknownValuePolicy.Skip, frozen.Attributes[0].UnknownValuePolicy);
    }

    [Fact]
    public void Freeze_WhenIncludeAdditions_ThenPrefixPlusAdditionsOnceAndWarn()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["red"], unknownValuePolicy: UnknownValuePolicy.Include,
                missingPolicy: MissingPolicy.AsAttribute),
        ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new IncludeAdditions("color", ["green", "blue"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(["red", "green", "blue"], frozen.Attributes[0].DeclaredDomain!); // once, not doubled
        Assert.Equal(UnknownValuePolicy.Warn, frozen.Attributes[0].UnknownValuePolicy);
        Assert.Equal(MissingPolicy.AsAttribute, frozen.Attributes[0].MissingPolicy); // unrelated field preserved
    }

    [Fact]
    public void Freeze_WhenIncludeAdditionsEmpty_ThenPrefixUnchangedAndWarn()
    {
        // A legitimate zero-additions include outcome: the effective domain is the prefix alone,
        // and the policy is still rewritten to warn.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["red", "green"], unknownValuePolicy: UnknownValuePolicy.Include),
        ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new IncludeAdditions("color", []));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(["red", "green"], frozen.Attributes[0].DeclaredDomain!);
        Assert.Equal(UnknownValuePolicy.Warn, frozen.Attributes[0].UnknownValuePolicy);
    }

    [Fact]
    public void Freeze_WhenPassthroughBins_ThenGroupsPlusSingletonsAndSkip()
    {
        var document = PassthroughDocument(
            new ValueGroupSection("A", ["a1", "a2"], null));
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new PassthroughBins("cat", ["x", "y"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        var vg = Assert.IsType<ValueGroupsDiscretizerSection>(frozen.Attributes[0].Discretizer);
        Assert.Equal(ValueGroupsUnmatched.Skip, vg.Unmatched);
        Assert.Collection(vg.Groups!,
            g => AssertGroup(g, "A", ["a1", "a2"], null),
            g => AssertGroup(g, "x", ["x"], null),
            g => AssertGroup(g, "y", ["y"], null));
    }

    [Fact]
    public void Freeze_WhenPassthroughBinsEmpty_ThenGroupsUnchangedAndSkip()
    {
        // A legitimate zero-discovery passthrough outcome: no singleton groups are appended, and
        // the pre-existing groups are preserved with unmatched rewritten to skip.
        var document = PassthroughDocument(new ValueGroupSection("A", ["a1"], null));
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new PassthroughBins("cat", []));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        var vg = Assert.IsType<ValueGroupsDiscretizerSection>(frozen.Attributes[0].Discretizer);
        Assert.Equal(ValueGroupsUnmatched.Skip, vg.Unmatched);
        var group = Assert.Single(vg.Groups!);
        AssertGroup(group, "A", ["a1"], null);
    }

    [Fact]
    public void Freeze_WhenCombined_ThenAllOutcomesAppliedWithoutReordering()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("score", DocumentFixtures.Column(0),
                discretizer: new EqualFrequencyDiscretizerSection(4, null, null), scale: new NominalScaleSection()),
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(1),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection()),
            DocumentFixtures.Attribute("tag", DocumentFixtures.Column(2),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["keep"], unknownValuePolicy: UnknownValuePolicy.Include),
            DocumentFixtures.Attribute("cat", DocumentFixtures.Column(3),
                discretizer: new ValueGroupsDiscretizerSection([new ValueGroupSection("A", ["a1"], null)], ValueGroupsUnmatched.Passthrough),
                scale: new NominalScaleSection()),
        ]);
        var resolved = Resolve(document, new SourceSchema(4));
        var calibrated = Create(resolved,
            new CalibratedCuts("score", [10, 20, 30]),
            new ObservedDomain("color", ["red"]),
            new IncludeAdditions("tag", ["extra"]),
            new PassthroughBins("cat", ["z"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(["score", "color", "tag", "cat"], frozen.Attributes.Select(a => a.Name));
        Assert.IsType<ManualCutsDiscretizerSection>(frozen.Attributes[0].Discretizer);
        Assert.Equal(["red"], frozen.Attributes[1].DeclaredDomain!);
        Assert.Equal(["keep", "extra"], frozen.Attributes[2].DeclaredDomain!);
        var vg = Assert.IsType<ValueGroupsDiscretizerSection>(frozen.Attributes[3].Discretizer);
        Assert.Equal(["A", "z"], vg.Groups!.Select(g => g.Label));
    }

    // ---- Templates, matchers, presence -----------------------------------------------------

    [Fact]
    public void Freeze_WhenIncludeDomainFromTemplate_ThenRedBlueOnce()
    {
        // The consensus case: a template supplies declared_domain = ["red"], the attribute uses
        // unknown = "include", calibration observes "blue" — the frozen explicit domain is
        // ["red", "blue"] exactly once (D-123 point 9). The base prefix comes from the template.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("color", DocumentFixtures.Column(0), template: "t", scale: new NominalScaleSection())],
            templates:
            [
                DocumentFixtures.Template("t", discretizer: new IdentityDiscretizerSection(),
                    declaredDomain: ["red"], unknownValuePolicy: UnknownValuePolicy.Include),
            ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new IncludeAdditions("color", ["blue"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Equal(["red", "blue"], frozen.Attributes[0].DeclaredDomain!);
        Assert.Equal(UnknownValuePolicy.Warn, frozen.Attributes[0].UnknownValuePolicy);
        Assert.Single(frozen.Templates); // the template is retained, not stripped
    }

    [Fact]
    public void Freeze_WhenPassthroughGroupsFromTemplate_ThenTemplateGroupsPreservedThenSingletons()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("cat", DocumentFixtures.Column(0), template: "t", scale: new NominalScaleSection())],
            templates:
            [
                DocumentFixtures.Template("t",
                    discretizer: new ValueGroupsDiscretizerSection([new ValueGroupSection("A", ["a1"], null)], ValueGroupsUnmatched.Passthrough)),
            ]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new PassthroughBins("cat", ["z"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        var vg = Assert.IsType<ValueGroupsDiscretizerSection>(frozen.Attributes[0].Discretizer);
        Assert.Equal(ValueGroupsUnmatched.Skip, vg.Unmatched);
        Assert.Collection(vg.Groups!,
            g => AssertGroup(g, "A", ["a1"], null),
            g => AssertGroup(g, "z", ["z"], null));
    }

    [Fact]
    public void Freeze_WhenPassthroughGroupPresence_ThenOmittedVersusEmptyValuesPreserved()
    {
        var document = PassthroughDocument(
            new ValueGroupSection("PatternOnly", null, "^p"),        // values omitted
            new ValueGroupSection("EmptyPlusPattern", [], "^q"));    // values = [] (authored)
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new PassthroughBins("cat", ["r"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        var vg = Assert.IsType<ValueGroupsDiscretizerSection>(frozen.Attributes[0].Discretizer);
        Assert.Collection(vg.Groups!,
            g => AssertGroup(g, "PatternOnly", null, "^p"),          // still omitted
            g => AssertGroup(g, "EmptyPlusPattern", [], "^q"),       // still explicit []
            g => AssertGroup(g, "r", ["r"], null));
    }

    [Fact]
    public void Freeze_WhenMatcherSuppliesDiscretizer_ThenFrozenOverrideShadowsMatcherWithoutSemanticChange()
    {
        // A matcher whose only authored field is a passthrough value_groups discretizer. Freezing
        // writes an explicit value_groups(skip) discretizer (tier 5) that overrides the matcher's,
        // so on re-resolve the matcher is fully shadowed — a permitted warning that changes no
        // semantics (D-123 point 9) — and the frozen document is fully frozen.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("cat", DocumentFixtures.Column(0), scale: new NominalScaleSection())],
            templates:
            [
                DocumentFixtures.Template("t",
                    discretizer: new ValueGroupsDiscretizerSection([new ValueGroupSection("A", ["a1"], null)], ValueGroupsUnmatched.Passthrough)),
            ],
            matchers: [DocumentFixtures.Matcher(nameRegex: "cat", template: "t")]);
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new PassthroughBins("cat", ["z"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Single(frozen.Matchers); // matcher retained
        var reResolved = SpecResolver.Resolve(frozen, new SourceSchema(1));
        Assert.True(reResolved.TryGetValue(out var reResolvedDoc), Describe(reResolved.Diagnostics));
        Assert.False(CalibratedSpec.RequiresData(reResolvedDoc.Resolved.Spec)); // fully frozen
        Assert.Contains(reResolved.Diagnostics, d => d.Code == DiagnosticCode.MatcherFullyShadowed);
        Assert.DoesNotContain(reResolved.Diagnostics,
            d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
    }

    [Theory]
    [InlineData(UnknownValuePolicy.Warn, MissingPolicy.Skip)]
    [InlineData(UnknownValuePolicy.Include, MissingPolicy.Skip)]
    [InlineData(UnknownValuePolicy.Warn, MissingPolicy.AsAttribute)]
    [InlineData(UnknownValuePolicy.Include, MissingPolicy.AsAttribute)]
    public void Freeze_WhenAuthoredEmptyDomainCombinations_ThenFrozenFullyFrozenAndPreserved(
        UnknownValuePolicy unknown, MissingPolicy missing)
    {
        // An authored declared_domain = [] combined with include and/or missing = "as_attribute".
        // Under include the outcome is IncludeAdditions (possibly zero); otherwise the [] is already
        // complete and there is no outcome. Either way the frozen document is fully frozen and the
        // missing policy is preserved.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("color", DocumentFixtures.Column(0),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: [], unknownValuePolicy: unknown, missingPolicy: missing),
        ]);
        var resolved = Resolve(document, new SourceSchema(1));

        SpecDocument frozen;
        if (unknown == UnknownValuePolicy.Include)
        {
            var calibrated = Create(resolved, new IncludeAdditions("color", ["obs"]));
            frozen = SpecFreezer.Freeze(resolved, calibrated);
            Assert.Equal(["obs"], frozen.Attributes[0].DeclaredDomain!); // [] prefix + additions
            Assert.Equal(UnknownValuePolicy.Warn, frozen.Attributes[0].UnknownValuePolicy);
        }
        else
        {
            Assert.False(CalibratedSpec.RequiresData(resolved.Resolved.Spec)); // authored [] is complete
            frozen = SpecFreezer.Freeze(resolved, CalibratedSpec.FromFullyDeclared(resolved.Resolved));
            Assert.Empty(frozen.Attributes[0].DeclaredDomain!);
        }

        Assert.Equal(missing, frozen.Attributes[0].MissingPolicy);
        AssertFullyFrozen(frozen, new SourceSchema(1));
    }

    // ---- Gate, flattening, canonical output ------------------------------------------------

    [Theory]
    [InlineData("omitted-domain")]
    [InlineData("omitted-domain-plus-include")]
    [InlineData("include")]
    [InlineData("equal-frequency")]
    [InlineData("equal-width-min-max")]
    [InlineData("equal-width-percentile")]
    [InlineData("passthrough")]
    public void Freeze_WhenDataDependentKind_ThenRecognizedBeforeAndFullyFrozenAfter(string kind)
    {
        var (document, outcome) = DataDependentCase(kind);
        var resolved = Resolve(document, new SourceSchema(1));

        Assert.True(CalibratedSpec.RequiresData(resolved.Resolved.Spec)); // recognized as data-dependent

        var frozen = SpecFreezer.Freeze(resolved, Create(resolved, outcome));

        AssertFullyFrozen(frozen, new SourceSchema(1)); // !RequiresData after re-resolution
    }

    // Builds the one-attribute document and its retained outcome for each data-dependent kind. A
    // switch statement (not expression) keeps each locally-typed value out of best-common-type
    // inference, and the [InlineData] selector stays a plain serializable string (no xUnit1045).
    private static (SpecDocument Document, AttributeCalibration Outcome) DataDependentCase(string kind)
    {
        DiscretizerSection discretizer;
        IReadOnlyList<string>? domain = null;
        UnknownValuePolicy? unknown = null;
        AttributeCalibration outcome;
        switch (kind)
        {
            case "omitted-domain":
                discretizer = new IdentityDiscretizerSection();
                outcome = new ObservedDomain("a", ["v"]);
                break;
            case "omitted-domain-plus-include":
                // Domain omitted → the observed-domain branch wins, so the outcome is ObservedDomain
                // even though include is selected; the freeze must still fold include → warn.
                discretizer = new IdentityDiscretizerSection();
                unknown = UnknownValuePolicy.Include;
                outcome = new ObservedDomain("a", ["v"]);
                break;
            case "include":
                discretizer = new IdentityDiscretizerSection();
                domain = ["seed"];
                unknown = UnknownValuePolicy.Include;
                outcome = new IncludeAdditions("a", ["v"]);
                break;
            case "equal-frequency":
                discretizer = new EqualFrequencyDiscretizerSection(4, null, null);
                outcome = new CalibratedCuts("a", [1, 2, 3]);
                break;
            case "equal-width-min-max":
                discretizer = new EqualWidthDiscretizerSection(4, EqualWidthRange.MinMax, null, null, null);
                outcome = new CalibratedCuts("a", [1, 2, 3]);
                break;
            case "equal-width-percentile":
                discretizer = new EqualWidthDiscretizerSection(4, EqualWidthRange.PercentileP1P99, null, null, null);
                outcome = new CalibratedCuts("a", [1, 2, 3]);
                break;
            case "passthrough":
                discretizer = new ValueGroupsDiscretizerSection([new ValueGroupSection("G", ["g"], null)], ValueGroupsUnmatched.Passthrough);
                outcome = new PassthroughBins("a", ["v"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("a", DocumentFixtures.Column(0),
                discretizer: discretizer, scale: new NominalScaleSection(),
                declaredDomain: domain, unknownValuePolicy: unknown),
        ]);
        return (document, outcome);
    }

    [Fact]
    public void Freeze_WhenComposedOverExtends_ThenRootExtendsFlattenedAndTemplatesRemain()
    {
        const string baseToml = """
            [spec]
            version = 1
            [binding]
            shape = "wide"
            [[template]]
            id = "t"
            discretizer = { kind = "identity" }
            """;
        const string derivedToml = """
            [spec]
            version = 1
            extends = "base.toml"
            [binding]
            shape = "wide"
            [[attribute]]
            name = "color"
            source = { kind = "column", index = 0 }
            template = "t"
            scale = { kind = "nominal" }
            """;

        var read = SpecReader.Read(derivedToml);
        Assert.True(read.TryGetValue(out var derived), Describe(read.Diagnostics));
        var composedResult = SpecComposer.Compose(derived, "derived.toml", new InlineBase("base.toml", baseToml));
        Assert.True(composedResult.TryGetValue(out var composed), Describe(composedResult.Diagnostics));
        Assert.Null(composed.Spec!.Extends); // composition already cleared it

        var resolved = Resolve(composed, new SourceSchema(1));
        var calibrated = Create(resolved, new ObservedDomain("color", ["red"]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        Assert.Null(frozen.Spec!.Extends);   // root extends flattened
        Assert.Single(frozen.Templates);      // template retained
        Assert.Equal(["red"], frozen.Attributes[0].DeclaredDomain!);
    }

    [Fact]
    public void Freeze_WhenWritten_ThenCanonicalNumbersAndOmittedEnds()
    {
        // -0 writes as 0 and 90.0 as 90; manual_cuts omits `ends`.
        var document = CutsDocument();
        var resolved = Resolve(document, new SourceSchema(1));
        // bins = 4 → bins - 1 = 3 cuts; -0 must write as 0 and 90.0 as 90.
        var calibrated = Create(resolved, new CalibratedCuts("score", [-0.0, 45.0, 90.0]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);
        var text = SpecWriter.Write(frozen);

        Assert.Contains("kind = \"manual_cuts\"", text);
        Assert.Contains("cuts = [0, 45, 90]", text);
        Assert.DoesNotContain("ends", text);
    }

    // ---- Fully-frozen fingerprint write flow -----------------------------------------------

    [Fact]
    public void Freeze_WhenFullyFrozenFingerprintFlow_ThenAllThreeStoredAndVerifyClean()
    {
        var document = CutsDocument();
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new CalibratedCuts("score", [25, 50, 75]));
        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        var (reResolved, plan) = FrozenPlan(frozen, new SourceSchema(1));
        var computed = SpecFingerprints.ComputeNative(reResolved, plan);
        var stored = WithFingerprints(frozen, computed);

        Assert.NotNull(stored.Spec!.SchemaFingerprint);
        Assert.NotNull(stored.Spec.CxtOutputFingerprint);
        Assert.NotNull(stored.Spec.DatOutputFingerprint);

        // Write → reread → recompute → verify with no stale diagnostic.
        var text = SpecWriter.Write(stored);
        var reread = SpecReader.Read(text);
        Assert.True(reread.TryGetValue(out var rereadDocument), Describe(reread.Diagnostics));
        var (rereadResolved, rereadPlan) = FrozenPlan(rereadDocument, new SourceSchema(1));
        Assert.Empty(SpecFingerprints.VerifyStored(rereadDocument, SpecFingerprints.ComputeNative(rereadResolved, rereadPlan)));
    }

    [Fact]
    public void Freeze_WhenComposedInputHadStalePins_ThenTheWriteFlowReplacesThem()
    {
        // Author intentionally wrong stored fingerprints; Freeze carries [spec] verbatim (stale
        // pins included), and the write flow recomputes and overwrites all three before it
        // serializes — so the written document verifies clean.
        var document = CutsDocument() with
        {
            Spec = new SpecSection(1, "stale-schema", "stale-cxt", "stale-dat", null, null),
        };
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new CalibratedCuts("score", [25, 50, 75]));

        var frozen = SpecFreezer.Freeze(resolved, calibrated);
        Assert.Equal("stale-schema", frozen.Spec!.SchemaFingerprint); // carried verbatim by Freeze

        var (reResolved, plan) = FrozenPlan(frozen, new SourceSchema(1));
        var computed = SpecFingerprints.ComputeNative(reResolved, plan);
        var stored = WithFingerprints(frozen, computed);

        Assert.NotEqual("stale-schema", stored.Spec!.SchemaFingerprint);
        Assert.Empty(SpecFingerprints.VerifyStored(stored, computed));
    }

    [Fact]
    public void Freeze_WhenSecondFreezeFingerprintWriteCycle_ThenByteIdentical()
    {
        var document = CutsDocument();
        var resolved = Resolve(document, new SourceSchema(1));
        var calibrated = Create(resolved, new CalibratedCuts("score", [25, 50, 75]));
        var schema = new SourceSchema(1);

        var text1 = FreezeFingerprintWrite(resolved, calibrated, schema);

        // Reread → resolve → freeze (now a no-op, fully declared) → fingerprint → write again.
        var reread = SpecReader.Read(text1);
        Assert.True(reread.TryGetValue(out var doc2), Describe(reread.Diagnostics));
        var resolved2 = Resolve(doc2, schema);
        var calibrated2 = CalibratedSpec.FromFullyDeclared(resolved2.Resolved);
        var text2 = FreezeFingerprintWrite(resolved2, calibrated2, schema);

        Assert.Equal(text1, text2);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static SpecDocument CutsDocument() =>
        DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("score", DocumentFixtures.Column(0),
                discretizer: new EqualFrequencyDiscretizerSection(4, null, null), scale: new NominalScaleSection()),
        ]);

    private static SpecDocument PassthroughDocument(params ValueGroupSection[] groups) =>
        DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("cat", DocumentFixtures.Column(0),
                discretizer: new ValueGroupsDiscretizerSection(groups, ValueGroupsUnmatched.Passthrough),
                scale: new NominalScaleSection()),
        ]);

    private static (ResolvedDocument Resolved, CalibratedSpec Calibrated) CalibrateCuts()
    {
        var document = CutsDocument();
        var resolved = Resolve(document, new SourceSchema(1));
        return (resolved, Create(resolved, new CalibratedCuts("score", [25, 50, 75])));
    }

    private static ResolvedDocument Resolve(SpecDocument document, SourceSchema schema)
    {
        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDoc), Describe(resolved.Diagnostics));
        return resolvedDoc;
    }

    private static CalibratedSpec Create(ResolvedDocument resolved, params AttributeCalibration[] outcomes)
    {
        var created = CalibratedSpec.Create(resolved.Resolved, outcomes);
        Assert.True(created.TryGetValue(out var calibrated), Describe(created.Diagnostics));
        return calibrated;
    }

    private static (ResolvedDocument Resolved, ConversionPlan Plan) FrozenPlan(SpecDocument frozen, SourceSchema schema)
    {
        var resolved = Resolve(frozen, schema);
        var planned = ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(resolved.Resolved));
        Assert.True(planned.TryGetValue(out var plan), Describe(planned.Diagnostics));
        return (resolved, plan);
    }

    private static string FreezeFingerprintWrite(ResolvedDocument resolved, CalibratedSpec calibrated, SourceSchema schema)
    {
        var frozen = SpecFreezer.Freeze(resolved, calibrated);
        var (reResolved, plan) = FrozenPlan(frozen, schema);
        var computed = SpecFingerprints.ComputeNative(reResolved, plan);
        return SpecWriter.Write(WithFingerprints(frozen, computed));
    }

    private static SpecDocument WithFingerprints(SpecDocument document, ComputedFingerprints computed) =>
        document with
        {
            Spec = document.Spec! with
            {
                SchemaFingerprint = computed.SchemaFingerprint,
                CxtOutputFingerprint = computed.CxtOutputFingerprint,
                DatOutputFingerprint = computed.DatOutputFingerprint,
            },
        };

    private static void AssertFullyFrozen(SpecDocument frozen, SourceSchema schema)
    {
        var resolved = SpecResolver.Resolve(frozen, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDoc), Describe(resolved.Diagnostics));
        Assert.False(CalibratedSpec.RequiresData(resolvedDoc.Resolved.Spec));
    }

    private static void AssertGroup(ValueGroupSection group, string label, IReadOnlyList<string>? values, string? pattern)
    {
        Assert.Equal(label, group.Label);
        if (values is null)
        {
            Assert.Null(group.Values);
        }
        else
        {
            Assert.NotNull(group.Values);
            Assert.Equal(values, group.Values);
        }

        Assert.Equal(pattern, group.Pattern);
    }

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    // A minimal single-file extends source for the flattening test.
    private sealed class InlineBase(string reference, string toml) : ISpecTextSource
    {
        public SpecSourceText? Load(string requested, string referrerKey) =>
            requested == reference ? new SpecSourceText(requested, toml) : null;
    }
}
