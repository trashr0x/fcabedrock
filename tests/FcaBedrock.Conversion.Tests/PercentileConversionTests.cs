using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// End-to-end <c>equal_width</c> <c>range = "percentile_p1_p99"</c> conversion (§11.4, M4 Slice
/// D / D-103). Slice C modelled the range in the Core enum but left it unreachable; Slice D
/// lands its exact bounded-memory calibration, so it becomes executable.
/// <para>
/// It is the count-sensitive twin of <c>min_max</c>: same equal-width derivation, same
/// authored-config fingerprint rule — only the span differs, being drawn from exact order
/// statistics rather than the extremes. Clipping the tails is the whole point: a single outlier
/// cannot stretch the bins.
/// </para>
/// </summary>
public sealed class PercentileConversionTests
{
    // 1..100, one observation each: N = 100, so p1 = 1 and p99 = 99 exactly (C_i = i).
    private static readonly string UniformCsv = string.Join('\n', Enumerable.Range(1, 100));

    private static AttributeSpec Pending(
        string name, int index, int bins, Scale scale,
        EqualWidthRange range = EqualWidthRange.PercentileP1P99, CutPrecision? precision = null,
        UnknownValuePolicy policy = UnknownValuePolicy.Warn, CultureInfo? culture = null) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            new CalibrationPending(
                new PendingEqualWidth(bins, range, precision ?? CutPrecision.Exact),
                culture ?? CultureInfo.InvariantCulture),
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, policy);

    private static AttributeSpec PendingPredicate(string name, string predicate, int bins, Scale scale) =>
        new(name, new PredicateSource(predicate, SourceValueType.Number), Include: true,
            new CalibrationPending(
                new PendingEqualWidth(bins, EqualWidthRange.PercentileP1P99, CutPrecision.Exact),
                CultureInfo.InvariantCulture),
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec Frozen(string name, int index, IReadOnlyList<double> cuts, Scale scale) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!,
            scale, DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static BedrockSpec Wide(params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Wide(hasHeader: false), attributes);

    private static GroupingOptions TinyBudget() => new(maxBufferedBytes: 308, maxMergeFanIn: 2);

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

    private static IReadOnlyList<double> CutsOf(CalibratedSpec calibrated, string attribute) =>
        Assert.IsType<CalibratedCuts>(calibrated.Calibrations.Single(c => c.AttributeName == attribute)).Cuts;

    // --- Exact order statistics ------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenUniformPopulation_ThenSpanIsTheExactP1AndP99()
    {
        // N = 100, C_i = i. p1 = first i with i·100 >= 100 → 1 → value 1.
        //                    p99 = first i with i·100 >= 9900 → 99 → value 99.
        // The span [1, 99] over 2 bins interpolates to the single cut 50.
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale())), UniformCsv);

        Assert.Equal([50.0], CutsOf(calibrated, "score"));
        var discretizer = Assert.IsType<EqualWidthDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);
        Assert.Equal(EqualWidthRange.PercentileP1P99, discretizer.Range);

        // A data-derived range authors no span, so vmin/vmax stay absent (D-094).
        Assert.Null(discretizer.VMin);
        Assert.Null(discretizer.VMax);
    }

    [Fact]
    public async Task Calibrate_WhenOutliersExist_ThenTheyAreClippedOutOfTheSpan()
    {
        // The reason percentile exists: min_max over this data would span [-100000, 100000] and put
        // every real value in one bin. p1/p99 clip the tails, so the bins cover the body.
        var csv = string.Join('\n', ["-100000", .. Enumerable.Range(1, 98).Select(i => i.ToString(CultureInfo.InvariantCulture)), "100000"]);

        var percentile = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale())), csv);
        var minMax = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale(), EqualWidthRange.MinMax)), csv);

        // N = 100 with C = [1, 2, …, 99, 100] over values -100000, 1..98, 100000.
        // p1  = first i with C_i·100 >= 100  → i = 1 → -100000. (1% of 100 is the very first value.)
        // p99 = first i with C_i·100 >= 9900 → i = 99 → 98. Span [-100000, 98] → cut -49951.
        Assert.Equal([-49951.0], CutsOf(percentile, "score"));

        // min_max spans [-100000, 100000] → cut 0: the outliers alone decide the bins.
        Assert.Equal([0.0], CutsOf(minMax, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenPopulationIsSkewed_ThenP99TracksTheBodyNotTheTail()
    {
        // 98 ones, then 5 and 1000: N = 100, C = [98, 99, 100].
        // p1  = first i with C_i·100 >= 100  → C_1·100 = 9800 → i = 1 → value 1.
        // p99 = first i with C_i·100 >= 9900 → C_1 = 9800 < 9900; C_2·100 = 9900 → i = 2 → value 5.
        var csv = string.Join('\n', [.. Enumerable.Repeat("1", 98), "5", "1000"]);

        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale())), csv);

        // Span [1, 5] over 2 bins → cut 3. The 1000 is deliberately outside the span.
        Assert.Equal([3.0], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenValuesRepeat_ThenCountsDecideThePercentilePositions()
    {
        // Percentile is count-SENSITIVE, unlike min_max: repeating a value moves the positions.
        var calibrated = await CalibrateOkAsync(
            Wide(Pending("score", 0, 2, new NominalScale())), string.Join('\n', [.. Enumerable.Repeat("10", 50), .. Enumerable.Repeat("20", 50)]));

        // N = 100, C = [50, 100]. p1 → C_1·100 = 5000 >= 100 → value 10.
        //                          p99 → 5000 < 9900; C_2·100 = 10000 >= 9900 → value 20.
        Assert.Equal([15.0], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenPopulationIsTiny_ThenTheSpanIsStillExact()
    {
        // N = 2, C = [1, 2]. p1 → C_1·100 = 100 >= 1·2 = 2 → value 4.
        //                     p99 → 100 >= 99·2 = 198? no; C_2·100 = 200 >= 198 → value 8.
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale())), "4\n8");

        Assert.Equal([6.0], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenP99LandsExactlyOnARankBoundary_ThenTheBoundaryGroupIsSelected()
    {
        // The exact-boundary pair, one observation apart — where an off-by-one-ULP rank target
        // would pick the wrong order statistic. Both have N = 100 and the same two values.
        //
        // 99 sevens + one 9: C = [99, 100]. p99 needs C_i·100 >= 99·100 = 9900, and C_1·100 is
        // EXACTLY 9900 — so the >= boundary is met at group 1 and p99 = 7 = p1. No spread.
        var atBoundary = await CalibrateAsync(
            Wide(Pending("score", 0, 2, new NominalScale())),
            string.Join('\n', [.. Enumerable.Repeat("7", 99), "9"]));

        Assert.False(atBoundary.IsOk);
        Assert.Equal(DiagnosticCode.CalibrationDataInsufficient, Assert.Single(atBoundary.Diagnostics).Code);

        // 98 sevens + two 9s: C = [98, 100]. Now C_1·100 = 9800 < 9900, so p99 moves to group 2
        // and the span becomes [7, 9] → cut 8. One observation decides between the two outcomes.
        var pastBoundary = await CalibrateOkAsync(
            Wide(Pending("score", 0, 2, new NominalScale())),
            string.Join('\n', [.. Enumerable.Repeat("7", 98), "9", "9"]));

        Assert.Equal([8.0], CutsOf(pastBoundary, "score"));
    }

    // --- Failure modes ---------------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenP1EqualsP99_ThenCalibrationDataInsufficient()
    {
        // A span with no spread cannot bound equal-width bins. Note this is NOT the distinct-value
        // guard — equal_width has none (D-089); it is the same "no usable spread" condition
        // min_max reports, reached through the percentile span.
        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), "7\n7\n7");

        Assert.False(result.IsOk); // in-path Error: no calibrated result
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.CalibrationDataInsufficient, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("score", diagnostic.Location?.AttributeName);
        Assert.Contains("percentile", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calibrate_WhenTheBodyIsFlatButTailsDiffer_ThenStillDataInsufficient()
    {
        // 1000 identical values plus one low and one high outlier: the outliers are exactly what
        // p1/p99 clip away, so the span collapses even though min/max would span widely.
        var csv = string.Join('\n', ["-5", .. Enumerable.Repeat("7", 1000), "9999"]);

        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), csv);

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.CalibrationDataInsufficient, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Calibrate_WhenPopulationIsEmpty_ThenCalibrationDataInsufficient()
    {
        var result = await CalibrateAsync(Wide(Pending("score", 0, 2, new NominalScale())), "?\n?");

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.CalibrationDataInsufficient, diagnostic.Code);
        Assert.Contains("no usable finite numeric value", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calibrate_WhenUnparseableUnderWarn_ThenExcludedAndAggregated()
    {
        var result = await CalibrateAsync(
            Wide(Pending("score", 0, 2, new NominalScale())), "1\nwibble\n50\n99");

        Assert.True(result.TryGetValue(out _));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task Calibrate_WhenUnparseableUnderFail_ThenErrorAndNoCalibratedResult()
    {
        var result = await CalibrateAsync(
            Wide(Pending("score", 0, 2, new NominalScale(), policy: UnknownValuePolicy.Fail)), "1\nwibble\n50\n99");

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable).Severity);
    }

    // --- Precision applies AFTER the span is selected (D-102's boundary) -------

    [Fact]
    public async Task Calibrate_WhenPrecisionIsExact_ThenCutsAreUnrounded() =>
        // Span [1, 99] over 4 bins: 1 + 98·0.25 = 25.5, 1 + 98·0.5 = 50, 1 + 98·0.75 = 74.5.
        Assert.Equal(
            [25.5, 50.0, 74.5],
            CutsOf(await CalibrateOkAsync(Wide(Pending("score", 0, 4, new NominalScale())), UniformCsv), "score"));

    [Fact]
    public async Task Calibrate_WhenPrecisionRoundsTo_ThenAppliedToTheSelectedSpansCuts()
    {
        // The span [1, 99] is selected first, then the SAME Slice C equal-width derivation
        // interpolates and rounds it — there is no second copy of that formula (D-102).
        var calibrated = await CalibrateOkAsync(
            Wide(Pending("score", 0, 4, new NominalScale(), precision: RoundToPrecision.Create(10))), UniformCsv);

        // Unrounded 25.5/50/74.5 → round_to 10 → 30/50/70.
        Assert.Equal([30.0, 50.0, 70.0], CutsOf(calibrated, "score"));
    }

    [Fact]
    public async Task Calibrate_WhenPrecisionCollapsesTheCuts_ThenCalibrationCutsInvalid()
    {
        // The span is fine; the rounding is what fails. Distinct from DataInsufficient, which is
        // about the span itself (D-088/D-089).
        var result = await CalibrateAsync(
            Wide(Pending("score", 0, 8, new NominalScale(), precision: RoundToPrecision.Create(1))), "0\n1\n2");

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.CalibrationCutsInvalid, diagnostic.Code);
        Assert.Equal("score", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public async Task Calibrate_WhenZeroIsTheSpanBound_ThenComputedCutsCarryPositiveZero()
    {
        // G-6: a computed -0 must never reach a bin identity, label, or hash. Only the bit pattern
        // can prove it, since -0.0 == 0.0.
        var calibrated = await CalibrateOkAsync(Wide(Pending("score", 0, 2, new NominalScale())), "-4\n4");

        var cut = CutsOf(calibrated, "score")[0];
        Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(cut));
        Assert.False(double.IsNegative(cut));
    }

    // --- Triple, both orderings ------------------------------------------------

    private static async Task<Diagnosed<CalibratedSpec>> CalibrateTripleAsync(BedrockSpec spec, string data)
    {
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        var schema = await source.GetSchemaAsync();
        return await Calibrator.CalibrateTripleAsync(ConversionFixtures.ResolveFor(spec, schema), source);
    }

    private static BedrockSpec Triple(TripleOrdering ordering, params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Triple(ordering), attributes);

    // s0 repeats its 1 three times; §5.3.1 collapses those to one observation per subject.
    private const string TripleGrouped =
        "s0,score,1\ns0,score,1\ns0,score,1\ns1,score,2\ns2,score,3\ns3,score,4";

    private const string TripleInterleaved =
        "s0,score,1\ns1,score,2\ns2,score,3\ns3,score,4\ns0,score,1\ns0,score,1";

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped, TripleGrouped)]
    [InlineData(TripleOrdering.Unordered, TripleInterleaved)]
    public async Task CalibrateTriple_WhenCountSensitive_ThenSubjectLocalDedupShapesTheSpan(
        TripleOrdering ordering, string data)
    {
        // Percentile is count-sensitive, so the §5.3.1 dedup applies on BOTH orderings — the
        // unordered path reaching it through the grouped second pass.
        var result = await CalibrateTripleAsync(
            Triple(ordering, PendingPredicate("score", "score", 2, new NominalScale())), data);

        // Deduped population {1,2,3,4}: N = 4, C = [1,2,3,4].
        // p1  = first i with C_i·100 >= 4   → i = 1 → 1.
        // p99 = first i with C_i·100 >= 396 → i = 4 → 4. Span [1,4] over 2 bins → cut 2.5.
        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal([2.5], CutsOf(calibrated!, "score"));
    }

    [Fact]
    public async Task CalibrateTriple_WhenBothOrderingsCarryTheSameObservations_ThenIdenticalCuts()
    {
        var grouped = await CalibrateTripleAsync(
            Triple(TripleOrdering.SubjectGrouped, PendingPredicate("score", "score", 2, new NominalScale())), TripleGrouped);
        var unordered = await CalibrateTripleAsync(
            Triple(TripleOrdering.Unordered, PendingPredicate("score", "score", 2, new NominalScale())), TripleInterleaved);

        Assert.True(grouped.TryGetValue(out var groupedState));
        Assert.True(unordered.TryGetValue(out var unorderedState));
        Assert.Equal(CutsOf(groupedState!, "score"), CutsOf(unorderedState!, "score"));
    }

    // --- Spill/non-spill and auto/frozen equivalence ---------------------------

    [Fact]
    public async Task Calibrate_WhenForcedToSpill_ThenIdenticalCutsToTheInMemoryPath()
    {
        var spec = Wide(Pending("score", 0, 4, new NominalScale()));

        var resident = await CalibrateOkAsync(spec, UniformCsv);
        var spilled = await CalibrateOkAsync(spec, UniformCsv, TinyBudget());

        Assert.Equal(CutsOf(resident, "score"), CutsOf(spilled, "score"));
    }

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

    [Theory]
    [InlineData(LabelStyle.Native)]
    [InlineData(LabelStyle.V2Compat)]
    public async Task Convert_WhenForcedToSpill_ThenByteIdenticalOutputToTheInMemoryPath(LabelStyle style)
    {
        var options = style == LabelStyle.V2Compat ? WriterOptions.V2Compat : WriterOptions.Native;
        var spec = Wide(Pending("score", 0, 4, new OrdinalScale(OrdinalDirection.Le)));

        var (residentCxt, residentDat) = await ConvertAsync(spec, UniformCsv, options, style);
        var (spilledCxt, spilledDat) = await ConvertAsync(spec, UniformCsv, options, style, TinyBudget());

        // §11.5's bounded-memory obligation: spilling must not move a single output byte.
        Assert.Equal(residentCxt, spilledCxt);
        Assert.Equal(residentDat, spilledDat);
    }

    [Theory]
    [InlineData(LabelStyle.Native)]
    [InlineData(LabelStyle.V2Compat)]
    public async Task Convert_WhenAutoVsFrozenManualCuts_ThenByteIdenticalCxtAndDat(LabelStyle style)
    {
        // D-088 covers BOTH auto discretizers, so percentile's frozen twin must match byte for
        // byte on the calibration dataset exactly as min_max's does.
        var options = style == LabelStyle.V2Compat ? WriterOptions.V2Compat : WriterOptions.Native;

        var auto = Wide(Pending("score", 0, 4, new OrdinalScale(OrdinalDirection.Le)));
        var cuts = CutsOf(await CalibrateOkAsync(auto, UniformCsv), "score");
        Assert.Equal([25.5, 50.0, 74.5], cuts);

        var frozen = Wide(Frozen("score", 0, cuts, new OrdinalScale(OrdinalDirection.Le)));

        var (autoCxt, autoDat) = await ConvertAsync(auto, UniformCsv, options, style);
        var (frozenCxt, frozenDat) = await ConvertAsync(frozen, UniformCsv, options, style);

        Assert.Equal(frozenCxt, autoCxt);
        Assert.Equal(frozenDat, autoDat);
    }

    [Fact]
    public async Task Convert_WhenAutoVsFrozen_ThenIdenticalSchemaFingerprint()
    {
        var auto = Wide(Pending("score", 0, 2, new NominalScale()));
        var calibrated = await CalibrateOkAsync(auto, UniformCsv);
        Assert.True(ConversionPlanner.Plan(calibrated).TryGetValue(out var autoPlan));

        var frozen = Wide(Frozen("score", 0, [50], new NominalScale()));
        var frozenSchema = await ConversionFixtures.SourceOver(UniformCsv, frozen.Binding).GetSchemaAsync();
        Assert.True(ConversionPlanner.Plan(
            CalibratedSpec.FromFullyDeclared(ConversionFixtures.ResolveFor(frozen, frozenSchema))).TryGetValue(out var frozenPlan));

        // Identical effective bins ⇒ identical schema fingerprint; the output fingerprints differ
        // only through the authored discretizer sub-object (D-094).
        Assert.Equal(
            Core.Fingerprinting.FingerprintCalculator.ComputeSchemaFingerprint(frozenPlan),
            Core.Fingerprinting.FingerprintCalculator.ComputeSchemaFingerprint(autoPlan));
    }

    [Fact]
    public async Task Calibrate_WhenRepeated_ThenDeterministic()
    {
        var spec = Wide(Pending("score", 0, 4, new NominalScale()));

        Assert.Equal(
            CutsOf(await CalibrateOkAsync(spec, UniformCsv), "score"),
            CutsOf(await CalibrateOkAsync(spec, UniformCsv), "score"));
    }
}
