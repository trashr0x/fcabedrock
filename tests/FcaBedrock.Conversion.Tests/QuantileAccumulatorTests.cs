using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The bounded fixed-capacity count-sensitive accumulator (§11.5, D-095/D-103): sizing,
/// fill-and-spill intake, the checked count arithmetic, and the two-pass extraction.
/// <para>
/// The tier-1 resource proofs deliberately <b>re-derive the model in the test</b> from the
/// observed capacity rather than calling <c>QuantileAccumulator.Modeled</c>, so the assertion
/// cannot pass merely by agreeing with itself.
/// </para>
/// </summary>
public sealed class QuantileAccumulatorTests
{
    // The test-side copy of the model. Deliberately duplicated arithmetic (never the production
    // constant): if a production constant changes silently, this must fail.
    private const long TestSlotBytes = 24 /* dictionary entry */ + 4 /* bucket */ + 16 /* sort-buffer element */;
    private const long TestFixedBytes = 384;

    private static long TestModeled(int capacity) => TestFixedBytes + (capacity * TestSlotBytes);

    private static GroupingOptions Options(
        long budget = GroupingOptions.DefaultMaxBufferedBytes,
        int fanIn = GroupingOptions.DefaultMaxMergeFanIn,
        ISpoolFileSystem? fileSystem = null,
        IGroupingObserver? observer = null) =>
        new(budget, fanIn, tempDirectory: null, fileSystem, observer);

    private sealed record Harness(
        QuantileAccumulator Accumulator, SpoolWorkspace<ValueCount> Workspace, CalibrationBudget Budget) : IDisposable
    {
        public void Dispose() => Workspace.Cleanup();
    }

    // The observer watches BOTH tiers, so it is wired to the spool options as well as the
    // accumulator — otherwise the run/write signals never fire and a resource assertion would
    // pass vacuously.
    private static Harness Build(
        long budget = GroupingOptions.DefaultMaxBufferedBytes,
        int fanIn = GroupingOptions.DefaultMaxMergeFanIn,
        int attributes = 1,
        RecordingCalibrationObserver? observer = null,
        ISpoolFileSystem? fileSystem = null,
        string name = "score")
    {
        var options = Options(budget, fanIn, fileSystem, observer);
        var workspace = new SpoolWorkspace<ValueCount>(
            options, ValueCountCodec.Instance, new GroupingReports(),
            QuantileAccumulator.MaxPendingDeletions(options.MaxMergeFanIn));
        var calibrationBudget = new CalibrationBudget(options.MaxBufferedBytes, attributes, observer);
        var accumulator = new QuantileAccumulator(
            name, CultureInfo.InvariantCulture, calibrationBudget, workspace, options, observer, CancellationToken.None);
        return new Harness(accumulator, workspace, calibrationBudget);
    }

    private static double[] Cuts(QuantileAccumulator accumulator, int bins, TiePolicy tie, CutPlacement placement)
    {
        accumulator.EndIntake();
        accumulator.PrepareReplay();
        var cuts = accumulator.TryExtractEqualFrequencyCuts(new PendingEqualFrequency(bins, tie, placement), out _);
        accumulator.Release();
        return cuts!;
    }

    private static void Feed(QuantileAccumulator accumulator, params double[] population)
    {
        var tally = new DiagnosticTally();
        foreach (var value in population)
        {
            accumulator.Observe(value.ToString("R", CultureInfo.InvariantCulture), tally);
        }
    }

    // --- Sizing and the fixed-capacity model ----------------------------------

    [Fact]
    public void Sizing_WhenBudgetIsAmple_ThenReportsTheAcceptedCapacityNotTheRequest()
    {
        var observer = new RecordingCalibrationObserver();
        using var harness = Build(budget: 100_000, observer: observer);

        var (attribute, capacity, modeled) = Assert.Single(observer.Sized);

        Assert.Equal("score", attribute);
        Assert.Equal(capacity, harness.Accumulator.Capacity);
        Assert.Equal(TestModeled(capacity), modeled);

        // The whole point of asking EnsureCapacity: the accepted capacity is a fixed point of the
        // runtime's own prime rounding, which the naive request is not. Charging the request would
        // under-count the arrays the runtime actually allocated.
        var naiveRequest = (int)((100_000 - TestFixedBytes) / TestSlotBytes);
        Assert.Equal(capacity, new Dictionary<double, long>(capacity).EnsureCapacity(capacity));
        Assert.NotEqual(naiveRequest, capacity);

        // And the accepted capacity still fits the share: sizing reduced deterministically rather
        // than accepting the round-up's overshoot.
        Assert.True(modeled <= 100_000, $"modeled {modeled} exceeds the 100000 share");
    }

