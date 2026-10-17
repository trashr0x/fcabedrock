using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The exact library text of every aggregated diagnostic that carries a count and a bounded sample
/// (§16.4, D-059/D-083): each one counts every occurrence, quotes at most three distinct examples in
/// the order its pass first meets them, and says what kind of thing it counted. One suite per
/// behaviour rather than per caller, so the shared sample contract is pinned in one place across the
/// emit, calibrate, restriction and observability producers and both grouping paths.
/// </summary>
public sealed class AggregatedDiagnosticSampleTests
{
    // --- emit: unknown and unparseable values ---------------------------------

    [Fact]
    public async Task Emit_WhenUnknownValuesRepeat_ThenTheMessageCountsEveryOccurrenceAndQuotesTheFirstThreeDistinct()
    {
        var spec = WideSpec(ConversionFixtures.Nominal("t", 0, "ok"));

        var (_, diagnostics) = await EmitWideAsync(spec, "a\na\nb\nc\nd\na\nok");

        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("t", diagnostic.Location?.AttributeName);
        Assert.Equal(
            "Attribute 't' had 6 occurrence(s) of values that are not in its declared domain (e.g. \"a\", \"b\", \"c\").",
            diagnostic.Message);
    }

    [Fact]
    public async Task Emit_WhenNumericSpellingsShareAnUnknownBin_ThenTheSampleKeepsEachRawSpelling()
    {
        // Numeric free_per_value checks the domain on the canonical number (D-096), so 2.50, 2.5 and
        // 2.500 are one bin outside it and 1 is the declared one. The sample still shows the first three
        // distinct values as read; 3.0 is the fourth and only adds to the count.
        var spec = WideSpec(NumericFreePerValue("v", ["1"]));

        var (objects, diagnostics) = await EmitWideAsync(spec, "2.50\n2.5\n2.500\n3.0\n2.50\n1");

        Assert.Equal(6, objects.Count);
        Assert.All(objects.Take(5), o => Assert.Empty(o.CrossedFormalAttributeIds));
        Assert.Equal([0], objects[5].CrossedFormalAttributeIds); // 1 -> v-1, the declared bin
        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "Attribute 'v' had 5 occurrence(s) of values that are not in its declared domain (e.g. \"2.50\", \"2.5\", \"2.500\").",
            diagnostic.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Emit_WhenAValueGroupsValueMatchesNoGroup_ThenTheMessageSaysSoEvenBesideADormantDomain(bool dormantDomain)
    {
        // value_groups never consults declared_domain (§11.6, D-055), so an authored domain beside it
        // is dormant and the reason is the groups' own: the value matched none of them.
        var spec = WideSpec(ValueGroups("edu", dormantDomain ? ["PhD"] : []));

        var (_, diagnostics) = await EmitWideAsync(spec, "PhD\nMasters\n11th\nPhD");

        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
        Assert.Equal(
            "Attribute 'edu' had 3 occurrence(s) of values that match no group (e.g. \"PhD\", \"Masters\").",
            diagnostic.Message);
        Assert.DoesNotContain("declared domain", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emit_WhenNumericValuesAreUnparseable_ThenTheMessageCountsOccurrences()
    {
        var spec = WideSpec(ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Warn, 30, 40, 50));

        var (_, diagnostics) = await EmitWideAsync(spec, "abc\nxyz\nabc\n40");

        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Equal(
            "Attribute 'age' had 3 occurrence(s) of values that could not be parsed as a number (e.g. \"abc\", \"xyz\").",
            diagnostic.Message);
    }

    [Fact]
    public async Task Emit_WhenAFilterOnlyRestrictionReadsUnparseableValues_ThenTheMessageCountsOccurrences()
    {
        var filter = new AttributeSpec(
            "age", new ColumnSource(0, SourceValueType.Number), Include: false, null, null, [],
            [new RestrictToRange(10, 20)], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = WideSpec(filter, ConversionFixtures.Nominal("t", 1, "x"));

        var (_, diagnostics) = await EmitWideAsync(spec, "n/a,x\nn/a,x\n15,x\n?x,x");

        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Equal(
            "The restrict_to filter on attribute 'age' read 3 occurrence(s) of values that could not be parsed as a number " +
            "(e.g. \"n/a\", \"?x\"); they match no numeric entry (§10.4).",
            diagnostic.Message);
    }

    // --- calibrate: unparseable values, in memory and spilled ------------------

    [Fact]
    public async Task Calibrate_WhenValuesAreUnparseable_ThenTheMessageCountsOccurrencesWhetherOrNotTheStoreSpills()
    {
        var pending = new AttributeSpec(
            "score", new ColumnSource(0, SourceValueType.Number), Include: true,
            new CalibrationPending(
                new PendingEqualFrequency(2, TiePolicy.Left, CutPlacement.RightValue), CultureInfo.InvariantCulture),
            new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = WideSpec(pending);
        const string Csv = "1\nx\n2\nx\ny\n3\n4";
        const string Expected =
            "Attribute 'score' had 3 occurrence(s) of present-but-unparseable numeric values during calibration " +
            "(e.g. \"x\", \"y\"); they were excluded from the calibration population (§11.5).";

        var observer = new RecordingObserver();

        var inMemory = await CalibrateWideAsync(spec, Csv, options: null);
        var spilled = await CalibrateWideAsync(
            spec, Csv, new GroupingOptions(maxBufferedBytes: 308, maxMergeFanIn: 2, observer: observer));

        Assert.NotEmpty(observer.Written); // the second run really used spill storage
        Assert.Equal(Expected, Assert.Single(inMemory, d => d.Code == DiagnosticCode.SourceValueUnparseable).Message);
        Assert.Equal(Expected, Assert.Single(spilled, d => d.Code == DiagnosticCode.SourceValueUnparseable).Message);
    }

    // --- object-key policies and empty rows ----------------------------------------------

    [Fact]
    public async Task Emit_WhenKeepReusesKeys_ThenBothAggregatesCountObjectsAndQuoteTheirSamples()
    {
        // [P001#2, P001, P001, P001]: rows 2 and 3 reuse P001; row 2's candidate P001#2 collides with
        // the literal first key and escalates to P001#2#1.
        var spec = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep), [ConversionFixtures.Nominal("a", 1, "x", "y")]);

        var (objects, diagnostics) = await EmitWideAsync(spec, "P001#2,x\nP001,y\nP001,x\nP001,y");

        Assert.Equal(["P001#2", "P001", "P001#2#1", "P001#3"], objects.Select(o => o.Name));
        Assert.Equal(
            "2 object name(s) reused a cleaned key and were kept as separate objects (e.g. \"P001\"); " +
            "duplicate_object_policy = \"keep\" (§6.1).",
            Assert.Single(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey).Message);
        Assert.Equal(
            "1 object name(s) were disambiguated to stay unique (e.g. \"P001\"→\"P001#2#1\"); " +
            "duplicate_object_policy = \"keep\" (§6.1).",
            Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectKeyNameDisambiguated).Message);
    }

    [Fact]
    public async Task Emit_WhenDedupeMergesRepeatedKeys_ThenTheSampleIsTheDistinctKeysInTheOrderTheyFirstRecur()
    {
        // Duplicate events in source order: B (row 2), A (row 3), B (row 4). The count is every merged
        // record; the sample keeps each key once.
        var spec = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe), [ConversionFixtures.Nominal("a", 1, "x", "y")]);

