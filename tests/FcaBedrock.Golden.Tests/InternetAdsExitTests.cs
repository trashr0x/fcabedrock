using System.Globalization;
using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Discovery;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Golden.Tests;

/// <summary>
/// The M6 exit: the one-file Internet-Ads workflow end-to-end (D-119). A single
/// self-contained spec over the complete Internet-Advertisements column layout —
/// 1,559 declared attributes, one <c>term_flag</c> template, one
/// <c>source_index_range = [4, 1557]</c> matcher — resolves, calibrates, plans,
/// converts, and emits byte-identical <c>.cxt</c>/<c>.dat</c> with the correct
/// incidences; and it equals its materialized twin on effective attributes, plan,
/// all three fingerprints, and bytes. The uncurated probe-style draft proves the
/// fully-shadowed matcher is visible yet inert, both de-shadow curations converge,
/// and a <b>real</b> <see cref="Prober.ProbeAsync"/> draft carries through the
/// actual resolve → calibrate → plan → emit → export chain.
/// <para>
/// <b>The contract boundaries are literal here, never the generator's own
/// constants</b> (the oracle must be independent of the artifact it grades): every
/// cardinality/boundary assertion uses the frozen literals 1,559 / 4 / 1,557 /
/// 1,554 / 1,558, the generator's public constants are checked <em>against</em>
/// those literals, and reduction-sensitivity guards prove the checks are real
/// gates. Structural projections enumerate every semantic field explicitly with
/// invariant formatting (record <c>ToString()</c> hides collection contents such
/// as a discretizer's cut vector). Emitted incidences, not just planned column
/// names, are asserted in both output formats.
/// </para>
/// <para>
/// Deliberately separate from the v2 golden comparisons (P-9): templates, matchers,
/// and the ad.data layout are native surface no v2 fixture uses. The corpus is
/// deterministic and synthetic — it mirrors the layout of Nicholas Kushmerick
/// (1999), Internet Advertisements (UCI), without copying any UCI data row.
/// </para>
/// </summary>
public sealed class InternetAdsExitTests
{
    // The frozen D-119 contract boundaries, as LITERALS independent of AdCorpus. These are the
    // oracle; the generator's own constants are graded against them.
    private const int Columns = 1559;
    private const int FirstTerm = 4;
    private const int LastTerm = 1557;
    private const int Terms = 1554; // 1557 - 4 + 1
    private const int ClassCol = 1558;
    private const int RowCount = 6;

    // ---------------------------------------------------------------------
    // Corpus: layout, determinism, and the reduction-sensitivity gate
    // ---------------------------------------------------------------------

    [Fact]
    public void Corpus_MirrorsTheAdDataLayout_ProvenAgainstLiteralContractBoundaries()
    {
        // The generator's own dimension constants must equal the frozen literals — a wrong
        // constant is caught here directly rather than validating itself downstream.
        Assert.Equal(Columns, AdCorpus.ColumnCount);
        Assert.Equal(FirstTerm, AdCorpus.FirstTermIndex);
        Assert.Equal(LastTerm, AdCorpus.LastTermIndex);
        Assert.Equal(Terms, AdCorpus.TermCount);
        Assert.Equal(ClassCol, AdCorpus.ClassIndex);
        Assert.Equal(RowCount, AdCorpus.RowCount);

        // The layout gate (literal width + partition), applied to the generated content.
        var coverage = AssertAdDataLayout(AdCorpus.Csv);
        Assert.True(coverage.SawTermOne && coverage.SawTermZero, "terms exercise both dichotomic outcomes");
        Assert.True(coverage.SawTermMissing, "terms include a missing cell");
        Assert.True(coverage.SawNumericMissing, "numeric columns include a missing cell");
        Assert.Equal(new[] { "ad.", "nonad." }, coverage.Classes.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void Corpus_WhenTheWidthIsReduced_ThenTheLiteralLayoutGateRejectsIt()
    {
        // A "coordinated reduced helper" proxy: drop the last field from every row. A gate that
        // echoed the generator's own width would accept 1,558; the literal gate must reject it.
        var reduced = string.Join(
            '\n',
            AdCorpus.Csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    var fields = line.Split(',');
                    return string.Join(',', fields.Take(fields.Length - 1));
                })) + "\n";

        Assert.ThrowsAny<Exception>(() => AssertAdDataLayout(reduced));
    }

    [Fact]
    public void Corpus_WhenGeneratedTwice_ThenBytesAndDomainsAreIdentical()
    {
        // Independent generations, not a shared mutable snapshot: two fresh corpus
        // builds are byte-identical to each other and to the cached value, and the independently
        // computed domains are stable.
        var first = AdCorpus.GenerateCsv();
        var second = AdCorpus.GenerateCsv();
        Assert.Equal(first, second);
        Assert.Equal(AdCorpus.Csv, first);

        for (var i = 0; i < Columns; i++)
        {
            Assert.Equal(AdCorpus.DomainOf(i), AdCorpus.DomainOf(i));
        }
    }

