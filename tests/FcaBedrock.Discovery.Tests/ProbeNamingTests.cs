using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The §7.1 / D-107 wide naming and binding matrix.
/// <para>
/// The matrix exists to serve one invariant: <b>no source selector is ever silently changed</b>.
/// A name may be synthesized or disambiguated; the column it reads may not. Every case below is
/// really asking the same question twice — "is the logical name §10.1-valid and unique?" and
/// "does the source still select the physical column it was discovered from?"
/// </para>
/// <para>
/// Driven through the <b>real</b> header-tolerant CSV path wherever the case is expressible as
/// bytes, not only through fabricated schemas: duplicate and blank headers were unreachable
/// before Slice B made the wide read header-tolerant, so a fabricated-schema-only suite would
/// pass while the very inputs it describes still threw at the adapter.
/// </para>
/// </summary>
public sealed class ProbeNamingTests
{
    private static (string Name, int? Index, string? ByName)[] Bindings(SpecDocument draft) =>
        [.. draft.Attributes.Select(a =>
        {
            var source = (ColumnSourceSection)a.Source!;
            return (a.Name!, source.Index, source.Name);
        })];

    private static BedrockDiagnostic? Adjustment(Diagnosed<SpecDocument> result) =>
        result.Diagnostics.Cast<BedrockDiagnostic?>()
            .FirstOrDefault(d => d!.Value.Code == DiagnosticCode.ProbeAttributeNameAdjusted);

