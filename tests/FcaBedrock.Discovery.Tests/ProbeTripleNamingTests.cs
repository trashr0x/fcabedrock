using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The §7.1 / D-107 <b>triple</b> naming and binding matrix: a usable predicate is both the
/// source selector and the attribute name; an unusable one keeps its <em>exact</em> selector and
/// receives <c>predicate_&lt;first-appearance-ordinal&gt;</c>.
/// <para>
/// <b>The rule under test is really one rule:</b> no source selector is ever silently changed.
/// For triple that bites hardest, because the selector <em>is</em> the string the data spells —
/// rename it and the attribute points at a predicate the source does not contain, so the draft
/// would fail its own convert guarantee while looking perfectly reasonable.
/// </para>
/// <para>
/// Exercised through real tolerant CSV reads wherever the case is expressible in CSV, not only
/// through fabricated rows: a quote- or newline-bearing predicate has to survive the tokenizer
/// before naming ever sees it.
/// </para>
/// </summary>
public sealed class ProbeTripleNamingTests
{
    private static readonly SourceReadSettings Settings = TripleProbeFixtures.TripleSettings();

    [Fact]
    public async Task ProbeTriple_WhenPredicatesAreUsable_ThenNameAndSelectorAreTheSameText()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,species,cat\ns1,colour,black\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["species", "colour"], draft.Attributes.Select(a => a.Name));
        Assert.Equal("species", TripleProbeFixtures.SourceOf(draft, "species").Name);
        Assert.Equal("colour", TripleProbeFixtures.SourceOf(draft, "colour").Name);
        Assert.All(draft.Attributes, a =>
            Assert.Equal(SourceValueType.String, ((PredicateSourceSection)a.Source!).ValueType));
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateLooksAwkwardButIsValid_ThenItIsNotRenamed()
    {
        // §10.1 is deliberately permissive: spaces, punctuation, `#`, and `?` are all fine in a
        // name, and a draft must round-trip a real vocabulary unrenamed.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,has bruises?,t\ns1,feature.1,x\ns1,gene#4,y\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["has bruises?", "feature.1", "gene#4"], draft.Attributes.Select(a => a.Name));
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateCarriesAQuote_ThenTheNameFallsBackAndTheSelectorSurvives()
    {
        // `"` is §10.1-invalid (it is the TOML key-quoting character). Through a REAL read: the
        // doubled quote inside a quoted field unescapes to one literal quote.
        const string csv = "s1,\"say \"\"hi\"\"\",loud\ns1,colour,black\n";

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["predicate_0", "colour"], draft.Attributes.Select(a => a.Name));

        // The selector keeps the exact predicate text, quote and all — only the NAME moved.
        Assert.Equal("say \"hi\"", TripleProbeFixtures.SourceOf(draft, "predicate_0").Name);
        Assert.Equal(["loud"], draft.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateCarriesANewline_ThenTheNameFallsBackAndTheSelectorSurvives()
    {
        // A quoted field may contain a line break, which is §10.1-invalid in a name but perfectly
        // legal as a predicate string. Again through a real read, so the tokenizer is part of the
        // claim rather than assumed away.
        const string csv = "s1,\"two\nlines\",v\n";

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal("predicate_0", Assert.Single(draft.Attributes).Name);
        Assert.Equal("two\nlines", TripleProbeFixtures.SourceOf(draft, "predicate_0").Name);
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateIsUnusable_ThenTheFallbackUsesItsFirstAppearanceOrdinal()
    {
        // The suffix is the ordinal of first appearance, not of the row or of the unusable
        // predicates among themselves — so it is stable under repetition and interleaving.
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,alpha,1\ns1,\"q\"\"uote\",2\ns1,beta,3\ns1,\"q\"\"uote\",4\n"));

        Assert.Equal(["alpha", "predicate_1", "beta"], draft.Attributes.Select(a => a.Name));
        Assert.Equal(["2", "4"], draft.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public async Task ProbeTriple_WhenAFallbackNameCollidesWithARealPredicate_ThenTheRealOneKeepsIt()
    {
        // The adversarial case: a genuine, perfectly usable predicate literally spelled
        // `predicate_0`, alongside an unusable predicate whose fallback wants that name. The real
        // predicate keeps it — its name is its selector's text — and the synthesized one
        // escalates. Neither attribute is dropped or merged, and neither selector changes.
        const string csv = "s1,\"q\"\"uote\",a\ns1,predicate_0,b\n";

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(2, draft.Attributes.Count);
        Assert.Equal("predicate_0", draft.Attributes[1].Name);
        Assert.Equal("predicate_0", TripleProbeFixtures.SourceOf(draft, "predicate_0").Name);

        // The escalated fallback: `predicate_0` is taken, so the ladder's first step applies.
        Assert.Equal("predicate_0#0", draft.Attributes[0].Name);
        Assert.Equal("q\"uote", ((PredicateSourceSection)draft.Attributes[0].Source!).Name);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheCollidingPredicateAppearsLater_ThenItStillKeepsItsName()
    {
        // Order of arrival must not decide who gets renamed: the whole first-appearance list is
        // planned at once, so a usable predicate discovered AFTER the unusable one still keeps
        // its own text.
        const string csv = "s1,\"q\"\"uote\",a\ns1,zzz,b\ns1,predicate_0,c\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));

        Assert.Equal(["predicate_0#0", "zzz", "predicate_0"], draft.Attributes.Select(a => a.Name));
        Assert.Equal("predicate_0", TripleProbeFixtures.SourceOf(draft, "predicate_0").Name);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheLadderIsAlsoTaken_ThenItEscalatesToTheFirstUnused()
    {
        // Both the fallback and its first ladder step are real predicates, so the escalation
        // continues ordinally. Bounded, deterministic, and still nobody is renamed but the
        // synthesized name.
        const string csv = "s1,\"q\"\"uote\",a\ns1,predicate_0,b\ns1,predicate_0#0,c\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));

        Assert.Equal(["predicate_0#1", "predicate_0", "predicate_0#0"], draft.Attributes.Select(a => a.Name));
    }

    [Fact]
    public async Task ProbeTriple_WhenNamesAreAdjusted_ThenOneAggregatedWarningCountsThemInOrder()
    {
        // One warning, not one per attribute — a 10,000-predicate vocabulary must not produce
        // 10,000 warnings (D-111). Count and bounded sample follow first-appearance order.
        const string csv =
            "s1,\"a\"\"1\",v\ns1,\"b\"\"2\",v\ns1,ok,v\ns1,\"c\"\"3\",v\ns1,\"d\"\"4\",v\n";

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(csv);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ProbeAttributeNameAdjusted, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("4 attribute name(s)", diagnostic.Message, StringComparison.Ordinal);

        // The first three adjusted names, in first-appearance order — and not the fourth.
        Assert.Contains("predicate_0, predicate_1, predicate_3", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("predicate_4", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenNoNameIsAdjusted_ThenNoWarningIsRaised()
    {
        // The control: the aggregated warning must not fire for an ordinary vocabulary. Note that
        // an IGNORED empty predicate is not an adjustment either — nothing was named.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,a,1\ns2,,2\ns3,b,3\n");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ProbeTriple_WhenNamesAreAdjusted_ThenEveryLogicalNameIsStillValidAndUnique()
    {
        // The two properties the whole matrix exists to guarantee, asserted directly rather than
        // inferred from the specific spellings above.
        const string csv =
            "s1,\"q\"\"1\",a\ns1,predicate_0,b\ns1,\"q\"\"2\",c\ns1,predicate_2,d\ns1,\"two\nlines\",e\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));
        var names = draft.Attributes.Select(a => a.Name!).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name =>
        {
            Assert.NotEmpty(name);
            Assert.DoesNotContain('"', name);
            Assert.DoesNotContain('\n', name);
            Assert.DoesNotContain('\r', name);
        });

        // And every selector is still the predicate the data spelled.
        Assert.Equal(
            ["q\"1", "predicate_0", "q\"2", "predicate_2", "two\nlines"],
            draft.Attributes.Select(a => ((PredicateSourceSection)a.Source!).Name));
    }

    [Fact]
    public async Task ProbeTriple_WhenAPredicateIsWhitespaceOnly_ThenItIsAnOrdinaryUsableName()
    {
        // A quoted single space survives the §5.1 outer trim, so it is a present, non-empty
        // predicate — and §10.1 permits it as a name. Contrast with a whitespace-only SUBJECT,
        // which the object-name predicate rejects: two different rules over two different
        // alphabets, deliberately not merged.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,\" \",v\n");
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(" ", Assert.Single(draft.Attributes).Name);
        Assert.Equal(" ", TripleProbeFixtures.SourceOf(draft, " ").Name);
    }

    [Fact]
    public async Task ProbeTriple_WhenNamesAreAdjusted_ThenTheDraftStillRereadsAndResolves()
    {
        // Naming is only correct if the result is still a usable spec: the escalated names must
        // survive the strict reader and bind back to their predicates.
        const string csv = "s1,\"q\"\"uote\",a\ns1,predicate_0,b\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));
        var toml = SpecWriter.Write(draft);
        var reread = SpecReader.Read(toml);

        Assert.True(reread.TryGetValue(out var document), ProbeFixtures.Describe(reread.Diagnostics));
        Assert.Equal(toml, SpecWriter.Write(document));
    }

    [Fact]
    public async Task ProbeTriple_WhenPredicatesArriveFromMemory_ThenNamingIsTheSame()
    {
        // Naming reads the record sequence, never the bytes — so a hand-built session with the
        // same predicates must produce the same names.
        var fromMemory = await Prober.ProbeTripleAsync(
            TripleProbeFixtures.Fake(("s1", "q\"uote", "a"), ("s1", "predicate_0", "b")), Settings);
        var fromCsv = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,\"q\"\"uote\",a\ns1,predicate_0,b\n");

        Assert.Equal(ProbeFixtures.Toml(fromCsv), ProbeFixtures.Toml(fromMemory));
    }
}
