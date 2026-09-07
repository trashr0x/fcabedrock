using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The retained-layout witnesses: <c>actual retained &lt;= modeled</c>, proved against layout facts
/// this test <b>observes from the running runtime</b> rather than restates as literals.
/// <para>
/// <see cref="RowCodecResidentTests"/> and <see cref="QuantileAccumulatorTests"/> already prove the
/// same bound against hand-stated .NET 10 CoreCLR <b>x64</b> layout numbers, and are correctly
/// gated to that architecture. That is exactly why they cannot extend D-082's numerical guarantee
/// to another target: re-running an x64 literal on ARM64 asserts nothing about ARM64, and skipping
/// the test asserts even less. These witnesses close that gap. Every input — the object header, the
/// reference size, array headers, string heap size, real accepted collection capacities, struct
/// strides, and the composition of each retained graph — is measured here on whatever runtime is
/// executing, so the witness is meaningful wherever it runs and <b>fails</b> rather than passes
/// vacuously if a layout differs from what the model charges.
/// </para>
/// <para>
/// <b>Method.</b> Each component is allocated in isolation on a warmed path and measured with
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/>, which is precise, synchronous, and
/// thread-local. The measured object is kept alive across the measurement, so what is observed is
/// the cost of state that is genuinely retained rather than a transient. Where a component cannot
/// be isolated by allocation — an object whose constructor necessarily allocates its own
/// collaborators — a focused field-layout inspection supplies a conservative upper bound instead of
/// a guessed number, and that technique is itself validated below against a type whose true size
/// this file can observe directly.
/// </para>
/// <para>
/// These are <b>not</b> architecture-gated. A model that under-charges on the executing runtime is
/// a correctness failure there (D-082's layout constants are correctness constants, never
/// performance knobs), which is precisely what a release gate needs to hear.
/// </para>
/// </summary>
public sealed class ResidentLayoutWitnessTests
{
    /// <summary>The runtime's reference size, as the model's own unit of reference cost.</summary>
    private static long ObservedReference => Unsafe.SizeOf<object>();

    /// <summary>
    /// The heap bytes one allocation of <paramref name="allocate"/> costs.
    /// <para>
    /// The path is warmed first so that JIT-time and first-call allocations are not attributed to
    /// the measurement, and the produced object is kept alive across the sample so a collection
    /// cannot make retained state look free. Repeated until two consecutive samples agree, which
    /// removes the occasional sample perturbed by an allocation-context refill.
    /// </para>
    /// </summary>
    private static long Observe(Func<object> allocate)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            GC.KeepAlive(allocate());
            GC.KeepAlive(allocate());

