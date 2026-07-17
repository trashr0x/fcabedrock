using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The D-097 filter-only diagnostic ownership and the three whole-stream emit-observability
/// aggregates (§16.4/D-058/D-105), plus the G-12 caller-discard contract.
/// <para>
/// This is the <b>only</b> suite that asserts on the observability warnings; every other emit
/// suite partitions them out (<see cref="ConversionFixtures.DataDiagnostics"/>) because they are
/// orthogonal to what those suites test.
/// </para>
/// </summary>
public sealed class EmitObservabilityTests
{
    // --- D-097: who owns the unparseable diagnostic ---------------------------

    [Theory]
    [InlineData(UnknownValuePolicy.Skip, null)]
    [InlineData(UnknownValuePolicy.Warn, DiagnosticSeverity.Warning)]
    [InlineData(UnknownValuePolicy.Fail, DiagnosticSeverity.Error)]
    [InlineData(UnknownValuePolicy.Include, DiagnosticSeverity.Warning)]
    public async Task Emit_WhenFilterOnlyNumericIsUnparseable_ThenItReportsAtThePolicySeverity(
        UnknownValuePolicy policy, DiagnosticSeverity? expected)
    {
        // D-097: a filter-only attribute is discarded before discretization, so the restriction
        // pass is its ONLY diagnostic owner — without this an unparseable filtered value would
        // report nothing, a silent data-quality hole exactly where a filter must be trustworthy.
        // `include` behaves as warn: an unparseable token cannot join a numeric domain (§10.6).
        var spec = Wide(
            FilterNumeric("age", 0, policy, new RestrictToRange(10, 20)),
            ConversionFixtures.Nominal("t", 1, "x"));

        var (_, diagnostics) = await EmitAsync(spec, "abc,x");

        var reported = diagnostics.Where(d => d.Code == DiagnosticCode.SourceValueUnparseable).ToList();
        if (expected is null)
        {
            Assert.Empty(reported);
            return;
        }

        var diagnostic = Assert.Single(reported);
        Assert.Equal(expected, diagnostic.Severity);
        Assert.Equal("age", diagnostic.Location?.AttributeName);
        Assert.Contains("abc", diagnostic.Message, StringComparison.Ordinal);

        // §10.4/§10.6/G-12: `fail` is Error/**abort** — the run is invalid, so the
        // normal-completion observability aggregates are suppressed. Under every non-aborting
        // policy they still fire (this fixture filters its only row, so NoObjectsEmitted would
        // otherwise be present). Asserted in BOTH directions: severity alone would not catch an
        // implementation that reported the Error and then described the discarded context anyway.
        var observability = diagnostics
            .Where(d => d.Code is DiagnosticCode.NoObjectsEmitted or DiagnosticCode.ObjectHasNoCrosses
                or DiagnosticCode.AttributeHasNoCrosses)
            .ToList();
        if (expected == DiagnosticSeverity.Error)
        {
            Assert.Empty(observability);
        }
        else
        {
            Assert.Contains(observability, d => d.Code == DiagnosticCode.NoObjectsEmitted);
        }
    }

    [Fact]
    public async Task Emit_WhenAFailPolicyObservationIsFollowedByASurvivor_ThenEnumerationDoesNotHaltMidStream()
    {
        // The discriminator for the accepted no-mid-stream-halt contract (D-105): a `fail`-policy
        // unparseable value is Error/**abort**, but "abort" is the operation-failed sense — the
        // stream still reads to completion, because the aggregated diagnostic needs the whole
        // population (D-050/D-059).
        //
        // The failing observation (row 1, "abc") sits BETWEEN two survivors. If the emitter
        // truncated at the abort, the object at input position 2 could never appear — so its
        // presence proves enumeration continued past the failing row. (And the survivors are named
        // "0" and "2", not "0" and "1", re-proving that a filtered row never renumbers row_index
        // under a fail policy.)
        //
        // "t-y" is an empty column here, so AttributeHasNoCrosses WOULD fire — its absence proves
        // the abort suppressed observability even though the stream completed normally.
        var spec = Wide(
            FilterNumeric("age", 0, UnknownValuePolicy.Fail, new RestrictToRange(10, 20)),
            ConversionFixtures.Nominal("t", 1, "x", "y"));

        var (objects, diagnostics) = await EmitAsync(spec, "15,x\nabc,x\n17,x");

        Assert.Equal(["0", "2"], objects.Select(o => o.Name)); // the post-abort survivor exists
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable
            && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
    }

