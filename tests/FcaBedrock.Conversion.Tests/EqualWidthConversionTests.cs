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
/// End-to-end <c>equal_width</c> conversion (calibrate → plan → emit) for both range modes
/// (§11.4, M4 Slice C / D-102), and the D-088 auto/frozen byte-equivalence: converting a
/// data-derived range on the fly and converting its <c>calibrate</c>-frozen <c>manual_cuts</c>
/// form produce byte-identical <c>.cxt</c> and <c>.dat</c> on the calibration dataset.
/// </summary>
public sealed class EqualWidthConversionTests
{
    // 0..100 inclusive: min 0, max 100 → over 4 bins the derived cuts are 25/50/75.
    private const string ScoreCsv = "0\n10\n25\n40\n60\n75\n99\n100";

    private static AttributeSpec Pending(
        string name, int index, int bins, Scale scale, CutPrecision? precision = null,
        UnknownValuePolicy policy = UnknownValuePolicy.Warn, EqualWidthRange range = EqualWidthRange.MinMax,
        CultureInfo? culture = null) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            new CalibrationPending(
                new PendingEqualWidth(bins, range, precision ?? CutPrecision.Exact),
                culture ?? CultureInfo.InvariantCulture),
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, policy);

    private static AttributeSpec Manual(string name, int index, int bins, double vmin, double vmax, Scale scale) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            EqualWidthDiscretizer.CreateManual(bins, vmin, vmax, CutPrecision.Exact, CultureInfo.InvariantCulture).Value!,
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec Frozen(string name, int index, IReadOnlyList<double> cuts, Scale scale) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!,
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static BedrockSpec Wide(params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Wide(hasHeader: false), attributes);

    private static async Task<Diagnosed<CalibratedSpec>> CalibrateAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        return await Calibrator.CalibrateAsync(ConversionFixtures.ResolveFor(spec, schema), source);
    }

    private static async Task<CalibratedSpec> CalibrateOkAsync(BedrockSpec spec, string csv)
    {
        var result = await CalibrateAsync(spec, csv);
        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return calibrated!;
    }

    private static IReadOnlyList<double> CalibratedCutsOf(CalibratedSpec calibrated, string attribute) =>
        Assert.IsType<CalibratedCuts>(calibrated.Calibrations.Single(c => c.AttributeName == attribute)).Cuts;

    // --- Wide min/max calibration --------------------------------------------

    [Fact]
    public async Task Calibrate_WhenWideMinMax_ThenCutsInterpolateTheObservedSpan()
    {
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 4, new NominalScale())), ScoreCsv);

        // min 0 / max 100 over 4 bins → 25/50/75, retained as the manifest-ready outcome (D-093).
        Assert.Equal([25.0, 50.0, 75.0], CalibratedCutsOf(calibrated, "score"));
        var discretizer = Assert.IsType<EqualWidthDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);
        Assert.Equal([25.0, 50.0, 75.0], discretizer.Cuts);
    }

    [Fact]
    public async Task Calibrate_WhenMinMax_ThenNoObservedDomainWarningOrDomainConsumed()
    {
        // §10.3: cut discretizers ignore declared_domain, so the observed-domain machinery must
        // not fire for equal_width — only the cut outcome is produced.
        var result = await CalibrateAsync(Wide(Pending("score", 0, 4, new NominalScale())), ScoreCsv);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ObservedDomainUsed);
        Assert.Empty(calibrated!.Spec.Attributes[0].DeclaredDomain);
        Assert.IsType<CalibratedCuts>(Assert.Single(calibrated.Calibrations));
    }

    [Fact]
    public async Task Calibrate_WhenRoundToPrecision_ThenCutsAreRounded()
    {
        var spec = Wide(Pending("score", 0, 4, new NominalScale(), RoundToPrecision.Create(10)));

        // Span [0, 99]: unrounded 24.75/49.5/74.25 → round_to 10 → 20/50/70.
        var calibrated = await CalibrateOkAsync(spec, "0\n99");

        Assert.Equal([20.0, 50.0, 70.0], CalibratedCutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenPrecisionCollapsesTheDerivedCuts_ThenCalibrationCutsInvalid()
    {
        // The calibrate-phase twin of EqualWidthCutsCollapsed: over the observed span [0, 2] with
        // 8 bins the unrounded cuts are 0.25…1.75, and round_to = 1 maps several onto the same
        // value. Same predicate as the authored case, different owner and phase (D-088/D-089).
        var spec = Wide(Pending("score", 0, 8, new NominalScale(), RoundToPrecision.Create(1)));

        var result = await CalibrateAsync(spec, "0\n2");

        Assert.False(result.IsOk); // no calibrated result
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.CalibrationCutsInvalid);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("score", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public async Task Calibrate_WhenPopulationHasNoUsableValue_ThenCalibrationDataInsufficient()
    {
        var result = await CalibrateAsync(Wide(Pending("score", 0, 4, new NominalScale())), "?\n?");

        Assert.False(result.IsOk); // in-path Error: no calibrated result (D-095)
        Assert.Equal(DiagnosticCode.CalibrationDataInsufficient, Assert.Single(result.Diagnostics).Code);
        Assert.Equal("score", result.Diagnostics[0].Location?.AttributeName);
    }

    [Fact]
    public async Task Calibrate_WhenEveryValueIsEqual_ThenCalibrationDataInsufficient()
    {
        // No spread: min == max cannot bound a span (§11.4).
        var result = await CalibrateAsync(Wide(Pending("score", 0, 4, new NominalScale())), "7\n7\n7");

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.CalibrationDataInsufficient, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Calibrate_WhenFewerDistinctValuesThanBins_ThenStillValid()
    {
        // D-089: the distinct-value guard is equal_frequency-ONLY. Equal-width bins are placed by
        // span, not by count, so two distinct values can define eight bins.
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 8, new NominalScale())), "0\n80\n0\n80");

        Assert.Equal([10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0], CalibratedCutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenValuesUnparseable_ThenExcludedFromTheSpanAndReportedForThisPhase()
    {
        // §7/§11.5: only finite parsed values contribute. The unparseable ones are excluded from
        // min/max and reported as this phase's own aggregate (D-100).
        var result = await CalibrateAsync(Wide(Pending("score", 0, 4, new NominalScale())), "0\nabc\n100\nNaN");

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal([25.0, 50.0, 75.0], CalibratedCutsOf(calibrated!, "score")); // span still [0, 100]
        var unparseable = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Equal(DiagnosticSeverity.Warning, unparseable.Severity);
    }

    [Theory]
    [InlineData(UnknownValuePolicy.Warn, DiagnosticSeverity.Warning)]
    [InlineData(UnknownValuePolicy.Include, DiagnosticSeverity.Warning)]
    public async Task Calibrate_WhenUnparseableUnderPolicy_ThenSeverityFollowsIt(
        UnknownValuePolicy policy, DiagnosticSeverity expected)
    {
        var result = await CalibrateAsync(Wide(Pending("score", 0, 4, new NominalScale(), policy: policy)), "0\nabc\n100");

        Assert.Equal(expected, Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable).Severity);
    }

    [Fact]
    public async Task Calibrate_WhenUnparseableUnderSkip_ThenSilent()
    {
        var result = await CalibrateAsync(
            Wide(Pending("score", 0, 4, new NominalScale(), policy: UnknownValuePolicy.Skip)), "0\nabc\n100");

        Assert.True(result.IsOk);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Calibrate_WhenUnparseableUnderFail_ThenErrorAndNoCalibratedResult()
    {
        var result = await CalibrateAsync(
            Wide(Pending("score", 0, 4, new NominalScale(), policy: UnknownValuePolicy.Fail)), "0\nabc\n100");

        Assert.False(result.IsOk); // the calibrate-phase Error aborts before emit (D-100)
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task Calibrate_WhenNonInvariantLocale_ThenThePopulationParsesUnderIt()
    {
        // P-11/§7: the calibration population is parsed under binding.locale, never the ambient
        // culture. Under de-DE the comma is the decimal separator, so "0,5" is a half — and the
        // semicolon delimiter keeps those values in one column.
        var binding = new Binding(
            SourceShape.Wide, "utf-8", ';', '"', HasHeader: false, "de-DE", "?", new RowIndexObjectKey());
        var german = CultureInfo.GetCultureInfo("de-DE");
        var spec = new BedrockSpec(binding, [
            Pending("score", 0, 4, new NominalScale(), culture: german),
        ]);

        var calibrated = await CalibrateOkAsync(spec, "0,5;x\n100,5;y");

        // Span [0.5, 100.5] over 4 bins → 25.5/50.5/75.5. Read as invariant, "0,5" would be
        // unparseable (or 5), so these cuts prove the locale actually drove the parse.
        Assert.Equal([25.5, 50.5, 75.5], CalibratedCutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenNonInvariantLocale_ThenCutLabelsStayInvariantSchemaStrings()
    {
        // §14: the locale governs PARSING only. The cut labels are invariant schema strings, so a
        // de-DE spec still renders "25.5", never "25,5" — otherwise the schema would be
        // locale-dependent.
        var binding = new Binding(
            SourceShape.Wide, "utf-8", ';', '"', HasHeader: false, "de-DE", "?", new RowIndexObjectKey());
        var spec = new BedrockSpec(binding, [
            Pending("score", 0, 4, new NominalScale(), culture: CultureInfo.GetCultureInfo("de-DE")),
        ]);
        var calibrated = await CalibrateOkAsync(spec, "0,5;x\n100,5;y");

        Assert.True(ConversionPlanner.Plan(calibrated).TryGetValue(out var plan));

        Assert.Equal(
            ["score-<25.5", "score-[25.5, 50.5)", "score-[50.5, 75.5)", "score->=75.5"],
            plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public async Task Calibrate_WhenManualRange_ThenNoDataPassAtAll()
    {
        // §7/D-089: a manual range is spec-determined, so the fast path must not enumerate rows.
        // A source whose rows throw proves the pass never happened.
        var spec = Wide(Manual("score", 0, 4, 0, 100, new NominalScale()));
        var source = new ThrowingRecordSource(ConversionFixtures.SourceOver(ScoreCsv, spec.Binding));
        var schema = await source.GetSchemaAsync();

        var result = await Calibrator.CalibrateAsync(ConversionFixtures.ResolveFor(spec, schema), source);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Empty(calibrated!.Calibrations);
    }

    [Fact]
    public async Task Calibrate_WhenAttributeExcluded_ThenParkedAndNotCalibrated()
    {
        // D-049: an excluded attribute's config is parked — it must not drive a data pass.
        var excluded = Pending("score", 0, 4, new NominalScale()) with { Include = false };
        var spec = Wide(excluded, ConversionFixtures.Nominal("g", 1, "b", "n"));

        var calibrated = await CalibrateOkAsync(spec, "0,b\n100,n");

        Assert.Empty(calibrated.Calibrations);
        Assert.IsType<CalibrationPending>(calibrated.Spec.Attributes[0].Discretizer); // still parked, untouched
    }

    [Fact]
    public async Task Calibrate_WhenRunTwice_ThenIdenticalCuts()
    {
        // P-7: same spec + same input ⇒ same calibration, every run.
        var spec = Wide(Pending("score", 0, 7, new NominalScale()));

        var first = await CalibrateOkAsync(spec, ScoreCsv);
        var second = await CalibrateOkAsync(spec, ScoreCsv);

        Assert.Equal(CalibratedCutsOf(first, "score"), CalibratedCutsOf(second, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenCancelled_ThenOperationCanceledPropagates()
    {
        var spec = Wide(Pending("score", 0, 4, new NominalScale()));
        var source = ConversionFixtures.SourceOver(ScoreCsv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Calibrator.CalibrateAsync(resolved, source, cts.Token));
    }

    [Fact]
    public async Task Calibrate_WhenRestrictToPresent_ThenItDoesNotTriggerCalibration()
    {
        // D-065/§7: restrict_to filters emitted objects, never the calibration population, so a
        // fully-declared attribute carrying one still needs no data pass.
        var spec = Wide(Manual("score", 0, 4, 0, 100, new NominalScale()) with
        {
            RestrictTo = [new RestrictToRange(0, 50)],
        });

        Assert.False(CalibratedSpec.RequiresData(spec));
    }

    // --- Triple min/max calibration ------------------------------------------

    private static BedrockSpec TripleSpec(TripleOrdering ordering) =>
        new(ConversionFixtures.Triple(ordering), [
            new AttributeSpec("score", new PredicateSource("score", SourceValueType.Number), Include: true,
                new CalibrationPending(new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact), CultureInfo.InvariantCulture),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn),
        ]);

    private static async Task<Diagnosed<CalibratedSpec>> CalibrateTripleAsync(BedrockSpec spec, string data)
    {
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        var schema = await source.GetSchemaAsync();
        return await Calibrator.CalibrateTripleAsync(ConversionFixtures.ResolveFor(spec, schema), source);
    }

    [Fact]
    public async Task CalibrateTriple_WhenSubjectGrouped_ThenSpanFromMatchingPredicateRows()
    {
        var result = await CalibrateTripleAsync(
            TripleSpec(TripleOrdering.SubjectGrouped),
            "s0,score,0\ns0,other,999\ns1,score,100\ns2,score,50");

        Assert.True(result.TryGetValue(out var calibrated));
        // The non-matching predicate (999) must not widen the span.
        Assert.Equal([25.0, 50.0, 75.0], CalibratedCutsOf(calibrated!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenUnordered_ThenSameSpanAsGrouped()
    {
        // min/max is order- and count-insensitive, which is exactly why the triple path needs no
        // subject-local deduplication for equal_width (D-095): a repeated observation cannot move
        // a min or a max. The interleaved input must calibrate identically.
        var grouped = await CalibrateTripleAsync(TripleSpec(TripleOrdering.SubjectGrouped), "s0,score,0\ns1,score,100");
        var unordered = await CalibrateTripleAsync(TripleSpec(TripleOrdering.Unordered), "s0,score,0\ns1,score,100\ns0,score,0");

        Assert.True(grouped.TryGetValue(out var groupedState));
        Assert.True(unordered.TryGetValue(out var unorderedState));
        Assert.Equal(CalibratedCutsOf(groupedState!, "score"), CalibratedCutsOf(unorderedState!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenRepeatedObservation_ThenSpanUnchanged()
    {
        var once = await CalibrateTripleAsync(TripleSpec(TripleOrdering.SubjectGrouped), "s0,score,0\ns1,score,100");
        var repeated = await CalibrateTripleAsync(
            TripleSpec(TripleOrdering.SubjectGrouped), "s0,score,0\ns0,score,0\ns0,score,0\ns1,score,100");

        Assert.True(once.TryGetValue(out var onceState));
        Assert.True(repeated.TryGetValue(out var repeatedState));
        Assert.Equal(CalibratedCutsOf(onceState!, "score"), CalibratedCutsOf(repeatedState!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenUnordered_ThenExactlyOneRawOrderReadNoGroupingPass()
    {
        // D-095/D-102: because min/max is count-insensitive, the unordered triple path must NOT
        // fall back to the grouped/spool machinery — no subject-local dedup, no second pass. A
        // cut-equality assertion alone would still pass if a wasteful grouped replay were added,
        // so this counts the enumerations directly and fails on the second.
        var spec = TripleSpec(TripleOrdering.Unordered);
        var inner = ConversionFixtures.TripleSourceOver("s0,score,0\ns1,score,100\ns0,score,50", spec.Binding);
        var source = new SingleReadTripleSource(inner);
        var schema = await source.GetSchemaAsync();

        var result = await Calibrator.CalibrateTripleAsync(ConversionFixtures.ResolveFor(spec, schema), source);

        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal([25.0, 50.0, 75.0], CalibratedCutsOf(calibrated!, "score"));
        Assert.Equal(1, source.Enumerations);
    }

    [Fact]
    public async Task CalibrateTriple_WhenSubjectUnusable_ThenObjectKeyValueInvalidHalts()
    {
        // G-3/D-099: the triple structural checks stay intact for the cut-calibration pass.
        var result = await CalibrateTripleAsync(TripleSpec(TripleOrdering.SubjectGrouped), "s0,score,0\n ,score,100");

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task CalibrateTriple_WhenSubjectGroupedNotContiguous_ThenHalts()
    {
        var result = await CalibrateTripleAsync(
            TripleSpec(TripleOrdering.SubjectGrouped), "s0,score,0\ns1,score,100\ns0,score,50");

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.TripleSubjectNotContiguous, Assert.Single(result.Diagnostics).Code);
    }

    // --- Emit -----------------------------------------------------------------

    private static async Task<(ConversionPlan Plan, List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)>
        CalibratePlanEmit(BedrockSpec spec, string csv, LabelStyle style = LabelStyle.Native)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        var calibration = await Calibrator.CalibrateAsync(resolved, source);
        Assert.True(calibration.TryGetValue(out var calibrated),
            string.Join("; ", calibration.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.True(ConversionPlanner.Plan(calibrated!, style).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (plan, objects, ConversionFixtures.DataDiagnostics(diagnostics));
    }

    [Fact]
    public async Task Emit_WhenCalibratedNominal_ThenValuesLandInTheirSpanBins()
    {
        var (plan, objects, diagnostics) = await CalibratePlanEmit(Wide(Pending("score", 0, 4, new NominalScale())), ScoreCsv);

        Assert.Equal(["score-<25", "score-[25, 50)", "score-[50, 75)", "score->=75"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Empty(diagnostics);
        // 0, 10 → <25; 25, 40 → [25,50); 60 → [50,75); 75, 99, 100 → >=75.
        Assert.Equal([[0], [0], [1], [1], [2], [3], [3], [3]], objects.Select(o => o.CrossedFormalAttributeIds.ToArray()));
    }

    [Fact]
    public async Task Emit_WhenValueOutsideTheCalibrationSpan_ThenStillLandsInTheFirstOrLastBin()
    {
        // §11.4: the auto discretizer's ends are open precisely so that data outside the
        // calibration range still bins. Calibrating on a narrow span and emitting a wider one is
        // the between-run data-change case.
        var spec = Wide(Manual("score", 0, 4, 0, 100, new NominalScale()));

        var (_, objects, diagnostics) = await CalibratePlanEmit(spec, "-500\n500");

        Assert.Empty(diagnostics);
        Assert.Equal([[0], [3]], objects.Select(o => o.CrossedFormalAttributeIds.ToArray()));
    }

    [Fact]
    public async Task Emit_WhenValueUnparseable_ThenExistingChannelsReportItOncePerPhase()
    {
        var spec = Wide(Pending("score", 0, 4, new NominalScale()));
        const string csv = "0\nabc\n100";

        var calibration = await CalibrateAsync(spec, csv);
        var (_, objects, emitDiagnostics) = await CalibratePlanEmit(spec, csv);

        // The object is kept with no cross (§11.5/D-050) — never silently dropped.
        Assert.Equal(3, objects.Count);
        Assert.Empty(objects[1].CrossedFormalAttributeIds);

        // D-100: calibrate and emit are independent passes, so the same value yields exactly one
        // aggregate per phase — not one deduplicated across them, and not none at emit.
        Assert.Single(calibration.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Single(emitDiagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
    }

    [Fact]
    public async Task Emit_WhenOrdinalOverCalibratedCuts_ThenCumulativeCrossingsAndAllThreshold()
    {
        var spec = Wide(Pending("score", 0, 4, new OrdinalScale(OrdinalDirection.Le)));

        var (plan, objects, _) = await CalibratePlanEmit(spec, ScoreCsv);

        Assert.Equal(["score-<25", "score-<50", "score-<75", "score-all"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal([0, 1, 2, 3], objects[0].CrossedFormalAttributeIds); // 0 → below every threshold
        Assert.Equal([3], objects[^1].CrossedFormalAttributeIds);         // 100 → only the tautological `all`
    }

    // --- Auto/frozen byte-equivalence (D-088) --------------------------------

    // Calibrate → plan → emit → write, the way the golden orchestrator does: the .cxt two-pass runs
    // through a replay session (disposed before the diagnostics are read), the .dat is single-pass.
    private static async Task<(byte[] Cxt, byte[] Dat)> ConvertAsync(BedrockSpec spec, string csv, WriterOptions options, LabelStyle style)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        var calibration = await Calibrator.CalibrateAsync(resolved, source);
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

    [Theory]
    [InlineData(LabelStyle.Native)]
    [InlineData(LabelStyle.V2Compat)]
    public async Task Convert_WhenAutoVsFrozenManualCuts_ThenByteIdenticalCxtAndDat(LabelStyle style)
    {
        // D-088: converting a min_max spec on the fly and converting its calibrate-frozen form
        // (manual_cuts over the resolved cuts, ends = "open") MUST produce byte-identical .cxt and
        // .dat on the calibration dataset — freezing changes WHEN the cuts resolve, never WHICH.
        var options = style == LabelStyle.V2Compat ? WriterOptions.V2Compat : WriterOptions.Native;

        // 1. Calibrate and capture the effective cuts.
        var auto = Wide(Pending("score", 0, 4, new OrdinalScale(OrdinalDirection.Le)));
        var cuts = CalibratedCutsOf(await CalibrateOkAsync(auto, ScoreCsv), "score");
        Assert.Equal([25.0, 50.0, 75.0], cuts);

        // 2. Build the frozen equivalent from exactly those cuts.
        var frozen = Wide(Frozen("score", 0, cuts, new OrdinalScale(OrdinalDirection.Le)));

        // 3. Convert the same dataset through both paths.
        var (autoCxt, autoDat) = await ConvertAsync(auto, ScoreCsv, options, style);
        var (frozenCxt, frozenDat) = await ConvertAsync(frozen, ScoreCsv, options, style);

        Assert.Equal(frozenCxt, autoCxt);
        Assert.Equal(frozenDat, autoDat);
    }

    [Fact]
    public async Task Convert_WhenAutoVsFrozen_ThenIdenticalFormalIdentitiesAndCrosses()
    {
        var auto = Wide(Pending("score", 0, 4, new NominalScale()));
        var frozen = Wide(Frozen("score", 0, [25, 50, 75], new NominalScale()));

        var (autoPlan, autoObjects, _) = await CalibratePlanEmit(auto, ScoreCsv);
        var (frozenPlan, frozenObjects, _) = await CalibratePlanEmit(frozen, ScoreCsv);

        Assert.Equal(frozenPlan.FormalAttributes.Select(f => f.Identity), autoPlan.FormalAttributes.Select(f => f.Identity));
        Assert.Equal(frozenPlan.FormalAttributes.Select(f => f.RenderedName), autoPlan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(frozenObjects.Select(o => o.CrossedFormalAttributeIds.ToArray()), autoObjects.Select(o => o.CrossedFormalAttributeIds.ToArray()));
    }

    [Fact]
    public async Task Convert_WhenAutoRunTwice_ThenByteIdentical()
    {
        // P-7: the whole calibrate → plan → emit chain is deterministic.
        var spec = Wide(Pending("score", 0, 4, new OrdinalScale(OrdinalDirection.Le)));

        var (firstCxt, firstDat) = await ConvertAsync(spec, ScoreCsv, WriterOptions.Native, LabelStyle.Native);
        var (secondCxt, secondDat) = await ConvertAsync(spec, ScoreCsv, WriterOptions.Native, LabelStyle.Native);

        Assert.Equal(firstCxt, secondCxt);
        Assert.Equal(firstDat, secondDat);
    }

    // --- Triple emit (§17 rule 8; both orderings) -----------------------------

    private static async Task<(ConversionPlan Plan, List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)>
        CalibratePlanEmitTriple(BedrockSpec spec, string data)
    {
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        var calibration = await Calibrator.CalibrateTripleAsync(resolved, source);
        Assert.True(calibration.TryGetValue(out var calibrated),
            string.Join("; ", calibration.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.True(ConversionPlanner.Plan(calibrated!).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (plan, objects, ConversionFixtures.DataDiagnostics(diagnostics));
    }

    // The same four observations in both orderings: contiguous, and subject-interleaved.
    private const string TripleGrouped = "s0,score,0\ns1,score,40\ns2,score,60\ns3,score,100";
    private const string TripleInterleaved = "s0,score,0\ns2,score,60\ns1,score,40\ns3,score,100\ns0,other,9";

    [Fact]
    public async Task EmitTriple_WhenSubjectGroupedCalibratedEqualWidth_ThenObjectsCrossTheirSpanBins()
    {
        var (plan, objects, diagnostics) = await CalibratePlanEmitTriple(TripleSpec(TripleOrdering.SubjectGrouped), TripleGrouped);

        Assert.Empty(diagnostics);
        Assert.Equal(["score-<25", "score-[25, 50)", "score-[50, 75)", "score->=75"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(["s0", "s1", "s2", "s3"], objects.Select(o => o.Name));
        Assert.Equal([[0], [1], [2], [3]], objects.Select(o => o.CrossedFormalAttributeIds.ToArray()));
    }

    [Fact]
    public async Task EmitTriple_WhenUnordered_ThenFirstAppearanceObjectsAndTheSameCrosses()
    {
        var (plan, objects, diagnostics) = await CalibratePlanEmitTriple(TripleSpec(TripleOrdering.Unordered), TripleInterleaved);

        Assert.Empty(diagnostics);
        // §17 rule 3: object order is first appearance in the raw input, not sorted.
        Assert.Equal(["s0", "s2", "s1", "s3"], objects.Select(o => o.Name));
        Assert.Equal([[0], [2], [1], [3]], objects.Select(o => o.CrossedFormalAttributeIds.ToArray()));
        Assert.Equal(4, plan.FormalAttributes.Count);
    }

    [Fact]
    public async Task EmitTriple_WhenBothOrderings_ThenIdenticalSchemaAndPerObjectCrosses()
    {
        // The ordering is a streaming property, not a semantic one: the same observations must give
        // the same columns and the same object→crosses mapping either way (§17 rule 4).
        var (groupedPlan, groupedObjects, _) = await CalibratePlanEmitTriple(TripleSpec(TripleOrdering.SubjectGrouped), TripleGrouped);
        var (unorderedPlan, unorderedObjects, _) = await CalibratePlanEmitTriple(TripleSpec(TripleOrdering.Unordered), TripleInterleaved);

        Assert.Equal(
            groupedPlan.FormalAttributes.Select(f => f.Identity),
            unorderedPlan.FormalAttributes.Select(f => f.Identity));
        Assert.Equal(
            groupedObjects.ToDictionary(o => o.Name, o => o.CrossedFormalAttributeIds.ToArray()),
            unorderedObjects.ToDictionary(o => o.Name, o => o.CrossedFormalAttributeIds.ToArray()));
    }

    [Fact]
    public async Task EmitTriple_WhenRunTwice_ThenIdenticalObjectsAndCrosses()
    {
        // P-7 on the triple path.
        var (_, first, _) = await CalibratePlanEmitTriple(TripleSpec(TripleOrdering.Unordered), TripleInterleaved);
        var (_, second, _) = await CalibratePlanEmitTriple(TripleSpec(TripleOrdering.Unordered), TripleInterleaved);

        Assert.Equal(first.Select(o => o.Name), second.Select(o => o.Name));
        Assert.Equal(
            first.Select(o => o.CrossedFormalAttributeIds.ToArray()),
            second.Select(o => o.CrossedFormalAttributeIds.ToArray()));
    }

    // A source that reports its schema but throws if any row is read — proves the no-data fast
    // path never enumerates (the D-098 rows-throwing-fake pattern).
    private sealed class ThrowingRecordSource(WideCsvSource inner) : IRecordSource
    {
        public SourceProvenance Provenance => inner.Provenance;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            inner.GetSchemaAsync(cancellationToken);

        public IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("a fully-declared spec must not read rows during calibration (§7).");
    }

    // Counts row enumerations and refuses a second one: the equal_width min/max calibration is a
    // single raw-order pass, so any grouped/replay fallback creeping in fails here rather than
    // hiding behind an identical cut list.
    private sealed class SingleReadTripleSource(TripleCsvSource inner) : ITripleRowSource
    {
        public int Enumerations { get; private set; }

        public SourceProvenance Provenance => inner.Provenance;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            inner.GetSchemaAsync(cancellationToken);

        public IAsyncEnumerable<TripleRow> ReadRowsAsync(CancellationToken cancellationToken = default)
        {
            Enumerations++;
            return Enumerations > 1
                ? throw new InvalidOperationException(
                    "equal_width min/max calibration is count-insensitive and must read the triple stream exactly once (D-095/D-102).")
                : inner.ReadRowsAsync(cancellationToken);
        }
    }
}