    [Fact]
    public void Sizing_WhenBudgetIsBelowTheFloor_ThenOneEntryAtTheFloorCost()
    {
        var observer = new RecordingCalibrationObserver();
        using var harness = Build(budget: 1, observer: observer);

        // A share below the floor cannot be honored — an accumulator retaining nothing cannot
        // count — so the overshoot is the documented per-attribute FloorBytes, not a failure.
        var (_, capacity, modeled) = Assert.Single(observer.Sized);
        Assert.True(capacity >= 1);
        Assert.Equal(TestModeled(capacity), modeled);
        Assert.Equal(QuantileAccumulator.FloorBytes, modeled);
    }

    [Fact]
    public void Sizing_WhenSeveralAttributesShareTheBudget_ThenTheAggregateHonoursTheStatedBound()
    {
        const long budget = 60_000;
        const int attributes = 4;
        var observer = new RecordingCalibrationObserver();
        var options = Options(budget: budget);
        var workspace = new SpoolWorkspace<ValueCount>(options, ValueCountCodec.Instance, new GroupingReports());
        try
        {
            var shared = new CalibrationBudget(budget, attributes, observer);
            for (var i = 0; i < attributes; i++)
            {
                _ = new QuantileAccumulator(
                    $"a{i}", CultureInfo.InvariantCulture, shared, workspace, options, observer, CancellationToken.None);
            }

            // Independently recomputed from the OBSERVED capacities — not from the production
            // formula — and checked against the honestly-stated bound (D-095's tier 1).
            var aggregate = observer.Sized.Sum(s => TestModeled(s.Capacity));
            Assert.Equal(attributes, observer.Sized.Count);
            Assert.True(
                aggregate <= Math.Max(budget, attributes * QuantileAccumulator.FloorBytes),
                $"aggregate {aggregate} exceeds max(budget, A·FloorBytes)");
        }
        finally
        {
            workspace.Cleanup();
        }
    }

    [Fact]
    public void Sizing_WhenBudgetIsTiny_ThenTheFloorClampKeepsEveryShareUsable()
    {
        // A pathologically small budget divided across many attributes would give a zero or
        // negative share without the clamp; the honest consequence is the A·FloorBytes arm of the
        // bound, which the test states rather than hides.
        var budget = new CalibrationBudget(budget: 8, countSensitiveAttributes: 64, observer: null);

        Assert.Equal(QuantileAccumulator.FloorBytes, budget.Share);
    }

    // --- Tier 1: the modeled bound proved against the REAL x64 layout -----------
    //
    // The RowCodecResidentTests posture (D-082): the actual retained bytes are derived from raw
    // .NET-10-CoreCLR-x64 layout literals stated HERE — never from the production constants — so
    // the bound is proved against reality rather than recomputed from the same formula. An
    // under-charged FixedBytes or SlotBytes therefore fails here rather than passing silently.

    private const long RealObjectHeader = 16; // sync-block index + method-table pointer
    private const long RealArrayHeader = 24;  // object header (16) + length/bounds (8)

    // Dictionary<double, long>.Entry: int hashCode + int next + double key + long value.
    private const long RealEntryBytes = 4 + 4 + 8 + 8;
    private const long RealBucketBytes = 4; // the parallel int[] bucket array

    // The Dictionary object itself: header + _buckets/_entries/_comparer/_keys/_values refs (5 × 8),
    // _fastModMultiplier (8), and _count/_freeList/_freeCount/_version (4 × 4).
    private const long RealDictionaryObject = RealObjectHeader + (5 * 8) + 8 + (4 * 4);

    // The QuantileAccumulator object itself, which FixedBytes also claims to cover. Its own
    // allocation is tier 1; what its reference SLOTS point to is partitioned:
    //   tier 1 — _counts, _sortBuffer (the population state, charged in full below);
    //   tier 2 — _runs and _consolidated, i.e. the run catalog, which the D-095 contract bounds by
    //            COUNT and shape (≤ fan-in handles), never by a byte constant.
    // Either way the 8-byte slot lives in this object and is counted here; only the pointees differ.
    private const long RealAccumulatorRefs = 9 * 8;      // name, culture, workspace, options, observer, budget, runs, counts, sortBuffer
    private const long RealAccumulatorToken = 8;         // CancellationToken wraps one source ref
    private const long RealAccumulatorInts = 2 * 4;      // _capacity, _resident
    private const long RealAccumulatorLongs = 2 * 8;     // _total, _spilledBytes
    private const long RealAccumulatorHandle = 24;       // SpoolRunHandle? = string ref + long + hasValue, padded

    private const long RealAccumulatorObject =
        RealObjectHeader + RealAccumulatorRefs + RealAccumulatorToken
        + RealAccumulatorInts + RealAccumulatorLongs + RealAccumulatorHandle;

