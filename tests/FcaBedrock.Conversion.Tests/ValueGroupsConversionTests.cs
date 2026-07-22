using System.Globalization;
using System.Runtime.CompilerServices;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// <c>value_groups</c> through Calibrate and Emit (§11.6, M4 Slice E / D-090/D-095/D-104):
/// pass-through discovery on the raw stream for wide and both triple orderings, the pass-ownership
/// contract when a count-sensitive attribute coexists, and classification/emission under all three
/// unmatched policies.
/// </summary>
public sealed class ValueGroupsConversionTests
{
    private static ValueGroup Group(string label, params string[] values) => ValueGroup.Create(label, values, null);

    private static AttributeSpec Passthrough(
        string name, int index, params ValueGroup[] groups) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true,
            new CalibrationPending(new PendingValueGroupsPassthrough(groups), CultureInfo.InvariantCulture),
            new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec PassthroughPredicate(string name, string predicate, params ValueGroup[] groups) =>
        new(name, new PredicateSource(predicate, SourceValueType.String), Include: true,
            new CalibrationPending(new PendingValueGroupsPassthrough(groups), CultureInfo.InvariantCulture),
            new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec Groups(
        string name, int index, ValueGroupsUnmatched unmatched, Scale scale,
        UnknownValuePolicy policy = UnknownValuePolicy.Warn, MissingPolicy missing = MissingPolicy.Skip,
        params ValueGroup[] groups) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true,
            ValueGroupsDiscretizer.Create(groups, unmatched), scale,
            DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, missing, policy);