        var (_, diagnostics) = await EmitWideAsync(spec, "A,x\nB,y\nB,x\nA,y\nB,y");

        Assert.Equal(
            "3 record(s) reused a cleaned key and merged onto the first occurrence (e.g. \"B\", \"A\"); " +
            "duplicate_object_policy = \"dedupe\" (§6.1).",
            Assert.Single(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey).Message);
    }

    [Fact]
    public async Task Emit_WhenObjectsCrossNothing_ThenTheMessageCountsObjectsAndQuotesTheirNames()
    {
        var spec = WideSpec(ConversionFixtures.Nominal("t", 0, "a"));

        var (_, diagnostics) = await EmitWideAsync(spec, "a\nz\nz\nz\nz", keepObservability: true);

        Assert.Equal(
            "4 emitted object(s) cross no formal attribute (e.g. \"1\", \"2\", \"3\"); their rows are empty (§16.4).",
            Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses).Message);
    }

    // --- both grouping paths keep one first-observed order ----------------------------------

    [Fact]
    public async Task EmitTriple_WhenUnorderedRowsSpill_ThenTheSampleKeepsTheInMemoryFirstObservedOrder()
    {
        // The unordered emit pass meets values in grouped object order (s1, s2, s3), so its first
        // observations are u1, u3, u2, not the raw u1, u2, u3. Spilling must not move them.
        var spec = new BedrockSpec(
            ConversionFixtures.Triple(TripleOrdering.Unordered), [ConversionFixtures.PredicateNominal("t", "t", ["ok"])]);
        const string Data = "s1,t,u1\ns2,t,u2\ns1,t,u3\ns3,t,u1\ns2,t,u4\ns3,t,ok";
        const string Expected =
            "Attribute 't' had 5 occurrence(s) of values that are not in its declared domain (e.g. \"u1\", \"u3\", \"u2\").";

        var observer = new RecordingObserver();

        var (inMemoryObjects, inMemory) = await EmitTripleAsync(spec, Data, GroupingOptions.Default);
        var (spilledObjects, spilled) = await EmitTripleAsync(
            spec, Data, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, observer: observer));

        Assert.NotEmpty(observer.Written);
        Assert.Equal(["s1", "s2", "s3"], inMemoryObjects.Select(o => o.Name));
        Assert.Equal(inMemoryObjects.Select(o => o.Name), spilledObjects.Select(o => o.Name));
        Assert.Equal(Expected, Assert.Single(inMemory, d => d.Code == DiagnosticCode.UnknownValueObserved).Message);
        Assert.Equal(Expected, Assert.Single(spilled, d => d.Code == DiagnosticCode.UnknownValueObserved).Message);
    }

    [Fact]
    public async Task EmitDedupe_WhenRowsSpill_ThenEverySampleKeepsTheInMemoryFirstObservedOrder()
    {
        var spec = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe), [ConversionFixtures.Nominal("a", 1, "ok")]);
        const string Csv = "k1,u1\nk2,u2\nk1,u3\nk3,u1\nk2,u4\nk3,ok";

        var observer = new RecordingObserver();

        var (_, inMemory) = await EmitWideAsync(spec, Csv);
        var (_, spilled) = await EmitWideAsync(
            spec, Csv, options: new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, observer: observer));

        Assert.NotEmpty(observer.Written);
        Assert.Equal(
            "Attribute 'a' had 5 occurrence(s) of values that are not in its declared domain (e.g. \"u1\", \"u3\", \"u2\").",
            Assert.Single(inMemory, d => d.Code == DiagnosticCode.UnknownValueObserved).Message);
        Assert.Equal(
            "3 record(s) reused a cleaned key and merged onto the first occurrence (e.g. \"k1\", \"k2\", \"k3\"); " +
            "duplicate_object_policy = \"dedupe\" (§6.1).",
            Assert.Single(inMemory, d => d.Code == DiagnosticCode.DuplicateObjectKey).Message);
        Assert.Equal(inMemory.Select(d => d.Message), spilled.Select(d => d.Message));
    }

    // --- helpers ----------------------------------------------------------------------------

    private static BedrockSpec WideSpec(params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Wide(hasHeader: false), attributes);

    private static AttributeSpec ValueGroups(string name, IReadOnlyList<string> domain) =>
        new(name, new ColumnSource(0, SourceValueType.String), Include: true,
            ValueGroupsDiscretizer.Create([ValueGroup.Create("School", ["11th"], null)], ValueGroupsUnmatched.Skip),
            new NominalScale(), DeclaredDomain: domain, RestrictTo: [], ConversionFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec NumericFreePerValue(string name, IReadOnlyList<string> domain) =>
        new(name, new ColumnSource(0, SourceValueType.Number), Include: true,
            new FreePerValueDiscretizer(SourceValueType.Number, CultureInfo.InvariantCulture),
            new NominalScale(), DeclaredDomain: domain, RestrictTo: [], ConversionFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> EmitWideAsync(
        BedrockSpec spec, string csv, bool keepObservability = false, GroupingOptions? options = null)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        var stream = options is null
            ? Emitter.EmitAsync(plan!, source, diagnostics)
            : Emitter.EmitAsync(plan!, source, diagnostics, options);
        await foreach (var emitted in stream)
        {
            objects.Add(emitted);
        }

        return (objects, keepObservability ? diagnostics : ConversionFixtures.DataDiagnostics(diagnostics));
    }

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> EmitTripleAsync(
        BedrockSpec spec, string data, GroupingOptions options)
    {
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan!, source, diagnostics, options))
        {
            objects.Add(emitted);
        }

        return (objects, ConversionFixtures.DataDiagnostics(diagnostics));
    }

    private static async Task<IReadOnlyList<BedrockDiagnostic>> CalibrateWideAsync(
        BedrockSpec spec, string csv, GroupingOptions? options)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());
        var result = options is null
            ? await Calibrator.CalibrateAsync(resolved, source)
            : await Calibrator.CalibrateAsync(resolved, source, options, observer: null, CancellationToken.None);
        Assert.True(result.TryGetValue(out _), string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return result.Diagnostics;
    }
}