    // ---------------------------------------------------------------------
    // 1. Full-width declarative execution
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Declarative_WhenConvertedFullWidth_ThenResolvesPlansEmitsCorrectlyAndTheMatcherConfiguresOnlyTheTerms()
    {
        var declarative = await ConvertAsync(AdSpecs.Declarative());

        // No Error/Fatal, and NO diagnostic at all in any stage — the fully applied declarative
        // form is clean (the matcher wins on the bare terms, so no shadow warning; it selects
        // 1,554, so no zero-match warning).
        AssertNoErrors(declarative);
        AssertExactDiagnostics(declarative);

        // Independent ordered inventory: exactly 1,559 attributes, each position's name and source
        // index locked against the literal contract, globally unique, complete 0…1558 coverage —
        // nothing synthesized from source width.
        AssertAuthoredInventory(declarative.Spec.Attributes);

        // Exactly the 1,554 terms at 4…1557 carry the template's dichotomic("1")+["1","0"].
        AssertMatcherTermConfig(declarative.Spec);

        // Indexes 0–3 and 1558 are OUTSIDE the matcher and retain their own configuration.
        Assert.IsType<ManualCutsDiscretizer>(declarative.Spec.Attributes[0].Discretizer);   // height
        Assert.IsType<NominalScale>(declarative.Spec.Attributes[0].Scale);
        Assert.IsType<ManualCutsDiscretizer>(declarative.Spec.Attributes[1].Discretizer);   // width
        Assert.IsType<ManualCutsDiscretizer>(declarative.Spec.Attributes[2].Discretizer);   // aratio
        Assert.IsType<DichotomicScale>(declarative.Spec.Attributes[3].Scale);               // local (its own explicit config)
        Assert.IsType<NominalScale>(declarative.Spec.Attributes[ClassCol].Scale);           // class

        // Representative EMITTED incidences (not just planned names): the six objects actually
        // cross the correct numeric/local/term/class/missing columns, in both output formats.
        AssertDeclarativeIncidences(declarative);

        // Sensitivity #1 (matcher load-bearing): removing the matcher leaves the bare terms with
        // no scale, so resolution fails — AttributeScalingMissing on the terms, nowhere else.
        var withoutMatcher = await ResolveOnlyAsync(AdSpecs.Declarative(includeMatcher: false));
        Assert.False(withoutMatcher.IsOk);
        Assert.Contains(withoutMatcher.Diagnostics, d => d.Code == DiagnosticCode.AttributeScalingMissing);

        // Sensitivity #2 (inventory gate is real): a coordinated reduction is rejected.
        Assert.ThrowsAny<Exception>(() => AssertAuthoredInventory(declarative.Spec.Attributes.Take(Columns - 1).ToList()));

        AssertProvenance(AdSpecs.Declarative());
    }

    // ---------------------------------------------------------------------
    // 2. Declarative versus materialized equivalence
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Declarative_WhenComparedToItsMaterializedTwin_ThenEveryEffectiveAttributePlanFingerprintAndByteMatches()
    {
        var declarative = await ConvertAsync(AdSpecs.Declarative());
        var materialized = await ConvertAsync(AdSpecs.Materialized());

        AssertIdentical(declarative, materialized);

        // Non-vacuity: the projections cover all 1,559 logical attributes and the expected 1,554
        // matcher-configured terms on both sides.
        AssertAuthoredInventory(declarative.Spec.Attributes);
        AssertAuthoredInventory(materialized.Spec.Attributes);
        AssertMatcherTermConfig(declarative.Spec);
        AssertMatcherTermConfig(materialized.Spec);
    }

    [Fact]
    public async Task Materialized_WhenATermColumnIsAdded_ThenTheProjectionsDiverge()
    {
        // Sensitivity anchor for the equivalence assertion above: an extra as_attribute on one
        // term plans a new -missing column, so the same exhaustive projections and hashes that
        // report "equal" for equivalent specs report "different" here.
        var baseline = await ConvertAsync(AdSpecs.Materialized());
        var changed = await ConvertAsync(AdSpecs.Materialized(term0004MissingColumn: true));

        Assert.NotEqual(baseline.ResolvedAttributes, changed.ResolvedAttributes);
        Assert.NotEqual(baseline.Plan, changed.Plan);
        Assert.NotEqual(baseline.Fingerprints.SchemaFingerprint, changed.Fingerprints.SchemaFingerprint);
        Assert.NotEqual(baseline.CxtBytes, changed.CxtBytes);
        AssertRendersFormalAttribute(changed.PlanObject, "term_0004-missing");
    }

