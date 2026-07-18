using System.Globalization;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// Retention, truncation, and the missing-value rules (D-106/D-108).
/// <para>
/// The boundary is the point of the suite. Truncation is <b>strictly greater-than</b>: an
/// attribute whose distinct values fit the limit exactly is complete and must not be marked, and
/// only a further distinct value beyond it truncates. That asymmetry is what lets a truncated
/// draft still recover the whole schema — retained prefix plus <c>include</c> re-appends the
/// tail in first-observation order — rather than merely reporting that something was lost.
/// </para>
/// </summary>
public sealed class ProbeRetentionTests
{
    private static ProbeOptions Limit(int limit) => ProbeOptions.Create(valueRetentionLimit: limit);

    private static AttributeSection Only(Diagnosed<SpecDocument> result) =>
        Assert.Single(ProbeFixtures.Draft(result).Attributes);

    [Fact]
    public async Task Probe_WhenDistinctValuesAreBelowTheLimit_ThenAuthorsTheCompleteDomain()
    {
        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\ny\n", options: Limit(5));
        var attribute = Only(result);

        Assert.Equal(["x", "y"], attribute.DeclaredDomain);
        Assert.Null(attribute.Description);
        Assert.Null(attribute.UnknownValuePolicy);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
    }

    [Fact]
    public async Task Probe_WhenDistinctValuesEqualTheLimit_ThenIsNotTruncated()
    {
        // The exact boundary D-108 settles: equality fits. A ">=" rule here would mark a domain
        // that is complete, and would then author `include` on a schema that needs no extension.
        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\ny\nz\n", options: Limit(3));
        var attribute = Only(result);

        Assert.Equal(["x", "y", "z"], attribute.DeclaredDomain);
        Assert.Null(attribute.Description);
        Assert.Null(attribute.UnknownValuePolicy);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
        Assert.Equal(ProbeDraftExpectations.NotesFor(3, 0), ProbeFixtures.Draft(result).Provenance!.Notes);
    }

    [Fact]
    public async Task Probe_WhenOneDistinctValueBeyondTheLimit_ThenTruncatesWithPrefixIncludeAndMarker()
    {
        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\ny\nz\nw\n", options: Limit(3));
        var attribute = Only(result);

        // The retained PREFIX in first-observation order — never a sample, never a sorted set.
        Assert.Equal(["x", "y", "z"], attribute.DeclaredDomain);
        Assert.Equal(ProbeDraftExpectations.MarkerFor(3), attribute.Description);
        Assert.Equal(UnknownValuePolicy.Include, attribute.UnknownValuePolicy);
        Assert.Equal(ProbeDraftExpectations.NotesFor(3, 1), ProbeFixtures.Draft(result).Provenance!.Notes);
    }

    [Fact]
    public async Task Probe_WhenTruncated_ThenTheMarkerNeverClaimsAnExactOverLimitCount()
    {
        // Probe knows only that AT LEAST one more distinct value exists: counting the rest would
        // require the very retention the limit bounds. The marker's wording is fixed for that
        // reason, and stays identical whether one or a thousand values were dropped.
        var many = "a\n" + string.Join("\n", Enumerable.Range(0, 500).Select(i => $"v{i}")) + "\n";

        var few = await ProbeFixtures.ProbeCsvAsync("a\nx\ny\nz\nw\n", options: Limit(3));
        var lots = await ProbeFixtures.ProbeCsvAsync(many, options: Limit(3));

        Assert.Equal(Only(few).Description, Only(lots).Description);
        Assert.Equal(ProbeDraftExpectations.MarkerFor(3), Only(lots).Description);
    }

    [Fact]
    public async Task Probe_WhenTruncated_ThenTheWarningIsAggregatedAndSampled()
    {
        var csv = "a,b,c,d\n" + string.Join("\n", Enumerable.Range(0, 6).Select(i => $"a{i},b{i},c{i},d{i}")) + "\n";

        var result = await ProbeFixtures.ProbeCsvAsync(csv, options: Limit(2));
        var warning = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);

        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("truncated 4 attribute domain(s) after 2 distinct values", warning.Message, StringComparison.Ordinal);
        Assert.Contains("a, b, c", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(e.g. a, b, c, d)", warning.Message, StringComparison.Ordinal);
        Assert.Equal(ProbeDraftExpectations.NotesFor(2, 4), ProbeFixtures.Draft(result).Provenance!.Notes);
    }