    private static async Task<Diagnosed<CalibratedSpec>> CalibrateWideAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        return await Calibrator.CalibrateAsync(ConversionFixtures.ResolveFor(spec, schema), source);
    }

    private static async Task<Diagnosed<CalibratedSpec>> CalibrateTripleAsync(BedrockSpec spec, string data)
    {
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        var schema = await source.GetSchemaAsync();
        return await Calibrator.CalibrateTripleAsync(ConversionFixtures.ResolveFor(spec, schema), source);
    }

    private static IReadOnlyList<string> BinsOf(CalibratedSpec calibrated, string attribute) =>
        Assert.IsType<ValueGroupsDiscretizer>(
            calibrated.Spec.Attributes.Single(a => a.Name == attribute).Discretizer).PassthroughBins;

    private static CalibratedSpec Ok(Diagnosed<CalibratedSpec> result)
    {
        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return calibrated!;
    }

    // --- Wide pass-through discovery ------------------------------------------

    [Fact]
    public async Task CalibrateAsync_WhenPassthrough_ThenUngroupedValuesBecomeBinsInFirstObservationOrder()
    {
        // The input is deliberately NOT in alphabetical order and interleaves grouped values, so
        // only genuine first-observation order reproduces [PhD, Masters] — a sorted or
        // last-observation implementation would fail (§17 rule 3).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var result = await CalibrateWideAsync(spec, "PhD\n11th\nMasters\nPhD\n11th\nMasters");

        Assert.Equal(["PhD", "Masters"], BinsOf(Ok(result), "edu"));
    }

    [Fact]
    public async Task CalibrateAsync_WhenPassthrough_ThenGroupedValuesAreExcludedFromDiscovery()
    {
        // Every value matches a group, so nothing is ungrouped: the discovery must consult the
        // matcher, not merely collect distinct values.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Passthrough("edu", 0, Group("School", "11th", "HS-grad"), Group("Uni", "Bachelors"))]);

        var result = await CalibrateWideAsync(spec, "11th\nHS-grad\nBachelors\n11th");

        Assert.Empty(BinsOf(Ok(result), "edu"));
    }

    [Fact]
    public async Task CalibrateAsync_WhenGroupsEmptyAndPassthrough_ThenEveryDistinctValueBecomesABin()
    {
        // §11.6/D-104's empty-groups form at the degenerate end of pass-through: with no group
        // able to claim anything, discovery is exactly the distinct observed values in
        // first-observation order (§17 rule 3). The input repeats and is unsorted, so only genuine
        // first-observation order reproduces the expectation.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0)]);

        var result = await CalibrateWideAsync(spec, "PhD\n11th\nPhD\nMasters");

        Assert.Equal(["PhD", "11th", "Masters"], BinsOf(Ok(result), "edu"));
        Assert.Equal(DiagnosticCode.ValueGroupsPassthroughDataDependent, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task CalibrateAsync_WhenPassthroughAndPatternGroup_ThenPatternMatchesAreGroupedNotDiscovered()
    {
        // Discovery routes through the same Core matcher emit uses, so a regex group claims its
        // values here exactly as it will at emit.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Passthrough("dx", 0, ValueGroup.Create("Cardiac", null, "^I[0-9]{2}"))]);

        var result = await CalibrateWideAsync(spec, "I21\nI50\nE11\nI21");

        Assert.Equal(["E11"], BinsOf(Ok(result), "dx"));
    }

    [Fact]
    public async Task CalibrateAsync_WhenPassthroughRepeatsAValue_ThenDiscoveryIsIdempotent()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var result = await CalibrateWideAsync(spec, "PhD\nPhD\nPhD");

        Assert.Equal(["PhD"], BinsOf(Ok(result), "edu"));
    }

    [Fact]
    public async Task CalibrateAsync_WhenPassthroughExecutes_ThenWarnsEvenWithZeroDiscoveries()
    {
        // Mode-triggered (D-090): the warning fires whenever passthrough calibrates, zero
        // discoveries included — the column set depends on this input either way.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var result = await CalibrateWideAsync(spec, "11th\n11th");

        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ValueGroupsPassthroughDataDependent, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("edu", warning.Location?.AttributeName);
        Assert.Contains("0 ungrouped value", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CalibrateAsync_WhenPassthroughDiscovers_ThenTheWarningCarriesTheCount()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var result = await CalibrateWideAsync(spec, "PhD\nMasters");

        Assert.Contains("2 ungrouped value", Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CalibrateAsync_WhenPassthroughDiscoversNothing_ThenTheEmptyOutcomeIsStillRetained()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var calibrated = Ok(await CalibrateWideAsync(spec, "11th"));

        var outcome = Assert.IsType<PassthroughBins>(Assert.Single(calibrated.Calibrations));
        Assert.Equal("edu", outcome.AttributeName);
        Assert.Empty(outcome.Values);
    }

    [Fact]
    public async Task CalibrateAsync_WhenMissingValues_ThenTheyAreNeverPassthroughBins()
    {
        // Missing is handled before the discretizer and follows missing_policy (§10.5); it is not
        // an ungrouped value, so the missing token must not become a bin.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var result = await CalibrateWideAsync(spec, "?\nPhD\n?");

        Assert.Equal(["PhD"], BinsOf(Ok(result), "edu"));
    }

    [Fact]
    public async Task CalibrateAsync_WhenSeveralPassthroughAttributes_ThenOutcomesAndDiagnosticsFollowSpecOrder()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [
            Passthrough("first", 0, Group("A", "a")),
            Passthrough("second", 1, Group("B", "b")),
        ]);

        var result = await CalibrateWideAsync(spec, "x,y\na,b");
        var calibrated = Ok(result);

        Assert.Equal(["first", "second"], calibrated.Calibrations.Select(c => c.AttributeName));
        Assert.Equal(["first", "second"], result.Diagnostics.Select(d => d.Location?.AttributeName));
    }

    [Fact]
    public async Task CalibrateAsync_WhenPassthroughOnly_ThenNoSpoolStorageIsTouchedAtAll()
    {
        // Passthrough is discovery-class, not count-sensitive: it must never reach the quantile
        // accumulator or the spill/merge machinery (D-095). The proof is a filesystem that fails
        // EVERY operation — if the calibration touched the workspace at all it would surface a
        // GroupingStorageFailed, so a clean run is direct evidence it did not. A tiny buffer budget
        // is set too, so a count-sensitive path would certainly have spilled.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);
        var source = ConversionFixtures.SourceOver("PhD\nMasters\n11th\nPhD", spec.Binding);
        var schema = await source.GetSchemaAsync();
        var fileSystem = new FakeSpoolFileSystem
        {
            OnCreateWorkspace = () => new IOException("the passthrough path must not create a workspace"),
            OnCreateRun = _ => new IOException("the passthrough path must not write a run"),
            OnOpenRun = _ => new IOException("the passthrough path must not read a run"),
        };
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, fileSystem: fileSystem);

        var result = await Calibrator.CalibrateAsync(
            ConversionFixtures.ResolveFor(spec, schema), source, options, observer: null, CancellationToken.None);

        Assert.Equal(["PhD", "Masters"], BinsOf(Ok(result), "edu"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
    }

    [Fact]
    public async Task CalibrateAsync_WhenPassthroughOnly_ThenNoAccumulatorIsSizedAndNoRunIsWritten()
    {
        // The positive twin of the test above, and the direct D-095/D-104 resource proof: the
        // SAME RecordingCalibrationObserver serves as both the grouping observer and the
        // calibration observer, so it sees the spool signals AND the accumulator's own tier-1
        // sizing events. Passthrough must produce neither — it retains only its bin set
        // (schema-scale metadata, P-16), never a budgeted population — so an unused accumulator
        // allocation or a passthrough contribution to the budget divisor would fail here even
        // though it would touch no filesystem and change no bins.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);
        var source = ConversionFixtures.SourceOver("PhD\nMasters\n11th\nPhD", spec.Binding);
        var schema = await source.GetSchemaAsync();
        var observer = new RecordingCalibrationObserver();
        var options = new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, observer: observer);

        var result = await Calibrator.CalibrateAsync(
            ConversionFixtures.ResolveFor(spec, schema), source, options, observer, CancellationToken.None);

        Assert.Equal(["PhD", "Masters"], BinsOf(Ok(result), "edu"));

        // Tier 1: no accumulator was sized at all, so no budget share was taken.
        Assert.Empty(observer.Sized);
        Assert.All(observer.Aggregates, bytes => Assert.Equal(0, bytes));

        // Tier 2: nothing was spooled — a tiny budget would certainly have spilled a real one.
        Assert.Empty(observer.Written);
        Assert.Empty(observer.Deleted);
        Assert.Equal(0, observer.PeakOpenReaders);
        Assert.Empty(observer.LiveRuns);
    }

    [Fact]
    public async Task CalibrateAsync_WhenACountSensitiveAttributeIsPresent_ThenTheSameWiringDoesReportSizing()
    {
        // The CONTROL for the test above, on the same (wide) entry point and the same observer
        // wiring: an equal_frequency attribute DOES report an AccumulatorSized event. Without this,
        // the passthrough-only Assert.Empty(Sized) would pass just as happily if the observer were
        // never wired at all — which is exactly the hole this pair closes.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [
            new AttributeSpec("score", new ColumnSource(0, SourceValueType.Number), Include: true,
                new CalibrationPending(new PendingEqualFrequency(2, TiePolicy.Left, CutPlacement.RightValue), CultureInfo.InvariantCulture),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn),
        ]);
        var source = ConversionFixtures.SourceOver("1\n2\n3", spec.Binding);
        var schema = await source.GetSchemaAsync();
        var observer = new RecordingCalibrationObserver();

        var result = await Calibrator.CalibrateAsync(
            ConversionFixtures.ResolveFor(spec, schema), source, new GroupingOptions(observer: observer),
            observer, CancellationToken.None);

        Ok(result);
        Assert.Equal("score", Assert.Single(observer.Sized).Attribute);
    }

    [Fact]
    public async Task CalibrateAsync_WhenCancelled_ThenOperationCanceledPropagatesRatherThanBecomingADiagnostic()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);
        var source = ConversionFixtures.SourceOver("PhD\nMasters", spec.Binding);
        var schema = await source.GetSchemaAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Calibrator.CalibrateAsync(ConversionFixtures.ResolveFor(spec, schema), source, cts.Token));
    }

    // --- Triple pass-through discovery ----------------------------------------

    // Grouped by subject; "PhD" first appears before "Masters" in raw order.
    private const string TripleGrouped =
        "s0,edu,PhD\ns0,edu,11th\ns1,edu,Masters\ns1,edu,PhD\ns2,edu,11th";

    // The same observations, subject-interleaved: raw order still yields PhD then Masters, but a
    // GROUPED reading (s0's rows first) would too — so the interleaved fixture below is the one
    // that discriminates.
    private const string TripleInterleaved =
        "s0,edu,PhD\ns1,edu,Masters\ns0,edu,11th\ns1,edu,PhD\ns2,edu,11th";

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped, TripleGrouped)]
    [InlineData(TripleOrdering.Unordered, TripleInterleaved)]
    public async Task CalibrateTripleAsync_WhenPassthrough_ThenDiscoveryIsRawFirstObservationOrder(
        TripleOrdering ordering, string data)
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(ordering), [PassthroughPredicate("edu", "edu", Group("School", "11th"))]);

        var result = await CalibrateTripleAsync(spec, data);

        Assert.Equal(["PhD", "Masters"], BinsOf(Ok(result), "edu"));
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenUnorderedAndRawOrderDiffersFromGroupedOrder_ThenRawOrderWins()
    {
        // The discriminating case for §17 rule 3 / D-095: grouping by subject would emit s0's rows
        // first and discover [Masters, PhD]; raw input order discovers [PhD, Masters]. Only a
        // genuine raw-order pass gives the latter.
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [PassthroughPredicate("edu", "edu", Group("School", "11th"))]);

        var result = await CalibrateTripleAsync(spec, "s1,edu,PhD\ns0,edu,Masters\ns0,edu,11th\ns1,edu,11th");

        Assert.Equal(["PhD", "Masters"], BinsOf(Ok(result), "edu"));
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenTheSameValueRecursAcrossSubjects_ThenOneBinAtItsFirstOccurrence()
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [PassthroughPredicate("edu", "edu", Group("School", "11th"))]);

        var result = await CalibrateTripleAsync(spec, "s0,edu,PhD\ns1,edu,PhD\ns2,edu,PhD");

        Assert.Equal(["PhD"], BinsOf(Ok(result), "edu"));
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenIdenticalTriplesRepeat_ThenDiscoveryIsIdempotent()
    {
        // Set-based discovery makes subject-local dedup a no-op here, which is exactly why
        // passthrough needs no count-sensitive machinery (D-095).
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.SubjectGrouped),
            [PassthroughPredicate("edu", "edu", Group("School", "11th"))]);

        var result = await CalibrateTripleAsync(spec, "s0,edu,PhD\ns0,edu,PhD\ns0,edu,PhD");

        Assert.Equal(["PhD"], BinsOf(Ok(result), "edu"));
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenSubjectNotContiguousUnderSubjectGrouped_ThenTripleSubjectNotContiguous()
    {
        // The G-3/D-099 structural checks are unchanged by Slice E.
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.SubjectGrouped),
            [PassthroughPredicate("edu", "edu", Group("School", "11th"))]);

        var result = await CalibrateTripleAsync(spec, TripleInterleaved);

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.TripleSubjectNotContiguous, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenSubjectIsUnusable_ThenObjectKeyValueInvalid()
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [PassthroughPredicate("edu", "edu", Group("School", "11th"))]);

        var result = await CalibrateTripleAsync(spec, "s0,edu,PhD\n ,edu,Masters");

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, Assert.Single(result.Diagnostics).Code);
    }

    // --- Source-pass ownership (D-095/D-103) ----------------------------------

    private sealed class CountingTripleSource(ITripleRowSource inner) : ITripleRowSource
    {
        public int Enumerations { get; private set; }

        public SourceProvenance Provenance => inner.Provenance;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            inner.GetSchemaAsync(cancellationToken);

        public async IAsyncEnumerable<TripleRow> ReadRowsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Enumerations++;
            await foreach (var row in inner.ReadRowsAsync(cancellationToken))
            {
                yield return row;
            }
        }
    }

    private static async Task<(int Passes, CalibratedSpec Calibrated)> CountTriplePassesAsync(
        BedrockSpec spec, string data, RecordingCalibrationObserver? observer = null)
    {
        var inner = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        var schema = await inner.GetSchemaAsync();
        var counting = new CountingTripleSource(inner);
        var options = observer is null
            ? GroupingOptions.Default
            : new GroupingOptions(observer: observer);
        var result = await Calibrator.CalibrateTripleAsync(
            ConversionFixtures.ResolveFor(spec, schema), counting, options, observer, CancellationToken.None);
        return (counting.Enumerations, Ok(result));
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenPassthroughOnlyAndUnordered_ThenExactlyOneRawPass()
    {
        // Passthrough alone never triggers the grouped second pass: it is discovery-class, so the
        // raw pass covers it. Asserted by ENUMERATION COUNT — equal bins would not prove it.
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [PassthroughPredicate("edu", "edu", Group("School", "11th"))]);

        var (passes, calibrated) = await CountTriplePassesAsync(spec, TripleInterleaved);

        Assert.Equal(1, passes);
        Assert.Equal(["PhD", "Masters"], BinsOf(calibrated, "edu"));
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenPassthroughCoexistsWithACountSensitiveTarget_ThenTwoPassesAndPassthroughIsFedOnlyFromTheRawOne()
    {
        // The D-103 one-pass-per-observer rule with a Slice E observer in the mix: the
        // equal_frequency attribute forces the grouped second pass, but the passthrough observer
        // must be fed ONLY from the raw pass — never both, or its bins would be discovered in
        // grouped order (and any diagnostic double-reported). Two passes exactly; never a third.
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered), [
            PassthroughPredicate("edu", "edu", Group("School", "11th")),
            new AttributeSpec("score", new PredicateSource("score", SourceValueType.Number), Include: true,
                new CalibrationPending(new PendingEqualFrequency(2, TiePolicy.Left, CutPlacement.RightValue), CultureInfo.InvariantCulture),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn),
        ]);

        // Raw order puts PhD before Masters; grouping by subject would put Masters first.
        var observer = new RecordingCalibrationObserver();
        var (passes, calibrated) = await CountTriplePassesAsync(spec,
            "s1,edu,PhD\ns1,score,3\ns0,edu,Masters\ns0,score,1\ns0,edu,11th\ns1,edu,11th\ns2,score,2",
            observer);

        Assert.Equal(2, passes);
        Assert.Equal(["PhD", "Masters"], BinsOf(calibrated, "edu"));

        // Budget OWNERSHIP, not just pass count: exactly ONE accumulator was sized, and it belongs
        // to the count-sensitive attribute. The passthrough attribute takes no accumulator and no
        // share of the divisor even while sharing a calibration with one that does — so its
        // presence cannot shrink the count-sensitive attribute's budget.
        var sized = Assert.Single(observer.Sized);
        Assert.Equal("score", sized.Attribute);
        Assert.DoesNotContain(observer.Sized, s => s.Attribute == "edu");
        Assert.DoesNotContain(observer.LiveRuns.Keys, a => string.Equals(a, "edu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenPassthroughOnlyAndSubjectGrouped_ThenExactlyOneRawPass()
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.SubjectGrouped),
            [PassthroughPredicate("edu", "edu", Group("School", "11th"))]);

        var (passes, _) = await CountTriplePassesAsync(spec, TripleGrouped);

        Assert.Equal(1, passes);
    }

    // --- Emit: classification under each policy -------------------------------

    private static async Task<(IReadOnlyList<string> Names, List<string> Rows, List<BedrockDiagnostic> Diagnostics)> EmitAsync(
        BedrockSpec spec, string csv)
    {
        Assert.True(ConversionFixtures.PlanFor(spec, await ConversionFixtures.SourceOver(csv, spec.Binding).GetSchemaAsync())
            .TryGetValue(out var plan));
        var diagnostics = new List<BedrockDiagnostic>();
        var rows = new List<string>();
        await foreach (var obj in Emitter.EmitAsync(plan!, ConversionFixtures.SourceOver(csv, spec.Binding), diagnostics))
        {
            rows.Add(string.Concat(Enumerable.Range(0, plan!.FormalAttributes.Count)
                .Select(i => obj.CrossedFormalAttributeIds.Contains(i) ? "X" : ".")));
        }

        return ([.. plan!.FormalAttributes.Select(a => a.RenderedName)], rows, ConversionFixtures.DataDiagnostics(diagnostics));
    }

    [Fact]
    public async Task EmitAsync_WhenSkipAndValueIsUngrouped_ThenNoCrossAndOneAggregatedUnknownWarning()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Groups("edu", 0, ValueGroupsUnmatched.Skip, new NominalScale(), UnknownValuePolicy.Warn, MissingPolicy.Skip,
                Group("School", "11th"))]);

        var (_, rows, diagnostics) = await EmitAsync(spec, "11th\nPhD\nMasters");

        Assert.Equal(["X", ".", "."], rows);
        var warning = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownValueObserved, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Theory]
    [InlineData(UnknownValuePolicy.Skip, null)]
    [InlineData(UnknownValuePolicy.Warn, DiagnosticSeverity.Warning)]
    [InlineData(UnknownValuePolicy.Fail, DiagnosticSeverity.Error)]
    [InlineData(UnknownValuePolicy.Include, DiagnosticSeverity.Warning)]
    public async Task EmitAsync_WhenSkipAndUngrouped_ThenTheUnknownPolicySeveritiesIt(
        UnknownValuePolicy policy, DiagnosticSeverity? expected)
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Groups("edu", 0, ValueGroupsUnmatched.Skip, new NominalScale(), policy, MissingPolicy.Skip, Group("School", "11th"))]);

        var (_, _, diagnostics) = await EmitAsync(spec, "11th\nPhD");

        if (expected is null)
        {
            Assert.Empty(diagnostics);
            return;
        }

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownValueObserved, diagnostic.Code);
        Assert.Equal(expected, diagnostic.Severity);
    }

    [Fact]
    public async Task EmitAsync_WhenSkipAndIncludePolicy_ThenBehavesAsWarnWithNoIncludeCalibrationMarker()
    {
        // §11.6/D-090: an unmatched value is not a domain gap (value_groups ignores
        // declared_domain, D-055), so `include` has nothing to extend — one Warning, no bin, and
        // crucially NO UnknownValuePolicyInclude and no domain extension. The spec must also stay
        // fully-declared: `include` must not drag it into a calibration pass.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Groups("edu", 0, ValueGroupsUnmatched.Skip, new NominalScale(), UnknownValuePolicy.Include, MissingPolicy.Skip,
                Group("School", "11th"))]);

        Assert.False(CalibratedSpec.RequiresData(spec));

        var (names, rows, diagnostics) = await EmitAsync(spec, "11th\nPhD");

        Assert.Equal(["edu-School"], names); // no column was added for PhD
        Assert.Equal(["X", "."], rows);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownValueObserved, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.UnknownValuePolicyInclude);
    }

    [Fact]
    public async Task EmitAsync_WhenOther_ThenUngroupedValuesCrossTheOtherColumn()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Groups("edu", 0, ValueGroupsUnmatched.Other, new NominalScale(), UnknownValuePolicy.Warn, MissingPolicy.Skip,
                Group("School", "11th"), Group("Uni", "Bachelors"))]);

        var (names, rows, diagnostics) = await EmitAsync(spec, "11th\nBachelors\nPhD");

        Assert.Equal(["edu-School", "edu-Uni", "edu-Other"], names);
        Assert.Equal(["X..", ".X.", "..X"], rows);
        Assert.Empty(diagnostics); // `other` bins everything, so nothing is unknown
    }

    [Fact]
    public async Task EmitAsync_WhenGroupsEmptyAndSkip_ThenNoColumnExistsAndEveryValueIsUnknown()
    {
        // §11.6/D-104: an empty group list under `skip` recognizes nothing, so `edu` contributes
        // no column and every observed value is unknown. The companion attribute keeps the context
        // non-degenerate, so the assertion is about `edu` producing nothing rather than about an
        // empty plan.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [
            Groups("edu", 0, ValueGroupsUnmatched.Skip, new NominalScale(), UnknownValuePolicy.Warn, MissingPolicy.Skip),
            ConversionFixtures.Nominal("t", 1, "x"),
        ]);

        var (names, rows, diagnostics) = await EmitAsync(spec, "11th,x\nPhD,x");

        Assert.Equal(["t-x"], names);
        Assert.Equal(["X", "X"], rows);
        var warning = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownValueObserved, warning.Code);
        Assert.Equal("edu", warning.Location?.AttributeName);
    }

    [Fact]
    public async Task EmitAsync_WhenGroupsEmptyAndOther_ThenEveryUsableValueCrossesTheOtherColumn()
    {
        // The complement, and why D-104 called an empty group list "coherent under `other`": the
        // synthetic bin is the whole universe, so one column collects every usable value. Missing
        // is still handled before the discretizer (§10.5) and must not fall into `Other`.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Groups("edu", 0, ValueGroupsUnmatched.Other, new NominalScale(), UnknownValuePolicy.Warn, MissingPolicy.Skip)]);

        var (names, rows, diagnostics) = await EmitAsync(spec, "11th\nPhD\n?");

        Assert.Equal(["edu-Other"], names);
        Assert.Equal(["X", "X", "."], rows);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task EmitAsync_WhenFirstMatchWins_ThenTheEarlierGroupClaimsTheValue()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Groups("edu", 0, ValueGroupsUnmatched.Skip, new NominalScale(), UnknownValuePolicy.Warn, MissingPolicy.Skip,
                ValueGroup.Create("Cardiac", null, "^I[0-9]{2}"), Group("Exact", "I21"))]);

        var (names, rows, _) = await EmitAsync(spec, "I21");

        Assert.Equal(["edu-Cardiac", "edu-Exact"], names);
        Assert.Equal(["X."], rows);
    }

    [Fact]
    public async Task EmitAsync_WhenCombinedMatchers_ThenValuesAndPatternBothClaimTheirValues()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Groups("edu", 0, ValueGroupsUnmatched.Skip, new NominalScale(), UnknownValuePolicy.Skip, MissingPolicy.Skip,
                ValueGroup.Create("G", ["Bachelors"], "^I[0-9]{2}"))]);

        var (_, rows, _) = await EmitAsync(spec, "Bachelors\nI21\nMasters");

        Assert.Equal(["X", "X", "."], rows);
    }

    [Fact]
    public async Task EmitAsync_WhenMissingPolicyIsAsAttribute_ThenMissingGetsItsOwnColumnNotOther()
    {
        // Missing is handled before the discretizer (§10.5), so it must not fall into `Other`.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Groups("edu", 0, ValueGroupsUnmatched.Other, new NominalScale(), UnknownValuePolicy.Warn, MissingPolicy.AsAttribute,
                Group("School", "11th"))]);

        var (names, rows, _) = await EmitAsync(spec, "11th\nPhD\n?");

        Assert.Equal(["edu-School", "edu-Other", "edu-missing"], names);
        Assert.Equal(["X..", ".X.", "..X"], rows);
    }

    // --- Emit: calibrated passthrough -----------------------------------------

    private static async Task<(IReadOnlyList<string> Names, List<string> Rows, List<BedrockDiagnostic> Diagnostics)>
        CalibrateAndEmitAsync(BedrockSpec spec, string csv, string? emitCsv = null)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var calibrated = Ok(await Calibrator.CalibrateAsync(ConversionFixtures.ResolveFor(spec, schema), source));
        Assert.True(ConversionPlanner.Plan(calibrated).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var rows = new List<string>();
        await foreach (var obj in Emitter.EmitAsync(plan!, ConversionFixtures.SourceOver(emitCsv ?? csv, spec.Binding), diagnostics))
        {
            rows.Add(string.Concat(Enumerable.Range(0, plan!.FormalAttributes.Count)
                .Select(i => obj.CrossedFormalAttributeIds.Contains(i) ? "X" : ".")));
        }

        return ([.. plan!.FormalAttributes.Select(a => a.RenderedName)], rows, ConversionFixtures.DataDiagnostics(diagnostics));
    }

    [Fact]
    public async Task Emit_WhenCalibratedPassthrough_ThenDiscoveredBinsAreColumnsAfterTheGroups()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var (names, rows, diagnostics) = await CalibrateAndEmitAsync(spec, "11th\nPhD\nMasters");

        Assert.Equal(["edu-School", "edu-PhD", "edu-Masters"], names);
        Assert.Equal(["X..", ".X.", "..X"], rows);
        Assert.Empty(diagnostics); // every value bins, so nothing is unknown at emit
    }

    [Fact]
    public async Task Emit_WhenGroupsEmptyAndCalibratedPassthrough_ThenTheDiscoveredBinsAreTheWholeColumnSet()
    {
        // Calibrate → plan → emit end-to-end for the empty-groups pass-through: with no declared
        // groups the discovered bins ARE the column set, in first-observation order, and every
        // value bins (so nothing is unknown at emit).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0)]);

        var (names, rows, diagnostics) = await CalibrateAndEmitAsync(spec, "PhD\n11th\nPhD");

        Assert.Equal(["edu-PhD", "edu-11th"], names);
        Assert.Equal(["X.", ".X", "X."], rows);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Emit_WhenAValueAppearsOnlyAfterCalibration_ThenTheKnownBinsGateMakesItUnknown()
    {
        // The between-pass data-change guard: a passthrough value the calibration never saw has no
        // planned column, so the planned KnownBins gate turns it into an unknown rather than
        // silently dropping it or inventing a column at emit.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var (names, rows, diagnostics) = await CalibrateAndEmitAsync(spec, "11th\nPhD", emitCsv: "11th\nPhD\nDPhil");

        Assert.Equal(["edu-School", "edu-PhD"], names);
        Assert.Equal(["X.", ".X", ".."], rows);
        var warning = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownValueObserved, warning.Code);
        Assert.Contains("DPhil", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emit_WhenCalibratedPassthroughRepeats_ThenIncidenceIsIdempotentAndDeterministic()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Passthrough("edu", 0, Group("School", "11th"))]);

        var first = await CalibrateAndEmitAsync(spec, "PhD\nPhD\n11th\nPhD");
        var second = await CalibrateAndEmitAsync(spec, "PhD\nPhD\n11th\nPhD");

        Assert.Equal(["edu-School", "edu-PhD"], first.Names);
        Assert.Equal([".X", ".X", "X.", ".X"], first.Rows);
        Assert.Equal(first.Names, second.Names);
        Assert.Equal(first.Rows, second.Rows);
    }

    // --- Emit: triple multi-value union ---------------------------------------

    [Fact]
    public async Task EmitTripleAsync_WhenAnObjectHasSeveralValues_ThenTheCrossesAreTheUnionOfTheirGroups()
    {
        // §5.3.1/§17 rule 8: a multi-valued triple object accumulates crosses by union — and two
        // values landing in the SAME group cross it once (idempotent at incidence level).
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [new AttributeSpec("edu", new PredicateSource("edu", SourceValueType.String), Include: true,
                ValueGroupsDiscretizer.Create(
                    [Group("School", "11th", "HS-grad"), Group("Uni", "Bachelors")], ValueGroupsUnmatched.Other),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);

        var data = "s0,edu,11th\ns0,edu,HS-grad\ns0,edu,Bachelors\ns1,edu,PhD\ns1,edu,Masters";
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var rows = new List<string>();
        await foreach (var obj in Emitter.EmitTripleAsync(plan!, ConversionFixtures.TripleSourceOver(data, spec.Binding), diagnostics))
        {
            rows.Add(string.Concat(Enumerable.Range(0, plan!.FormalAttributes.Count)
                .Select(i => obj.CrossedFormalAttributeIds.Contains(i) ? "X" : ".")));
        }

        Assert.Equal(["edu-School", "edu-Uni", "edu-Other"], plan!.FormalAttributes.Select(a => a.RenderedName));

        // s0: 11th and HS-grad both → School (one cross), Bachelors → Uni. s1: both → Other, once.
        Assert.Equal(["XX.", "..X"], rows);
    }
}
