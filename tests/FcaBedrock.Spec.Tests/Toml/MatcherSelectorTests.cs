using System.Globalization;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// §9.2's two matcher selectors (D-115/D-121): whole-logical-name
/// <c>name_regex</c> and the inclusive, zero-based, resolved-physical
/// <c>source_index_range</c>. Selection decides which attributes a template
/// configures, so it is schema-affecting surface, not implementation freedom.
/// <para>
/// Selection is asserted through its <b>effect</b> — did the template's field
/// reach the resolved attribute — because that is the only thing selection is for,
/// and it keeps the tests independent of any internal selection structure.
/// </para>
/// </summary>
public sealed class MatcherSelectorTests
{
    // The template every case applies; `as_attribute` is a visible, order-independent
    // marker that a selector chose an attribute.
    private const MissingPolicy Marker = MissingPolicy.AsAttribute;

    private static Diagnosed<BedrockSpec> Resolve(SpecDocument document, SourceSchema? schema = null)
    {
        var resolved = SpecResolver.Resolve(document, schema);
        return resolved.TryGetValue(out var doc)
            ? Diagnosed<BedrockSpec>.Ok(doc.Resolved.Spec, resolved.Diagnostics)
            : Diagnosed<BedrockSpec>.Failed(resolved.Diagnostics);
    }

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private static SpecDocument WideDocument(MatcherSection matcher, params AttributeSection[] attributes) =>
        DocumentFixtures.Document(
            attributes,
            templates: [DocumentFixtures.Template("t", missingPolicy: Marker)],
            matchers: [matcher]);

    private static AttributeSection Nominal(string name, int index) =>
        DocumentFixtures.Nominal(name, index, ["x"]);

    // Which of the resolved attributes the matcher actually configured.
    private static IEnumerable<string> Selected(BedrockSpec spec) =>
        spec.Attributes.Where(a => a.MissingPolicy == Marker).Select(a => a.Name);

    private static IEnumerable<string> ResolveSelected(SpecDocument document, SourceSchema? schema = null)
    {
        var result = Resolve(document, schema);
        Assert.True(result.TryGetValue(out var spec), Describe(result.Diagnostics));
        return Selected(spec);
    }

    // --- name_regex: whole-name matching ---

    [Fact]
    public void NameRegex_WhenUnanchored_ThenItStillMatchesTheWholeNameOnly()
    {
        // §9.2/D-115's headline divergence from value_groups.pattern (§11.6, partial):
        // a selector is an identity test over configuration-bounded names, so an
        // UNANCHORED pattern must not match a substring. Under the partial reading
        // `feature_\d+` would also configure `xfeature_12x` — silently configuring an
        // attribute the author never named, the costlier error D-115 rejected.
        var selected = ResolveSelected(WideDocument(
            DocumentFixtures.Matcher(nameRegex: "feature_\\d+"),
            Nominal("feature_12", 0),
            Nominal("xfeature_12x", 1)));

        Assert.Equal(["feature_12"], selected);
    }

    [Fact]
    public void NameRegex_WhenAlternationIsUnanchored_ThenBothBranchesAreWholeNameBound()
    {
        // The wrapping group is load-bearing, not cosmetic: bare anchors would bind as
        // `\Aa|b\z` — "starts with a, OR ends with b" — so `ab` and `ba` would match.
        // Wrapped as `\A(?:a|b)\z` neither does.
        var selected = ResolveSelected(WideDocument(
            DocumentFixtures.Matcher(nameRegex: "a|b"),
            Nominal("a", 0), Nominal("b", 1), Nominal("ab", 2), Nominal("ba", 3)));

        Assert.Equal(["a", "b"], selected);
    }

    [Fact]
    public void NameRegex_WhenExplicitAnchorsAreAuthored_ThenTheyAreRedundantButLegal()
    {
        // §9.2: explicit anchors "remain legal but are redundant when they express that
        // same boundary" — so the wrapped form must not double-anchor into never matching.
        var selected = ResolveSelected(WideDocument(
            DocumentFixtures.Matcher(nameRegex: "^feature_\\d+$"),
            Nominal("feature_12", 0),
            Nominal("other", 1)));

        Assert.Equal(["feature_12"], selected);
    }