    [Fact]
    public async Task Probe_WhenHeadersAreUniqueAndUsable_ThenAllBindByNameWithNoWarning()
    {
        var result = await ProbeFixtures.ProbeCsvAsync("age,city\n1,x\n");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [("age", null, "age"), ("city", null, "city")],
            Bindings(ProbeFixtures.Draft(result)));
    }

    [Fact]
    public async Task Probe_WhenSourceIsHeaderless_ThenSynthesizesColumnNamesWithoutWarning()
    {
        // D-111 states this carve-out explicitly: plain `column_N` synthesis is routine, not an
        // adjustment. There was no authored name to depart from, so warning would be noise on
        // every headerless probe.
        var settings = ProbeFixtures.WideSettings(hasHeader: false);
        var session = new FcaBedrock.Sources.WideCsvSession(ProbeFixtures.Bytes("a,b,c\n"), settings);

        var result = await Prober.ProbeAsync(session, settings);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [("column_0", 0, null), ("column_1", 1, null), ("column_2", 2, null)],
            Bindings(ProbeFixtures.Draft(result)));
    }

    [Fact]
    public async Task Probe_WhenHeadersDuplicate_ThenBothBindByIndexAndTheLaterIsDisambiguated()
    {
        // Neither column may bind by name: a name matching two columns is SourceBindingInvalid
        // (§10.2), so a by-name draft would fail its own resolve guarantee. The first claimant
        // keeps the header text verbatim — its NAME is untouched — and only the second is
        // adjusted, which is what the warning counts.
        var result = await ProbeFixtures.ProbeCsvAsync("a,a\n1,2\n");

        Assert.Equal([("a", 0, null), ("a#1", 1, null)], Bindings(ProbeFixtures.Draft(result)));

        var warning = Adjustment(result);
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning!.Value.Severity);
        Assert.Contains("adjusted 1 attribute name(s)", warning.Value.Message, StringComparison.Ordinal);
        Assert.Contains("a#1", warning.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenDuplicateHeadersAreNonAdjacent_ThenDisambiguationStillUsesSourceIndex()
    {
        // The ladder keys off the physical column, not the occurrence ordinal, so an intervening
        // column cannot shift the assigned names.
        var result = await ProbeFixtures.ProbeCsvAsync("a,b,a\n1,2,3\n");

        Assert.Equal(
            [("a", 0, null), ("b", null, "b"), ("a#2", 2, null)],
            Bindings(ProbeFixtures.Draft(result)));
    }

    [Fact]
    public async Task Probe_WhenHeaderCellIsBlank_ThenFallsBackToColumnNameAndWarns()
    {
        var result = await ProbeFixtures.ProbeCsvAsync("a,,c\n1,2,3\n");

        Assert.Equal(
            [("a", null, "a"), ("column_1", 1, null), ("c", null, "c")],
            Bindings(ProbeFixtures.Draft(result)));

        // A blank header IS an adjustment, unlike headerless synthesis: something was authored,
        // and the draft could not use it.
        Assert.Contains("adjusted 1 attribute name(s)", Adjustment(result)!.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenSeveralHeaderCellsAreBlank_ThenEachFallbackIsDistinct()
    {
        // Multiply-blank headers are the case Sep's own header mode rejected outright. The
        // fallback embeds the physical index, so distinctness is structural rather than a
        // collision the ladder has to resolve.
        var result = await ProbeFixtures.ProbeCsvAsync("a,,,d\n1,2,3,4\n");

        Assert.Equal(
            [("a", null, "a"), ("column_1", 1, null), ("column_2", 2, null), ("d", null, "d")],
            Bindings(ProbeFixtures.Draft(result)));
        Assert.Contains("adjusted 2 attribute name(s)", Adjustment(result)!.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenHeaderCellContainsAQuote_ThenItIsUnusableAndFallsBack()
    {
        // §10.1 excludes the TOML key-quoting character. Written as a quoted CSV field with a
        // doubled quote, so the header cell really contains one after unescape.
        var result = await ProbeFixtures.ProbeCsvAsync("\"a\"\"b\",c\n1,2\n");

        Assert.Equal(
            [("column_0", 0, null), ("c", null, "c")],
            Bindings(ProbeFixtures.Draft(result)));
        Assert.NotNull(Adjustment(result));
    }

    [Fact]
    public async Task Probe_WhenHeaderCellContainsANewline_ThenItIsUnusableAndFallsBack()
    {
        var result = await ProbeFixtures.ProbeCsvAsync("\"a\nb\",c\n1,2\n");

        Assert.Equal(
            [("column_0", 0, null), ("c", null, "c")],
            Bindings(ProbeFixtures.Draft(result)));
        Assert.NotNull(Adjustment(result));
    }

    [Fact]
    public async Task Probe_WhenHeaderCellEqualsTheMissingToken_ThenItIsAnOrdinaryUsableName()
    {
        // Header cells are metadata and are NEVER missing-normalized (§5.1 normalization is a
        // data-field rule). A header of "?" under the default token is the literal name "?",
        // which is §10.1-valid, so it binds by name like any other unique header.
        var result = await ProbeFixtures.ProbeCsvAsync("?,b\nx,y\n");

        Assert.Empty(result.Diagnostics);
        Assert.Equal([("?", null, "?"), ("b", null, "b")], Bindings(ProbeFixtures.Draft(result)));
    }

    [Fact]
    public async Task Probe_WhenAHeaderAlreadySpellsAFallbackName_ThenTheFallbackEscalatesInstead()
    {
        // The adversarial case D-107 calls out: a real header that happens to spell the name the
        // fallback would synthesize. Resolving against the COMPLETE set of logical names — the
        // by-name columns' names are reserved before any fallback is assigned — is what keeps
        // the unique usable header's own name intact.
        var result = await ProbeFixtures.ProbeCsvAsync("column_1,\n1,2\n");
        var bindings = Bindings(ProbeFixtures.Draft(result));

        Assert.Equal([("column_1", null, "column_1"), ("column_1#1", 1, null)], bindings);
        Assert.Equal(bindings.Length, bindings.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Probe_WhenAHeaderAlreadySpellsADisambiguatedName_ThenTheLadderStepsPastIt()
    {
        // "a" duplicates, so the second occurrence wants "a#1" — which column 2 already owns as
        // a real header. The ladder must step past it rather than mint a duplicate.
        var result = await ProbeFixtures.ProbeCsvAsync("a,a,a#1\n1,2,3\n");
        var bindings = Bindings(ProbeFixtures.Draft(result));

        Assert.Equal(
            [("a", 0, null), ("a#2", 1, null), ("a#1", null, "a#1")],
            bindings);
        Assert.Equal(bindings.Length, bindings.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Probe_WhenNamesAreAdjusted_ThenTheWarningIsAggregatedWithABoundedSample()
    {
        // One warning, never one per attribute: a wide source can carry thousands of columns.
        // The sample is the first three in physical order, so it is a function of the schema.
        var result = await ProbeFixtures.ProbeCsvAsync("a,,,,,\n1,2,3,4,5,6\n");

        var warnings = result.Diagnostics
            .Where(d => d.Code == DiagnosticCode.ProbeAttributeNameAdjusted)
            .ToList();

        var warning = Assert.Single(warnings);
        Assert.Contains("adjusted 5 attribute name(s)", warning.Message, StringComparison.Ordinal);
        Assert.Contains("column_1, column_2, column_3", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("column_4", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenNamesAreAdjusted_ThenTheDraftStillCarriesAtMostOneNamePerColumn()
    {
        // The whole matrix's post-condition, over an input mixing every branch: unique, blank,
        // duplicate, unusable, and collision-prone headers at once.
        var result = await ProbeFixtures.ProbeCsvAsync("keep,,dup,dup,column_1,\"q\"\"t\"\n1,2,3,4,5,6\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(6, draft.Attributes.Count);
        Assert.Equal(6, draft.Attributes.Select(a => a.Name!).Distinct(StringComparer.Ordinal).Count());
        Assert.All(draft.Attributes, a => Assert.True(AttributeNamingProbe.IsUsable(a.Name!)));

        // Selectors: a by-name column names its own header; a by-index column keeps its physical
        // position. Neither is ever repointed.
        var indexed = draft.Attributes
            .Select(a => (ColumnSourceSection)a.Source!)
            .Where(s => s.Index is not null)
            .Select(s => s.Index!.Value)
            .ToList();
        Assert.Equal(indexed.Distinct(), indexed);
    }

    [Fact]
    public async Task Probe_WhenSchemaHeaderIsShorterThanTheColumnCount_ThenTrailingColumnsAreHeaderless()
    {
        // Only reachable from a hand-built schema (the CSV adapter derives the count FROM the
        // header), but representable — so it is defined rather than an index-out-of-range.
        var session = ProbeFixtures.Fake(new SourceSchema(3, ["a"]), ["1", "2", "3"]);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings());

        Assert.Equal(
            [("a", null, "a"), ("column_1", 1, null), ("column_2", 2, null)],
            Bindings(ProbeFixtures.Draft(result)));
    }

    // §10.1 restated on the test side, so "every assigned name is a valid attribute name" is
    // checked against the rule rather than against the production predicate.
    private static class AttributeNamingProbe
    {
        public static bool IsUsable(string name) =>
            name.Length > 0 && !name.Contains('\n') && !name.Contains('\r') && !name.Contains('"');
    }
}
