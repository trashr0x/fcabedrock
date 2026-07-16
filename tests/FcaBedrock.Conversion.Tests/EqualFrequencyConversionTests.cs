using System.Buffers.Binary;
using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// End-to-end <c>equal_frequency</c> conversion (calibrate → plan → emit) over wide and both
/// triple orderings (§11.5, M4 Slice D / D-103), the D-088 auto/frozen byte-equivalence, and
/// the D-095 spill/non-spill byte-equivalence.
/// </summary>
public sealed class EqualFrequencyConversionTests
{
    // The §11.5 worked example: [1,2,2,2,3,4] with bins = 3 and tie_policy = "left" → cuts 3,4.
    private const string TiedCsv = "1\n2\n2\n2\n3\n4";

    private static AttributeSpec Pending(
        string name, int index, int bins, Scale scale,
        TiePolicy tie = TiePolicy.Left, CutPlacement placement = CutPlacement.RightValue,
        UnknownValuePolicy policy = UnknownValuePolicy.Warn, CultureInfo? culture = null) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            new CalibrationPending(new PendingEqualFrequency(bins, tie, placement), culture ?? CultureInfo.InvariantCulture),
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, policy);

    private static AttributeSpec PendingPredicate(
        string name, string predicate, int bins, Scale scale,
        TiePolicy tie = TiePolicy.Left, CutPlacement placement = CutPlacement.RightValue) =>
        new(name, new PredicateSource(predicate, SourceValueType.Number), Include: true,
            new CalibrationPending(new PendingEqualFrequency(bins, tie, placement), CultureInfo.InvariantCulture),
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec Frozen(string name, int index, IReadOnlyList<double> cuts, Scale scale) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!,
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static BedrockSpec Wide(params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Wide(hasHeader: false), attributes);

    private static GroupingOptions TinyBudget() =>
        // Small enough that any multi-distinct population must spill: the D-095 identity is only
        // meaningful if the spill path genuinely runs.
        new(maxBufferedBytes: 308, maxMergeFanIn: 2);

    private static async Task<Diagnosed<CalibratedSpec>> CalibrateAsync(
        BedrockSpec spec, string csv, GroupingOptions? options = null)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        return options is null
            ? await Calibrator.CalibrateAsync(resolved, source)
            : await Calibrator.CalibrateAsync(resolved, source, options, observer: null, CancellationToken.None);
    }

    private static async Task<CalibratedSpec> CalibrateOkAsync(BedrockSpec spec, string csv, GroupingOptions? options = null)
    {
        var result = await CalibrateAsync(spec, csv, options);
        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return calibrated!;
    }

    private static async Task<Diagnosed<CalibratedSpec>> CalibrateTripleAsync(
        BedrockSpec spec, string data, GroupingOptions? options = null)
    {
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        return options is null
            ? await Calibrator.CalibrateTripleAsync(resolved, source)
            : await Calibrator.CalibrateTripleAsync(resolved, source, options, observer: null, CancellationToken.None);
    }

    private static IReadOnlyList<double> CutsOf(CalibratedSpec calibrated, string attribute) =>
        Assert.IsType<CalibratedCuts>(calibrated.Calibrations.Single(c => c.AttributeName == attribute)).Cuts;

    // --- Wide calibration ------------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenTiedPopulation_ThenTheSpecExampleCuts()
    {
        // §11.5's own worked example, end to end through the real pipeline: two boundaries fall
        // inside the tied 2-run, and the formula must still yield TWO distinct ascending cuts.
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv);

        Assert.Equal([3.0, 4.0], CutsOf(calibrated, "score"));
        var discretizer = Assert.IsType<EqualFrequencyDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);
        Assert.Equal([3.0, 4.0], discretizer.Cuts);
        Assert.Equal(3, discretizer.Bins);
    }

    [Theory]
    [InlineData(TiePolicy.Left, 3.0)]
    [InlineData(TiePolicy.Right, 2.0)]
    public async Task Calibrate_WhenBoundaryLandsInsideATiedRun_ThenTiePolicyDecidesTheSide(TiePolicy tie, double expected)
    {
        // §11.5's second example: [1,2,2,2,3] with bins = 2 — "left" puts the whole 2-group in the
        // lower bin (cut 3), "right" in the upper (cut 2). Neither ever splits the group.
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale(), tie)), "1\n2\n2\n2\n3");

        Assert.Equal([expected], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenMidpointPlacement_ThenCutsSitBetweenTheGapValues()
    {
        // [5,5,5,9] with bins = 2 and "right": d = 0 is infeasible, so the window takes gap 1 =
        // (5, 9); midpoint places the cut at 5 + (9-5)/2 = 7.
        var calibrated = await CalibrateOkAsync(
            Wide(Pending("score", 0, 2, new NominalScale(), TiePolicy.Right, CutPlacement.Midpoint)), "5\n5\n5\n9");

        Assert.Equal([7.0], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenEveryRowIsAnIndependentObservation_ThenRepeatsShiftTheCuts()
    {
        // §7: wide rows are independent observations — no dedup by object key, no
        // duplicate_object_policy. Repeating a value must therefore move the counts, and with them
        // the cuts: [1,2,3,4] alone splits at 3, but weighting 1 heavily drags the boundary down.
        var plain = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale())), "1\n2\n3\n4");
        var weighted = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale())), "1\n1\n1\n1\n1\n2\n3\n4");

        Assert.Equal([3.0], CutsOf(plain, "score"));
        Assert.Equal([2.0], CutsOf(weighted, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenDuplicateObjectKeysExist_ThenTheCountsAreUnaffected()
    {
        // The corollary: the wide population is row-scoped, so an object-key column plays no part
        // in it — the same rows under a keyed binding calibrate identically (D-099).
        var keyed = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep),
            [Pending("score", 1, 2, new NominalScale())]);

        var calibrated = await CalibrateOkAsync(keyed, "k,1\nk,2\nk,3\nk,4");

        Assert.Equal([3.0], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenLocaleIsAuthored_ThenValuesParseUnderIt()
    {
        // P-11: never the ambient locale — the resolved binding.locale governs. The delimiter is
        // a semicolon precisely because de-DE's decimal separator is a comma: a comma-delimited
        // source could not carry these values at all.
        var spec = new BedrockSpec(
            ConversionFixtures.Wide(';', hasHeader: false) with { Locale = "de-DE" },
            [Pending("score", 0, 2, new NominalScale(), culture: CultureInfo.GetCultureInfo("de-DE"))]);

        // Population {1.5, 2.5, 3.5, 4.5}: N = 4, C = [1,2,3,4]. k = 1 targets N·k = 4, and
        // C_2·bins = 4 → an exact edge → d = 2; the window [1,3] leaves it → cut = v_3 = 3.5.
        // Under the invariant culture these cells would not parse at all.
        var calibrated = await CalibrateOkAsync(spec, "1,5\n2,5\n3,5\n4,5");

        Assert.Equal([3.5], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenFewerDistinctValuesThanBins_ThenCalibrationDataInsufficient()
    {
        // §11.5's distinct-value guard: count-placed bins genuinely cannot exist without enough
        // distinct values to separate them, so calibration stops rather than silently producing
        // fewer bins. (equal_width has no such guard — its bins are placed by span, D-089.)
        var result = await CalibrateAsync(Wide(Pending("score", 0, 3, new NominalScale())), "1\n1\n2\n2");

        Assert.False(result.IsOk); // in-path Error: no calibrated result (D-095)
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.CalibrationDataInsufficient, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("score", diagnostic.Location?.AttributeName);
        Assert.Contains("2 distinct usable value(s)", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calibrate_WhenDistinctEqualsBins_ThenAccepted() =>
        // The boundary of the guard: m == bins is the tightest feasible shape, not a failure.
        Assert.Equal([2.0, 3.0], CutsOf(await CalibrateOkAsync(Wide(Pending("score", 0, 3, new NominalScale())), "1\n2\n3"), "score"));

    [Fact]
    public async Task Calibrate_WhenPopulationIsEmpty_ThenCalibrationDataInsufficient()
    {
        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), "?\n?");

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.CalibrationDataInsufficient);
    }

    [Theory]
    [InlineData(UnknownValuePolicy.Warn, DiagnosticSeverity.Warning)]
    [InlineData(UnknownValuePolicy.Include, DiagnosticSeverity.Warning)]
    public async Task Calibrate_WhenUnparseableUnderANonFatalPolicy_ThenExcludedAndAggregated(
        UnknownValuePolicy policy, DiagnosticSeverity severity)
    {
        // §11.5/D-100: present-but-unparseable values are excluded from the population — they never
        // influence a cut — and reported as this phase's own aggregated SourceValueUnparseable.
        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale(), policy: policy)), "1\nwibble\n2\n3");

        Assert.True(result.TryGetValue(out var calibrated));

        // Exactly as if "wibble" were never in the file: population {1,2,3}, N = 3, C = [1,2,3].
        // k = 1 targets 3, strictly inside group 2 (2 < 3 < 4) → "left" → d = 2 → cut = v_3 = 3.
        Assert.Equal([3.0], CutsOf(calibrated!, "score"));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Equal(severity, diagnostic.Severity);
        Assert.Equal("score", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public async Task Calibrate_WhenUnparseableUnderSkip_ThenSilent()
    {
        var result = await CalibrateAsync(
            Wide(Pending("score", 0, 2, new NominalScale(), policy: UnknownValuePolicy.Skip)), "1\nwibble\n2\n3");

        Assert.True(result.TryGetValue(out _));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
    }

    [Fact]
    public async Task Calibrate_WhenUnparseableUnderFail_ThenErrorAndNoCalibratedResult()
    {
        var result = await CalibrateAsync(
            Wide(Pending("score", 0, 2, new NominalScale(), policy: UnknownValuePolicy.Fail)), "1\nwibble\n2\n3");

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable).Severity);
    }

    [Fact]
    public async Task Calibrate_WhenNonFiniteValues_ThenExcludedFromThePopulation()
    {
        // §7: a numeric value contributes only when it parses to a FINITE number — NaN and ±∞ are
        // present-but-invalid, not missing, and never influence a cut. The population is therefore
        // {1,2,3} and the cut is 3, exactly as in the unparseable case above.
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale())), "1\nNaN\nInfinity\n2\n3");

        Assert.Equal([3.0], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenNoObservedDomainIsNeeded_ThenNoDomainWarningAndNoDomainConsumed()
    {
        // §10.3: cut discretizers ignore declared_domain, so the observed-domain machinery must
        // not fire — only the cut outcome is produced.
        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), "1\n2\n3");

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ObservedDomainUsed);
        Assert.Empty(calibrated!.Spec.Attributes[0].DeclaredDomain);
        Assert.IsType<CalibratedCuts>(Assert.Single(calibrated.Calibrations));
    }

    [Fact]
    public async Task Calibrate_WhenRepeated_ThenDeterministic()
    {
        // P-7: same spec + same input ⇒ same cuts, every run.
        var first = await CalibrateOkAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv);
        var second = await CalibrateOkAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv);

        Assert.Equal(CutsOf(first, "score"), CutsOf(second, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenCancelled_ThenOperationCanceledPropagatesRatherThanADiagnostic()
    {
        var spec = Wide(Pending("score", 0, 2, new NominalScale()));
        var source = ConversionFixtures.SourceOver(TiedCsv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Cancellation is never converted into a diagnostic (P-14).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Calibrator.CalibrateAsync(resolved, source, cancelled.Token));
    }

    [Fact]
    public async Task Calibrate_WhenSeveralAttributesAreCountSensitive_ThenEachGetsItsOwnCuts()
    {
        var spec = Wide(
            Pending("a", 0, 2, new NominalScale()),
            Pending("z", 1, 2, new NominalScale()));

        var calibrated = await CalibrateOkAsync(spec, "1,10\n2,20\n3,30\n4,40");

        Assert.Equal([3.0], CutsOf(calibrated, "a"));
        Assert.Equal([30.0], CutsOf(calibrated, "z"));
        Assert.Equal(["a", "z"], calibrated.Calibrations.Select(c => c.AttributeName)); // spec-attribute order
    }

    // --- Triple calibration: the §5.3.1 subject-local rule ---------------------

    // Four subjects, each with a "score" observation; s0 repeats its 1 three times. Under §5.3.1
    // the repeats collapse to ONE observation for s0, so the population is {1,2,3,4} → cut 3.
    private const string TripleGrouped =
        "s0,score,1\ns0,score,1\ns0,score,1\n" +
        "s1,score,2\n" +
        "s2,score,3\n" +
        "s3,score,4";

    // The same observations, subject-interleaved: subject_grouped would reject this, and the
    // unordered path must reach the identical population.
    private const string TripleInterleaved =
        "s0,score,1\ns1,score,2\ns2,score,3\ns3,score,4\n" +
        "s0,score,1\ns0,score,1";

    private static BedrockSpec Triple(TripleOrdering ordering, params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Triple(ordering), attributes);

    [Fact]
    public async Task CalibrateTriple_WhenSubjectGroupedWithRepeats_ThenEachDistinctObservationCountsOncePerSubject()
    {
        var result = await CalibrateTripleAsync(
            Triple(TripleOrdering.SubjectGrouped, PendingPredicate("score", "score", 2, new NominalScale())),
            TripleGrouped);

        // Without the dedup s0's value 1 would carry count 3 and drag the boundary to 2.
        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal([3.0], CutsOf(calibrated!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenUnorderedWithRepeats_ThenTheGroupedPassAppliesTheSameRule()
    {
        var result = await CalibrateTripleAsync(
            Triple(TripleOrdering.Unordered, PendingPredicate("score", "score", 2, new NominalScale())),
            TripleInterleaved);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal([3.0], CutsOf(calibrated!, "score"));
    }

    // The discriminating vector for unordered count-sensitive ownership: s0 observes 1 three
    // times, s1 observes 2, s2 observes 3. Deduped the population is {1,2,3} — N = 3, C = [1,2,3]
    // — and boundary 1 targets 3, strictly inside group 2 (2 < 3 < 4), so "left" gives d = 2 and
    // the cut is v_3 = 3.
    //
    // If the raw pass ALSO fed the count-sensitive observer, the counts would be raw multiplicity
    // plus the deduped contribution: 1→4, 2→2, 3→2, N = 8, C = [4,6,8]. Boundary 1 would then
    // target 8 = C_1·bins exactly — an edge → d = 1 → cut v_2 = 2. The two paths disagree, which
    // is what makes this vector a proof rather than a coincidence.
    private const string TripleRepeatsInterleaved =
        "s0,score,1\ns1,score,2\ns2,score,3\ns0,score,1\ns0,score,1";

    [Fact]
    public async Task CalibrateTriple_WhenUnorderedAndASubjectRepeatsAValue_ThenOnlyTheGroupedPassCounts()
    {
        var result = await CalibrateTripleAsync(
            Triple(TripleOrdering.Unordered, PendingPredicate("score", "score", 2, new NominalScale())),
            TripleRepeatsInterleaved);

        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        // 3, not 2: the count-sensitive observer is fed EXCLUSIVELY by the grouped second pass, so
        // s0's three raw rows contribute exactly one observation (§5.3.1/D-103).
        Assert.Equal([3.0], CutsOf(calibrated!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenUnorderedAndSubjectGroupedCarryTheSameRepeats_ThenIdenticalCuts()
    {
        // The same observations, contiguous: both orderings must reach the identical population,
        // which is only true if each feeds the count-sensitive observer exactly once.
        var grouped = await CalibrateTripleAsync(
            Triple(TripleOrdering.SubjectGrouped, PendingPredicate("score", "score", 2, new NominalScale())),
            "s0,score,1\ns0,score,1\ns0,score,1\ns1,score,2\ns2,score,3");
        var unordered = await CalibrateTripleAsync(
            Triple(TripleOrdering.Unordered, PendingPredicate("score", "score", 2, new NominalScale())),
            TripleRepeatsInterleaved);

        Assert.True(grouped.TryGetValue(out var groupedState));
        Assert.True(unordered.TryGetValue(out var unorderedState));
        Assert.Equal([3.0], CutsOf(groupedState!, "score"));
        Assert.Equal(CutsOf(groupedState!, "score"), CutsOf(unorderedState!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenUnorderedAndAValueIsUnparseable_ThenItIsTalliedOncePerPhaseNotTwice()
    {
        // The same ownership rule seen through the diagnostic channel: an unparseable value read
        // by BOTH passes would be counted twice in this phase's aggregate (D-100 makes the rule
        // per-phase, not per-pass).
        var result = await CalibrateTripleAsync(
            Triple(TripleOrdering.Unordered, PendingPredicate("score", "score", 2, new NominalScale())),
            "s0,score,1\ns1,score,wibble\ns2,score,2\ns3,score,3");

        Assert.True(result.TryGetValue(out _),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Contains("had 1 present-but-unparseable", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CalibrateTriple_WhenBothOrderingsCarryTheSameObservations_ThenIdenticalCuts()
    {
        // §17: the triple encoding is just another shape — a subject's rows being scattered cannot
        // change the population, only how it must be gathered.
        var grouped = await CalibrateTripleAsync(
            Triple(TripleOrdering.SubjectGrouped, PendingPredicate("score", "score", 2, new NominalScale())), TripleGrouped);
        var unordered = await CalibrateTripleAsync(
            Triple(TripleOrdering.Unordered, PendingPredicate("score", "score", 2, new NominalScale())), TripleInterleaved);

        Assert.True(grouped.TryGetValue(out var groupedState));
        Assert.True(unordered.TryGetValue(out var unorderedState));
        Assert.Equal(CutsOf(groupedState!, "score"), CutsOf(unorderedState!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenTheSameValueOccursUnderDifferentSubjects_ThenEachSubjectContributesOnce()
    {
        // The dedup is subject-LOCAL, never a dataset-wide seen set (D-095): two subjects both
        // observing 1 contribute two counts, not one — collapsing those would be wrong AND
        // unbounded.
        var result = await CalibrateTripleAsync(
            Triple(TripleOrdering.SubjectGrouped, PendingPredicate("score", "score", 2, new NominalScale())),
            "s0,score,1\ns1,score,1\ns2,score,2\ns3,score,3");

        Assert.True(result.TryGetValue(out var calibrated));

        // Population {1,1,2,3}: N = 4, C = [2,3,4]. k = 1 targets 4; C_1·2 = 4 → an exact edge →
        // d = 1 → cut 2. Had the two 1s collapsed, N would be 3 and the cut would be 3.
        Assert.Equal([2.0], CutsOf(calibrated!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenSpellingsDifferForOneSubject_ThenBothCountBeforeNumericAggregation()
    {
        // §5.3.1's key is the RAW cleaned observation, not the parsed number: "2" and "2.0" are two
        // distinct observations, so both survive the dedup — and only then do they aggregate onto
        // the same numeric value. Population {1, 2, 2, 3}: N = 4, C = [1,3,4]; k = 1 targets 4,
        // strictly inside group 2 (2 < 4 < 6) → "left" → d = 2 → cut 3.
        var result = await CalibrateTripleAsync(
            Triple(TripleOrdering.SubjectGrouped, PendingPredicate("score", "score", 2, new NominalScale())),
            "s0,score,2\ns0,score,2.0\ns1,score,1\ns2,score,3");

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal([3.0], CutsOf(calibrated!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenSubjectNotContiguousUnderSubjectGrouped_ThenTripleSubjectNotContiguous()
    {
        var result = await CalibrateTripleAsync(
            Triple(TripleOrdering.SubjectGrouped, PendingPredicate("score", "score", 2, new NominalScale())),
            TripleInterleaved);

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.TripleSubjectNotContiguous, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task CalibrateTriple_WhenSubjectIsUnusable_ThenObjectKeyValueInvalid()
    {
        var result = await CalibrateTripleAsync(
            Triple(TripleOrdering.Unordered, PendingPredicate("score", "score", 2, new NominalScale())),
            "s0,score,1\n ,score,2\ns2,score,3");

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, Assert.Single(result.Diagnostics).Code);
    }

    // --- Source-pass counting: the grouped second pass is needs-driven --------

    // Wraps a triple source to count how many times it is enumerated, so the pass contract is
    // asserted directly rather than inferred.
    private sealed class CountingTripleSource(ITripleRowSource inner) : ITripleRowSource
    {
        public int Enumerations { get; private set; }

        public SourceProvenance Provenance => inner.Provenance;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            inner.GetSchemaAsync(cancellationToken);

        public async IAsyncEnumerable<TripleRow> ReadRowsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Enumerations++;
            await foreach (var row in inner.ReadRowsAsync(cancellationToken))
            {
                yield return row;
            }
        }
    }

    private static async Task<int> CountTriplePassesAsync(BedrockSpec spec, string data)
    {
        var inner = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        var schema = await inner.GetSchemaAsync();
        var counting = new CountingTripleSource(inner);
        var result = await Calibrator.CalibrateTripleAsync(ConversionFixtures.ResolveFor(spec, schema), counting);
        Assert.True(result.TryGetValue(out _), string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return counting.Enumerations;
    }

    [Fact]
    public async Task CalibrateTriple_WhenSubjectGroupedAndCountSensitive_ThenOneSourcePass() =>
        // Contiguous subjects need no gathering: the dedup runs inline on the raw stream.
        Assert.Equal(1, await CountTriplePassesAsync(
            Triple(TripleOrdering.SubjectGrouped, PendingPredicate("score", "score", 2, new NominalScale())), TripleGrouped));

    [Fact]
    public async Task CalibrateTriple_WhenUnorderedAndCountSensitive_ThenExactlyTwoSourcePasses() =>
        // The raw pass (discovery order + structural checks) plus the grouped pass. Never three:
        // sources are replayable by contract, and this is the D-003-sanctioned bounded pre-pass.
        Assert.Equal(2, await CountTriplePassesAsync(
            Triple(TripleOrdering.Unordered, PendingPredicate("score", "score", 2, new NominalScale())), TripleInterleaved));

    [Fact]
    public async Task CalibrateTriple_WhenUnorderedAndOnlyCountInsensitiveNeedsExist_ThenNoGroupedPass()
    {
        // An observed domain is set-idempotent, so it does not need a subject's rows together —
        // running the grouped pass anyway would cost a whole read for nothing.
        var spec = Triple(TripleOrdering.Unordered,
            ConversionFixtures.PredicateNominal("g", "g", []));

        Assert.Equal(1, await CountTriplePassesAsync(spec, "s0,g,x\ns1,g,y\ns0,g,x"));
    }

    // --- Planning and emit -----------------------------------------------------

    private static async Task<(ConversionPlan Plan, List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)>
        CalibratePlanEmitAsync(BedrockSpec spec, string csv, LabelStyle style = LabelStyle.Native)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var calibrated = await Calibrator.CalibrateAsync(ConversionFixtures.ResolveFor(spec, schema), source);
        Assert.True(calibrated.TryGetValue(out var state),
            string.Join("; ", calibrated.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.True(ConversionPlanner.Plan(state!, style).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (plan, objects, diagnostics);
    }

    [Fact]
    public async Task Plan_WhenNominalOverCalibratedCuts_ThenExactlyBinsColumnsWithOpenEnds()
    {
        var (plan, objects, _) = await CalibratePlanEmitAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv);

        // bins - 1 cuts → bins structural bins, ends open (§11.5).
        Assert.Equal(["score-<3", "score-[3, 4)", "score->=4"], plan.FormalAttributes.Select(f => f.RenderedName));

        // Identities and crosses, not just names: 1/2/2/2 → <3, 3 → [3,4), 4 → >=4.
        Assert.Equal([0], objects[0].CrossedFormalAttributeIds);
        Assert.Equal([0], objects[3].CrossedFormalAttributeIds);
        Assert.Equal([1], objects[4].CrossedFormalAttributeIds);
        Assert.Equal([2], objects[5].CrossedFormalAttributeIds);
    }

    [Fact]
    public async Task Emit_WhenValueIsOutsideTheCalibrationSpan_ThenItStillFallsInAnEndBin()
    {
        // The point of the implicit open ends (§11.4/§11.5): new data outside the calibrated span
        // is binned, never dropped.
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv);
        var discretizer = Assert.IsType<EqualFrequencyDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);

        Assert.Equal(BinResult.Bin("<3"), discretizer.Discretize("-9999"));
        Assert.Equal(BinResult.Bin(">=4"), discretizer.Discretize("9999"));
    }

    [Fact]
    public async Task Plan_WhenDichotomicOverCalibratedCuts_ThenOneColumnForTheTrueBin()
    {
        var (plan, objects, _) = await CalibratePlanEmitAsync(
            Wide(Pending("score", 0, 3, new DichotomicScale("<3"), TiePolicy.Left)), TiedCsv);

        // §12.2: one column, named for the attribute alone — the chosen bin is the column's
        // meaning, not part of its name.
        Assert.Equal(["score"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal([0], objects[0].CrossedFormalAttributeIds); // 1 → in the <3 bin
        Assert.Empty(objects[5].CrossedFormalAttributeIds);      // 4 → not
    }

    [Theory]
    [InlineData(OrdinalDirection.Le, new[] { "score-<3", "score-<4", "score-all" })]
    [InlineData(OrdinalDirection.Ge, new[] { "score-all", "score->=3", "score->=4" })]
    public async Task Plan_WhenOrdinalOverCalibratedCuts_ThenCutGeometryOrdersTheBins(
        OrdinalDirection direction, string[] expected)
    {
        // §12.3: the cut geometry is the ordering authority — equal_frequency needs no ordinal
        // implementation of its own, and the open end renders the tautological `all` (D-047).
        var (plan, _, _) = await CalibratePlanEmitAsync(
            Wide(Pending("score", 0, 3, new OrdinalScale(direction))), TiedCsv);

        Assert.Equal(expected, plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public async Task Emit_WhenOrdinalLeOverCalibratedCuts_ThenCumulativeCrossings()
    {
        var (_, objects, _) = await CalibratePlanEmitAsync(
            Wide(Pending("score", 0, 3, new OrdinalScale(OrdinalDirection.Le))), TiedCsv);

        Assert.Equal([0, 1, 2], objects[0].CrossedFormalAttributeIds); // 1 → below every threshold
        Assert.Equal([2], objects[5].CrossedFormalAttributeIds);       // 4 → only the tautological `all`
    }

    [Fact]
    public async Task Plan_WhenOrdinalDropTop_ThenTheTautologicalColumnIsDropped()
    {
        var (plan, _, _) = await CalibratePlanEmitAsync(
            Wide(Pending("score", 0, 3, new OrdinalScale(OrdinalDirection.Le, DropTop: true))), TiedCsv);

        Assert.Equal(["score-<3", "score-<4"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public async Task Emit_WhenAValueIsUnparseable_ThenTheObjectSurvivesWithNoCrossAndADiagnostic()
    {
        // §11.5/D-050: present-but-invalid, not missing — the object is kept, no cross is emitted,
        // and the emit phase reports its OWN aggregate (the calibrate phase reported its own).
        var (_, objects, diagnostics) = await CalibratePlanEmitAsync(
            Wide(Pending("score", 0, 2, new NominalScale())), "1\n2\n3\nwibble");

        Assert.Equal(4, objects.Count);
        Assert.Empty(objects[3].CrossedFormalAttributeIds);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
    }

    // --- Auto/frozen byte-equivalence (D-088) ---------------------------------

    private static async Task<(byte[] Cxt, byte[] Dat)> ConvertAsync(
        BedrockSpec spec, string csv, WriterOptions options, LabelStyle style, GroupingOptions? grouping = null)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        var calibration = grouping is null
            ? await Calibrator.CalibrateAsync(resolved, source)
            : await Calibrator.CalibrateAsync(resolved, source, grouping, observer: null, CancellationToken.None);
        Assert.True(calibration.TryGetValue(out var calibrated),
            string.Join("; ", calibration.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.True(ConversionPlanner.Plan(calibrated!, style).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        IAsyncEnumerable<EmittedObject> Emit(ICollection<BedrockDiagnostic> sink) => Emitter.EmitAsync(plan, source, sink);

        var cxt = new MemoryStream();
        using (var session = EmitReplay.Begin(Emit, diagnostics))
        {
            await CxtWriter.WriteAsync(plan, session.Open, options, cxt);
        }

        var dat = new MemoryStream();
        await DatWriter.WriteAsync(Emit(diagnostics), options, dat);

        Assert.Empty(diagnostics);
        return (cxt.ToArray(), dat.ToArray());
    }

    public static TheoryData<string, string, int, TiePolicy, CutPlacement> Distributions() => new()
    {
        { "tied", TiedCsv, 3, TiePolicy.Left, CutPlacement.RightValue },
        { "tied-right", "1\n2\n2\n2\n3", 2, TiePolicy.Right, CutPlacement.RightValue },
        { "midpoint", "5\n5\n5\n9", 2, TiePolicy.Right, CutPlacement.Midpoint },
        { "negative-fractional", "-4\n-1\n2.5\n6", 2, TiePolicy.Left, CutPlacement.Midpoint },
        { "saturated", TiedCsv, 4, TiePolicy.Left, CutPlacement.RightValue },
    };

    [Theory]
    [MemberData(nameof(Distributions))]
    public async Task Convert_WhenAutoVsFrozenManualCuts_ThenByteIdenticalCxtAndDat(
        string name, string csv, int bins, TiePolicy tie, CutPlacement placement)
    {
        _ = name;

        // D-088: converting an equal_frequency spec on the fly and converting its calibrate-frozen
        // form (manual_cuts over the resolved cuts, ends = "open") MUST produce byte-identical
        // .cxt and .dat on the calibration dataset — freezing changes WHEN the cuts resolve, never
        // WHICH. Both label styles, because v2-compat rendering is a separate path.
        foreach (var (options, style) in new[]
                 {
                     (WriterOptions.Native, LabelStyle.Native),
                     (WriterOptions.V2Compat, LabelStyle.V2Compat),
                 })
        {
            var auto = Wide(Pending("score", 0, bins, new OrdinalScale(OrdinalDirection.Le), tie, placement));
            var cuts = CutsOf(await CalibrateOkAsync(auto, csv), "score");
            var frozen = Wide(Frozen("score", 0, cuts, new OrdinalScale(OrdinalDirection.Le)));

            var (autoCxt, autoDat) = await ConvertAsync(auto, csv, options, style);
            var (frozenCxt, frozenDat) = await ConvertAsync(frozen, csv, options, style);

            Assert.Equal(frozenCxt, autoCxt);
            Assert.Equal(frozenDat, autoDat);
        }
    }

    [Fact]
    public async Task Convert_WhenAutoVsFrozen_ThenIdenticalIdentitiesAndCrossesAndSchemaFingerprint()
    {
        var auto = Wide(Pending("score", 0, 3, new NominalScale()));
        var frozen = Wide(Frozen("score", 0, [3, 4], new NominalScale()));

        var (autoPlan, autoObjects, _) = await CalibratePlanEmitAsync(auto, TiedCsv);
        var (frozenPlan, frozenObjects, _) = await CalibratePlanEmitAsync(frozen, TiedCsv);

        // Formal identities and crossing incidence, not just rendered names.
        Assert.Equal(frozenPlan.FormalAttributes.Select(f => f.Identity), autoPlan.FormalAttributes.Select(f => f.Identity));
        Assert.Equal(
            frozenObjects.Select(o => o.CrossedFormalAttributeIds),
            autoObjects.Select(o => o.CrossedFormalAttributeIds));

        // Identical effective bins ⇒ identical schema fingerprint (D-094).
        Assert.Equal(
            Core.Fingerprinting.FingerprintCalculator.ComputeSchemaFingerprint(frozenPlan),
            Core.Fingerprinting.FingerprintCalculator.ComputeSchemaFingerprint(autoPlan));
    }

    [Theory]
    [InlineData(LabelStyle.Native)]
    [InlineData(LabelStyle.V2Compat)]
    public async Task Convert_WhenRepeated_ThenByteIdentical(LabelStyle style)
    {
        // P-7: determinism is a test, not an aspiration.
        var options = style == LabelStyle.V2Compat ? WriterOptions.V2Compat : WriterOptions.Native;
        var spec = Wide(Pending("score", 0, 3, new NominalScale()));

        var (firstCxt, firstDat) = await ConvertAsync(spec, TiedCsv, options, style);
        var (secondCxt, secondDat) = await ConvertAsync(spec, TiedCsv, options, style);

        Assert.Equal(firstCxt, secondCxt);
        Assert.Equal(firstDat, secondDat);
    }

    // --- Spill/non-spill equivalence (D-095) ----------------------------------

    [Theory]
    [MemberData(nameof(Distributions))]
    public async Task Calibrate_WhenForcedToSpill_ThenIdenticalCutsToTheInMemoryPath(
        string name, string csv, int bins, TiePolicy tie, CutPlacement placement)
    {
        _ = name;
        var spec = Wide(Pending("score", 0, bins, new NominalScale(), tie, placement));

        var resident = await CalibrateOkAsync(spec, csv);
        var spilled = await CalibrateOkAsync(spec, csv, TinyBudget());

        // §11.5: the spill and non-spill paths MUST produce identical cuts — an approximation
        // under memory pressure would break both determinism and the D-088 equivalence.
        Assert.Equal(CutsOf(resident, "score"), CutsOf(spilled, "score"));
    }

    [Theory]
    [InlineData(LabelStyle.Native)]
    [InlineData(LabelStyle.V2Compat)]
    public async Task Convert_WhenForcedToSpill_ThenByteIdenticalOutputToTheInMemoryPath(LabelStyle style)
    {
        var options = style == LabelStyle.V2Compat ? WriterOptions.V2Compat : WriterOptions.Native;
        var spec = Wide(Pending("score", 0, 4, new OrdinalScale(OrdinalDirection.Le)));

        var (residentCxt, residentDat) = await ConvertAsync(spec, TiedCsv, options, style);
        var (spilledCxt, spilledDat) = await ConvertAsync(spec, TiedCsv, options, style, TinyBudget());

        // The whole obligation in one assertion: bounded memory must not move a single output byte.
        Assert.Equal(residentCxt, spilledCxt);
        Assert.Equal(residentDat, spilledDat);
    }

    [Fact]
    public async Task Calibrate_WhenNothingSpills_ThenNoSpoolStorageIsTouched()
    {
        // The zero-spill path stays entirely in memory — even under an unusable temp root, which is
        // what proves no workspace was created (the lazy-workspace invariant).
        var fileSystem = new FakeSpoolFileSystem { OnCreateWorkspace = () => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(fileSystem: fileSystem);

        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv, options);

        Assert.Equal([3.0, 4.0], CutsOf(calibrated, "score"));
    }

    // --- Storage failures (D-095) ---------------------------------------------

    [Fact]
    public async Task Calibrate_WhenASpillWriteFails_ThenGroupingStorageFailedErrorAndNoCalibratedResult()
    {
        var fileSystem = new FakeSpoolFileSystem { OnCreateRun = _ => StorageFaults.DiskFull() };
        var options = new GroupingOptions(maxBufferedBytes: 308, maxMergeFanIn: 2, fileSystem: fileSystem);

        var result = await CalibrateAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv, options);

        // In-path: Error, no calibrated result — and it crosses the seam as a diagnostic, never as
        // a GroupingStorageException (P-14).
        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task Calibrate_WhenAMergeReadFails_ThenGroupingStorageFailedErrorAndNoCalibratedResult()
    {
        var fileSystem = new FakeSpoolFileSystem { OnOpenRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 308, maxMergeFanIn: 2, fileSystem: fileSystem);

        var result = await CalibrateAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv, options);

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed).Severity);
    }

    [Fact]
    public async Task Calibrate_WhenTheMergeOutputCannotBeCreated_ThenGroupingStorageFailedErrorAndNoCalibratedResult()
    {
        // The MergeWrite channel isolated from the Spill channel: the intake spills succeed and
        // only the merge's OWN output creation fails, so this cannot be mistaken for the
        // already-covered spill-write case.
        var spillsSeen = 0;
        var fileSystem = new FakeSpoolFileSystem();
        fileSystem.OnCreateRun = _ => ++spillsSeen > 3 ? StorageFaults.DiskFull() : null;
        var options = new GroupingOptions(maxBufferedBytes: 308, fileSystem: fileSystem);

        // 8 distinct values at capacity 3 → three spills, then the post-intake merge's output is
        // the fourth run created — the first one this filesystem refuses.
        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), "1\n2\n3\n4\n5\n6\n7\n8", options);

        Assert.False(result.IsOk); // in-path: no calibrated result
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("MergeWrite", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calibrate_WhenAMergeReadFaultsMidStream_ThenGroupingStorageFailedErrorAndNoCalibratedResult()
    {
        // The merge READ-fault channel, distinct from the merge OPEN channel above: the run opens
        // fine and then the device faults mid-read. It must still cross the seam as a diagnostic,
        // never as a GroupingStorageException.
        //
        // This fault is unconditional, so it fires at the merger's first input open — the replay
        // reader is never reached. The replay channel is proved separately, at the accumulator,
        // where the fault can be armed AFTER consolidation completes (QuantileAccumulatorTests).
        var fileSystem = new FakeSpoolFileSystem
        {
            WrapReadStream = (_, inner) =>
            {
                inner.Dispose();
                return new ThrowingReadStream();
            },
        };
        var options = new GroupingOptions(maxBufferedBytes: 308, fileSystem: fileSystem);

        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), "1\n2\n3\n4\n5\n6\n7\n8", options);

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(GroupingOperation.MergeRead, Assert.IsType<GroupingStorageFailure>(diagnostic.Context).Operation);
    }

    [Fact]
    public async Task Calibrate_WhenAMergedRunIsFramedWrong_ThenCorruptRunErrorAndNoCalibratedResult()
    {
        // The framing channel: a run whose bytes decode inconsistently is safely-identifiable
        // corruption, not a malformed row silently accepted into the population. Unconditional, so
        // like the read-fault case above this lands on the merger's input; the replay reader's own
        // framing channel is proved at the accumulator.
        var fileSystem = new FakeSpoolFileSystem
        {
            WrapReadStream = (_, inner) =>
            {
                inner.Dispose();
                var truncated = ForgedRun(1.0, 5);
                return new MemoryStream(truncated[..^3], writable: false); // drops the payload tail
            },
        };
        var options = new GroupingOptions(maxBufferedBytes: 308, fileSystem: fileSystem);

        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), "1\n2\n3\n4\n5\n6\n7\n8", options);

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var failure = Assert.IsType<GroupingStorageFailure>(diagnostic.Context);
        Assert.Contains(failure.Kind, new[] { SpoolFailureKind.TruncatedRun, SpoolFailureKind.CorruptRun });
    }

    [Fact]
    public async Task Calibrate_WhenALaterAttributeFailsStorage_ThenAnEarlierAttributesDiagnosticSurvives()
    {
        // P-14: an aggregating operation collects EVERY diagnostic, not just the one that stopped
        // it. Attribute "a" has too few distinct values to bound its cuts (a data error it
        // diagnoses on its own, in phase 2, first); attribute "z" then hits a storage failure
        // during ITS post-intake merge. The first attribute's diagnostic must survive the second's
        // failure.
        //
        // The fan-in is left at the default deliberately: at a small fan-in "z" would consolidate
        // online during intake and fail there — before any attribute finalizes — which would test
        // a different path entirely. Only a post-intake merge failure exercises this ordering.
        var fileSystem = new FakeSpoolFileSystem { OnOpenRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 308, fileSystem: fileSystem);
        var spec = Wide(
            Pending("a", 0, 3, new NominalScale()),   // only two distinct values → DataInsufficient
            Pending("z", 1, 2, new NominalScale()));  // enough values to spill, then fail its merge

        var result = await CalibrateAsync(spec, "1,10\n1,20\n2,30\n2,40\n1,50\n2,60\n1,70\n2,80", options);

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.CalibrationDataInsufficient && d.Location?.AttributeName == "a");
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
    }

    [Fact]
    public async Task Calibrate_WhenOnlyTheWorkspaceTeardownFails_ThenTheWarningStillReachesTheResult()
    {
        // The ledger is snapshotted AFTER cleanup, so a failure confined to final workspace
        // deletion — recorded by teardown itself — is still reported rather than being written
        // after the returned value was already built (D-095's cleanup-Warning channel).
        var fileSystem = new FakeSpoolFileSystem { OnDeleteWorkspace = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 308, maxMergeFanIn: 2, fileSystem: fileSystem);

        var result = await CalibrateAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv, options);

        // Cleanup-only: calibration still succeeds, and the Warning is present.
        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal([3.0, 4.0], CutsOf(calibrated!, "score"));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("CleanupDelete", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calibrate_WhenOnlyCleanupDeletesFail_ThenWarningAndCalibrationStillSucceeds()
    {
        // The two-channel model (D-082): a cleanup-only failure never costs a correct result.
        var fileSystem = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 308, maxMergeFanIn: 2, fileSystem: fileSystem);

        var result = await CalibrateAsync(Wide(Pending("score", 0, 3, new NominalScale())), TiedCsv, options);

        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal([3.0, 4.0], CutsOf(calibrated!, "score"));
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed).Severity);
    }

    [Fact]
    public async Task Calibrate_WhenAMergeSumOverflows_ThenCalibrationPopulationTooLargeAtTheSeam()
    {
        // G-13's public contract, not just the internal exception: code, Error severity, the
        // owning attribute, no calibrated result, and no GroupingStorageException leaking across
        // the seam. A long.MaxValue-sized fixture is not constructible (the plan forbids trying),
        // so the counts are injected through the SAME failure-injection seam the storage tests use:
        // every run read back is replaced by a forged one-row run carrying long.MaxValue. Merging
        // two such runs is exactly the checked sum G-13 owns.
        var forged = ForgedRun(value: 1.0, count: long.MaxValue);
        var fileSystem = new FakeSpoolFileSystem
        {
            WrapReadStream = (_, inner) =>
            {
                inner.Dispose();
                return new MemoryStream(forged, writable: false);
            },
        };
        var options = new GroupingOptions(maxBufferedBytes: 308, maxMergeFanIn: 2, fileSystem: fileSystem);

        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), "1\n2\n3\n4\n5\n6", options);

        Assert.False(result.IsOk); // in-path: no calibrated result
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.CalibrationPopulationTooLarge);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("score", diagnostic.Location?.AttributeName);

        // Distinct from its neighbours: too MUCH data is not too little, and not a storage fault.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.CalibrationDataInsufficient);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
    }

    // One framed spool record: a length prefix, then rank (int32) + seq (int64) + the codec's
    // fixed 16-byte payload. Hand-built so the count is unreachable by any real observation — and
    // framed correctly, so the merger genuinely decodes it rather than rejecting it as corrupt.
    private static byte[] ForgedRun(double value, long count)
    {
        const int recordLength = SpoolRunFormat.HeaderBytes + ValueCountCodec.PayloadBytes;
        var run = new byte[sizeof(int) + recordLength];
        var span = run.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, recordLength);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 0);   // rank (unused for value ordering)
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], 0);   // seq
        BinaryPrimitives.WriteDoubleLittleEndian(span[16..], value);
        BinaryPrimitives.WriteInt64LittleEndian(span[24..], count);
        return run;
    }

    // --- Tier-2 structural bounds (D-095/D-103) -------------------------------

    [Fact]
    public async Task Calibrate_WhenForcedToSpill_ThenTheStructuralBoundsHold()
    {
        var observer = new RecordingCalibrationObserver();
        var options = new GroupingOptions(maxBufferedBytes: 308, maxMergeFanIn: 2, observer: observer);
        var csv = string.Join('\n', Enumerable.Range(1, 200));
        var spec = Wide(Pending("score", 0, 4, new NominalScale()));
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();

        var result = await Calibrator.CalibrateAsync(
            ConversionFixtures.ResolveFor(spec, schema), source, options, observer, CancellationToken.None);

        Assert.True(result.TryGetValue(out _));

        // Tier 2 is bounded by COUNT and SHAPE — never a pinned byte constant, because a
        // FileStream's internal graph is runtime-owned and any such claim would be unvalidatable.
        Assert.True(observer.PeakOpenReaders <= 2, $"readers peaked at {observer.PeakOpenReaders}");
        Assert.True(observer.PeakLiveRuns <= 2, $"live runs peaked at {observer.PeakLiveRuns}");
        Assert.Equal(0, observer.PeakPendingDeletions);

        // Tier 1: the modeled accumulator aggregate never exceeded its share, and dropped to zero
        // before the post-intake merge.
        Assert.All(observer.Aggregates, bytes => Assert.True(bytes <= Math.Max(308, QuantileAccumulator.FloorBytes)));
        Assert.Equal(0, observer.Aggregates[^1]);
    }

    [Fact]
    public async Task Calibrate_WhenSpecHasNoCountSensitiveAttribute_ThenNoAccumulatorIsAllocated()
    {
        // min/max is bounded by construction (two doubles), so it must never take a budget share
        // or touch the quantile engine (D-102).
        var observer = new RecordingCalibrationObserver();
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [
            new AttributeSpec("score", new ColumnSource(0, SourceValueType.Number), Include: true,
                new CalibrationPending(new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact), CultureInfo.InvariantCulture),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn),
        ]);
        var source = ConversionFixtures.SourceOver(TiedCsv, spec.Binding);
        var schema = await source.GetSchemaAsync();

        var result = await Calibrator.CalibrateAsync(
            ConversionFixtures.ResolveFor(spec, schema), source,
            new GroupingOptions(observer: observer), observer, CancellationToken.None);

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(observer.Sized);
        Assert.Empty(observer.Written);
    }
}
