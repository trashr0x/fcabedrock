using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// D-106's triple observation semantics: predicates discovered in first-appearance order, values
/// observed set-based and idempotently under ordinal identity, missing values and missing
/// predicates contributing nothing — all from one pass over the cleaned rows.
/// <para>
/// The asymmetry worth naming: a wide probe's attributes are the schema's columns and are known
/// before the pass; a triple probe's are the predicates and are only known once it has finished.
/// Everything else — the ordering rule, the idempotence, the treatment of missing — is
/// deliberately identical, because a draft's meaning must not depend on which shape produced it.
/// </para>
/// </summary>
public sealed class ProbeTripleObservationTests
{
    private static readonly SourceReadSettings Settings = TripleProbeFixtures.TripleSettings();

    [Fact]
    public async Task ProbeTriple_WhenPredicatesInterleave_ThenAttributesFollowFirstAppearance()
    {
        // Not sorted, and not grouped: the order attributes appear in the draft is the order the
        // data first mentioned them (§17 rule 3's principle).
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,species,cat\ns1,colour,black\ns2,species,dog\ns2,size,small\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["species", "colour", "size"], draft.Attributes.Select(a => a.Name));
    }

    [Fact]
    public async Task ProbeTriple_WhenValuesRepeat_ThenDomainsAreOrderedDistinct()
    {
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,b\ns2,p,a\ns3,p,b\ns4,p,c\ns5,p,a\n"));

        Assert.Equal(["b", "a", "c"], draft.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheSameTripleRepeats_ThenObservationIsIdempotent()
    {
        // Repeated triples change nothing — not the order, not the domain, not the accounting.
        // That is what makes one pass sufficient (D-106).
        var once = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,p,x\ns1,q,y\n");
        var thrice = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,x\ns1,q,y\ns1,p,x\ns1,q,y\ns1,p,x\n");

        Assert.Equal(ProbeFixtures.Toml(once), ProbeFixtures.Toml(thrice));
    }

    [Fact]
    public async Task ProbeTriple_WhenValuesDifferOnlyByCase_ThenTheyAreDistinct()
    {
        // Ordinal identity (P-12): no culture, no case folding, no normalization.
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,Cat\ns2,p,cat\ns3,p,CAT\n"));

        Assert.Equal(["Cat", "cat", "CAT"], draft.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenPredicatesDifferOnlyByCase_ThenTheyAreDistinctAttributes()
    {
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,Colour,black\ns2,colour,white\n"));

        Assert.Equal(["Colour", "colour"], draft.Attributes.Select(a => a.Name));
        Assert.Equal(["black"], draft.Attributes[0].DeclaredDomain);
        Assert.Equal(["white"], draft.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenAValueIsMissing_ThenThePredicateIsStillDiscovered()
    {
        // §5.3/D-085: a present predicate with a missing value is an observation of the
        // ATTRIBUTE, not of a value. The attribute is real; only its domain is thinner.
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,species,?\ns2,species,cat\ns3,colour,\n"));

        Assert.Equal(["species", "colour"], draft.Attributes.Select(a => a.Name));
        Assert.Equal(["cat"], draft.Attributes[0].DeclaredDomain);

        // Every value missing: the attribute is authored, the domain is omitted (not `[]`), and
        // the Calibrate phase will discover it from data (§10.3, D-071).
        Assert.Null(draft.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenEveryValueOfEveryPredicateIsMissing_ThenTheDraftStillSucceeds()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,p,?\ns2,q,?\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["p", "q"], draft.Attributes.Select(a => a.Name));
        Assert.All(draft.Attributes, a => Assert.Null(a.DeclaredDomain));
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateIsMissing_ThenTheRowIsIgnored()
    {
        // §7.1: an empty or missing predicate names no attribute and is not an error. The row's
        // subject is still validated — see the structural suite.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,?,cat\ns2,,dog\ns3,species,fish\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("species", Assert.Single(draft.Attributes).Name);
        Assert.Equal(["fish"], draft.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenNoPredicateIsEverPresent_ThenReportsNoAttributesDiscovered()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,?,cat\ns2,,dog\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ProbeNoAttributesDiscovered, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(result.TryGetValue(out _));
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheSourceIsEmpty_ThenThePreflightRejectsTheRoleMap()
    {
        // An empty source has zero columns, so no role map — not even the 0/1/2 default — can
        // address it. That is a binding problem, and the preflight owns binding problems, so it
        // is reported as such and the rows are never read. Deliberately NOT
        // ProbeNoAttributesDiscovered: the wide shape reaches that code because a zero-column
        // schema is still a legal thing to bind against, whereas a triple source that cannot even
        // supply its three roles has failed earlier and for a different reason.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(string.Empty);

        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticCode.SourceBindingInvalid, d.Code));
        Assert.NotEmpty(result.Diagnostics);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public async Task ProbeTriple_WhenRowsAreRagged_ThenAbsentRolesAreMissingNotErrors()
    {
        // §5.4/D-085: a short row's unreached cells arrive as null — uniformly missing, never a
        // structural failure. Here the predicate is absent on one row and the value on another.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,species,cat\ns2\ns3,colour\ns4,species,dog\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["species", "colour"], draft.Attributes.Select(a => a.Name));
        Assert.Equal(["cat", "dog"], draft.Attributes[0].DeclaredDomain);
        Assert.Null(draft.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheMissingTokenIsCustom_ThenOnlyThatTokenIsMissing()
    {
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,NA\ns2,p,?\ns3,p,x\n", missingToken: "NA"));

        // `?` is now an ordinary value; `NA` is the missing token.
        Assert.Equal(["?", "x"], draft.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheMissingTokenIsDisabled_ThenOnlyEmptyCellsAreMissing()
    {
        // §5.1: an empty configured token disables token matching; empty cells are always missing.
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,?\ns2,p,\ns3,p,x\n", missingToken: string.Empty));

        Assert.Equal(["?", "x"], draft.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheSourceHasAHeader_ThenItIsSchemaAndNotData()
    {
        // has_header = true on a triple source: row 1 is consumed as the schema, so its cells
        // never become a predicate or a value.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "subject,predicate,value\ns1,species,cat\n", hasHeader: true);
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal("species", Assert.Single(draft.Attributes).Name);
        Assert.Equal(["cat"], draft.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenSuccessful_ThenEnumeratesRowsExactlyOnceThroughTheResolvedRoles()
    {
        // "One pass" for triple, asserted the same way as for wide: one ROW enumeration (the
        // schema read is metadata), no Bind, no grouping pass, no second read to resolve names.
        var session = TripleProbeFixtures.Fake(("s1", "p", "x"), ("s2", "q", "y"), ("s3", "p", "z"));

        await Prober.ProbeTripleAsync(session, Settings);

        Assert.Equal(1, session.RowEnumerations);
        Assert.Equal(1, session.SchemaReads);
        Assert.Equal(3, session.RowsYielded);
        Assert.Equal(new TripleColumns(0, 1, 2), Assert.Single(session.RolesRead));
    }

    [Fact]
    public async Task ProbeTriple_WhenTruncating_ThenStillEnumeratesRowsExactlyOnce()
    {
        // Never a grouped or count-sensitive second pass, not even to find out what was dropped:
        // set-based observation has nothing to learn from a second look (D-106).
        var session = TripleProbeFixtures.Fake(("s", "p", "a"), ("s", "p", "b"), ("s", "p", "c"));

        await Prober.ProbeTripleAsync(session, Settings, null, ProbeOptions.Create(valueRetentionLimit: 1));

        Assert.Equal(1, session.RowEnumerations);
    }

    [Fact]
    public async Task ProbeTriple_WhenSubjectGrouped_ThenStillEnumeratesRowsExactlyOnce()
    {
        // Contiguity validation is done in the same pass, from a seen-subject set — not by
        // grouping or spooling (D-110's inherited carve-out).
        var session = TripleProbeFixtures.Fake(("s1", "p", "a"), ("s1", "q", "b"), ("s2", "p", "c"));

        await Prober.ProbeTripleAsync(
            session, TripleProbeFixtures.TripleSettings(ordering: TripleOrdering.SubjectGrouped));

        Assert.Equal(1, session.RowEnumerations);
    }

    [Fact]
    public async Task ProbeTriple_WhenDrivenByANonFileSession_ThenNoCsvOrStreamIsInvolved()
    {
        // The D-109 seam, proven for triple: rows arrive from memory with no delimiter, encoding,
        // stream, or Sep anywhere, and the draft is the same one the CSV path produces.
        var fromMemory = await Prober.ProbeTripleAsync(
            TripleProbeFixtures.Fake(("s1", "species", "cat"), ("s1", "colour", null), ("s2", "species", "dog")),
            Settings);
        var fromCsv = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,species,cat\ns1,colour,?\ns2,species,dog\n");

        Assert.Equal(ProbeFixtures.Toml(fromCsv), ProbeFixtures.Toml(fromMemory));
    }
}