    [Fact]
    public void Projection_WhenCutsOrStoredCulturePerturb_ThenItDiscriminatesWhileAmbientCultureStaysInvariant()
    {
        // The structural projection must enumerate a discretizer's cut VECTOR (record
        // ToString() renders the collection as its type, hiding the values), its stored parsing
        // CULTURE (a semantic field, §11.2/D-061), and must format numbers invariantly (an
        // ambient comma-decimal culture must not change the text).
        var cuts1 = ManualCutsDiscretizer.Create([40.0, 80.5, 120.0], BinEnds.Open, CultureInfo.InvariantCulture);
        var cuts2 = ManualCutsDiscretizer.Create([41.0, 80.5, 120.0], BinEnds.Open, CultureInfo.InvariantCulture);
        var cutsFr = ManualCutsDiscretizer.Create([40.0, 80.5, 120.0], BinEnds.Open, new CultureInfo("fr-FR"));
        Assert.True(cuts1.TryGetValue(out var d1));
        Assert.True(cuts2.TryGetValue(out var d2));
        Assert.True(cutsFr.TryGetValue(out var dFr));

        // Nested-field sensitivity: a single perturbed cut moves the projection, and the value is
        // actually present (proving the vector is enumerated, not the collection type).
        Assert.NotEqual(DescribeDiscretizer(d1), DescribeDiscretizer(d2));
        Assert.Contains("80.5", DescribeDiscretizer(d1), StringComparison.Ordinal);
        Assert.Contains("40", DescribeDiscretizer(d1), StringComparison.Ordinal);

        // Stored-culture sensitivity: identical cuts but a different PARSING culture are
        // semantically different discretizers (they parse "80,5" vs "80.5"), so the projection
        // must distinguish them — this is stored culture, not ambient formatting.
        Assert.NotEqual(DescribeDiscretizer(d1), DescribeDiscretizer(dFr));
        Assert.Contains("culture=fr-FR", DescribeDiscretizer(dFr), StringComparison.Ordinal);

        // Ambient-culture invariance: under a comma-decimal AMBIENT culture the projection text is
        // unchanged — "80.5", never "80,5" — and both the invariant and the fr-FR-stored
        // projections are ambient-independent.
        var invariant = DescribeDiscretizer(d1);
        var stored = DescribeDiscretizer(dFr);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal(invariant, DescribeDiscretizer(d1));
            Assert.Equal(stored, DescribeDiscretizer(dFr));
            Assert.Contains("80.5", DescribeDiscretizer(d1), StringComparison.Ordinal);
            Assert.DoesNotContain("80,5", DescribeDiscretizer(d1), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---------------------------------------------------------------------
    // 3. Uncurated shadow visibility and neutrality
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Uncurated_WhenTheDraftFieldsShadowTheTemplate_ThenAllThreeFieldsLoseTheWarningFiresAndTheOutputIsNeutral()
    {
        var uncurated = await ConvertAsync(AdSpecs.Uncurated(withTemplateMatcher: true));

        // Before the warning: every selected term resolved to the DRAFT's explicit identity +
        // nominal + observed domain — not the template's dichotomic/["1","0"]. Nominal proves the
        // scale field lost; the observed domain (["0","1"] on odd-index terms) proves the domain
        // field lost.
        AssertTermsAreDraftNominal(uncurated.Spec);

        // The full-width inventory is still exactly 1,559 with its authored names.
        AssertAuthoredInventory(uncurated.Spec.Attributes);

        // Exactly one MatcherFullyShadowed, in the resolve stage, and NOTHING else in any stage.
        AssertExactDiagnostics(uncurated, ("resolve", DiagnosticCode.MatcherFullyShadowed));
        AssertNoErrors(uncurated);

        // Neutrality: byte/fingerprint/plan/effective-attribute identical to the SAME document
        // minus the template and matcher (independently built), which itself has no warning.
        var shadowBaseline = await ConvertAsync(AdSpecs.Uncurated(withTemplateMatcher: false));
        AssertExactDiagnostics(shadowBaseline);
        AssertIdentical(shadowBaseline, uncurated);
    }

    // ---------------------------------------------------------------------
    // 4. Draft-level de-shadow equivalence
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Deshadow_WhenTermsAreMatcherWonVersusMaterialized_ThenTheyConvergeAndTheMatcherConfiguresAllAndOnlyTheTerms()
    {
        // Both forms keep the probe-style STRING non-term columns (isolating the de-shadow
        // property from the declarative numeric curation) and differ only in the terms.
        var matcherWon = await ConvertAsync(AdSpecs.DeshadowMatcher());
        var materialized = await ConvertAsync(AdSpecs.DeshadowMaterialized());

        AssertIdentical(matcherWon, materialized);

        // Clean scale boundary with literal 3/4 and 1557/1558 pins: index 3 nominal (string),
        // 4 and 1557 dichotomic (matcher), 1558 nominal — and exactly 1,554 dichotomic, all inside
        // [4, 1557].
        AssertMatcherBoundary(matcherWon.Spec);
        AssertMatcherTermConfig(matcherWon.Spec);

        // The declarative de-shadow form has no shadow / zero-selection warning.
        AssertExactDiagnostics(matcherWon);
    }

    // ---------------------------------------------------------------------
    // 5. Real probe → curate → convert → export journey
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Probe_WhenTheRealDraftIsCuratedAndConverted_ThenTheOrderedDraftCarriesThroughToBothOutputs()
    {
        var readSettings = SourceReadSettings.CreateWide(hasHeader: false);

        var probe = await ProbeAsync(readSettings);
        Assert.True(probe.TryGetValue(out var draft), Describe(probe.Diagnostics));

        // A successful probe over a clean, non-truncating, headerless source emits NO diagnostic
        // (headerless column_N synthesis is not an adjustment; no domain truncates).
        Assert.Empty(probe.Diagnostics);

        // The full ordered draft inventory: position i is exactly `column_i` bound to index i,
        // string identity + nominal + its complete observed domain; names and indexes are globally
        // unique and cover 0…1558. A permuted/duplicated/omitted probe fails this.
        AssertProbeInventory(draft);

        // Sensitivity: reversing the returned attributes breaks the ordered inventory immediately.
        Assert.ThrowsAny<Exception>(() => AssertProbeInventory(draft with { Attributes = [.. draft.Attributes.Reverse()] }));

        // Curate the RETURNED document: add template/matcher, strip the three shadowing fields from
        // exactly indexes 4…1557, preserve source bindings and all non-term configuration, attach
        // the required provenance.
        var curated = Curate(draft);

        // Curation preservation, proven positionally against the draft.
        for (var i = 0; i < Columns; i++)
        {
            var before = draft.Attributes[i];
            var after = curated.Attributes[i];
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Source, after.Source); // record equality: source binding preserved
            if (i is >= FirstTerm and <= LastTerm)
            {
                Assert.Null(after.Discretizer);
                Assert.Null(after.Scale);
                Assert.Null(after.DeclaredDomain);
            }
            else
            {
                Assert.Equal(before.Discretizer, after.Discretizer);
                Assert.Equal(before.Scale, after.Scale);
                Assert.Equal(before.DeclaredDomain, after.DeclaredDomain);
            }
        }

        Assert.Equal(AdSpecs.SourceUrl, curated.Provenance!.SourceUrl);
        Assert.Equal(AdSpecs.Notes, curated.Provenance.Notes);

        // One continuous real-object journey through the actual chain.
        var converted = await ConvertDocumentAsync(curated, [], AdCorpus.Csv);

        AssertNoErrors(converted);
        AssertExactDiagnostics(converted); // matcher wins on the bare-again terms: no warning

        // Exactly the 1,554 terms are now matcher-configured dichotomic; the rest stayed the
        // probe's string nominal.
        var dichotomic = 0;
        for (var i = 0; i < Columns; i++)
        {
            if (converted.Spec.Attributes[i].Scale is DichotomicScale)
            {
                Assert.InRange(i, FirstTerm, LastTerm);
                dichotomic++;
            }
        }

        Assert.Equal(Terms, dichotomic);

        // All three fingerprints and non-empty exact bytes, plus representative emitted incidences.
        Assert.NotEmpty(converted.Fingerprints.SchemaFingerprint);
        Assert.NotEmpty(converted.Fingerprints.CxtOutputFingerprint);
        Assert.NotEmpty(converted.Fingerprints.DatOutputFingerprint);
        Assert.NotEmpty(converted.CxtBytes);
        Assert.NotEmpty(converted.DatBytes);
        AssertProbeIncidences(converted);
    }

    // ---------------------------------------------------------------------
    // 6. Repeatability
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Workflow_WhenRunTwiceFromIndependentDocumentsAndInputBytes_ThenArtifactsAndAllStageDiagnosticsAreIdentical()
    {
        // Independently generated input bytes (not a shared static corpus), asserted equal, then
        // fed to two independent conversions.
        var csvA = AdCorpus.GenerateCsv();
        var csvB = AdCorpus.GenerateCsv();
        Assert.Equal(csvA, csvB);
        Assert.Equal(AdSpecs.Declarative(), AdSpecs.Declarative());

        var first = await ConvertAsync(AdSpecs.Declarative(), csvA);
        var second = await ConvertAsync(AdSpecs.Declarative(), csvB);
        AssertIdentical(first, second);
        AssertSameDiagnosticStream(first, second);

        // A form that actually PRODUCES a diagnostic (the fully-shadowed matcher), so the ordered
        // stage-labelled tuple comparison is non-vacuous.
        var shadowA = await ConvertAsync(AdSpecs.Uncurated(withTemplateMatcher: true));
        var shadowB = await ConvertAsync(AdSpecs.Uncurated(withTemplateMatcher: true));
        AssertIdentical(shadowA, shadowB);
        AssertSameDiagnosticStream(shadowA, shadowB);
        Assert.NotEmpty(FullDiagnosticStream(shadowA));

        // The real probe journey, twice: probe is deterministic and the curated conversion is
        // byte- and diagnostic-identical.
        var settings = SourceReadSettings.CreateWide(hasHeader: false);
        var probe1 = await ProbeAsync(settings);
        var probe2 = await ProbeAsync(settings);
        Assert.True(probe1.TryGetValue(out var draft1));
        Assert.True(probe2.TryGetValue(out var draft2));
        Assert.Empty(probe1.Diagnostics);
        Assert.Empty(probe2.Diagnostics);
        Assert.Equal(DescribeDraft(draft1), DescribeDraft(draft2));

        var journey1 = await ConvertDocumentAsync(Curate(draft1), [], AdCorpus.Csv);
        var journey2 = await ConvertDocumentAsync(Curate(draft2), [], AdCorpus.Csv);
        AssertIdentical(journey1, journey2);
        AssertSameDiagnosticStream(journey1, journey2);
    }

    // ---------------------------------------------------------------------
    // 7. Closing locks
    // ---------------------------------------------------------------------

    [Fact]
    public void ClosingLocks_WhenTheRegistryAndM6ContractAreInspected_ThenTheExitStateHolds()
    {
        var defined = Enum.GetNames<DiagnosticCode>();

        // 81 at the M6 exit, plus OutputCxtSizeAdvisory landed at its M7 Slice B export emit site
        // (D-123) — the one ruled registry move, 81 → 82. The M6 contract below is unchanged.
        Assert.Equal(82, defined.Length);

        foreach (var code in new[]
                 {
                     DiagnosticCode.TemplateIdMissing,
                     DiagnosticCode.TemplateIdDuplicate,
                     DiagnosticCode.TemplateReferenceUnknown,
                     DiagnosticCode.MatcherSelectorInvalidForShape,
                     DiagnosticCode.MatcherSelectsNoAttributes,
                     DiagnosticCode.MatcherFullyShadowed,
                     DiagnosticCode.FormalAttributeNameInvalid,
                 })
        {
            Assert.Contains(code.ToString(), defined);
        }

        Assert.DoesNotContain("TemplateMatcherNotImplementedV1", defined);
        Assert.Contains(nameof(DiagnosticCode.SpecSurfaceNotYetSupported), defined);
    }

    // ---------------------------------------------------------------------
    // Provenance: authored on every form, and fingerprint-inert
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Provenance_IsAuthoredOnEveryGeneratedFormAndIsFingerprintInert()
    {
        // Every generated exit spec authors the exact Kushmerick/UCI provenance — parsed and
        // asserted on the document, not merely present in the header text.
        foreach (var toml in new[]
                 {
                     AdSpecs.Declarative(),
                     AdSpecs.Declarative(includeMatcher: false),
                     AdSpecs.Materialized(),
                     AdSpecs.Materialized(term0004MissingColumn: true),
                     AdSpecs.Uncurated(withTemplateMatcher: true),
                     AdSpecs.Uncurated(withTemplateMatcher: false),
                     AdSpecs.DeshadowMatcher(),
                     AdSpecs.DeshadowMaterialized(),
                 })
        {
            AssertProvenance(toml);
        }

        // Inertness, proven directly: the SAME resolved document with vs without provenance
        // produces identical schema/cxt/dat fingerprints AND byte-identical .cxt/.dat — provenance
        // reaches no fingerprint and no output byte (§4/§14).
        var read = SpecReader.Read(AdSpecs.Declarative());
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));
        Assert.NotNull(document.Provenance);

