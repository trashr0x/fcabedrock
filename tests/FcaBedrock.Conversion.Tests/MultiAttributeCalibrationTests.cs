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
/// Several count-sensitive attributes calibrating <b>at the same time</b> (§11.5, D-082/D-095/D-103).
/// <para>
/// One <c>CalibrationRun</c> owns one spool workspace, and every count-sensitive accumulator spills
/// into it. So the D-082 degraded-cleanup allowance — retained run bytes plus the next merge output
/// must not exceed <c>3T</c> — has exactly one honest reading here: both sides are the
/// <b>workspace's</b>. <c>T</c> is the cumulative original-spill payload of every accumulator sharing
/// that workspace, and it is what the retained bytes of that same workspace are measured against.
/// </para>
/// <para>
/// Reading <c>T</c> as one attribute's payload while the retained bytes are everyone's makes the
/// allowance shrink as attributes are added: with A comparable accumulators the left side grows with
/// A and the right side does not, so a valid population is refused with a storage diagnostic on
/// perfectly healthy storage. These tests pin the externally visible consequence — the same cuts,
/// the same order, the same diagnostics, and the same bytes as the in-memory path — rather than the
/// private arithmetic that produces it.
/// </para>
/// <para>
/// The emit path's <c>FirstAppearanceGrouping</c> is not exercised here for the same property: it
/// creates a workspace per grouping call and sums its baseline over that same workspace, so both
/// sides already describe one set.
/// </para>
/// </summary>
public sealed class MultiAttributeCalibrationTests
{
    // 24 rows, five numeric columns of deliberately unequal cardinality:
    //   0 seq   1..24 ascending, one observation each  → 24 distinct, the largest spill payload
    //   1 small 200 + i%4                              → 4 distinct, a deliberately tiny payload
    //   2 rev   24..1 descending, one observation each → the same population as seq, arriving in the
    //                                                    opposite order (so a merge that depended on
    //                                                    arrival order could not agree with seq)
    //   3 tied  100 + i%6                              → 6 distinct, four observations each
    //   4 skew  1..24 ascending                        → the percentile-range population
    private const int Rows = 24;

    private static readonly string Csv = string.Join(
        '\n',
        Enumerable.Range(1, Rows).Select(i => string.Create(
            CultureInfo.InvariantCulture,
            $"{i},{200 + ((i - 1) % 4)},{Rows - i + 1},{100 + ((i - 1) % 6)},{i}")));

    // Small enough that every accumulator is sized to the per-attribute floor and must spill
    // repeatedly, and a fan-in of 2 forces online consolidation during intake as well as the final
    // replay merge. The same shape the existing single-attribute spill-equivalence tests use.
    private static GroupingOptions TinyBudget(
        ISpoolFileSystem? fileSystem = null, ICalibrationObserver? observer = null) =>
        new(maxBufferedBytes: 308, maxMergeFanIn: 2, tempDirectory: null, fileSystem, observer);

    private static AttributeSpec EqualFrequency(
        string name, int index, int bins,
        TiePolicy tie = TiePolicy.Left, CutPlacement placement = CutPlacement.RightValue) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            new CalibrationPending(new PendingEqualFrequency(bins, tie, placement), CultureInfo.InvariantCulture),
            new OrdinalScale(OrdinalDirection.Le), DeclaredDomain: [], RestrictTo: [],
            ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec Percentile(string name, int index, int bins) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            new CalibrationPending(
                new PendingEqualWidth(bins, EqualWidthRange.PercentileP1P99, CutPrecision.Exact),
                CultureInfo.InvariantCulture),
            new OrdinalScale(OrdinalDirection.Le), DeclaredDomain: [], RestrictTo: [],
            ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec Frozen(string name, int index, IReadOnlyList<double> cuts) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!,
            new OrdinalScale(OrdinalDirection.Le), DeclaredDomain: [], RestrictTo: [],
            ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    // The attribute ORDER matters and is not incidental: `small` is finalized second, while `rev`,
    // `tied` and `skew` still hold their much larger runs in the shared workspace. An allowance
    // derived from `small`'s own payload cannot cover them.
    private static BedrockSpec MultiAttributeSpec() =>
        new(ConversionFixtures.Wide(hasHeader: false),
        [
            EqualFrequency("seq", 0, 4),
            EqualFrequency("small", 1, 3),
            EqualFrequency("rev", 2, 3),
            EqualFrequency("tied", 3, 3),
            Percentile("skew", 4, 2),
        ]);