    // A Dictionary sized to `capacity` allocates buckets and entries at exactly that prime, and the
    // accumulator's sort buffer is a ValueCount[capacity] alongside them. Everything FixedBytes
    // claims — the accumulator object, the Dictionary object, and the three array headers — is
    // accounted here, so the partition is complete rather than partial.
    private static long RealRetainedBytes(int capacity, long elementBytes) =>
        RealAccumulatorObject
        + RealDictionaryObject
        + (RealArrayHeader + (RealBucketBytes * capacity))   // int[] buckets
        + (RealArrayHeader + (RealEntryBytes * capacity))    // Entry[] entries
        + (RealArrayHeader + (elementBytes * capacity));     // ValueCount[] sort buffer

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(1931)]
    public void Modeled_WhenComparedToTheRealX64Layout_ThenConservativeForBothTheDictionaryAndTheSortBuffer(int capacity)
    {
        // Unsafe.SizeOf reads the real ValueCount stride, so a layout change breaks this rather
        // than silently under-charging the model.
        var elementBytes = Unsafe.SizeOf<ValueCount>();
        Assert.Equal(16, elementBytes);

        var real = RealRetainedBytes(capacity, elementBytes);
        var modeled = QuantileAccumulator.Modeled(capacity);

        Assert.True(modeled >= real, $"Modeled({capacity}) = {modeled} under-charges the real retained {real}");
    }

    [Fact]
    public void Modeled_WhenDecomposed_ThenBothConstantsCoverTheirRealCounterparts()
    {
        // The two constants isolated, each against its independently-stated real counterpart: the
        // per-entry slot must cover bucket + entry + sort-buffer element, and the fixed part must
        // cover the Dictionary object plus the three array headers.
        var elementBytes = Unsafe.SizeOf<ValueCount>();
        var realSlot = RealBucketBytes + RealEntryBytes + elementBytes;
        var realFixed = RealAccumulatorObject + RealDictionaryObject + (3 * RealArrayHeader);

        var modeledSlot = QuantileAccumulator.Modeled(1) - QuantileAccumulator.Modeled(0);
        var modeledFixed = QuantileAccumulator.Modeled(0);

        Assert.True(modeledSlot >= realSlot, $"SlotBytes {modeledSlot} under-charges the real slot {realSlot}");
        Assert.True(modeledFixed >= realFixed, $"FixedBytes {modeledFixed} under-charges the real fixed {realFixed}");

        // And the test-side copy of the model tracks production, so the bound assertions elsewhere
        // in this file are computing what production actually charges.
        Assert.Equal(TestSlotBytes, modeledSlot);
        Assert.Equal(TestFixedBytes, modeledFixed);
    }

    [Fact]
    public void FloorBytes_WhenDerivedIndependently_ThenMatchesTheProductionConstant()
    {
        // The floor is Modeled(the runtime's ACCEPTED capacity for one requested entry) — derived
        // here from the runtime directly, so a production floor that guessed at the prime rounding
        // (rather than asking) would fail.
        var acceptedForOne = new Dictionary<double, long>(1).EnsureCapacity(1);

        Assert.Equal(TestModeled(acceptedForOne), QuantileAccumulator.FloorBytes);
        Assert.True(QuantileAccumulator.FloorBytes >= RealRetainedBytes(acceptedForOne, Unsafe.SizeOf<ValueCount>()));
    }

    [Fact]
    public void Sizing_WhenAccumulatorIsAllocated_ThenTheRealHeapCostStaysUnderTheModeledBytes()
    {
        // The GC-delta sanity check the layout arithmetic cannot give: measure the actual managed
        // bytes an accumulator's retained state costs and confirm the model covers it. x64-gated
        // like SpoolConfidentialityTests, because the layout constants are validated for .NET 10
        // CoreCLR x64 only (the roadmap's M8 cross-platform item owns the other targets).
        Assert.SkipUnless(
            RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "x64 retained-layout constants (D-082/D-103); other targets are the roadmap's M8 item.");

        const long budget = 100_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        using var harness = Build(budget: budget);
        var after = GC.GetAllocatedBytesForCurrentThread();

        // The sizing probes are transients the GC reclaims (the D-082 exclusion), so this measures
        // allocation rather than the stable graph — which is why it is a SANITY check: the retained
        // state cannot exceed what was allocated, and the modeled bytes must cover the retained
        // state. Both directions are asserted against the accepted capacity.
        var modeled = QuantileAccumulator.Modeled(harness.Accumulator.Capacity);
        var real = RealRetainedBytes(harness.Accumulator.Capacity, Unsafe.SizeOf<ValueCount>());

        Assert.True(modeled >= real, $"modeled {modeled} under-charges real retained {real}");
        Assert.True(modeled <= budget, $"modeled {modeled} exceeds the {budget} share");
        Assert.True(after - before >= real, $"allocated {after - before} is below the real retained {real}");
    }

    // --- Intake, spill, and release -------------------------------------------

    [Fact]
    public void Intake_WhenExistingKeysRepeat_ThenNoSpillHappens()
    {
        var observer = new RecordingCalibrationObserver();
        using var harness = Build(budget: TestModeled(3), fanIn: 2, observer: observer);

        // Far more observations than the capacity, but only two DISTINCT values: an existing-key
        // increment allocates nothing and can never force a spill.
        Feed(harness.Accumulator, Enumerable.Repeat(1.0, 500).Concat(Enumerable.Repeat(2.0, 500)).ToArray());

        Assert.False(harness.Accumulator.Spilled);
        Assert.Empty(observer.Written);
        Assert.Equal(1000, harness.Accumulator.Total);
    }

    [Fact]
    public void Intake_WhenNewKeysExceedCapacity_ThenSpillsAndTheDictionaryNeverGrows()
    {
        var observer = new RecordingCalibrationObserver();
        using var harness = Build(budget: TestModeled(3), fanIn: 4, observer: observer);
        var capacity = harness.Accumulator.Capacity;

        Feed(harness.Accumulator, Enumerable.Range(1, 40).Select(i => (double)i).ToArray());

        Assert.True(harness.Accumulator.Spilled);
        Assert.NotEmpty(observer.Written);

        // The capacity is the budget here: it is fixed at sizing and survives every spill, so the
        // modeled bytes are a constant for the accumulator's whole life.
        Assert.Equal(capacity, harness.Accumulator.Capacity);
        Assert.All(observer.Aggregates, bytes => Assert.True(bytes <= TestModeled(capacity)));
    }

    [Fact]
    public void Intake_WhenManyTinySpills_ThenTheRunCatalogStaysWithinTheFanIn()
    {
        // Without online consolidation the catalog would retain one handle per spill — memory
        // proportional to the population, which is exactly what the bounded model forbids.
        var observer = new RecordingCalibrationObserver();
        using var harness = Build(budget: TestModeled(3), fanIn: 3, observer: observer);

        Feed(harness.Accumulator, Enumerable.Range(1, 400).Select(i => (double)i).ToArray());

        Assert.True(observer.Written.Count(w => w.Initial) > 3, "the population must genuinely spill many times");
        Assert.True(observer.PeakLiveRuns <= 3, $"live runs peaked at {observer.PeakLiveRuns}, above the fan-in");
        Assert.Contains(observer.Written, w => !w.Initial); // consolidation output proves it ran
    }

    [Fact]
    public void EndIntake_WhenSpilled_ThenResidentStateIsReleasedBeforeAnyPostIntakeMerge()
    {
        var observer = new RecordingCalibrationObserver();
        using var harness = Build(budget: TestModeled(3), fanIn: 4, observer: observer);
        Feed(harness.Accumulator, Enumerable.Range(1, 40).Select(i => (double)i).ToArray());

        harness.Accumulator.EndIntake();
        harness.Budget.ReportAggregate();

        // The dictionary and the sort buffer are both gone before the merge begins, so the merge
        // never runs alongside the state it replaced.
        Assert.Equal(0, harness.Accumulator.ModeledBytes);
        Assert.Equal(0, observer.Aggregates[^1]);

        harness.Accumulator.PrepareReplay();
        Assert.Equal(0, harness.Accumulator.ModeledBytes);
    }

    [Fact]
    public void EndIntake_WhenNothingSpilled_ThenNoStorageIsEverTouched()
    {
        var observer = new RecordingCalibrationObserver();
        using var harness = Build(budget: 100_000, observer: observer);
        Feed(harness.Accumulator, [3, 1, 2, 1]);

        var cuts = Cuts(harness.Accumulator, bins: 2, TiePolicy.Left, CutPlacement.RightValue);

        // The zero-spill path uses the buffer it already owns and runs the same two-pass walk in
        // memory: no workspace, no disk, and the same code deciding the cuts.
        Assert.False(harness.Workspace.Created);
        Assert.Empty(observer.Written);
        Assert.Equal([2.0], cuts);
    }

    // --- Checked count arithmetic ---------------------------------------------

    [Fact]
    public void Intake_WhenTheRunningTotalWouldOverflow_ThenCalibrationPopulationTooLargeIsSignalled()
    {
        using var harness = Build(budget: 100_000);
        Feed(harness.Accumulator, [1.0]);

        // Seeded rather than fed: a long.MaxValue-sized fixture is not constructible, and the
        // condition is a contract-totality row, not a data scenario (G-13).
        harness.Accumulator.SeedTotalForTest(long.MaxValue);

        var ex = Assert.Throws<CalibrationPopulationOverflowException>(() => Feed(harness.Accumulator, [2.0]));
        Assert.Equal("score", ex.AttributeName);
    }

    // --- Numeric identity ------------------------------------------------------

    [Fact]
    public void Intake_WhenBothZeroSpellingsOccur_ThenTheyAggregateAsOnePositiveZeroValue()
    {
        using var harness = Build(budget: 100_000);
        var tally = new DiagnosticTally();

        harness.Accumulator.Observe("-0.0", tally);
        harness.Accumulator.Observe("0", tally);
        harness.Accumulator.Observe("0.0", tally);
        harness.Accumulator.Observe("5", tally);

        // One distinct value, not two: with bins = 2 and m = 2 the only gap is 0 → 5, so a
        // phantom -0 group would have made m = 3 and changed the cuts.
        var cuts = Cuts(harness.Accumulator, bins: 2, TiePolicy.Left, CutPlacement.RightValue);

        Assert.Equal([5.0], cuts);
        Assert.Equal(4, harness.Accumulator.Total);
    }

    [Fact]
    public void Intake_WhenTheZeroCutIsSelected_ThenItCarriesPositiveZeroBits()
    {
        using var harness = Build(budget: 100_000);
        var tally = new DiagnosticTally();
        harness.Accumulator.Observe("-0.0", tally);
        harness.Accumulator.Observe("-1", tally);

        // The gap is -1 → 0, so right_value places the cut on the zero group's value. It must be
        // positive zero: a -0 cut would render "-0" into a bin label and a hash (G-6).
        var cuts = Cuts(harness.Accumulator, bins: 2, TiePolicy.Left, CutPlacement.RightValue);

        Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(cuts[0]));
    }

    [Fact]
    public void Intake_WhenAValueIsUnparseableOrNonFinite_ThenExcludedFromThePopulationAndTallied()
    {
        using var harness = Build(budget: 100_000);
        var tally = new DiagnosticTally();

        harness.Accumulator.Observe("1", tally);
        harness.Accumulator.Observe("wibble", tally);
        harness.Accumulator.Observe("Infinity", tally);
        harness.Accumulator.Observe("NaN", tally);
        harness.Accumulator.Observe("2", tally);

        // §7/§11.5: only finite parsed values enter the population; the rest are excluded and
        // aggregated for this phase's own SourceValueUnparseable.
        Assert.Equal(2, harness.Accumulator.Total);
        Assert.Equal(3, tally.Count);
        Assert.Equal([2.0], Cuts(harness.Accumulator, bins: 2, TiePolicy.Left, CutPlacement.RightValue));
    }

    // --- The pinned equal-frequency vectors (spill and non-spill alike) --------

    public static TheoryData<string, double[], int, TiePolicy, CutPlacement, double[]> Vectors() => new()
    {
        // 1. §11.5's own example: two boundaries fall inside the tied 2-run, and the formula must
        //    still yield TWO distinct ascending cuts — three bins, never a collapse to two.
        { "spec-example-left", [1, 2, 2, 2, 3, 4], 3, TiePolicy.Left, CutPlacement.RightValue, [3, 4] },

        // 2. §11.5's second example, both policies: the tied 2-group goes low (cut 3) or high (cut 2).
        { "spec-example-tie-left", [1, 2, 2, 2, 3], 2, TiePolicy.Left, CutPlacement.RightValue, [3] },
        { "spec-example-tie-right", [1, 2, 2, 2, 3], 2, TiePolicy.Right, CutPlacement.RightValue, [2] },

        // 3. An exact group edge: N·k = 4 = C_2·bins, so the policy does not apply and the even
        //    split {1,2},{3,4} results — where a naive "always apply the policy" rule gives 2.
        { "exact-group-edge", [1, 2, 3, 4], 2, TiePolicy.Right, CutPlacement.RightValue, [3] },

        // 4. G-5 example 1 — collision: both boundaries prefer gap 2, so the window pushes the
        //    first down to gap 1 (against "left") to keep a gap for the second.
        { "g5-collision", [1, 2, 2, 2, 3], 3, TiePolicy.Left, CutPlacement.RightValue, [2, 3] },

        // 5. G-5 example 2 — last-group edge: d = m = 5 is not a gap, so the tied 5-group lands
        //    upper despite "left".
        { "g5-last-edge", [1, 2, 3, 4, 5, 5, 5, 5, 5, 5], 2, TiePolicy.Left, CutPlacement.RightValue, [5] },

        // 6. G-5 example 3 — first-group edge: d = 0 is not a gap, so the tied 5-group lands lower
        //    despite "right".
        { "g5-first-edge", [5, 5, 5, 5, 5, 5, 6, 7, 8, 9], 2, TiePolicy.Right, CutPlacement.RightValue, [6] },

        // 7. d = 0 infeasible on a two-distinct population.
        { "head-tie-right", [5, 5, 5, 9], 2, TiePolicy.Right, CutPlacement.RightValue, [9] },

        // 8. Midpoint placement over the same gap as vector 7: (5 + 9)/2 = 7.
        { "head-tie-midpoint", [5, 5, 5, 9], 2, TiePolicy.Right, CutPlacement.Midpoint, [7] },

        // 9. Negative and fractional values through the midpoint form.
        //    m = 4, C = [1,2,3,4], N = 4, bins = 2. k = 1 targets N·k = 4 = C_2·bins → an exact
        //    edge → d = 2; window [1,3] leaves it at gap 2 = (-1, 2.5). That gap crosses zero, so
        //    the average form gives (-1 + 2.5)/2 = 0.75.
        { "negative-midpoint", [-4, -1, 2.5, 6], 2, TiePolicy.Left, CutPlacement.Midpoint, [0.75] },

        // 10. A saturated allocation with FOUR distinct values, so it can also be forced through
        //     the spill path below (the floor capacity is three entries). m = 4, C = [1,4,5,6],
        //     N = 6, bins = 4. k = 1 targets 6, inside group 2 (4 < 6 < 16) → "left" prefers
        //     d = 2, but hi = 4-1-(4-1-1) = 1 reserves gaps for the two later boundaries, so it
        //     is pushed DOWN to gap 1 → cut 2. k = 2 targets 12, inside group 2 → d = 2, window
        //     [2,2] → cut 3. k = 3 targets 18, inside group 3 (16 < 18 < 20) → d = 3, window
        //     [3,3] → cut 4.
        { "saturated-override", [1, 2, 2, 2, 3, 4], 4, TiePolicy.Left, CutPlacement.RightValue, [2, 3, 4] },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void ExtractEqualFrequencyCuts_WhenPopulationIsPinned_ThenCutsMatchTheHandDerivedVector(
        string name, double[] population, int bins, TiePolicy tie, CutPlacement placement, double[] expected)
    {
        _ = name;
        using var harness = Build(budget: 100_000);
        Feed(harness.Accumulator, population);

        Assert.Equal(expected, Cuts(harness.Accumulator, bins, tie, placement));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void ExtractEqualFrequencyCuts_WhenForcedToSpill_ThenIdenticalToTheInMemoryPath(
        string name, double[] population, int bins, TiePolicy tie, CutPlacement placement, double[] expected)
    {
        _ = name;

        // Every vector under a budget so small the population cannot stay resident: §11.5 requires
        // the spill and non-spill paths to produce identical cuts. Vectors whose distinct count is
        // at or below the floor capacity stay resident even here — the floor is the runtime's
        // smallest dictionary bucket, not something a budget can shrink — so the spill path itself
        // is proved by the assertion below and by the saturated case that follows.
        using var harness = Build(budget: TestModeled(1), fanIn: 2);
        Feed(harness.Accumulator, population);

        Assert.Equal(
            population.Distinct().Count() > harness.Accumulator.Capacity,
            harness.Accumulator.Spilled);
        Assert.Equal(expected, Cuts(harness.Accumulator, bins, tie, placement));
    }

    [Fact]
    public void ExtractEqualFrequencyCuts_WhenASaturatedAllocationIsForcedThroughSpill_ThenTheReplayAllocatesTheSameGaps()
    {
        // The case the consolidated-run replay exists for: the feasibility window overrides the
        // tie preference, which needs the GLOBAL distinct count before the first boundary is
        // allocated and the gap-adjacent values after it — neither knowable from one forward walk.
        // An easy distribution's byte equality would not prove the replay works; this does.
        double[] population = [1, 2, 2, 2, 3, 4];
        using var spilled = Build(budget: TestModeled(1), fanIn: 2, name: "spilled");
        using var resident = Build(budget: 100_000, name: "resident");

        Feed(spilled.Accumulator, population);
        Feed(resident.Accumulator, population);

        // Read before extraction: Release() drops the runs, so the flag only tells the truth here.
        Assert.True(spilled.Accumulator.Spilled);
        Assert.False(resident.Accumulator.Spilled);

        var spilledCuts = Cuts(spilled.Accumulator, bins: 4, TiePolicy.Left, CutPlacement.RightValue);
        var residentCuts = Cuts(resident.Accumulator, bins: 4, TiePolicy.Left, CutPlacement.RightValue);

        Assert.Equal([2.0, 3.0, 4.0], spilledCuts);
        Assert.Equal(residentCuts, spilledCuts);
    }

    // --- The replay channels, isolated from the merge channels -----------------
    //
    // Both the merger's input reads and the replay reader go through SpoolWorkspace.OpenRun, so
    // they share the MergeRead operation label and an UNCONDITIONAL fault would always land on the
    // merger first — proving the wrong channel. These arm the fault only after EndIntake and
    // PrepareReplay have completed cleanly, so the consolidated run exists and the very next read
    // is the replay's own.

    private sealed record ReplayHarness(
        QuantileAccumulator Accumulator, SpoolWorkspace<ValueCount> Workspace, FakeSpoolFileSystem FileSystem) : IDisposable
    {
        public void Dispose() => Workspace.Cleanup();
    }

    // Spills, consolidates, and stops at the brink of extraction — the caller then arms its fault.
    private static ReplayHarness ConsolidatedAndReadyToReplay()
    {
        var fileSystem = new FakeSpoolFileSystem();
        var options = Options(budget: TestModeled(1), fanIn: 2, fileSystem: fileSystem);
        var workspace = new SpoolWorkspace<ValueCount>(
            options, ValueCountCodec.Instance, new GroupingReports(), QuantileAccumulator.MaxPendingDeletions(2));
        var budget = new CalibrationBudget(options.MaxBufferedBytes, 1, observer: null);
        var accumulator = new QuantileAccumulator(
            "score", CultureInfo.InvariantCulture, budget, workspace, options, observer: null, CancellationToken.None);

        Feed(accumulator, [1, 2, 3, 4, 5, 6, 7, 8]);
        accumulator.EndIntake();
        accumulator.PrepareReplay(); // the merge runs HERE, cleanly, before any fault is armed
        Assert.True(accumulator.Spilled);
        return new ReplayHarness(accumulator, workspace, fileSystem);
    }

    private static void AssertReplayFails(ReplayHarness harness) =>
        Assert.Throws<GroupingStorageException>(() =>
            harness.Accumulator.TryExtractEqualFrequencyCuts(
                new PendingEqualFrequency(2, TiePolicy.Left, CutPlacement.RightValue), out _));

    [Fact]
    public void Replay_WhenTheConsolidatedRunCannotBeOpened_ThenTheStorageFailureSurfaces()
    {
        using var harness = ConsolidatedAndReadyToReplay();

        // The replay reader's OPEN faults: an in-path storage failure, never a silent partial
        // population.
        harness.FileSystem.OnOpenRun = _ => StorageFaults.AccessDenied();

        AssertReplayFails(harness);
    }

    [Fact]
    public void Replay_WhenTheConsolidatedRunReadFaults_ThenTheStorageFailureSurfaces()
    {
        using var harness = ConsolidatedAndReadyToReplay();

        // The replay reader's READ faults after a successful open — a device error mid-walk.
        harness.FileSystem.WrapReadStream = (_, inner) =>
        {
            inner.Dispose();
            return new ThrowingReadStream();
        };

        AssertReplayFails(harness);
    }

    [Fact]
    public void Replay_WhenTheConsolidatedRunIsFramedWrong_ThenTheStorageFailureSurfaces()
    {
        using var harness = ConsolidatedAndReadyToReplay();

        // The replay reader's FRAMING channel: a record that claims more bytes than the run holds
        // is safely-identifiable corruption, not a row silently decoded into the population.
        harness.FileSystem.WrapReadStream = (_, inner) =>
        {
            inner.Dispose();
            var run = ForgedRun(1.0, 5);
            return new MemoryStream(run[..^3], writable: false); // the payload tail is missing
        };

        AssertReplayFails(harness);
    }

    // One correctly-prefixed spool record whose payload can then be truncated by the caller:
    // a length prefix, then rank (int32) + seq (int64) + the codec's fixed 16-byte payload.
    private static byte[] ForgedRun(double value, long count)
    {
        const int recordLength = SpoolRunFormat.HeaderBytes + ValueCountCodec.PayloadBytes;
        var run = new byte[sizeof(int) + recordLength];
        var span = run.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, recordLength);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 0);
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], 0);
        BinaryPrimitives.WriteDoubleLittleEndian(span[16..], value);
        BinaryPrimitives.WriteInt64LittleEndian(span[24..], count);
        return run;
    }

    [Fact]
    public void ExtractEqualFrequencyCuts_WhenFewerDistinctValuesThanBins_ThenNull()
    {
        using var harness = Build(budget: 100_000);
        Feed(harness.Accumulator, [1, 1, 2, 2]);
        harness.Accumulator.EndIntake();
        harness.Accumulator.PrepareReplay();

        // §11.5: two distinct values cannot separate three count-placed bins, so calibration
        // stops rather than silently producing fewer bins.
        var cuts = harness.Accumulator.TryExtractEqualFrequencyCuts(
            new PendingEqualFrequency(3, TiePolicy.Left, CutPlacement.RightValue), out var distinct);

        Assert.Null(cuts);
        Assert.Equal(2, distinct);
    }

    [Fact]
    public void ExtractEqualFrequencyCuts_WhenDistinctEqualsBins_ThenAcceptedWithOneGapPerBoundary()
    {
        using var harness = Build(budget: 100_000);
        Feed(harness.Accumulator, [1, 2, 3]);

        // m == bins is the tightest feasible shape: every gap is spoken for.
        Assert.Equal([2.0, 3.0], Cuts(harness.Accumulator, bins: 3, TiePolicy.Left, CutPlacement.RightValue));
    }

    [Fact]
    public void ExtractEqualFrequencyCuts_WhenBinsIsLarge_ThenExactlyBinsMinusOneDistinctAscendingCuts()
    {
        using var harness = Build(budget: 100_000);
        Feed(harness.Accumulator, Enumerable.Range(1, 20).Select(i => (double)i).ToArray());

        var cuts = Cuts(harness.Accumulator, bins: 7, TiePolicy.Left, CutPlacement.RightValue);

        Assert.Equal(6, cuts.Length);
        Assert.Equal(cuts.Length, cuts.Distinct().Count());
        Assert.Equal(cuts.OrderBy(c => c), cuts);
    }

    // --- Percentile extraction -------------------------------------------------

    [Fact]
    public void ExtractPercentileSpan_WhenPopulationIsUniform_ThenExactOrderStatistics()
    {
        using var harness = Build(budget: 100_000);
        Feed(harness.Accumulator, Enumerable.Range(1, 100).Select(i => (double)i).ToArray());
        harness.Accumulator.EndIntake();
        harness.Accumulator.PrepareReplay();

        // N = 100, each count 1 → C_i = i. p1 = first i with i·100 >= 100 → i = 1 → value 1.
        // p99 = first i with i·100 >= 9900 → i = 99 → value 99. Exact order statistics, never
        // interpolated between neighbours.
        Assert.True(harness.Accumulator.TryExtractPercentileSpan(out var p1, out var p99));
        Assert.Equal(1.0, p1);
        Assert.Equal(99.0, p99);
        harness.Accumulator.Release();
    }

    [Fact]
    public void ExtractPercentileSpan_WhenPopulationIsSkewed_ThenTailOutliersDoNotBecomeTheSpan()
    {
        using var harness = Build(budget: 100_000);

        // 98 ones, then 5 and 1000: N = 100, C = [98, 99, 100].
        // p1  = first i with C_i·100 >= 100  → C_1 = 98 → value 1.
        // p99 = first i with C_i·100 >= 9900 → C_1·100 = 9800 < 9900; C_2·100 = 9900 >= 9900 → value 5.
        // The 1000 outlier is deliberately outside the span — that is what percentile clipping is for.
        Feed(harness.Accumulator, [.. Enumerable.Repeat(1.0, 98), 5.0, 1000.0]);
        harness.Accumulator.EndIntake();
        harness.Accumulator.PrepareReplay();

        Assert.True(harness.Accumulator.TryExtractPercentileSpan(out var p1, out var p99));
        Assert.Equal(1.0, p1);
        Assert.Equal(5.0, p99);
        harness.Accumulator.Release();
    }

    [Fact]
    public void ExtractPercentileSpan_WhenEveryValueIsEqual_ThenFalse()
    {
        using var harness = Build(budget: 100_000);
        Feed(harness.Accumulator, [7, 7, 7]);
        harness.Accumulator.EndIntake();
        harness.Accumulator.PrepareReplay();

        Assert.False(harness.Accumulator.TryExtractPercentileSpan(out var p1, out var p99));
        Assert.Equal(p1, p99);
        harness.Accumulator.Release();
    }

    [Fact]
    public void ExtractPercentileSpan_WhenPopulationIsEmpty_ThenFalse()
    {
        using var harness = Build(budget: 100_000);
        harness.Accumulator.EndIntake();
        harness.Accumulator.PrepareReplay();

        Assert.False(harness.Accumulator.TryExtractPercentileSpan(out _, out _));
        Assert.Equal(0, harness.Accumulator.Total);
        harness.Accumulator.Release();
    }

    [Fact]
    public void ExtractPercentileSpan_WhenForcedToSpill_ThenIdenticalToTheInMemoryPath()
    {
        using var resident = Build(budget: 100_000, name: "resident");
        using var spilled = Build(budget: TestModeled(1), fanIn: 2, name: "spilled");
        var population = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();

        Feed(resident.Accumulator, population);
        Feed(spilled.Accumulator, population);
        resident.Accumulator.EndIntake();
        resident.Accumulator.PrepareReplay();
        spilled.Accumulator.EndIntake();
        spilled.Accumulator.PrepareReplay();

        Assert.True(resident.Accumulator.TryExtractPercentileSpan(out var residentP1, out var residentP99));
        Assert.True(spilled.Accumulator.TryExtractPercentileSpan(out var spilledP1, out var spilledP99));
        Assert.True(spilled.Accumulator.Spilled);
        Assert.False(resident.Accumulator.Spilled);
        Assert.Equal(residentP1, spilledP1);
        Assert.Equal(residentP99, spilledP99);
        resident.Accumulator.Release();
        spilled.Accumulator.Release();
    }
}