        var withProvenance = await ConvertDocumentAsync(document, read.Diagnostics, AdCorpus.Csv);
        var withoutProvenance = await ConvertDocumentAsync(document with { Provenance = null }, read.Diagnostics, AdCorpus.Csv);

        Assert.Equal(withProvenance.Fingerprints.SchemaFingerprint, withoutProvenance.Fingerprints.SchemaFingerprint);
        Assert.Equal(withProvenance.Fingerprints.CxtOutputFingerprint, withoutProvenance.Fingerprints.CxtOutputFingerprint);
        Assert.Equal(withProvenance.Fingerprints.DatOutputFingerprint, withoutProvenance.Fingerprints.DatOutputFingerprint);
        Assert.Equal(withProvenance.CxtBytes, withoutProvenance.CxtBytes);
        Assert.Equal(withProvenance.DatBytes, withoutProvenance.DatBytes);
    }

    // ---------------------------------------------------------------------
    // Inventory / boundary oracles (literal contract, independent of the generator's constants)
    // ---------------------------------------------------------------------

    /// <summary>The independently derived logical name for physical column <paramref name="i"/> —
    /// literal boundaries, its own copy of the naming rule, so a generator naming bug is caught.</summary>
    private static string ExpectedAuthoredName(int i) => i switch
    {
        0 => "height",
        1 => "width",
        2 => "aratio",
        3 => "local",
        ClassCol => "class",
        _ => $"term_{i:D4}",
    };

    /// <summary>Locks the ordered inventory of an authored form: exactly 1,559 attributes, each
    /// position's name and 0-based source index, global name/index uniqueness, and complete
    /// 0…1558 coverage — all against literals.</summary>
    private static void AssertAuthoredInventory(IReadOnlyList<AttributeSpec> attributes)
    {
        Assert.Equal(Columns, attributes.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new HashSet<int>();
        for (var i = 0; i < Columns; i++)
        {
            var a = attributes[i];
            Assert.Equal(ExpectedAuthoredName(i), a.Name);
            var source = Assert.IsType<ColumnSource>(a.Source);
            Assert.Equal(i, source.Index);
            Assert.True(names.Add(a.Name), $"duplicate name '{a.Name}'");
            Assert.True(indexes.Add(source.Index), $"duplicate index {source.Index}");
        }

        Assert.Equal(Columns, names.Count);
        Assert.Equal(Columns, indexes.Count);
    }

    /// <summary>Every term (physical index 4…1557, literal) is the matcher/template's effective
    /// dichotomic("1") + ["1","0"]; there are exactly 1,554.</summary>
    private static void AssertMatcherTermConfig(BedrockSpec spec)
    {
        Assert.Equal(Columns, spec.Attributes.Count);
        var terms = 0;
        for (var i = FirstTerm; i <= LastTerm; i++)
        {
            var a = spec.Attributes[i];
            Assert.Equal($"term_{i:D4}", a.Name);
            Assert.IsType<IdentityDiscretizer>(a.Discretizer);
            Assert.Equal("1", Assert.IsType<DichotomicScale>(a.Scale).TrueValue);
            Assert.Equal(new[] { "1", "0" }, a.DeclaredDomain);
            terms++;
        }

        Assert.Equal(Terms, terms);
    }

    /// <summary>On the all-string de-shadow form the matcher effect is a clean scale boundary:
    /// index 3 nominal, 4…1557 dichotomic, 1558 nominal — exactly 1,554 dichotomic, all inside the
    /// literal [4, 1557].</summary>
    private static void AssertMatcherBoundary(BedrockSpec spec)
    {
        Assert.IsType<NominalScale>(spec.Attributes[FirstTerm - 1].Scale);  // index 3
        Assert.IsType<DichotomicScale>(spec.Attributes[FirstTerm].Scale);   // index 4
        Assert.IsType<DichotomicScale>(spec.Attributes[LastTerm].Scale);    // index 1557
        Assert.IsType<NominalScale>(spec.Attributes[LastTerm + 1].Scale);   // index 1558

        var dichotomic = 0;
        for (var i = 0; i < Columns; i++)
        {
            if (spec.Attributes[i].Scale is DichotomicScale)
            {
                Assert.InRange(i, FirstTerm, LastTerm);
                dichotomic++;
            }
        }

        Assert.Equal(Terms, dichotomic);
    }

    private static void AssertTermsAreDraftNominal(BedrockSpec spec)
    {
        Assert.Equal(Columns, spec.Attributes.Count);
        for (var i = FirstTerm; i <= LastTerm; i++)
        {
            var a = spec.Attributes[i];
            Assert.IsType<IdentityDiscretizer>(a.Discretizer);
            Assert.IsType<NominalScale>(a.Scale);                 // not the template's dichotomic
            Assert.Equal(AdCorpus.DomainOf(i), a.DeclaredDomain); // observed, not the template's ["1","0"]
        }
    }

    // ---- The layout gate (literal width + partition) ----

    private sealed record LayoutCoverage(
        bool SawTermOne, bool SawTermZero, bool SawTermMissing, bool SawNumericMissing, IReadOnlySet<string> Classes);

    /// <summary>Validates a CSV against the literal ad.data layout, re-splitting the text. Throws
    /// on any deviation, so it doubles as the reduction-sensitivity gate.</summary>
    private static LayoutCoverage AssertAdDataLayout(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(RowCount, lines.Length);

        var sawTermOne = false;
        var sawTermZero = false;
        var sawTermMissing = false;
        var sawNumericMissing = false;
        var classes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var fields = line.Split(',');
            Assert.Equal(Columns, fields.Length);

            for (var col = 0; col <= 2; col++)
            {
                var v = fields[col];
                if (v == AdCorpus.MissingToken)
                {
                    sawNumericMissing = true;
                    continue;
                }

                Assert.True(
                    double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                    $"column {col} value '{v}' is not an invariant-culture number");
            }

            Assert.Contains(fields[3], new[] { "1", "0" });

            for (var col = FirstTerm; col <= LastTerm; col++)
            {
                var v = fields[col];
                Assert.Contains(v, new[] { "1", "0", AdCorpus.MissingToken });
                sawTermOne |= v == "1";
                sawTermZero |= v == "0";
                sawTermMissing |= v == AdCorpus.MissingToken;
            }

            Assert.Contains(fields[ClassCol], new[] { "ad.", "nonad." });
            classes.Add(fields[ClassCol]);
        }

        return new LayoutCoverage(sawTermOne, sawTermZero, sawTermMissing, sawNumericMissing, classes);
    }

    // =====================================================================
    // Probe inventory (ordered, literal)
    // =====================================================================

    private static void AssertProbeInventory(SpecDocument draft)
    {
        Assert.Equal(Columns, draft.Attributes.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new HashSet<int>();
        for (var i = 0; i < Columns; i++)
        {
            var a = draft.Attributes[i];
            Assert.Equal($"column_{i}", a.Name); // headerless synthesis (AttributeNaming.FallbackName)
            var source = Assert.IsType<ColumnSourceSection>(a.Source);
            Assert.Equal(i, source.Index);
            Assert.Null(source.Name);
            Assert.Equal(SourceValueType.String, source.ValueType);
            Assert.IsType<IdentityDiscretizerSection>(a.Discretizer);
            Assert.IsType<NominalScaleSection>(a.Scale);
            Assert.NotNull(a.DeclaredDomain);
            Assert.Equal(AdCorpus.DomainOf(i), a.DeclaredDomain!); // complete observed domain, cross-checked
            Assert.True(names.Add(a.Name!), $"duplicate name '{a.Name}'");
            Assert.True(indexes.Add(source.Index!.Value), $"duplicate index {source.Index}");
        }

        Assert.Equal(Columns, names.Count);
        Assert.Equal(Columns, indexes.Count);
    }

    private static SpecDocument Curate(SpecDocument draft)
    {
        var attributes = new List<AttributeSection>(draft.Attributes.Count);
        foreach (var attribute in draft.Attributes)
        {
            var index = ((ColumnSourceSection)attribute.Source!).Index!.Value;
            attributes.Add(index is >= FirstTerm and <= LastTerm
                ? attribute with { Discretizer = null, Scale = null, DeclaredDomain = null }
                : attribute);
        }

        return draft with
        {
            Provenance = new ProvenanceSection(
                Author: null, CreatedAt: null, SourceUrl: AdSpecs.SourceUrl,
                SourceHash: null, DerivedFrom: null, Notes: AdSpecs.Notes),
            Templates = [AdSpecs.TermTemplateSection()],
            Matchers = [AdSpecs.TermMatcherSection()],
            Attributes = attributes,
        };
    }

    /// <summary>A deterministic exhaustive projection of a probe draft's ordered attributes, for
    /// probe-determinism comparison.</summary>
    private static string[] DescribeDraft(SpecDocument draft) =>
        [.. draft.Attributes.Select(a => string.Join(
            "|",
            $"name={a.Name}",
            $"source={a.Source}",
            $"discretizer={a.Discretizer}",
            $"scale={a.Scale}",
            $"domain=[{(a.DeclaredDomain is null ? "<none>" : string.Join(",", a.DeclaredDomain))}]"))];

    // =====================================================================
    // Emitted incidences (both formats)
    // =====================================================================

    private static void AssertDeclarativeIncidences(Converted c)
    {
        var row0 = CrossedNames(c, 0);
        Assert.Contains("local", row0);            // local = 1
        Assert.Contains("class-ad.", row0);        // class = ad.
        Assert.Contains("term_0004", row0);        // term_0004 (dichotomic) = 1 at row 0
        Assert.DoesNotContain("class-nonad.", row0);
        Assert.DoesNotContain("aratio-missing", row0);

        var row1 = CrossedNames(c, 1);
        Assert.Contains("class-nonad.", row1);     // class = nonad.
        Assert.Contains("aratio-missing", row1);   // aratio = ? under as_attribute
        Assert.DoesNotContain("local", row1);      // local = 0
        Assert.DoesNotContain("class-ad.", row1);

        // Numeric binning discriminates: rows 0 (125, top bin) and 3 (33, bottom bin) each cross
        // exactly one height bin, and different ones.
        var height0 = Assert.Single(row0, n => n.StartsWith("height-", StringComparison.Ordinal));
        var height3 = Assert.Single(CrossedNames(c, 3), n => n.StartsWith("height-", StringComparison.Ordinal));
        Assert.NotEqual(height0, height3);

        // Missing-term behaviour: at row 4 the term column at physical index 7 is `?`, so its
        // dichotomic column must NOT cross (a missing token is not "true"), while its non-missing
        // neighbours at indexes 6 and 8 do cross.
        var row4 = CrossedNames(c, 4);
        Assert.DoesNotContain("term_0007", row4);
        Assert.Contains("term_0006", row4);
        Assert.Contains("term_0008", row4);

        AssertBothFormatsAgreeOnObject(c, 0);
        AssertBothFormatsAgreeOnObject(c, 1);
        AssertBothFormatsAgreeOnObject(c, 4); // exact CXT/DAT serialization of the missing-term row
    }

    private static void AssertProbeIncidences(Converted c)
    {
        // Term dichotomic emit (both outcomes across rows) and class nominal emit (both classes).
        var row0 = CrossedNames(c, 0);
        Assert.Contains("column_4", row0);          // term at index 4, dichotomic, = 1 at row 0
        Assert.Contains("column_1558-ad.", row0);   // class nominal
        Assert.DoesNotContain("column_1558-nonad.", row0);

        var row1 = CrossedNames(c, 1);
        Assert.Contains("column_1558-nonad.", row1);
        Assert.DoesNotContain("column_4", row1);     // term = 0 at row 1
        Assert.DoesNotContain("column_1558-ad.", row1);

        // Missing-term behaviour on the real-probe path: index 7 is `?` at row 4, so its
        // dichotomic column must NOT cross; its non-missing neighbour at index 6 does.
        var row4 = CrossedNames(c, 4);
        Assert.DoesNotContain("column_7", row4);
        Assert.Contains("column_6", row4);

        AssertBothFormatsAgreeOnObject(c, 0);
        AssertBothFormatsAgreeOnObject(c, 1);
        AssertBothFormatsAgreeOnObject(c, 4); // exact CXT/DAT serialization of the missing-term row
    }

    /// <summary>The emitted object's crossed formal-attribute ids as their rendered names.</summary>
    private static IReadOnlySet<string> CrossedNames(Converted c, int objectIndex)
    {
        var byId = c.PlanObject.FormalAttributes.ToDictionary(f => f.Id, f => f.RenderedName);
        return c.Objects[objectIndex].CrossedFormalAttributeIds.Select(id => byId[id]).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Both output formats serialize exactly the emitted object's crosses: the .cxt matrix
    /// row's 'X' positions equal the crossed ids, and the .dat line's tokens are the base-indexed
    /// ids in order.</summary>
    private static void AssertBothFormatsAgreeOnObject(Converted c, int objectIndex)
    {
        var expected = c.Objects[objectIndex].CrossedFormalAttributeIds;

        // .cxt: matrix column index == formal id (CxtWriter fills row[id] = 'X').
        var lines = c.Cxt.Split('\n');
        var objectCount = int.Parse(lines[2], CultureInfo.InvariantCulture);
        var attributeCount = int.Parse(lines[3], CultureInfo.InvariantCulture);
        Assert.Equal(c.PlanObject.FormalAttributes.Count, attributeCount);
        var matrixRow = lines[5 + objectCount + attributeCount + objectIndex];
        Assert.Equal(attributeCount, matrixRow.Length);
        // Both are ascending id order (§17 rule 8), so a plain sequence comparison is exact.
        var crossedInCxt = Enumerable.Range(0, matrixRow.Length).Where(j => matrixRow[j] == 'X').ToList();
        Assert.Equal(expected, crossedInCxt);

        // .dat: base-indexed ids in ascending order (§17 rule 8).
        var datLine = c.Dat.Split('\n')[objectIndex];
        var datTokens = datLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            expected.Select(id => (id + WriterOptions.Native.BaseIndex).ToString(CultureInfo.InvariantCulture)),
            datTokens);
    }

    // =====================================================================
    // Diagnostics (stage-labelled, exact)
    // =====================================================================

    private static void AssertNoErrors(Converted c)
    {
        foreach (var stage in c.Stages)
        {
            Assert.DoesNotContain(stage.Diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
        }
    }

    /// <summary>The complete ordered (stage, code) stream across EVERY stage must equal
    /// <paramref name="expected"/> exactly — so an unapproved warning in any stage fails.</summary>
    private static void AssertExactDiagnostics(Converted c, params (string Stage, DiagnosticCode Code)[] expected)
    {
        var actual = c.Stages
            .SelectMany(s => s.Diagnostics.Select(d => (s.Stage, d.Code)))
            .ToArray();
        Assert.Equal(expected, actual);
    }

    private static (string Stage, DiagnosticCode Code, DiagnosticSeverity Severity, string? Location, string Message)[]
        FullDiagnosticStream(Converted c) =>
        [.. c.Stages.SelectMany(s => s.Diagnostics.Select(d => (s.Stage, d.Code, d.Severity, d.Location?.ToString(), d.Message)))];

    private static void AssertSameDiagnosticStream(Converted first, Converted second) =>
        Assert.Equal(FullDiagnosticStream(first), FullDiagnosticStream(second));

    // =====================================================================
    // Equivalence (byte[])
    // =====================================================================

    private static void AssertIdentical(Converted expected, Converted actual)
    {
        Assert.Equal(expected.ResolvedAttributes, actual.ResolvedAttributes);
        Assert.Equal(expected.Plan, actual.Plan);

        Assert.Equal(expected.Fingerprints.SchemaFingerprint, actual.Fingerprints.SchemaFingerprint);
        Assert.Equal(expected.Fingerprints.CxtOutputFingerprint, actual.Fingerprints.CxtOutputFingerprint);
        Assert.Equal(expected.Fingerprints.DatOutputFingerprint, actual.Fingerprints.DatOutputFingerprint);

        Assert.Equal(expected.CxtBytes, actual.CxtBytes);
        Assert.Equal(expected.DatBytes, actual.DatBytes);
    }

    private static void AssertProvenance(string toml)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));
        Assert.NotNull(document.Provenance);
        Assert.Equal(AdSpecs.SourceUrl, document.Provenance!.SourceUrl);
        Assert.Equal(AdSpecs.Notes, document.Provenance.Notes);
    }

    private static void AssertRendersFormalAttribute(ConversionPlan plan, string renderedName) =>
        Assert.Contains(plan.FormalAttributes, f => f.RenderedName == renderedName);

    // =====================================================================
    // Explicit, invariant structural projections
    // =====================================================================

    private static string DescribeSource(SourceBinding source) => source switch
    {
        ColumnSource c => $"column(index={c.Index},type={c.ValueType})",
        PredicateSource p => $"predicate(name={p.Predicate},type={p.ValueType})",
        _ => source.ToString()!,
    };

    private static string DescribeAttributeSource(AttributeSource source) => source switch
    {
        ColumnAttributeSource c => $"column(index={c.Index})",
        PredicateAttributeSource p => $"predicate(name={p.Predicate})",
        _ => source.ToString()!,
    };

    private static string DescribeDiscretizer(Discretizer? discretizer) => discretizer switch
    {
        null => "none",
        IdentityDiscretizer => "identity",
        ManualCutsDiscretizer m =>
            $"manual_cuts(ends={m.Ends},culture={m.Culture.Name},cuts=[{string.Join(",", m.Cuts.Select(c => Number(c)))}])",
        _ => $"{discretizer.Kind}", // no other kind occurs in this corpus
    };

    private static string DescribeScale(Scale? scale) => scale switch
    {
        null => "none",
        NominalScale => "nominal",
        DichotomicScale d => $"dichotomic(true={d.TrueValue})",
        _ => scale.ToString()!, // no other scale occurs in this corpus
    };

    private static string DescribeIdentity(FormalAttributeIdentity id) =>
        $"identity(name={id.AttributeName},scale={id.Scale},bin={id.BinKey},op={id.Operator})";

    private static string DescribeBin(CanonicalBin bin) => bin switch
    {
        ValueBin v => $"value({v.Label})",
        NumericCutBin n => $"numeric(lo={Number(n.Lo)},hi={Number(n.Hi)})",
        TextCutBin t => $"text(lo={t.Lo ?? "<inf>"},hi={t.Hi ?? "<inf>"})",
        _ => bin.ToString()!,
    };

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(double? value) => value is { } v ? Number(v) : "<inf>";

    private static string[] DescribeAttributes(BedrockSpec spec) =>
        [.. spec.Attributes.Select(a => string.Join(
            "|",
            $"name={a.Name}",
            $"source={DescribeSource(a.Source)}",
            $"include={a.Include}",
            $"discretizer={DescribeDiscretizer(a.Discretizer)}",
            $"scale={DescribeScale(a.Scale)}",
            $"domain=[{string.Join(",", a.DeclaredDomain ?? [])}]",
            $"restrict=[{string.Join(",", a.RestrictTo)}]",
            $"labels=[{string.Join(",", a.ValueLabels.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}]",
            $"missing={a.MissingPolicy}",
            $"unknown={a.UnknownValuePolicy}",
            $"display={a.DisplayName}",
            $"format={a.NameFormat?.Text ?? "<none>"}"))];

    private static string[] DescribePlan(ConversionPlan plan)
    {
        var lines = new List<string>();

        foreach (var formal in plan.FormalAttributes)
        {
            lines.Add(string.Join(
                "|",
                $"formal#{formal.Id.ToString(CultureInfo.InvariantCulture)}",
                $"rendered={formal.RenderedName}",
                $"identity={DescribeIdentity(formal.Identity)}",
                $"bin={DescribeBin(formal.Bin)}"));
        }

        foreach (var attribute in plan.Attributes)
        {
            var crosses = attribute.CrossesByBin
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}=>[{string.Join(",", p.Value)}]");
            lines.Add(string.Join(
                "|",
                $"planned={attribute.Name}",
                $"source={DescribeAttributeSource(attribute.Source)}",
                $"discretizer={DescribeDiscretizer(attribute.Discretizer)}",
                $"knownBins=[{string.Join(",", attribute.KnownBins.OrderBy(b => b, StringComparer.Ordinal))}]",
                $"crosses=[{string.Join(";", crosses)}]",
                $"missingId={attribute.MissingFormalAttributeId?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}",
                $"unknown={attribute.UnknownValuePolicy}"));
        }

        foreach (var restriction in plan.Restrictions)
        {
            lines.Add(string.Join(
                "|",
                $"restriction={restriction.AttributeName}",
                $"source={DescribeAttributeSource(restriction.Source)}",
                $"valueType={restriction.ValueType}",
                $"entries=[{string.Join(",", restriction.Entries)}]",
                $"unknown={restriction.UnknownValuePolicy}"));
        }

        lines.Add($"objectKey={plan.ObjectKey}");
        lines.Add($"execution={plan.Execution}");
        lines.Add($"labelStyle={plan.LabelStyle}");
        return [.. lines];
    }

    // =====================================================================
    // Harness — the real production chain, plus native fingerprints
    // =====================================================================

    private static async Task<Converted> ConvertAsync(string toml, string? csv = null)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));
        return await ConvertDocumentAsync(document, read.Diagnostics, csv ?? AdCorpus.Csv);
    }

    private static async Task<Converted> ConvertDocumentAsync(
        SpecDocument document, IReadOnlyList<BedrockDiagnostic> readDiagnostics, string csv)
    {
        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), Describe(settings.Diagnostics));

        var session = new WideCsvSession(() => Stream(csv), readSettings);
        var schema = await session.GetSchemaAsync();

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument), Describe(resolved.Diagnostics));

        var token = resolvedDocument.Resolved;
        var source = session.Bind(token);

        var calibrateDiagnostics = new List<BedrockDiagnostic>();
        CalibratedSpec calibrated;
        if (CalibratedSpec.RequiresData(token.Spec))
        {
            var result = await Calibrator.CalibrateAsync(token, source);
            Assert.True(result.TryGetValue(out var calibratedValue), Describe(result.Diagnostics));
            calibrated = calibratedValue;
            calibrateDiagnostics.AddRange(result.Diagnostics);
        }
        else
        {
            calibrated = CalibratedSpec.FromFullyDeclared(token);
        }

        var planned = ConversionPlanner.Plan(calibrated);
        Assert.True(planned.TryGetValue(out var plan), Describe(planned.Diagnostics));

        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit =
            sink => Emitter.EmitAsync(plan, source, sink);

        using var cxtStream = new MemoryStream();
        var cxtDiagnostics = new List<BedrockDiagnostic>();
        using (var replay = EmitReplay.Begin(emit, cxtDiagnostics))
        {
            await CxtWriter.WriteAsync(plan, replay.Open, WriterOptions.Native, cxtStream);
        }

        using var datStream = new MemoryStream();
        var datDiagnostics = new List<BedrockDiagnostic>();
        await DatWriter.WriteAsync(emit(datDiagnostics), WriterOptions.Native, datStream);

        // A third replay to capture the emitted objects for incidence inspection (bounded: 6
        // objects over a tiny corpus — a test convenience, not the streaming production path).
        var objects = new List<EmittedObject>();
        await foreach (var emitted in emit(new List<BedrockDiagnostic>()))
        {
            objects.Add(emitted);
        }

        var cxtBytes = cxtStream.ToArray();
        var datBytes = datStream.ToArray();

        return new Converted(
            cxtBytes,
            datBytes,
            Encoding.UTF8.GetString(cxtBytes),
            Encoding.UTF8.GetString(datBytes),
            SpecFingerprints.ComputeNative(resolvedDocument, plan),
            [
                new StageDiagnostics("read", readDiagnostics),
                new StageDiagnostics("read-settings", settings.Diagnostics),
                new StageDiagnostics("resolve", resolved.Diagnostics),
                new StageDiagnostics("calibrate", calibrateDiagnostics),
                new StageDiagnostics("plan", planned.Diagnostics),
                new StageDiagnostics("cxt-emit", cxtDiagnostics),
                new StageDiagnostics("dat-emit", datDiagnostics),
            ],
            DescribeAttributes(token.Spec),
            DescribePlan(plan),
            token.Spec,
            plan,
            objects);
    }

    private static async Task<Diagnosed<ResolvedDocument>> ResolveOnlyAsync(string toml)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));

        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), Describe(settings.Diagnostics));

        var session = new WideCsvSession(() => Stream(AdCorpus.Csv), readSettings);
        var schema = await session.GetSchemaAsync();
        return SpecResolver.Resolve(document, schema);
    }

    private static async Task<Diagnosed<SpecDocument>> ProbeAsync(SourceReadSettings readSettings)
    {
        var session = new WideCsvSession(() => Stream(AdCorpus.Csv), readSettings);
        return await Prober.ProbeAsync(session, readSettings);
    }

    private static MemoryStream Stream(string csv) => new(Encoding.UTF8.GetBytes(csv));

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private sealed record StageDiagnostics(string Stage, IReadOnlyList<BedrockDiagnostic> Diagnostics);

    private sealed record Converted(
        byte[] CxtBytes,
        byte[] DatBytes,
        string Cxt,
        string Dat,
        ComputedFingerprints Fingerprints,
        IReadOnlyList<StageDiagnostics> Stages,
        IReadOnlyList<string> ResolvedAttributes,
        IReadOnlyList<string> Plan,
        BedrockSpec Spec,
        ConversionPlan PlanObject,
        IReadOnlyList<EmittedObject> Objects);
}