    private static async Task<Diagnosed<CalibratedSpec>> CalibrateAsync(
        BedrockSpec spec, string csv, GroupingOptions? options = null)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        return options is null
            ? await Calibrator.CalibrateAsync(resolved, source)
            : await Calibrator.CalibrateAsync(resolved, source, options, options.Observer as ICalibrationObserver, CancellationToken.None);
    }

    private static async Task<CalibratedSpec> CalibrateOkAsync(
        BedrockSpec spec, string csv, GroupingOptions? options = null)
    {
        var result = await CalibrateAsync(spec, csv, options);
        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return calibrated!;
    }

    private static IReadOnlyList<double> CutsOf(CalibratedSpec calibrated, string attribute) =>
        Assert.IsType<CalibratedCuts>(calibrated.Calibrations.Single(c => c.AttributeName == attribute)).Cuts;

    // Renders every outcome in spec order. ImmutableArray compares by reference, so two runs that
    // produced identical cuts would never be `Equal`; rendering compares what the outcome says and
    // reports the two lists on a mismatch. The ORDER is part of the comparison on purpose.
    private static string Describe(CalibratedSpec calibrated) =>
        string.Join(
            " | ",
            calibrated.Calibrations.Select(outcome => outcome switch
            {
                CalibratedCuts cuts => $"{cuts.AttributeName} cuts=[{string.Join(", ", cuts.Cuts.Select(Format))}]",
                ObservedDomain domain => $"{domain.AttributeName} observed=[{string.Join(", ", domain.Values)}]",
                IncludeAdditions include => $"{include.AttributeName} included=[{string.Join(", ", include.Values)}]",
                PassthroughBins bins => $"{bins.AttributeName} passthrough=[{string.Join(", ", bins.Values)}]",
                _ => throw new InvalidOperationException($"unrecognized outcome {outcome.GetType().Name}"),
            }));

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static async Task<(byte[] Cxt, byte[] Dat)> ConvertAsync(
        BedrockSpec spec, string csv, GroupingOptions? grouping = null)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        var calibration = grouping is null
            ? await Calibrator.CalibrateAsync(resolved, source)
            : await Calibrator.CalibrateAsync(resolved, source, grouping, observer: null, CancellationToken.None);
        Assert.True(calibration.TryGetValue(out var calibrated),
            string.Join("; ", calibration.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.True(ConversionPlanner.Plan(calibrated!, LabelStyle.Native).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        IAsyncEnumerable<EmittedObject> Emit(ICollection<BedrockDiagnostic> sink) => Emitter.EmitAsync(plan, source, sink);

        var cxt = new MemoryStream();
        using (var session = EmitReplay.Begin(Emit, diagnostics))
        {
            await CxtWriter.WriteAsync(plan, session.Open, WriterOptions.Native, cxt);
        }

        var dat = new MemoryStream();
        await DatWriter.WriteAsync(Emit(diagnostics), WriterOptions.Native, dat);

        Assert.Empty(ConversionFixtures.DataDiagnostics(diagnostics));
        return (cxt.ToArray(), dat.ToArray());
    }

    // --- The regression: several accumulators sharing one workspace ------------

    [Fact]
    public async Task Calibrate_WhenSeveralAttributesSpillIntoOneWorkspace_ThenTheExactCutsSurvive()
    {
        var spec = MultiAttributeSpec();

        var spilled = await CalibrateOkAsync(spec, Csv, TinyBudget());

        // `seq` is 1..24, one observation each, so its equal-frequency boundaries are arithmetic:
        // boundary k of 4 falls exactly at the edge after 24k/4 observations, and RightValue places
        // the cut on the upper value — 7, 13, 19. `rev` is the same population arriving backwards,
        // three bins: 9 and 17. Hand-derived from the corpus, not from the calibrator.
        Assert.Equal([7.0, 13.0, 19.0], CutsOf(spilled, "seq"));
        Assert.Equal([9.0, 17.0], CutsOf(spilled, "rev"));

        // The tie-heavy and percentile populations are checked against the in-memory path below
        // rather than re-derived here: an expectation that re-implemented the D-103 feasibility
        // window would be agreeing with the code under test.
        Assert.Equal(5, spilled.Calibrations.Count);
    }

    [Fact]
    public async Task Calibrate_WhenSeveralAttributesSpillIntoOneWorkspace_ThenIdenticalToTheInMemoryPath()
    {
        var spec = MultiAttributeSpec();

        var resident = await CalibrateOkAsync(spec, Csv);
        var spilledResult = await CalibrateAsync(spec, Csv, TinyBudget());

        Assert.True(spilledResult.TryGetValue(out var spilled),
            string.Join("; ", spilledResult.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        // §11.5/D-095: bounded memory decides where the population lives, never what it says. Every
        // outcome, in spec order, including the tie-heavy and percentile ones.
        Assert.Equal(Describe(resident), Describe(spilled!));

        // And the same diagnostics: a spurious storage failure would show up here as an extra Error
        // even if the cuts had somehow survived.
        Assert.Empty(spilledResult.Diagnostics);
    }

    [Fact]
    public async Task Calibrate_WhenASmallAttributeFinalizesWhileLargerRunsAreStillLive_ThenItStillCalibrates()
    {
        // The asymmetric case in isolation: one accumulator with 24 distinct values and one with 4,
        // the small one finalized first. Its own spill payload cannot cover the other's retained
        // runs, and it must not have to.
        var spec = new BedrockSpec(
            ConversionFixtures.Wide(hasHeader: false),
            [EqualFrequency("small", 1, 3), EqualFrequency("seq", 0, 4)]);

        var resident = await CalibrateOkAsync(spec, Csv);
        var spilled = await CalibrateOkAsync(spec, Csv, TinyBudget());

        Assert.Equal(Describe(resident), Describe(spilled));
        Assert.Equal([7.0, 13.0, 19.0], CutsOf(spilled, "seq"));
    }

    [Fact]
    public async Task Convert_WhenSeveralAttributesSpillIntoOneWorkspace_ThenByteIdenticalOutput()
    {
        var spec = MultiAttributeSpec();

        var (residentCxt, residentDat) = await ConvertAsync(spec, Csv);
        var (spilledCxt, spilledDat) = await ConvertAsync(spec, Csv, TinyBudget());

        // The whole obligation in two assertions: a shared workspace under memory pressure must not
        // move a single output byte of either format.
        Assert.Equal(residentCxt, spilledCxt);
        Assert.Equal(residentDat, spilledDat);
    }

    [Fact]
    public async Task Convert_WhenSeveralAttributesSpill_ThenAutoAndFrozenAgreeByteForByte()
    {
        // D-088 under the multi-attribute spill path: freezing changes when the cuts resolve, never
        // which — including when every cut was resolved from a spilled population.
        var auto = MultiAttributeSpec();
        var calibrated = await CalibrateOkAsync(auto, Csv, TinyBudget());

        var frozen = new BedrockSpec(
            ConversionFixtures.Wide(hasHeader: false),
            [
                Frozen("seq", 0, CutsOf(calibrated, "seq")),
                Frozen("small", 1, CutsOf(calibrated, "small")),
                Frozen("rev", 2, CutsOf(calibrated, "rev")),
                Frozen("tied", 3, CutsOf(calibrated, "tied")),
                Frozen("skew", 4, CutsOf(calibrated, "skew")),
            ]);

        var (autoCxt, autoDat) = await ConvertAsync(auto, Csv, TinyBudget());
        var (frozenCxt, frozenDat) = await ConvertAsync(frozen, Csv);

        Assert.Equal(frozenCxt, autoCxt);
        Assert.Equal(frozenDat, autoDat);
    }

    // --- Independent accounting witness ---------------------------------------

    [Fact]
    public async Task Calibrate_WhenSeveralAttributesSpill_ThenRetainedBytesStayWithinThreeTimesTheOriginalPayload()
    {
        var observer = new RecordingCalibrationObserver();

        var calibrated = await CalibrateOkAsync(MultiAttributeSpec(), Csv, TinyBudget(observer: observer));

        // T, accumulated in the test from the observed run writes: ONLY the original spills. The
        // merge output written alongside them is deliberately excluded, which is what makes the
        // bound below the tight one rather than a self-satisfying restatement.
        var originalPayload = observer.Written.Where(w => w.Initial).Sum(w => w.Size);
        var mergeOutput = observer.Written.Where(w => !w.Initial).Sum(w => w.Size);

        Assert.True(observer.Written.Count(w => w.Initial) > 5, "every attribute must genuinely spill more than once");
        Assert.True(mergeOutput > 0, "online consolidation and the replay merge must both have run");

        // The workspace-wide guarantee: retained bytes never passed three times the cumulative
        // ORIGINAL payload of the whole workspace, at any observed write or merge boundary.
        Assert.InRange(observer.PeakLiveBytes, 1, 3 * originalPayload);

        // T is cumulative across completed attributes, not reset when one finishes: the last
        // attribute's merge still had to be covered, and the run completed.
        Assert.Equal(5, calibrated.Calibrations.Count);

        // The bounds this correction must not have loosened, checked in the same run.
        Assert.True(observer.PeakLiveRuns <= 2, $"an attribute's run catalog peaked at {observer.PeakLiveRuns}, above the fan-in");
        Assert.True(observer.PeakOpenReaders <= 2, $"open readers peaked at {observer.PeakOpenReaders}, above the fan-in");
        Assert.Equal(0, observer.PeakPendingDeletions); // healthy storage retains nothing
        Assert.All(observer.Aggregates, modelled => Assert.InRange(
            modelled, 0, Math.Max(308, 5 * QuantileAccumulator.FloorBytes)));
    }

    [Fact]
    public async Task Calibrate_WhenDeletesFailPersistently_ThenTheRunStillHaltsWithAStorageError()
    {
        // The correction removes a false allowance, not the real one. With every consumed run stuck
        // on disk, the workspace genuinely fills, and calibration must still stop with an Error and
        // no result rather than merge its way through a failing device.
        var fileSystem = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var observer = new RecordingCalibrationObserver();

        var result = await CalibrateAsync(
            MultiAttributeSpec(), Csv, TinyBudget(fileSystem, observer));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // Bookkeeping stayed bounded right up to the halt (D-103's 4 x fan-in cap).
        Assert.True(observer.PeakPendingDeletions <= QuantileAccumulator.MaxPendingDeletions(2));
    }

    [Fact]
    public async Task Calibrate_WhenASpillWriteFailsUnderSeveralAttributes_ThenStillAnErrorAndNoResult()
    {
        // The other direction: a genuine in-path storage failure is still an Error, and still
        // crosses the seam as a diagnostic rather than a GroupingStorageException (P-14).
        var fileSystem = new FakeSpoolFileSystem { OnCreateRun = _ => StorageFaults.DiskFull() };

        var result = await CalibrateAsync(MultiAttributeSpec(), Csv, TinyBudget(fileSystem));

        Assert.False(result.IsOk);
        Assert.Equal(
            DiagnosticSeverity.Error,
            Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed).Severity);
    }

    [Fact]
    public async Task Calibrate_WhenCancelledMidIntake_ThenCancellationWinsOverAnyStorageDiagnostic()
    {
        var source = ConversionFixtures.SourceOver(Csv, MultiAttributeSpec().Binding);
        var schema = await source.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(MultiAttributeSpec(), schema);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Calibrator.CalibrateAsync(resolved, source, TinyBudget(), observer: null, cancellation.Token).AsTask());
    }

    // --- Triple: both orderings, several count-sensitive predicates ------------

    // Twelve subjects, four count-sensitive predicates. `s{i}` carries:
    //   p_seq   i               (12 distinct — the large payload)
    //   p_few   200 + i % 3     (3 distinct — the small one)
    //   p_rev   100 - i         (12 distinct, descending)
    //   p_tied  300 + i % 4     (4 distinct)
    //
    // Subject s0 additionally carries the two halves of the §5.3.1 rule, each switchable so a test
    // can observe what removing it does:
    //   `duplicateRow`    an exact repeat of `s0,p_seq,0` — the same cleaned (subject, predicate,
    //                     value), which must contribute exactly once however often it appears;
    //   `extraSpellings`  four further spellings of the same number — DISTINCT raw observations
    //                     under §5.3.1, which each contribute, and which then all fold into the one
    //                     canonical numeric value 0, giving it a count of five.
    private static string TripleData(bool interleaved, bool duplicateRow = true, bool extraSpellings = true)
    {
        var groups = Enumerable.Range(0, 12).Select(i => new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"s{i},p_seq,{i}"),
            string.Create(CultureInfo.InvariantCulture, $"s{i},p_few,{200 + (i % 3)}"),
            string.Create(CultureInfo.InvariantCulture, $"s{i},p_rev,{100 - i}"),
            string.Create(CultureInfo.InvariantCulture, $"s{i},p_tied,{300 + (i % 4)}"),
        }).ToList();

        if (duplicateRow)
        {
            groups[0].Add("s0,p_seq,0");
        }

        if (extraSpellings)
        {
            groups[0].AddRange(["s0,p_seq,0.0", "s0,p_seq,0.00", "s0,p_seq,0.000", "s0,p_seq,0.0000"]);
        }

        if (!interleaved)
        {
            return string.Join('\n', groups.SelectMany(g => g));
        }

        // Predicate-major: every subject recurs non-contiguously, so subject_grouped would reject it
        // and the unordered path's grouping backend must reconstruct the subjects. Emitting by slot
        // keeps each subject's first appearance in s0..s11 order, which is what makes the two
        // layouts comparable at all (§17 rule 3).
        var slots = groups.Max(g => g.Count);
        return string.Join(
            '\n',
            Enumerable.Range(0, slots).SelectMany(slot => groups.Where(g => slot < g.Count).Select(g => g[slot])));
    }

    private static AttributeSpec PredicateEqualFrequency(string name, string predicate, int bins) =>
        new(name, new PredicateSource(predicate, SourceValueType.Number), Include: true,
            new CalibrationPending(
                new PendingEqualFrequency(bins, TiePolicy.Left, CutPlacement.RightValue), CultureInfo.InvariantCulture),
            new OrdinalScale(OrdinalDirection.Le), DeclaredDomain: [], RestrictTo: [],
            ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static BedrockSpec TripleSpec(TripleOrdering ordering) =>
        new(ConversionFixtures.Triple(ordering),
        [
            PredicateEqualFrequency("few", "p_few", 2),
            PredicateEqualFrequency("seq", "p_seq", 3),
            PredicateEqualFrequency("rev", "p_rev", 3),
            PredicateEqualFrequency("tied", "p_tied", 2),
        ]);

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

    private static async Task<CalibratedSpec> CalibrateTripleOkAsync(
        BedrockSpec spec, string data, GroupingOptions? options = null)
    {
        var result = await CalibrateTripleAsync(spec, data, options);
        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return calibrated!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalibrateTriple_WhenSeveralPredicatesSpill_ThenIdenticalToTheInMemoryPath(bool interleaved)
    {
        var ordering = interleaved ? TripleOrdering.Unordered : TripleOrdering.SubjectGrouped;
        var spec = TripleSpec(ordering);
        var data = TripleData(interleaved);

        var resident = await CalibrateTripleOkAsync(spec, data);
        var spilled = await CalibrateTripleOkAsync(spec, data, TinyBudget());

        Assert.Equal(Describe(resident), Describe(spilled));
    }

    [Fact]
    public async Task CalibrateTriple_WhenSeveralPredicatesSpill_ThenBothOrderingsAgree()
    {
        // The same observations in two physical layouts: the grouped single pass and the unordered
        // path's second, backend-grouped pass must count the same population. This is where a
        // count-calibration workspace and a grouping workspace are live in the same run.
        var grouped = await CalibrateTripleOkAsync(
            TripleSpec(TripleOrdering.SubjectGrouped), TripleData(interleaved: false), TinyBudget());
        var unordered = await CalibrateTripleOkAsync(
            TripleSpec(TripleOrdering.Unordered), TripleData(interleaved: true), TinyBudget());

        Assert.Equal(Describe(grouped), Describe(unordered));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalibrateTriple_WhenSeveralPredicatesSpill_ThenSubjectLocalDeduplicationSurvives(bool interleaved)
    {
        // §5.3.1 under the multi-attribute spill path, proved by consequence in both directions
        // rather than by reading a private counter — and under an ordering that also drives the
        // grouping backend, so the count-calibration workspace and a grouping workspace are live in
        // the same run.
        var ordering = interleaved ? TripleOrdering.Unordered : TripleOrdering.SubjectGrouped;
        var spec = TripleSpec(ordering);

        var both = await CalibrateTripleOkAsync(spec, TripleData(interleaved), TinyBudget());

        // One half: an exact repeat of a cleaned (subject, predicate, value) contributes ONCE, so
        // deleting it must change nothing at all.
        var withoutDuplicate = await CalibrateTripleOkAsync(
            spec, TripleData(interleaved, duplicateRow: false), TinyBudget());
        Assert.Equal(Describe(both), Describe(withoutDuplicate));

        // The other half: differently spelled observations of the same number are DISTINCT
        // observations that each contribute, so deleting them must move the boundary. Without this
        // the test above would also pass for an implementation that deduplicated everything.
        var withoutSpellings = await CalibrateTripleOkAsync(
            spec, TripleData(interleaved, extraSpellings: false), TinyBudget());
        Assert.NotEqual(
            string.Join(",", CutsOf(both, "seq").Select(Format)),
            string.Join(",", CutsOf(withoutSpellings, "seq").Select(Format)));

        // And the spellings folded into one canonical value rather than five: `seq`'s population is
        // twelve distinct numbers whatever their spelling, so three bins remain satisfiable and the
        // other predicates are untouched.
        Assert.Equal(4, both.Calibrations.Count);
        Assert.Equal(Describe(withoutSpellings).Split(" | ")[0], Describe(both).Split(" | ")[0]); // `few` unaffected
    }
}