    [Fact]
    public async Task Probe_WhenNothingTruncates_ThenTheNotesAreStillWritten()
    {
        // Written even at zero (D-108): a draft that omitted its notes would leave a reader
        // unable to tell a complete domain from one that merely fit.
        var draft = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync("a\nx\n"));

        Assert.Equal(ProbeDraftExpectations.NotesFor(100_000, 0), draft.Provenance!.Notes);
    }

    [Fact]
    public async Task Probe_WhenAColumnIsEntirelyMissing_ThenTheDomainIsOmittedNotEmpty()
    {
        // Omitted, not `[]`. Both resolve as "absent" (§10.3), but an authored empty list claims
        // the user declared a zero-value domain — a different statement from "not yet known".
        var result = await ProbeFixtures.ProbeCsvAsync("a,b\nx,?\ny,\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["x", "y"], draft.Attributes[0].DeclaredDomain);
        Assert.Null(draft.Attributes[1].DeclaredDomain);
        Assert.DoesNotContain("declared_domain = []", ProbeFixtures.Toml(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenSourceIsHeaderOnly_ThenAttributesAreAuthoredWithNoDomains()
    {
        // A header-only source succeeds (D-107): the attributes are real, and their emptiness is
        // a convert-time signal, not a probe failure.
        var result = await ProbeFixtures.ProbeCsvAsync("a,b\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(2, draft.Attributes.Count);
        Assert.All(draft.Attributes, a => Assert.Null(a.DeclaredDomain));
    }

    [Fact]
    public async Task Probe_WhenValuesRepeat_ThenOrderIsFirstObservationAndAccountingIsUnaffected()
    {
        // Set-based and idempotent (D-106): a repeat moves nothing and costs nothing, which is
        // what makes one record pass sufficient.
        var result = await ProbeFixtures.ProbeCsvAsync("a\nb\nc\nb\na\nc\na\n", hasHeader: false, options: Limit(3));

        Assert.Equal(["a", "b", "c"], Only(result).DeclaredDomain);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
    }

    [Fact]
    public async Task Probe_WhenAValueRepeatsAfterTruncation_ThenItIsStillNotRetained()
    {
        // Past the limit the distinct set is frozen, so a repeat of a dropped value must not
        // sneak back in — which is also what keeps retention bounded rather than merely capped
        // at first sight.
        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\ny\nz\nz\nx\n", hasHeader: false, options: Limit(2));

        Assert.Equal(["a", "x"], Only(result).DeclaredDomain);
        Assert.Equal(ProbeDraftExpectations.MarkerFor(2), Only(result).Description);
    }

    [Fact]
    public async Task Probe_WhenMissingTokenIsTheDefault_ThenQuestionMarkCellsAreNotObserved()
    {
        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\n?\ny\n");

        Assert.Equal(["x", "y"], Only(result).DeclaredDomain);
    }

    [Fact]
    public async Task Probe_WhenMissingTokenIsCustom_ThenOnlyThatTokenIsMissing()
    {
        // The token is data-side and exact: under `NA`, a literal "?" is an ordinary value.
        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\nNA\n?\n", missingToken: "NA");

        Assert.Equal(["x", "?"], Only(result).DeclaredDomain);
        Assert.Equal("NA", ProbeFixtures.Draft(result).Binding!.MissingToken);
    }

    [Fact]
    public async Task Probe_WhenMissingTokenIsDisabled_ThenOnlyEmptyCellsAreMissing()
    {
        // An empty token disables TOKEN matching only; an empty cell is always missing (§5.1).
        // The empty cell is written as a real field of a two-column row rather than as a blank
        // line, so the case under test is an empty CELL and not the tokenizer's line handling.
        var result = await ProbeFixtures.ProbeCsvAsync("a,b\nx,1\n?,2\n,3\ny,4\n", missingToken: "");
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["x", "?", "y"], draft.Attributes[0].DeclaredDomain);
        Assert.Equal("", draft.Binding!.MissingToken);
        Assert.Contains("missing_token = \"\"", ProbeFixtures.Toml(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenRowsAreRagged_ThenAbsentCellsAreMissingAndExtrasAreIgnored()
    {
        // A short row's unreached cells are missing, not an error (§5.4); a long row's extra
        // fields lie outside the ordered schema and so can create nothing.
        var result = await ProbeFixtures.ProbeCsvAsync("a,b\nx\ny,z,ignored\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(2, draft.Attributes.Count);
        Assert.Equal(["x", "y"], draft.Attributes[0].DeclaredDomain);
        Assert.Equal(["z"], draft.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public async Task Probe_WhenValuesDifferOnlyByCase_ThenTheyAreDistinctOrdinally()
    {
        // P-12: ordinal identity, never culture-aware. A culture-aware comparison could fold or
        // reorder these differently on another machine — a determinism bug on a path that
        // decides output columns.
        var result = await ProbeFixtures.ProbeCsvAsync("a\nStraße\nSTRASSE\nstrasse\n");

        Assert.Equal(["Straße", "STRASSE", "strasse"], Only(result).DeclaredDomain);
    }

    // A compact deterministic generator for the ordered-distinct/truncation property: for any
    // value sequence and any limit, the retained domain is the first `limit` distinct values in
    // first-observation order, and truncation is exactly "more distinct values existed". Rolled
    // by hand rather than pulled from a property-testing package — the repo has none, and M5
    // does not introduce one (P-5).
    public static TheoryData<string, int> RetentionCases()
    {
        var data = new TheoryData<string, int>();
        string[] sequences =
        [
            "", "a", "a,a,a", "a,b,c", "c,b,a", "a,b,a,c,b,d", "a,a,b,b,c,c",
            "d,c,b,a,d,c,b,a", "x,y,z,x,w,v,u", "m,m,n,m,o,n,p",
        ];

        foreach (var sequence in sequences)
        {
            for (var limit = 1; limit <= 5; limit++)
            {
                data.Add(sequence, limit);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RetentionCases))]
    public async Task Probe_WhenGivenAnySequenceAndLimit_ThenRetainsTheOrderedDistinctPrefix(string sequence, int limit)
    {
        var values = sequence.Length == 0 ? [] : sequence.Split(',');
        var csv = "col\n" + string.Concat(values.Select(v => v + "\n"));

        var result = await ProbeFixtures.ProbeCsvAsync(csv, options: Limit(limit));
        var attribute = Only(result);

        // The oracle, computed independently of the engine.
        var distinct = new List<string>();
        foreach (var value in values)
        {
            if (!distinct.Contains(value, StringComparer.Ordinal))
            {
                distinct.Add(value);
            }
        }

        var expectedDomain = distinct.Take(limit).ToList();
        var expectedTruncated = distinct.Count > limit;

        if (expectedDomain.Count == 0)
        {
            Assert.Null(attribute.DeclaredDomain);
        }
        else
        {
            Assert.Equal(expectedDomain, attribute.DeclaredDomain);
        }

        Assert.Equal(expectedTruncated ? ProbeDraftExpectations.MarkerFor(limit) : null, attribute.Description);
        Assert.Equal(expectedTruncated ? UnknownValuePolicy.Include : (UnknownValuePolicy?)null, attribute.UnknownValuePolicy);
        Assert.Equal(
            ProbeDraftExpectations.NotesFor(limit, expectedTruncated ? 1 : 0),
            ProbeFixtures.Draft(result).Provenance!.Notes);
    }

    [Fact]
    public async Task Probe_WhenTruncatedDomainIsLong_ThenTheWriterWrapsItDeterministically()
    {
        // The retention limit and the canonical writer's wrapping cutoff (D-113) are unrelated
        // knobs that meet here: a long retained prefix must still render as diff-friendly TOML.
        var values = Enumerable.Range(0, 30).Select(i => i.ToString(CultureInfo.InvariantCulture));
        var csv = "col\n" + string.Concat(values.Select(v => v + "\n"));

        var toml = ProbeFixtures.Toml(await ProbeFixtures.ProbeCsvAsync(csv, options: Limit(20)));

        Assert.Contains("declared_domain = [\n  \"0\",\n", toml, StringComparison.Ordinal);
        Assert.Contains("\n]\n", toml, StringComparison.Ordinal);
    }
}