    [Fact]
    public async Task Emit_WhenAnIncludedAttributeFailPolicyAborts_ThenObservabilityIsSuppressedToo()
    {
        // The abort rule is about the RUN's validity, not about which path found the problem: an
        // included attribute's `fail` Error suppresses the aggregates exactly as a filter-only
        // one's does. Otherwise "restriction fail" and "discretization fail" — two identical
        // Error/abort outcomes — would report differently.
        var spec = Wide(ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Fail, 30.0));

        var (objects, diagnostics) = await EmitAsync(spec, "abc");

        Assert.Single(objects); // the stream still COMPLETES — `fail` aborts the operation, not the read
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable
            && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
    }

    [Theory]
    [InlineData(DuplicateObjectPolicy.Dedupe)]
    [InlineData(DuplicateObjectPolicy.Keep)]
    public async Task Emit_WhenFailPolicyAbortsOnAKeyedPath_ThenObservabilityIsSuppressed(DuplicateObjectPolicy policy)
    {
        // Every emit path must honour the abort, not just the streaming one.
        var spec = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, policy),
            [
                FilterNumeric("age", 1, UnknownValuePolicy.Fail, new RestrictToRange(10, 20)),
                ConversionFixtures.Nominal("t", 2, "a", "b"),
            ]);

        var (_, diagnostics) = await EmitAsync(spec, "p1,abc,a");

        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable
            && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenFailPolicyAborts_ThenObservabilityIsSuppressed(TripleOrdering ordering)
    {
        var spec = new BedrockSpec(
            ConversionFixtures.Triple(ordering),
            [
                new AttributeSpec("stage", new PredicateSource("stage", SourceValueType.Number),
                    Include: false, null, null, [], [new RestrictToRange(10, 20)],
                    ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Fail),
                ConversionFixtures.PredicateNominal("t", "t", ["a", "b"]),
            ]);
        var source = ConversionFixtures.TripleSourceOver("s1,stage,abc\ns1,t,a", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        await foreach (var _ in Emitter.EmitTripleAsync(plan!, source, diagnostics))
        {
        }

        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable
            && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
    }

    [Fact]
    public async Task Emit_WhenFilterOnlyNumericValidlyDoesNotMatch_ThenItIsSilent()
    {
        // D-097: a valid non-match is the restriction WORKING, not an anomaly.
        var spec = Wide(
            FilterNumeric("age", 0, UnknownValuePolicy.Warn, new RestrictToRange(10, 20)),
            ConversionFixtures.Nominal("t", 1, "x"));

        var (objects, diagnostics) = await EmitAsync(spec, "50,x");

        Assert.Empty(objects); // filtered out…
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved); // …and never "unknown"
    }

    [Fact]
    public async Task Emit_WhenFilterOnlyValueIsMissing_ThenItIsSilent()
    {
        var spec = Wide(
            FilterNumeric("age", 0, UnknownValuePolicy.Warn, new RestrictToRange(10, 20)),
            ConversionFixtures.Nominal("t", 1, "x"));

        var (objects, diagnostics) = await EmitAsync(spec, "?,x");

        Assert.Empty(objects);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
    }

    [Fact]
    public async Task Emit_WhenIncludedAndRestrictedIsUnparseable_ThenCountedExactlyOnceEvenThoughFiltered()
    {
        // D-097's at-most-once rule, at its sharpest. The attribute is included AND restricted, so
        // BOTH the classification pass and the restriction pass read the same cell. The
        // classification pass owns the diagnostic (it runs for every formed object, filtered or
        // not — "restrictions filter objects, not observations"), and the restriction path must
        // stay silent for it. A naive implementation reports twice.
        var spec = Wide(
            ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Warn, 30.0) with
            {
                RestrictTo = [new RestrictToRange(10, 20)],
            });

        var (objects, diagnostics) = await EmitAsync(spec, "abc");

        Assert.Empty(objects); // unparseable matches nothing → filtered out
        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Equal("age", diagnostic.Location?.AttributeName);
        Assert.Contains("1 value(s)", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emit_WhenTwoAttributesShareAColumn_ThenEachOwnsItsOwnAggregate()
    {
        // D-033: a source may repeat across attributes. Tallies are ATTRIBUTE-owned, so an
        // included attribute and a filter-only one reading the same cell each report once — one
        // cell, two attributes, two diagnostics. That is not double-counting: D-097's rule is
        // at-most-once per observation PER ATTRIBUTE per pass.
        var spec = Wide(
            ConversionFixtures.NumericCuts("age", 0, UnknownValuePolicy.Warn, 30.0),
            FilterNumeric("ageFilter", 0, UnknownValuePolicy.Warn, new RestrictToRange(10, 20)));

        var (_, diagnostics) = await EmitAsync(spec, "abc");

        var reported = diagnostics.Where(d => d.Code == DiagnosticCode.SourceValueUnparseable).ToList();
        Assert.Equal(2, reported.Count);
        Assert.Equal(["age", "ageFilter"], reported.Select(d => d.Location?.AttributeName));
    }

    [Fact]
    public async Task Emit_WhenFilterOnlyStringIsAnyValue_ThenNoUnparseableIsPossible()
    {
        // A string restriction never parses, so it can never report unparseable — every raw value
        // is a legitimate non-match.
        var spec = Wide(Filter("Gene", 0, new RestrictToValue("Bmp5")), ConversionFixtures.Nominal("t", 1, "x"));

        var (_, diagnostics) = await EmitAsync(spec, "!!!,x");

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
    }

    [Fact]
    public async Task Emit_WhenManyFilterOnlyValuesAreUnparseable_ThenOneAggregateWithCountAndBoundedSample()
    {
        // Aggregated, never one per row (§16.4): a malformed column at 73M records must not
        // produce 73M diagnostics.
        var spec = Wide(
            FilterNumeric("age", 0, UnknownValuePolicy.Warn, new RestrictToRange(10, 20)),
            ConversionFixtures.Nominal("t", 1, "x"));

        var (_, diagnostics) = await EmitAsync(spec, "a,x\nb,x\nc,x\nd,x\ne,x");

        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Contains("5 value(s)", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("(e.g. a, b, c)", diagnostic.Message, StringComparison.Ordinal); // bounded to 3
    }

    // --- NoObjectsEmitted ----------------------------------------------------

    [Fact]
    public async Task Emit_WhenRestrictionExcludesEveryObject_ThenNoObjectsEmittedWarns()
    {
        var spec = Wide(Filter("Gene", 0, new RestrictToValue("Bmp5")), ConversionFixtures.Nominal("t", 1, "x"));

        var (objects, diagnostics) = await EmitAsync(spec, "Wnt1,x\nShh,x");

        Assert.Empty(objects);
        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task Emit_WhenTheInputHasNoDataRows_ThenNoObjectsEmittedWarns()
    {
        // A header but no rows: the schema (and therefore the column set) exists, the data does
        // not. Distinct from an all-filtered stream, and it must warn the same way.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: true), [ConversionFixtures.Nominal("t", 0, "x")]);

        var (objects, diagnostics) = await EmitAsync(spec, "t");

        Assert.Empty(objects);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
    }

    [Fact]
    public async Task Emit_WhenZeroObjectsAndZeroColumns_ThenOnlyNoObjectsEmittedWarns()
    {
        // An all-filter-only plan has no columns (NoFormalAttributes warns at PLAN, not here), and
        // nothing survives → zero rows. With no columns there is nothing to be uncrossed, so
        // AttributeHasNoCrosses must stay silent rather than reporting "0 attributes".
        var spec = Wide(Filter("Gene", 0, new RestrictToValue("Bmp5")));

        var (objects, diagnostics) = await EmitAsync(spec, "Wnt1");

        Assert.Empty(objects);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
    }

    [Fact]
    public async Task Emit_WhenObjectsAreEmitted_ThenNoObjectsEmittedStaysSilent()
    {
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "x"));

        var (_, diagnostics) = await EmitAsync(spec, "x");

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
    }

    // --- AttributeHasNoCrosses -----------------------------------------------

    [Fact]
    public async Task Emit_WhenColumnsAreNeverCrossed_ThenOneAggregateWithCountAndPlanOrderSample()
    {
        // §7: the column vocabulary is fixed over the INPUT UNIVERSE before filtering, so empty
        // columns after a restriction are expected, not an error. One aggregate, count + up to
        // three RENDERED names in PLAN order.
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a", "b", "c", "d", "e"));

        var (_, diagnostics) = await EmitAsync(spec, "a"); // only "a" is ever crossed

        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("4 formal attribute(s)", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("t-b, t-c, t-d", diagnostic.Message, StringComparison.Ordinal); // plan order, bounded to 3
        Assert.DoesNotContain("t-e", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emit_WhenARestrictionEmptiesAColumn_ThenItIsReportedNotSuppressed()
    {
        // The §7 consequence made concrete: calibration/vocabulary precede filtering, so a
        // surviving population need not span every bin.
        var spec = Wide(
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("tissue", 1, "endoderm", "mesoderm"));

        var (objects, diagnostics) = await EmitAsync(spec, "Bmp5,endoderm\nWnt1,mesoderm");

        Assert.Single(objects); // only the Bmp5 row survives, so mesoderm is never crossed
        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.Contains("1 formal attribute(s)", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("tissue-mesoderm", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emit_WhenEveryColumnIsCrossed_ThenAttributeHasNoCrossesStaysSilent()
    {
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a", "b"));

        var (_, diagnostics) = await EmitAsync(spec, "a\nb");

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
    }

    // --- ObjectHasNoCrosses --------------------------------------------------

    [Fact]
    public async Task Emit_WhenObjectsHaveNoCrosses_ThenOneAggregateWithCountAndEmissionOrderSample()
    {
        // §10.1: an empty row is legal and still written. Aggregated with up to three object names
        // in EMISSION order.
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a"));

        // Rows 1..4 hold values outside the domain → unknown → no cross. Row 0 crosses.
        var (objects, diagnostics) = await EmitAsync(spec, "a\nz\nz\nz\nz");

        Assert.Equal(5, objects.Count); // all still emitted
        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("4 emitted object(s)", diagnostic.Message, StringComparison.Ordinal);

        // Emission order, bounded to three: objects 1..4 are empty, so the sample is exactly
        // "1, 2, 3" — object 4 is counted but not sampled.
        Assert.Contains("(e.g. 1, 2, 3)", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emit_WhenAFilteredObjectWouldHaveBeenEmpty_ThenItIsNotCounted()
    {
        // Only EMITTED objects count: an object restrict_to excluded never had a row, so it can
        // neither be an empty row nor cross a column.
        var spec = Wide(
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("t", 1, "a"));

        // Row 0 survives and crosses; rows 1-2 are filtered AND would have been empty.
        var (objects, diagnostics) = await EmitAsync(spec, "Bmp5,a\nWnt1,z\nWnt1,z");

        Assert.Single(objects);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
    }

    [Fact]
    public async Task Emit_WhenEveryObjectCrossesSomething_ThenObjectHasNoCrossesStaysSilent()
    {
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a", "b"));

        var (_, diagnostics) = await EmitAsync(spec, "a\nb");

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
    }

    [Fact]
    public async Task Emit_WhenSomeRowsAndColumnsAreEmpty_ThenBothAggregatesFireInThePinnedOrder()
    {
        // The mixed case, and the pinned order: whole context (NoObjectsEmitted — silent here),
        // then rows (ObjectHasNoCrosses), then columns (AttributeHasNoCrosses).
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a", "b"));

        var (_, diagnostics) = await EmitAsync(spec, "a\nz"); // row 1 empty; column "t-b" empty

        var observability = diagnostics
            .Where(d => d.Code is DiagnosticCode.NoObjectsEmitted or DiagnosticCode.ObjectHasNoCrosses
                or DiagnosticCode.AttributeHasNoCrosses)
            .Select(d => d.Code)
            .ToArray();

        Assert.Equal([DiagnosticCode.ObjectHasNoCrosses, DiagnosticCode.AttributeHasNoCrosses], observability);
    }

    // --- shape coverage ------------------------------------------------------

    [Fact]
    public async Task EmitDedupe_WhenAMergedGroupIsEmpty_ThenTheAggregatesStillFire()
    {
        var spec = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe),
            [ConversionFixtures.Nominal("t", 1, "a", "b")]);

        var (objects, diagnostics) = await EmitAsync(spec, "p1,a\np1,a\np2,z");

        Assert.Equal(["p1", "p2"], objects.Select(o => o.Name));
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);   // p2
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses); // t-b
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenColumnsAndRowsAreEmpty_ThenTheAggregatesFire(TripleOrdering ordering)
    {
        var spec = new BedrockSpec(
            ConversionFixtures.Triple(ordering),
            [ConversionFixtures.PredicateNominal("t", "t", ["a", "b"])]);
        var source = ConversionFixtures.TripleSourceOver("s1,t,a\ns2,other,q", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan!, source, diagnostics))
        {
            objects.Add(emitted);
        }

        Assert.Equal(["s1", "s2"], objects.Select(o => o.Name));
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);   // s2: unbound predicate
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses); // t-b
    }

    [Theory]
    [InlineData(DuplicateObjectPolicy.Dedupe)]
    [InlineData(DuplicateObjectPolicy.Keep)]
    public async Task Emit_WhenAKeyedPathEmitsNothing_ThenNoObjectsEmittedWarns(DuplicateObjectPolicy policy)
    {
        // NoObjectsEmitted must fire on EVERY path, not just wide streaming — the dedupe path in
        // particular closes its objects in a different place, so its zero-object case is a
        // distinct code path.
        var spec = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, policy),
            [
                Filter("Gene", 1, new RestrictToValue("Bmp5")),
                ConversionFixtures.Nominal("t", 2, "a"),
            ]);

        var (objects, diagnostics) = await EmitAsync(spec, "p1,Wnt1,a\np1,Shh,a");

        Assert.Empty(objects);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenEverySubjectIsFiltered_ThenNoObjectsEmittedWarns(TripleOrdering ordering)
    {
        var spec = new BedrockSpec(
            ConversionFixtures.Triple(ordering),
            [
                TripleFilter("Gene", "Gene", new RestrictToValue("Bmp5")),
                ConversionFixtures.PredicateNominal("t", "t", ["a"]),
            ]);
        var source = ConversionFixtures.TripleSourceOver("s1,Gene,Wnt1\ns1,t,a\ns2,Gene,Shh\ns2,t,a", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan!, source, diagnostics))
        {
            objects.Add(emitted);
        }

        Assert.Empty(objects);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
    }

    [Fact]
    public async Task Emit_WhenAnObjectSurvivesAnAllFilterOnlyZeroColumnPlan_ThenItIsAnEmptyRow()
    {
        // The other zero-column case: a plan with NO columns that DOES emit objects. Every
        // emitted object trivially crosses nothing, so ObjectHasNoCrosses reports them — while
        // NoObjectsEmitted stays silent (objects exist) and AttributeHasNoCrosses stays silent
        // (there are no columns to be empty). NoFormalAttributes is a PLAN warning, not an emit
        // one, so it is absent from this collector.
        var spec = Wide(Filter("Gene", 0, new RestrictToValue("Bmp5")));

        var (objects, diagnostics) = await EmitAsync(spec, "Bmp5\nBmp5\nWnt1");

        Assert.Equal(["0", "1"], objects.Select(o => o.Name)); // survivors, keeping input positions
        Assert.All(objects, o => Assert.Empty(o.CrossedFormalAttributeIds));

        var diagnostic = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
        Assert.Contains("2 emitted object(s)", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
    }

    // --- halt suppression ----------------------------------------------------

    [Fact]
    public async Task EmitTriple_WhenGroupingStorageFailsInPath_ThenTheObservabilityWarningsAreSuppressed()
    {
        // The storage half of the halt rule, with a REAL injected failure rather than an inferred
        // one: an in-path spool failure stops delivery, so the stream is truncated and the
        // aggregates would describe a partial read. The storage Error itself must still surface.
        var fs = new FakeSpoolFileSystem { OnCreateRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs); // force a spill
        var spec = new BedrockSpec(
            ConversionFixtures.Triple(TripleOrdering.Unordered),
            [ConversionFixtures.PredicateNominal("t", "t", ["a", "b"])]);
        var source = ConversionFixtures.TripleSourceOver("s1,t,a\ns2,t,a\ns3,t,a\ns1,t,a", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        await foreach (var _ in Emitter.EmitTripleAsync(plan!, source, diagnostics, options))
        {
        }

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed
            && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
    }

    [Fact]
    public async Task Emit_WhenAStructuralHaltOccurs_ThenTheObservabilityWarningsAreSuppressed()
    {
        // The established emit-aggregate rule (§16.4), applied unchanged: after a halt the stream
        // is truncated, so "no objects" or "this column is empty" would describe the HALT rather
        // than the data. The halt is forced for real — an invalid object key at record 0.
        var spec = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Fail),
            [ConversionFixtures.Nominal("t", 1, "a", "b")]);

        var (objects, diagnostics) = await EmitAsync(spec, "?,a");

        Assert.Empty(objects);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectKeyValueInvalid);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
    }

    [Fact]
    public async Task EmitTriple_WhenNonContiguousSubjectHalts_ThenTheObservabilityWarningsAreSuppressed()
    {
        var spec = new BedrockSpec(
            ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("t", "t", ["a", "b"])]);
        var source = ConversionFixtures.TripleSourceOver("s1,t,a\ns2,t,a\ns1,t,a", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        await foreach (var _ in Emitter.EmitTripleAsync(plan!, source, diagnostics))
        {
        }

        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.TripleSubjectNotContiguous);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
    }

    [Fact]
    public async Task Emit_WhenTheConsumerDisposesEarly_ThenNoNormalCompletionWarningIsReported()
    {
        // Early disposal is not normal completion: the aggregates must not fire for a stream the
        // caller abandoned, or a partial read would be reported as an empty context.
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a", "b"));
        var source = ConversionFixtures.SourceOver("a\nb", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        await using (var enumerator = Emitter.EmitAsync(plan!, source, diagnostics).GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync()); // take one object, then abandon
        }

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Emit_WhenCancelled_ThenItThrowsWithoutReportingNormalCompletionWarnings()
    {
        // Cancellation stays exceptional (P-14) and never becomes a diagnostic.
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a", "b"));
        var source = ConversionFixtures.SourceOver("a\nb", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var diagnostics = new List<BedrockDiagnostic>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Emitter.EmitAsync(plan!, source, diagnostics, cts.Token))
            {
            }
        });

        Assert.Empty(diagnostics);
    }

    // --- replay single-counting ----------------------------------------------

    [Fact]
    public async Task Emit_WhenReplayedForCxt_ThenEachObservabilityWarningIsReportedExactlyOnce()
    {
        // The .cxt writer enumerates the stream TWICE. The warnings route through the ordinary
        // data sink, so the session's first-pass claim single-counts them — both passes are
        // genuinely opened here, so a per-pass implementation would double them.
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a", "b"));
        var source = ConversionFixtures.SourceOver("a\nz", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        using (var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan!, source, sink), diagnostics))
        {
            await Drain(session.Open()); // pass 1 (names)
            await Drain(session.Open()); // pass 2 (incidence)
        }

        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
    }

    [Fact]
    public async Task Emit_WhenReplayedThroughTheRealCxtWriter_ThenTheWarningsAreSingleCountedAndTheBytesAreWritten()
    {
        // End to end through the actual writer, not a hand-rolled two-pass.
        var spec = Wide(ConversionFixtures.Nominal("t", 0, "a", "b"));
        var source = ConversionFixtures.SourceOver("a\nz", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        using var stream = new MemoryStream();
        using (var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan!, source, sink), diagnostics))
        {
            await CxtWriter.WriteAsync(plan!, session.Open, WriterOptions.Native, stream);
        }

        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);

        // The degenerate context is still structurally valid and written (§16.4).
        var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.StartsWith("B\n", text, StringComparison.Ordinal);
        Assert.Contains("X.\n..\n", text, StringComparison.Ordinal); // row 0 crosses t-a; row 1 empty
    }

    // --- G-12: the caller-discard contract ------------------------------------

    [Fact]
    public async Task Cxt_WhenAStructuralHaltTruncatesBothPasses_ThenTheWriteSucceedsButAnErrorIsPresent()
    {
        // G-12, the case the object-name-sequence invariant CANNOT catch: a DETERMINISTIC halt
        // truncates both passes IDENTICALLY, so the names still align, the writer returns
        // successfully, and a structurally well-formed but TRUNCATED artifact exists on disk —
        // alongside an Error. Bytes already written to a caller-owned sink cannot be retracted,
        // so artifact validity is a diagnostic question: any Error/Fatal after disposal ⇒ the
        // caller must discard the output. Transactional publication is M7's, not the writer's.
        var spec = new BedrockSpec(
            ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("t", "t", ["a", "b"])]);
        var source = ConversionFixtures.TripleSourceOver("s1,t,a\ns2,t,b\ns1,t,a", spec.Binding); // s1 recurs
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        using var stream = new MemoryStream();
        using (var session = EmitReplay.Begin(sink => Emitter.EmitTripleAsync(plan!, source, sink), diagnostics))
        {
            // Does not throw: both passes truncate at the same point, so the invariant holds.
            await CxtWriter.WriteAsync(plan!, session.Open, WriterOptions.Native, stream);
        }

        // A well-formed file WAS produced…
        var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.StartsWith("B\n", text, StringComparison.Ordinal);
        Assert.NotEmpty(text);

        // …and it is INVALID, discoverable only from the diagnostics — inspected after disposal,
        // which is when the final cross-pass aggregates land.
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.TripleSubjectNotContiguous
            && d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
    }

    [Fact]
    public async Task Dat_WhenARestrictionFailPolicyErrors_ThenRowsWereAlreadyWrittenAndAnErrorIsPresent()
    {
        // G-12's .dat half: the stream writes rows immediately, so by the time the filter-only
        // `fail` aggregate reports its Error the bytes are already in the caller's sink. Nothing
        // can retract them — the caller must discard the artifact.
        var spec = Wide(
            FilterNumeric("age", 0, UnknownValuePolicy.Fail, new RestrictToRange(10, 20)),
            ConversionFixtures.Nominal("t", 1, "x"));
        var source = ConversionFixtures.SourceOver("15,x\nabc,x", spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        using var stream = new MemoryStream();
        await DatWriter.WriteAsync(Emitter.EmitAsync(plan!, source, diagnostics), WriterOptions.Native, stream);

        // Row 0 (age 15) survived and was written before "abc" was ever read…
        Assert.NotEmpty(stream.ToArray());
        Assert.Equal("1\n", System.Text.Encoding.UTF8.GetString(stream.ToArray()));

        // …and the run is invalid.
        var error = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.SourceValueUnparseable);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);

        // The abort suppresses the normal-completion aggregates: this fixture leaves "t-x"
        // uncrossed by the filtered row, so AttributeHasNoCrosses would otherwise describe an
        // artifact the caller is being told to throw away.
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
    }

    [Fact]
    public async Task Emit_WhenTheRunIsClean_ThenNoErrorOrFatalIsPresent()
    {
        // The positive half of the G-12 rule: a clean run carries no Error/Fatal, so the artifact
        // is valid. Without this the contract would be untestable in the direction that matters.
        var spec = Wide(
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("t", 1, "x"));

        var (objects, diagnostics) = await EmitAsync(spec, "Bmp5,x");

        Assert.Single(objects);
        Assert.DoesNotContain(diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
    }

    // --- helpers ------------------------------------------------------------

    private static AttributeSpec Filter(string name, int index, params RestrictToEntry[] entries) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec TripleFilter(string name, string predicate, params RestrictToEntry[] entries) =>
        new(name, new PredicateSource(predicate, SourceValueType.String), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec FilterNumeric(
        string name, int index, UnknownValuePolicy policy, params RestrictToEntry[] entries) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, policy);

    private static BedrockSpec Wide(params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Wide(hasHeader: false), attributes);

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)>
        EmitAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan!, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (objects, diagnostics);
    }

    private static async Task Drain(IAsyncEnumerable<EmittedObject> objects)
    {
        await foreach (var _ in objects)
        {
        }
    }
}