    [Fact]
    public void NameRegex_WhenLogicalNameAndPredicateAreCrossed_ThenOnlyTheLogicalNameSelects()
    {
        // §9.2/D-115: `name_regex` targets the complete logical attribute.name — "never a
        // source header, predicate text, display_name, or a rendered formal name".
        //
        // Deliberately CROSSED so the alternatives select DIFFERENT attributes rather than
        // the same one: attribute `logical_age` sits on predicate `physical_age`, while a
        // second attribute is *named* `physical_age`. Under the correct contract the
        // pattern `^physical_age$` selects the second; an implementation matching
        // predicates would select the first. A fixture where name and predicate agree
        // cannot tell those two implementations apart — which is exactly the gap this
        // closes.
        //
        // The display name and the rendered names are also made non-matching, so the
        // remaining two wrong targets are excluded in the same case.
        var document = DocumentFixtures.Document(
            [
                DocumentFixtures.Attribute("logical_age", new PredicateSourceSection("physical_age", null),
                    discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                    declaredDomain: ["x"]) with { DisplayName = "physical_age display" },
                DocumentFixtures.Attribute("physical_age", new PredicateSourceSection("other_predicate", null),
                    discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                    declaredDomain: ["x"]),
            ],
            binding: DocumentFixtures.TripleBinding(),
            templates: [DocumentFixtures.Template("t", missingPolicy: Marker)],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^physical_age$")]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec), Describe(result.Diagnostics));
        Assert.Equal(["physical_age"], Selected(spec));
    }

    [Fact]
    public void NameRegex_WhenLogicalNameAndBoundHeaderAreCrossed_ThenOnlyTheLogicalNameSelects()
    {
        // The wide analogue, crossed the same way: `alpha` binds by header name `beta`,
        // while a second attribute is *named* `beta` and binds by index. `^beta$` must
        // select the second — matching the bound header would select the first.
        var document = DocumentFixtures.Document(
            [
                DocumentFixtures.Attribute("alpha", DocumentFixtures.NamedColumn("beta"),
                    discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]),
                DocumentFixtures.Attribute("beta", DocumentFixtures.Column(0),
                    discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]),
            ],
            templates: [DocumentFixtures.Template("t", missingPolicy: Marker)],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^beta$")]);

        var selected = ResolveSelected(document, new SourceSchema(2, ["alpha", "beta"]));

        Assert.Equal(["beta"], selected);
    }

    [Fact]
    public void NameRegex_WhenCaseDiffers_ThenMatchingIsCaseSensitiveByDefault()
    {
        var selected = ResolveSelected(WideDocument(
            DocumentFixtures.Matcher(nameRegex: "age"),
            Nominal("age", 0), Nominal("AGE", 1)));

        Assert.Equal(["age"], selected);
    }

    [Fact]
    public void NameRegex_WhenInlineIgnoreCaseIsAuthored_ThenItIsHonored()
    {
        // §9.2: authored inline options are honored. The wrapping group must not scope
        // `(?i)` away from the rest of the pattern.
        var selected = ResolveSelected(WideDocument(
            DocumentFixtures.Matcher(nameRegex: "(?i)age"),
            Nominal("age", 0), Nominal("AGE", 1), Nominal("agex", 2)));

        Assert.Equal(["age", "AGE"], selected);
    }

