using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// D-112 category 6: wide/triple symmetry. Two sources carrying the <em>same</em> information in
/// the two shapes must produce drafts that mean the same thing.
/// <para>
/// <b>What must match:</b> attribute names, domains and their order, the universal
/// <c>identity</c> + <c>nominal</c> cell, explicit string typing, truncation markers and
/// recovery, provenance notes, and diagnostic ordering. <b>What must differ, and must keep
/// differing:</b> the <c>[binding]</c> (only triple has an ordering and a role map) and the
/// source carrier (a column selector vs a predicate selector). Collapsing that second list would
/// be as wrong as failing the first — the shapes address their sources differently on purpose.
/// </para>
/// <para>
/// Also here: the diagnostic inventory. Slice D added no enum member; it gave two <em>existing</em>
/// structural codes their probe-phase sites (D-111), and everything else probe can say was
/// already live.
/// </para>
/// </summary>
public sealed class ProbeShapeParityTests
{
    private static string FixturePath(string example, string file) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "v2", example, file);

    /// <summary>
    /// Asserts the two drafts agree on everything except their shape-specific carriers.
    /// </summary>
    private static void AssertSemanticParity(SpecDocument wide, SpecDocument triple)
    {
        Assert.Equal(
            wide.Attributes.Select(a => a.Name),
            triple.Attributes.Select(a => a.Name));
        Assert.Equal(
            wide.Attributes.Select(a => a.DeclaredDomain),
            triple.Attributes.Select(a => a.DeclaredDomain));
        Assert.Equal(
            wide.Attributes.Select(a => a.Description),
            triple.Attributes.Select(a => a.Description));
        Assert.Equal(
            wide.Attributes.Select(a => a.UnknownValuePolicy),
            triple.Attributes.Select(a => a.UnknownValuePolicy));

        // The universal draft cell, in both shapes.
        Assert.All(wide.Attributes.Concat(triple.Attributes), a =>
        {
            Assert.IsType<IdentityDiscretizerSection>(a.Discretizer);
            Assert.IsType<NominalScaleSection>(a.Scale);
        });

        // Same [spec] and [provenance]: neither is shape-dependent.
        Assert.Equal(wide.Spec, triple.Spec);
        Assert.Equal(wide.Provenance, triple.Provenance);

        // …and the differences that must survive.
        Assert.Equal(SourceShape.Wide, wide.Binding!.Shape);
        Assert.Equal(SourceShape.Triple, triple.Binding!.Shape);
        Assert.Null(wide.Binding.Ordering);
        Assert.NotNull(triple.Binding.Ordering);
        Assert.Null(wide.Binding.Columns);
        Assert.NotNull(triple.Binding.Columns);
        Assert.All(wide.Attributes, a => Assert.IsType<ColumnSourceSection>(a.Source));
        Assert.All(triple.Attributes, a => Assert.IsType<PredicateSourceSection>(a.Source));

        // Both author an explicit string value_type — the typing claim is shape-independent.
        Assert.All(
            wide.Attributes.Select(a => ((ColumnSourceSection)a.Source!).ValueType),
            t => Assert.Equal(SourceValueType.String, t));
        Assert.All(
            triple.Attributes.Select(a => ((PredicateSourceSection)a.Source!).ValueType),
            t => Assert.Equal(SourceValueType.String, t));
    }

    [Fact]
    public async Task Drafts_WhenTheSameDataArrivesInBothShapes_ThenTheyAgreeSemantically()
    {
        const string wideCsv = "colour,size\nred,big\nblue,small\nred,big\n";
        const string tripleCsv =
            "0,colour,red\n0,size,big\n1,colour,blue\n1,size,small\n2,colour,red\n2,size,big\n";

        var wide = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(wideCsv));
        var triple = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(tripleCsv));

        AssertSemanticParity(wide, triple);
        Assert.Equal(["red", "blue"], triple.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public async Task Drafts_WhenBothTruncate_ThenTheMarkersNotesAndRecoveryMatch()
    {
        // Truncation is a retention property, not a shape property, so both shapes must produce
        // the same prefix, the same marker, the same `include`, and the same notes count.
        const string wideCsv = "col\nq\nr\ns\nt\n";
        const string tripleCsv = "0,col,q\n1,col,r\n2,col,s\n3,col,t\n";
        var options = ProbeOptions.Create(valueRetentionLimit: 2);

        var wideResult = await ProbeFixtures.ProbeCsvAsync(wideCsv, options: options);
        var tripleResult = await TripleProbeFixtures.ProbeTripleCsvAsync(tripleCsv, options: options);

        AssertSemanticParity(ProbeFixtures.Draft(wideResult), ProbeFixtures.Draft(tripleResult));
        Assert.Equal(["q", "r"], ProbeFixtures.Draft(tripleResult).Attributes[0].DeclaredDomain);

        // Same diagnostics, in the same order — the aggregates flush identically in both engines.
        Assert.Equal(
            wideResult.Diagnostics.Select(d => d.Code),
            tripleResult.Diagnostics.Select(d => d.Code));
        Assert.Equal(wideResult.Diagnostics, tripleResult.Diagnostics);
    }

    [Fact]
    public async Task Drafts_WhenBothAdjustNamesAndTruncate_ThenTheWarningOrderMatches()
    {
        // Naming first, then truncation, in both shapes. A caller that handles probe diagnostics
        // must not have to branch on shape to know the order.
        const string wideCsv = "\"q\"\"1\"\na\nb\n";
        const string tripleCsv = "0,\"q\"\"1\",a\n1,\"q\"\"1\",b\n";
        var options = ProbeOptions.Create(valueRetentionLimit: 1);

        var wideResult = await ProbeFixtures.ProbeCsvAsync(wideCsv, options: options);
        var tripleResult = await TripleProbeFixtures.ProbeTripleCsvAsync(tripleCsv, options: options);

        Assert.Equal(
            [DiagnosticCode.ProbeAttributeNameAdjusted, DiagnosticCode.ProbeDomainTruncated],
            wideResult.Diagnostics.Select(d => d.Code));
        Assert.Equal(
            wideResult.Diagnostics.Select(d => d.Code),
            tripleResult.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public async Task Drafts_WhenAnAttributeIsAllMissing_ThenBothOmitTheDomain()
    {
        const string wideCsv = "a,b\nx,?\ny,?\n";
        const string tripleCsv = "0,a,x\n0,b,?\n1,a,y\n1,b,?\n";

        var wide = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(wideCsv));
        var triple = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(tripleCsv));

        AssertSemanticParity(wide, triple);
        Assert.Null(triple.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public async Task Drafts_WhenProbedFromMiniAdultInBothShapes_ThenTheyAgreeSemantically()
    {
        // The real fixtures: the same dataset, one wide file and one triple file, probed
        // independently. The v2 triple file is grouped by PREDICATE rather than by subject, so
        // agreeing here is a genuine claim about discovery order, not a coincidence of layout.
        var wideSettings = ProbeFixtures.WideSettings();
        var wide = ProbeFixtures.Draft(await Prober.ProbeAsync(
            new WideCsvSession(() => File.OpenRead(FixturePath("mini-adult", "mini-adult.data")), wideSettings),
            wideSettings));

        var tripleSettings = TripleProbeFixtures.TripleSettings();
        var triple = ProbeFixtures.Draft(await Prober.ProbeTripleAsync(
            new TripleCsvSession(
                () => File.OpenRead(FixturePath("mini-adult", "mini-adult_triples.data")), tripleSettings),
            tripleSettings));

        AssertSemanticParity(wide, triple);
    }

    [Fact]
    public async Task Drafts_WhenProbedFromMiniMushroomInBothShapes_ThenTheyAgreeSemantically()
    {
        var wideSettings = ProbeFixtures.WideSettings();
        var wide = ProbeFixtures.Draft(await Prober.ProbeAsync(
            new WideCsvSession(
                () => File.OpenRead(FixturePath("mini-mushroom", "mini-mushroom.data")), wideSettings),
            wideSettings));

        var tripleSettings = TripleProbeFixtures.TripleSettings();
        var triple = ProbeFixtures.Draft(await Prober.ProbeTripleAsync(
            new TripleCsvSession(
                () => File.OpenRead(FixturePath("mini-mushroom", "mini-mushroom_triples.data")), tripleSettings),
            tripleSettings));

        AssertSemanticParity(wide, triple);
    }

    // --- The diagnostic inventory ---------------------------------------------------------------

    [Fact]
    public void DiagnosticCode_WhenSliceDLanded_ThenNoSixthProbeCodeExists()
    {
        // Slice D added no enum member. The two structural conditions it reports reuse
        // `ObjectKeyValueInvalid` and `TripleSubjectNotContiguous` — one condition, one code,
        // three phases (D-067/D-111) — rather than minting probe-specific twins, and the triple
        // "nothing to author" case reuses `ProbeNoAttributesDiscovered`.
        var probeCodes = Enum.GetNames<DiagnosticCode>()
            .Where(name => name.StartsWith("Probe", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "ProbeAttributeNameAdjusted", "ProbeDomainTruncated", "ProbeLimitExceeded",
                "ProbeNoAttributesDiscovered", "ProbeSourceReadFailed",
            ],
            probeCodes);
    }

    [Fact]
    public async Task Probe_WhenBothShapesAreExercised_ThenEveryProbeCodeStillHasALiveSite()
    {
        // Every code Discovery owns, produced from a real probe in this run — so a code cannot
        // quietly lose its emit site while the registry test keeps counting it.
        var seen = new HashSet<DiagnosticCode>();

        void Record(Diagnosed<SpecDocument> result)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                seen.Add(diagnostic.Code);
            }
        }

        Record(await Prober.ProbeAsync(
            new ProbeFixtures.FakeWideSession(
                new SourceSchema(1, ["a"]), [], schemaFailure: () => new IOException("x")),
            ProbeFixtures.WideSettings()));
        Record(await ProbeFixtures.ProbeCsvAsync(string.Empty));
        Record(await ProbeFixtures.ProbeCsvAsync("a,a\n1,2\n"));
        Record(await ProbeFixtures.ProbeCsvAsync(
            "a\nx\ny\n", options: ProbeOptions.Create(valueRetentionLimit: 1)));
        Record(await ProbeFixtures.ProbeCsvAsync(
            "a,b\n1,2\n", options: ProbeOptions.Create(maxDiscoveredAttributes: 1)));

        // The two structural codes: probe-phase sites, live only on the triple path (wide probe
        // is row-index based and does no object-key validation, D-106).
        Record(await TripleProbeFixtures.ProbeTripleCsvAsync("s1,p,a\n?,p,b\n"));
        Record(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,a\ns2,p,b\ns1,p,c\n", ordering: TripleOrdering.SubjectGrouped));

        // An exact set, not a "contains": a code produced here that is not on the list would mean
        // probe had grown a diagnostic nobody settled.
        Assert.Equal(
            new HashSet<DiagnosticCode>
            {
                DiagnosticCode.ProbeSourceReadFailed,
                DiagnosticCode.ProbeNoAttributesDiscovered,
                DiagnosticCode.ProbeAttributeNameAdjusted,
                DiagnosticCode.ProbeDomainTruncated,
                DiagnosticCode.ProbeLimitExceeded,
                DiagnosticCode.ObjectKeyValueInvalid,
                DiagnosticCode.TripleSubjectNotContiguous,
            },
            seen);
    }

    [Fact]
    public async Task Probe_WhenWideInputIsStructurallyOdd_ThenNoObjectKeyValidationHappens()
    {
        // The asymmetry that is deliberate: wide probe is row-index based, so blank and
        // whitespace-only CELLS are ordinary data — never `ObjectKeyValueInvalid`. Only a triple
        // subject names an object (D-106).
        var result = await ProbeFixtures.ProbeCsvAsync("a\n\" \"\n\n");

        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code is DiagnosticCode.ObjectKeyValueInvalid or DiagnosticCode.TripleSubjectNotContiguous);
        Assert.True(result.IsOk, ProbeFixtures.Describe(result.Diagnostics));
    }
}
