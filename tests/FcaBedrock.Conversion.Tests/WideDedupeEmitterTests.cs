using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

// Wide object_key.mode = "column" under duplicate_object_policy = "dedupe" (§6.1, D-083): rows sharing
// a cleaned key collapse to one object, crosses unioned onto the first, in first-occurrence order — run
// on the shared grouping/spool backend. Driven end-to-end through WideCsvSource; the spill-forcing
// overload proves spilling is byte-neutral.
public sealed class WideDedupeEmitterTests
{
    // Key on column 0, a nominal attribute "a" on column 1 (domain x, y → a-x id 0, a-y id 1).
    private static BedrockSpec DedupeSpec() =>
        new(ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe), [ConversionFixtures.Nominal("a", 1, "x", "y")]);

    [Fact]
    public async Task Dedupe_WhenNonContiguousDuplicateKey_ThenFirstOccurrenceOrderUnionAndOneInfo()
    {
        // k1 at rows 0 and 2 (non-contiguous, k2 between). k1 collapses to one object at its first
        // position, unioning a-x (row 0) and a-y (row 2). One aggregated Info.
        var (objects, diagnostics) = await RunAsync(DedupeSpec(), "k1,x\nk2,y\nk1,y");

        Assert.Equal(["k1", "k2"], objects.Select(o => o.Name));
        Assert.Equal([0, 1], objects[0].CrossedFormalAttributeIds); // k1: a-x AND a-y (union)
        Assert.Equal([1], objects[1].CrossedFormalAttributeIds);    // k2: a-y
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.DuplicateObjectKey, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("dedupe", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dedupe_WhenAllKeysUnique_ThenNoInfo()
    {
        var (objects, diagnostics) = await RunAsync(DedupeSpec(), "k1,x\nk2,y");

        Assert.Empty(diagnostics); // silent when unique
        Assert.Equal(["k1", "k2"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task Dedupe_WhenConflictingValues_ThenBothCrossesOnOneObject()
    {
        // Two rows, one key, conflicting values → the object crosses both mutually-exclusive bins (§6.1).
        var (objects, _) = await RunAsync(DedupeSpec(), "k1,x\nk1,y");

        Assert.Equal(["k1"], objects.Select(o => o.Name));
        Assert.Equal([0, 1], objects[0].CrossedFormalAttributeIds);
    }

    [Fact]
    public async Task Dedupe_WhenMissingThenPresentUnderAsAttribute_ThenBoth()
    {
        // as_attribute: a-x id 0, a-y id 1, a-missing id 2. k1 row 0 is present-missing (→ a-missing),
        // row 1 is x (→ a-x); the merged object crosses both.
        var spec = new BedrockSpec(ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe),
            [ConversionFixtures.Nominal("a", 1, UnknownValuePolicy.Warn, MissingPolicy.AsAttribute, "x", "y")]);

        var (objects, _) = await RunAsync(spec, "k1,?\nk1,x");

        Assert.Equal(["k1"], objects.Select(o => o.Name));
        Assert.Equal([0, 2], objects[0].CrossedFormalAttributeIds); // a-x AND a-missing
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dedupe_WhenKeyColumnAlsoBoundAsAttribute_ThenAllCrossesUnion(bool spillForced)
    {
        // D-033: col 0 is both the key and attribute "a"; col 1 is attribute "b". a-x 0, a-y 1, b-p 2,
        // b-q 3. key x (rows 0,1) unions a-x + b-p + b-q; key y (row 2) is a-y + b-p. In-memory + spilled.
        var spec = new BedrockSpec(ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe),
            [ConversionFixtures.Nominal("a", 0, "x", "y"), ConversionFixtures.Nominal("b", 1, "p", "q")]);
        var options = spillForced ? new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2) : null;

        var (objects, diagnostics) = await RunAsync(spec, "x,p\nx,q\ny,p", options);

        Assert.Equal(["x", "y"], objects.Select(o => o.Name));
        Assert.Equal([0, 2, 3], objects[0].CrossedFormalAttributeIds); // x: a-x, b-p, b-q
        Assert.Equal([1, 2], objects[1].CrossedFormalAttributeIds);    // y: a-y, b-p
        Assert.Single(diagnostics); // one Info for the x duplicate
    }

    [Fact]
    public async Task Dedupe_WhenInvalidKeyMidStream_ThenErrorOriginalIndexInfoSuppressedAndNoPostErrorInfluence()
    {
        // k1 duplicates (rows 0,1), the ? key at row 2 is unusable (missing token), k2 at row 3 must not
        // influence output. The key prefix truncates at row 2 (inclusive) so row 3 is never read; the
        // structural halt drops the pending group (as the triple path does) and suppresses the Info, and
        // the Error names the original record index.
        var (objects, diagnostics) = await RunAsync(DedupeSpec(), "k1,x\nk1,y\n?,x\nk2,y");

        Assert.Empty(objects); // the pending k1 group is dropped on the halt; k2 (row 3) never reached
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Location?.RecordIndex);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey);
    }

    [Fact]
    public async Task Dedupe_WhenKeysDifferOnlyByCase_ThenDistinctObjectsOrdinal()
    {
        // P-12: keys compare ordinally; K1 and k1 are distinct objects, no duplicate.
        var (objects, diagnostics) = await RunAsync(DedupeSpec(), "K1,x\nk1,y");

        Assert.Empty(diagnostics);
        Assert.Equal(["K1", "k1"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task Dedupe_WhenDuplicatesInterleaved_ThenInfoSampleInSourceOrder()
    {
        // Keys A B B A: duplicate events are B (row 2) then A (row 3), so the bounded sample is "B, A"
        // in source order (not grouped order).
        var (_, diagnostics) = await RunAsync(DedupeSpec(), "A,x\nB,y\nB,x\nA,y");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("2 record", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("B, A", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dedupe_WhenRunTwice_ThenIdenticalNamesAndCrosses()
    {
        const string csv = "k1,x\nk2,y\nk1,y\nk2,x";

        var (first, _) = await RunAsync(DedupeSpec(), csv);
        var (second, _) = await RunAsync(DedupeSpec(), csv);

        Assert.Equal(first.Select(o => o.Name), second.Select(o => o.Name));
        Assert.Equal(first.Select(o => o.CrossedFormalAttributeIds), second.Select(o => o.CrossedFormalAttributeIds));
    }

    [Fact]
    public async Task Dedupe_WhenSpillForced_ThenSameAsInMemory()
    {
        const string csv = "k1,x\nk2,y\nk1,y\nk3,x\nk2,x\nk1,x";

        var (inMemory, inMemoryDiags) = await RunAsync(DedupeSpec(), csv);
        var (spilled, spilledDiags) = await RunAsync(DedupeSpec(), csv, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2));

        Assert.Equal(inMemory.Select(o => o.Name), spilled.Select(o => o.Name));
        Assert.Equal(inMemory.Select(o => o.CrossedFormalAttributeIds), spilled.Select(o => o.CrossedFormalAttributeIds));
        Assert.Equal(inMemoryDiags.Count, spilledDiags.Count);
    }

    [Fact]
    public async Task Dedupe_WhenInPathStorageFails_ThenErrorAndHaltsNoExceptionEscapes()
    {
        var fs = new FakeSpoolFileSystem { OnCreateRun = _ => StorageFaults.DiskFull() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);

        var (objects, diagnostics) = await RunAsync(DedupeSpec(), "k1,x\nk2,y\nk1,y", options);

        Assert.Empty(objects);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.GroupingStorageFailed, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task Dedupe_WhenCleanupFails_ThenWarningAndCompleteOutput()
    {
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);

        var (objects, diagnostics) = await RunAsync(DedupeSpec(), "k1,x\nk2,y\nk1,y\nk3,x", options);

        Assert.Equal(["k1", "k2", "k3"], objects.Select(o => o.Name)); // completed
        var storage = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
        Assert.Equal(DiagnosticSeverity.Warning, storage.Severity);
    }

    [Fact]
    public async Task Dedupe_WhenManyEmptyFieldsSpilled_ThenInitialRunsWithinBudgetPlusMaxRecordAndBoundedResident()
    {
        // Wide, mostly-empty rows stress the resident accounting (Measure undercounts the retained
        // per-field references). budget = 1 measures the max record size and the max resident row; under
        // budget = B every initial run stays within B + maxRecord (independent — from the actual files)
        // and the resident peak stays within 2·B + one max-resident row: at a spill the buffer is at most
        // one List backing-array doubling (≤ B) past budget, plus one more row's retained bytes (F2).
        var spec = new BedrockSpec(ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe),
        [
            ConversionFixtures.Nominal("a", 1, "x", "y"),
            ConversionFixtures.Nominal("b", 2, "x", "y"),
            ConversionFixtures.Nominal("c", 3, "x", "y"),
        ]);
        var csv = string.Join('\n', Enumerable.Range(0, 60).Select(i => $"k{i},x,,y")); // column 2 is empty

        var perRecord = new RecordingObserver();
        await RunAsync(spec, csv, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 4, observer: perRecord));
        var maxRecordSize = perRecord.Written.Where(w => w.Initial).Max(w => w.Size);
        var maxResidentRow = perRecord.PeakResidentBytes; // budget = 1 spills each row alone → peak = max resident row

        const long budget = 512;
        var observer = new RecordingObserver();
        var (objects, diagnostics) = await RunAsync(spec, csv, new GroupingOptions(maxBufferedBytes: budget, maxMergeFanIn: 4, observer: observer));

        Assert.Empty(diagnostics);
        Assert.Equal(60, objects.Count);
        Assert.All(
            observer.Written.Where(w => w.Initial),
            w => Assert.True(w.Size <= budget + maxRecordSize, $"initial run {w.Size} exceeded budget + maxRecord {budget + maxRecordSize}"));
        Assert.Contains(observer.Written, w => !w.Initial); // multi-stage: an intermediate run may exceed the budget
        Assert.True(observer.PeakResidentBytes <= (2 * budget) + maxResidentRow, "resident peak exceeded 2·budget + max-resident row");
    }

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunAsync(
        BedrockSpec spec, string csv, GroupingOptions? options = null)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        var stream = options is null
            ? Emitter.EmitAsync(plan, source, diagnostics)
            : Emitter.EmitAsync(plan, source, diagnostics, options);
        await foreach (var emitted in stream)
        {
            objects.Add(emitted);
        }

        // Observability warnings are orthogonal here and partitioned out (see DataDiagnostics).
        return (objects, ConversionFixtures.DataDiagnostics(diagnostics));
    }
}