    [Fact]
    public void NameRegex_WhenAmbientCultureIsTurkish_ThenMatchingIsUnaffected()
    {
        // P-12/D-115: matching is CultureInvariant, so the classic Turkish dotless-i
        // hazard cannot make the same spec select different attributes on two machines.
        // Under a culture-sensitive engine `(?i)I` would match `ı` under tr-TR.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var selected = ResolveSelected(WideDocument(
                DocumentFixtures.Matcher(nameRegex: "(?i)FILE"),
                Nominal("file", 0), Nominal("fıle", 1)));

            Assert.Equal(["file"], selected);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("(unclosed")]
    [InlineData("a{2,1}")]
    [InlineData("[z-a]")]
    public void NameRegex_WhenEmptyOrUncompilable_ThenSpecFieldInvalidAtParse(string pattern)
    {
        // §9.2/D-115: non-empty and compilable, checked at PARSE. (These four already fail
        // raw compilation; the wrapper-only case below is what proves parse judges the
        // WRAPPED form rather than the raw one.)
        var result = SpecReader.Read(
            "[[matcher]]\nmatch = { name_regex = \"" + pattern.Replace("\\", "\\\\", StringComparison.Ordinal)
            + "\" }\ntemplate = \"t\"\n");

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecFieldInvalid);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code != DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void NameRegex_WhenRawCompilableButWrappedInvalid_ThenSpecFieldInvalidAtParse()
    {
        // The discriminator for "parse judges the form that EXECUTES" (D-115/D-121).
        //
        // `(?x)a #c` compiles fine on its own: under IgnorePatternWhitespace a `#` starts
        // a comment running to end-of-line, and there is no more line. Wrapped as
        // `\A(?:(?x)a #c)\z` that same comment swallows the wrapper's own `)` — so the
        // group is never closed and compilation fails.
        //
        // Validating the RAW pattern would therefore accept this spec at parse and then
        // throw at evaluation. This test fails the moment the two construction sites
        // diverge, which is the whole reason MatcherSelectors owns both.
        const string pattern = "(?x)a #c";
        Assert.True(RawCompiles(pattern), "precondition: the pattern must compile UNWRAPPED");

        var result = SpecReader.Read(
            $"[[matcher]]\nmatch = {{ name_regex = \"{pattern}\" }}\ntemplate = \"t\"\n");

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, Assert.Single(result.Diagnostics).Code);
    }

    private static bool RawCompiles(string pattern)
    {
        try
        {
            _ = new System.Text.RegularExpressions.Regex(
                pattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                System.Text.RegularExpressions.Regex.InfiniteMatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Fact]
    public void NameRegex_WhenAttributeHasNoName_ThenItIsNeverSelected()
    {
        // An attribute with no name has no logical name to test against, and is already
        // an AttributeNameMissing Error; a catch-all pattern must not quietly configure it.
        var result = Resolve(WideDocument(
            DocumentFixtures.Matcher(nameRegex: ".*"),
            DocumentFixtures.Attribute(null, DocumentFixtures.Column(0))));

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.AttributeNameMissing);

        // The zero-match Warning names both its ordinal and its template reference (§16.4).
        var warning = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.MatcherSelectsNoAttributes);
        Assert.Contains("#1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("\"t\"", warning.Message, StringComparison.Ordinal);
    }

    // --- source_index_range: inclusive, zero-based, resolved physical index ---

    [Fact]
    public void SourceIndexRange_WhenEvaluated_ThenBothEndpointsAreInclusiveAndZeroBased()
    {
        // §9.2: inclusive on BOTH ends, over the same zero-based space as source.index.
        // Index 0 inside the range is what proves zero-based rather than one-based.
        var selected = ResolveSelected(WideDocument(
            DocumentFixtures.Matcher(sourceIndexRange: [0, 2]),
            Nominal("a", 0), Nominal("b", 1), Nominal("c", 2), Nominal("d", 3)));

        Assert.Equal(["a", "b", "c"], selected);
    }

    [Fact]
    public void SourceIndexRange_WhenSeveralAttributesShareAColumn_ThenAllOfThemMatch()
    {
        // §9.2/D-033: "EVERY declared logical attribute bound to an in-range index
        // matches", repeated bindings included — one field carrying two scalings.
        var selected = ResolveSelected(WideDocument(
            DocumentFixtures.Matcher(sourceIndexRange: [1, 1]),
            Nominal("age", 1), Nominal("age_copy", 1), Nominal("other", 0)));

        Assert.Equal(["age", "age_copy"], selected);
    }

    [Fact]
    public void SourceIndexRange_WhenTheSourceIsNameBound_ThenItParticipatesViaTheResolvedIndex()
    {
        // §9.2: the range is evaluated AFTER ordinary header/schema binding, so a
        // name-bound source participates normally — matching on the index the header
        // resolved it to, not on anything authored.
        var document = WideDocument(
            DocumentFixtures.Matcher(sourceIndexRange: [1, 1]),
            DocumentFixtures.Attribute("age", DocumentFixtures.NamedColumn("age"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]),
            DocumentFixtures.Attribute("id", DocumentFixtures.NamedColumn("id"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]));

        var selected = ResolveSelected(document, new SourceSchema(2, ["id", "age"]));

        Assert.Equal(["age"], selected);
    }

    [Fact]
    public void SourceIndexRange_WhenTheRangeOverCoversTheSource_ThenItIsLegalAndSelectsWhatExists()
    {
        // §9.2: "an endpoint beyond the source width is legal over-coverage" — the
        // Internet-Ads idiom of writing a generous range. Neither clamped nor rejected.
        var selected = ResolveSelected(
            WideDocument(DocumentFixtures.Matcher(sourceIndexRange: [0, 1553]), Nominal("a", 0), Nominal("b", 1)),
            new SourceSchema(2));

        Assert.Equal(["a", "b"], selected);
    }

    [Fact]
    public void SourceIndexRange_WhenASourceCannotBeAddressed_ThenExactlyOneBindingDiagnosticReports()
    {
        // D-115/D-121's single-emission rule: a name-bound source with no schema is the
        // ordinary SourceBindingInvalid, and "no matcher-specific duplicate condition is
        // introduced". The addressing pass owns that diagnostic, and selection consuming
        // the same table is what makes a second one impossible — this is the test that
        // fails if selection re-resolves the source.
        var document = WideDocument(
            DocumentFixtures.Matcher(sourceIndexRange: [0, 9]),
            DocumentFixtures.Attribute("age", DocumentFixtures.NamedColumn("age"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]));

        var result = Resolve(document); // no schema supplied

        Assert.False(result.TryGetValue(out _));
        var binding = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceBindingInvalid);
        Assert.Equal("age", binding.Location?.AttributeName);

        // Unaddressable ⇒ unselected, so the matcher legitimately warns that it chose nothing.
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.MatcherSelectsNoAttributes);
    }

    [Theory]
    [InlineData("[0]")]
    [InlineData("[0, 1, 2]")]
    [InlineData("[]")]
    [InlineData("[0, \"1\"]")]
    [InlineData("[0, true]")]
    [InlineData("[0.5, 1.5]")]
    [InlineData("[-1, 3]")]
    [InlineData("[5, 2]")]
    public void SourceIndexRange_WhenMalformed_ThenExactlyOneSpecFieldInvalidAtParse(string range)
    {
        // §9.2/D-115: exactly two TOML integers with 0 <= lo <= hi. Wrong arity, a
        // non-integer, a negative endpoint, and reversed endpoints each fail at parse —
        // and each reports ONCE, so a malformed range is one authoring fix, not two
        // diagnostics describing the same array.
        var result = SpecReader.Read(
            $"[[matcher]]\nmatch = {{ source_index_range = {range} }}\ntemplate = \"t\"\n");

        Assert.False(result.TryGetValue(out _));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
    }

    // --- Selector arity and the template reference (parse) ---

    [Theory]
    [InlineData("match = { name_regex = \"^a$\", source_index_range = [0, 1] }\ntemplate = \"t\"")]
    [InlineData("match = { }\ntemplate = \"t\"")]
    public void Match_WhenBothOrNeitherSelectorIsAuthored_ThenExactlyOneSpecFieldInvalid(string body)
    {
        // §9.2: exactly one selector. AND/OR semantics for two authored selectors would
        // be ambiguous, so the arity itself is the condition — reported once, rather than
        // cascading into per-selector complaints.
        var result = SpecReader.Read($"[[matcher]]\n{body}\n");

        Assert.False(result.TryGetValue(out _));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
    }

    [Fact]
    public void Matcher_WhenTemplateKeyIsMissing_ThenSpecFieldInvalidAtParse()
    {
        var result = SpecReader.Read("[[matcher]]\nmatch = { name_regex = \"^a$\" }\n");

        Assert.False(result.TryGetValue(out _));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("no template", diagnostic.Message, StringComparison.Ordinal);
    }

    // --- Shape compatibility ---

    [Fact]
    public void SourceIndexRange_WhenShapeIsTriple_ThenMatcherSelectorInvalidForShape()
    {
        // §9.2/D-115: a predicate source has no column index, so a range is incompatible
        // with triple — one Error per incompatible matcher, and resolution fails.
        var document = DocumentFixtures.Document(
            [TriplePredicate("age", "age")],
            binding: DocumentFixtures.TripleBinding(),
            templates: [DocumentFixtures.Template("t", missingPolicy: Marker)],
            matchers: [DocumentFixtures.Matcher(sourceIndexRange: [0, 4])]);

        var result = Resolve(document);

        Assert.False(result.TryGetValue(out _));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.MatcherSelectorInvalidForShape);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // §16.4: a matcher-scoped diagnostic identifies BOTH its declaration ordinal and
        // its template reference deterministically — an ordinal alone leaves the reader
        // counting matchers to find which template was involved.
        Assert.Contains("#1", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("\"t\"", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NameRegex_WhenShapeIsTriple_ThenItSelectsSeveralAttributesOverOnePredicate()
    {
        // D-115's repeated-source TRIPLE case, and the reason a regex matcher stays legal
        // under triple: ONE selector chooses two logical attributes bound to the single
        // predicate `age`, both receive the template's field, and both are retained in
        // attribute declaration order.
        var document = DocumentFixtures.Document(
            [TriplePredicate("age", "age"), TriplePredicate("age_ordinal", "age")],
            binding: DocumentFixtures.TripleBinding(),
            templates: [DocumentFixtures.Template("t", missingPolicy: Marker)],
            matchers: [DocumentFixtures.Matcher(nameRegex: "^age(_ordinal)?$")]);

        var result = Resolve(document);

        Assert.True(result.TryGetValue(out var spec), Describe(result.Diagnostics));
        Assert.Equal(["age", "age_ordinal"], spec.Attributes.Select(a => a.Name));
        Assert.Equal(["age", "age_ordinal"], Selected(spec));

        // Both really are bound to the one predicate — the repeated source, not two sources.
        Assert.All(spec.Attributes, a => Assert.Equal("age", Assert.IsType<PredicateSource>(a.Source).Predicate));
    }

    // --- Boundedness: configuration and schema only ---

    [Fact]
    public void Evaluate_WhenNoSourceIsOpen_ThenSelectionStillResolvesFromSchemaAlone()
    {
        // §9.2/D-118: matcher evaluation is a one-time configuration- and schema-bounded
        // step that reads NO data rows. Resolve has no data access at all — it takes a
        // SpecDocument and an optional SourceSchema — so resolving a range matcher
        // against a hand-built schema, with no session, stream, or file anywhere, is the
        // structural proof: there is no row to enumerate.
        var selected = ResolveSelected(
            WideDocument(DocumentFixtures.Matcher(sourceIndexRange: [0, 0]), Nominal("a", 0), Nominal("b", 1)),
            new SourceSchema(2, ["a", "b"]));

        Assert.Equal(["a"], selected);
    }

    [Fact]
    public void Evaluate_WhenRunTwice_ThenSelectionIsIdentical()
    {
        var document = WideDocument(
            DocumentFixtures.Matcher(nameRegex: "^f_\\d+$"),
            Nominal("f_1", 0), Nominal("g", 1), Nominal("f_2", 2));

        Assert.Equal(ResolveSelected(document), ResolveSelected(document));
    }

    private static AttributeSection TriplePredicate(string name, string predicate) =>
        DocumentFixtures.Attribute(name, new PredicateSourceSection(predicate, null),
            discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(), declaredDomain: ["x"]);
}