            var first = Sample(allocate);
            var second = Sample(allocate);
            if (first == second)
            {
                return first;
            }
        }

        throw new InvalidOperationException("the allocation measurement did not settle on a stable value.");
    }

    private static long Sample(Func<object> allocate)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var produced = allocate();
        var after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(produced);
        return after - before;
    }

    // ---- observed primitives ---------------------------------------------------------------

    /// <summary>The real heap size of a string of <paramref name="length"/> UTF-16 code units.</summary>
    private static long ObservedString(int length) => Observe(() => new string('x', length));

    /// <summary>The real heap size of a reference array of <paramref name="length"/> elements.</summary>
    private static long ObservedReferenceArray(int length) => Observe(() => new string?[length]);

    /// <summary>The real array header, isolated as the cost of a zero-length array.</summary>
    private static long ObservedArrayHeader => ObservedReferenceArray(0);

    [Fact]
    public void Observation_ShouldRecoverTheRuntimesOwnArrayGeometry()
    {
        // A self-check on the instrument before anything is proved with it: the per-element cost
        // recovered from two array sizes must be the reference size the runtime reports, and the
        // header must be constant across lengths.
        var header = ObservedArrayHeader;
        var perElement = (ObservedReferenceArray(64) - ObservedReferenceArray(32)) / 32;

        Assert.Equal(ObservedReference, perElement);
        Assert.Equal(header + (16 * ObservedReference), ObservedReferenceArray(16));
        Assert.True(header > 0);
    }

    // ---- string, array, and buffer witnesses ------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(1_000)]
    public void StringCost_ShouldCoverTheObservedStringHeapSize(int length)
    {
        // Odd and boundary lengths on purpose: the model rounds up, and a rounding rule that was
        // right for even lengths only would slip through a coarser sample.
        var observed = ObservedString(length);

        Assert.True(
            ResidentModel.StringCost(length) >= observed,
            $"StringCost({length}) = {ResidentModel.StringCost(length)} under-charges the observed {observed}.");
    }

    [Fact]
    public void StringCost_ShouldCoverAStringCarryingSurrogatePairsAndControlCharacters()
    {
        // Cleaned source values are arbitrary text, so the witness has to cover more than ASCII.
        // Length is in UTF-16 code units, which is what the model charges by.
        var text = string.Concat(Enumerable.Repeat("\U0001F600\u00E9", 40));
        var observed = Observe(() => new string(text.AsSpan()));

        Assert.True(ResidentModel.StringCost(text.Length) >= observed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(4_096)]
    public void BackingArrayBytes_ShouldCoverTheObservedArrayForBothRowStrides(int capacity)
    {
        // The strides come from the runtime, so a struct layout change is observed rather than
        // assumed; the observed arrays are the real ones the grouping buffer holds.
        AssertBackingArray<TripleRow>(capacity);
        AssertBackingArray<DedupeRow>(capacity);
    }

    private static void AssertBackingArray<TRow>(int capacity)
    {
        var stride = (long)Unsafe.SizeOf<RankedRow<TRow>>();
        var observed = Observe(() => new RankedRow<TRow>[capacity]);
        var modeled = ResidentModel.BackingArrayBytes(capacity, stride);

        Assert.True(
            modeled >= observed,
            $"BackingArrayBytes({capacity}, {stride}) for {typeof(TRow).Name} = {modeled} under-charges {observed}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(1_024)]
    public void BufferBytes_ShouldCoverTheObservedListObjectPlusItsRealAcceptedBackingArray(int requested)
    {
        // The list's REAL accepted capacity is asked for, not assumed: a List<T> built with a
        // requested capacity may hold a larger array, and the model must cover what it actually
        // holds rather than what was asked for.
        var stride = (long)Unsafe.SizeOf<RankedRow<DedupeRow>>();
        var list = new List<RankedRow<DedupeRow>>(requested);
        var accepted = list.Capacity;

        // A List<T> with no capacity shares the empty array singleton, so this isolates the list
        // object itself; the sized one adds exactly its backing array.
        var listObject = Observe(() => new List<RankedRow<DedupeRow>>(0));
        var whole = Observe(() => new List<RankedRow<DedupeRow>>(accepted));
        var observed = Math.Max(whole, listObject + Observe(() => new RankedRow<DedupeRow>[accepted]));

        Assert.True(
            ResidentModel.BufferBytes(accepted, stride) >= observed,
            $"BufferBytes({accepted}, {stride}) = {ResidentModel.BufferBytes(accepted, stride)} under-charges {observed}.");
        Assert.True(listObject > 0, "the list object itself must have an observable cost.");
    }

    // ---- complete-graph row witnesses -------------------------------------------------------

    [Fact]
    public void DedupeMeasureResident_ShouldCoverTheCompleteObservedRetainedGraph()
    {
        // Complete graph composition, each component observed separately and summed: the record
        // object, its name, its field array, and every non-null field string. Nothing here is a
        // stated constant, and nothing that the row retains is left out.
        string?[] fields = ["ab", null, "wxyz", string.Empty, new string('q', 33)];
        const string name = "object-12345";

        var observedFieldArray = ObservedReferenceArray(fields.Length);
        var observedStrings = fields.Where(field => field is not null).Sum(field => ObservedString(field!.Length));
        var observedName = ObservedString(name.Length);
        var observedRecord = Observe(() => new ObjectRecord(name, fields));

        var observed = observedRecord + observedName + observedFieldArray + observedStrings;
        var modeled = DedupeRowCodec.Instance.MeasureResident(DedupeRow.Live(new ObjectRecord(name, fields), 0));

        Assert.True(modeled >= observed, $"MeasureResident {modeled} under-charges the observed graph {observed}.");
    }

    [Fact]
    public void DedupeMeasureResident_ShouldChargeAReferencePerFieldIncludingNulls()
    {
        // The array a record retains costs a slot per field whether or not the field is null, so a
        // wide all-null row is the case a per-string-only model would under-charge.
        const int width = 4_096;
        var row = DedupeRow.Live(new ObjectRecord("x", new string?[width]), 0);
        var observed = ObservedReferenceArray(width) + ObservedString(1);

        Assert.True(DedupeRowCodec.Instance.MeasureResident(row) >= observed);
    }

    [Fact]
    public void TripleMeasureResident_ShouldCoverItsObservedStringsAndNothingLess()
    {
        var row = new TripleRow(0, "subject-0001", null, "a value with spaces");
        var observed = ObservedString("subject-0001".Length) + ObservedString("a value with spaces".Length);

        Assert.True(TripleRowCodec.Instance.MeasureResident(row) >= observed);
    }

    // ---- quantile-accumulator witnesses -----------------------------------------------------

    [Fact]
    public void FieldLayoutUpperBound_ShouldBeConservativeForATypeWhoseRealSizeIsObservable()
    {
        // The technique used below for the accumulator object, validated first against a type this
        // file CAN observe directly. A dictionary with no capacity allocates only its own object -
        // buckets and entries stay null until something is stored - so the observed value is the
        // true object size, and the field walk must not come in under it.
        var observed = Observe(() => new Dictionary<double, long>());
        var bound = FieldLayoutUpperBound(typeof(Dictionary<double, long>));

        Assert.True(bound >= observed, $"the field-layout bound {bound} is below the observed object size {observed}.");
    }

    [Fact]
    public void QuantileModeled_ShouldCoverTheObservedRetainedAccumulatorGraph()
    {
        // The accumulator's retained state, each part measured on the executing runtime: the
        // dictionary object, the buckets and entries it really allocates at the accepted capacity,
        // the sort buffer, and the accumulator object itself. Only the last needs the field-layout
        // bound, because its constructor necessarily allocates the others.
        foreach (var requested in (int[])[3, 7, 1_931])
        {
            var accepted = new Dictionary<double, long>(requested).EnsureCapacity(requested);

            var dictionaryObject = Observe(() => new Dictionary<double, long>());
            var dictionaryWhole = Observe(() => new Dictionary<double, long>(accepted));
            var sortBuffer = Observe(() => new ValueCount[accepted]);
            var accumulatorObject = FieldLayoutUpperBound(typeof(QuantileAccumulator));

            var observed = dictionaryWhole + sortBuffer + accumulatorObject;
            var modeled = QuantileAccumulator.Modeled(accepted);

            Assert.True(
                modeled >= observed,
                $"Modeled({accepted}) = {modeled} under-charges the observed retained graph {observed} "
                + $"(dictionary {dictionaryWhole}, sort buffer {sortBuffer}, accumulator {accumulatorObject}).");
            Assert.True(dictionaryWhole > dictionaryObject, "a sized dictionary must cost more than an empty one.");
        }
    }

    [Fact]
    public void QuantileFloorBytes_ShouldCoverTheSmallestAccumulatorTheRuntimeWillActuallyBuild()
    {
        // The floor exists because a budget share below one entry cannot be honoured. It has to
        // cover a real accumulator at the capacity the runtime accepts for a single entry, not at
        // the capacity that was requested.
        var accepted = new Dictionary<double, long>(1).EnsureCapacity(1);
        var observed = Observe(() => new Dictionary<double, long>(accepted))
            + Observe(() => new ValueCount[accepted])
            + FieldLayoutUpperBound(typeof(QuantileAccumulator));

        Assert.Equal(QuantileAccumulator.Modeled(accepted), QuantileAccumulator.FloorBytes);
        Assert.True(QuantileAccumulator.FloorBytes >= observed);
    }

    [Fact]
    public void QuantileSlotCost_ShouldCoverTheObservedPerEntryGrowth()
    {
        // Isolating the per-entry term: what one more accepted entry really costs across the
        // dictionary and the sort buffer, against what the model charges for it.
        const int small = 1_931;
        const int large = 7_919;
        var acceptedSmall = new Dictionary<double, long>(small).EnsureCapacity(small);
        var acceptedLarge = new Dictionary<double, long>(large).EnsureCapacity(large);

        var observedGrowth =
            Observe(() => new Dictionary<double, long>(acceptedLarge)) - Observe(() => new Dictionary<double, long>(acceptedSmall))
            + Observe(() => new ValueCount[acceptedLarge]) - Observe(() => new ValueCount[acceptedSmall]);
        var modeledGrowth = QuantileAccumulator.Modeled(acceptedLarge) - QuantileAccumulator.Modeled(acceptedSmall);

        Assert.True(
            modeledGrowth >= observedGrowth,
            $"the modeled growth {modeledGrowth} under-charges the observed {observedGrowth}.");
    }

    [Fact]
    public void Witnesses_ShouldRecordTheRuntimeTheyActuallyProved()
    {
        // A witness is only evidence for the target that executed it. Recording the identity here
        // makes the run self-describing, so a results file cannot later be read as covering a
        // target it never ran on.
        Assert.False(string.IsNullOrWhiteSpace(RuntimeInformation.RuntimeIdentifier));
        Assert.False(string.IsNullOrWhiteSpace(RuntimeInformation.FrameworkDescription));
        Assert.True(Enum.IsDefined(RuntimeInformation.ProcessArchitecture));
    }

    /// <summary>
    /// A conservative upper bound on one object's heap size, from its declared instance fields.
    /// <para>
    /// Each field is charged its runtime size rounded up to its own natural alignment, and the
    /// total is rounded to the object-allocation granularity, so declared padding is over-counted
    /// rather than under-counted. That direction is the whole point: this stands in for an observed
    /// size only where isolating the allocation is impossible, and a bound that could come in under
    /// the truth would silently weaken every assertion built on it. The technique is checked
    /// against an observable type above.
    /// </para>
    /// </summary>
    private static long FieldLayoutUpperBound(Type type)
    {
        var header = ObservedArrayHeader; // header + length word: at least the object header itself
        var total = 0L;

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.DeclaringType != current)
                {
                    continue;
                }

                var size = SizeOf(field.FieldType);
                var alignment = Math.Min(size, ObservedReference);
                total += alignment <= 0 ? size : ((size + alignment - 1) / alignment) * alignment;
            }
        }

        var granularity = ObservedReference * 2;
        return ((header + total + granularity - 1) / granularity) * granularity;
    }

    private static long SizeOf(Type type)
    {
        if (!type.IsValueType)
        {
            return ObservedReference;
        }

        var sizeOf = typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf), BindingFlags.Public | BindingFlags.Static)!;
        return (int)sizeOf.MakeGenericMethod(type).Invoke(null, null)!;
    }
}
