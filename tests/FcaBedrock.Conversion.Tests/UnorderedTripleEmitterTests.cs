using System.Text;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// Slice D/F: triple ordering = "unordered". Interleaved (predicate-major) input is regrouped into
// subject-contiguous, first-appearance order and emitted through the same Emitter.EmitTripleAsync as
// subject_grouped. EmitTripleAsync owns ordering selection from plan.Execution (D-082), so tests pass
// the raw source; the emitter builds the unordered wrapper with emitter-owned per-enumeration reports.
public sealed class UnorderedTripleEmitterTests
{
    [Fact]
    public async Task Unordered_WhenInterleavedNamedSubjects_ThenFirstAppearanceObjectOrder()
    {
        // The load-bearing Slice D property: interleaved input emits objects in first-appearance
        // order of the cleaned subject — NOT sorted (a sort would begin "Alice..."). Mirrors the
        // mini-adult_triples_named arrival order (Slice G locks the bytes).
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
        [
            ConversionFixtures.PredicateNominal("role", "role", ["dev", "ops"]),
            ConversionFixtures.PredicateNominal("team", "team", ["red", "blue"]),
        ]);
        const string data =
            "Sam,role,dev\nJenny,role,ops\nJohn,role,dev\nAndrew,role,ops\n" +
            "Mary,role,dev\nLaura,role,ops\nAlice,role,dev\nTim,role,ops\n" +
            "Sam,team,red\nJenny,team,red\nJohn,team,blue\nAndrew,team,blue\n" +
            "Mary,team,red\nLaura,team,red\nAlice,team,blue\nTim,team,blue";

        var (objects, diagnostics) = await RunAsync(spec, data, TripleOrdering.Unordered);

        Assert.Empty(diagnostics);
        Assert.Equal(
            ["Sam", "Jenny", "John", "Andrew", "Mary", "Laura", "Alice", "Tim"],
            objects.Select(o => o.Name));
        Assert.Equal([0, 2], objects[0].CrossedFormalAttributeIds); // Sam: role-dev (0), team-red (2)
    }

    [Fact]
    public async Task Unordered_WhenInterleavedMushroom_ThenMatchesSubjectGrouped()
    {
        // The unordered path over interleaved input reproduces the subject_grouped objects+crosses
        // exactly (§17 rules 4/8): same first-appearance order (m0..m4), same union crosses.
        var spec = ConversionFixtures.MushroomTripleSpec();

        var (interleaved, interleavedDiags) = await RunAsync(spec, ConversionFixtures.MushroomTripleDataInterleaved, TripleOrdering.Unordered);
        var (grouped, groupedDiags) = await RunAsync(spec, ConversionFixtures.MushroomTripleData, TripleOrdering.SubjectGrouped);

        Assert.Empty(interleavedDiags);
        Assert.Empty(groupedDiags);
        Assert.Equal(["m0", "m1", "m2", "m3", "m4"], interleaved.Select(o => o.Name));
        Assert.Equal(grouped.Select(o => o.Name), interleaved.Select(o => o.Name));
        Assert.Equal(
            grouped.Select(o => o.CrossedFormalAttributeIds),
            interleaved.Select(o => o.CrossedFormalAttributeIds));
    }

    [Fact]
    public async Task Unordered_WhenSubjectFirstSeenViaUnboundPredicate_ThenItLeadsInFirstAppearance()
    {
        // A subject whose first row binds no attribute still forms its object at that first-appearance
        // position (§10.1 / §17 rule 4), ahead of a subject that appears later — the interleaved run
        // (Alpha recurs after Beta) would be TripleSubjectNotContiguous under subject_grouped.
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);

        var (objects, diagnostics) = await RunAsync(spec, "Alpha,unbound,v\nBeta,a,x\nAlpha,a,y", TripleOrdering.Unordered);

        Assert.Empty(diagnostics);
        Assert.Equal(["Alpha", "Beta"], objects.Select(o => o.Name));
        Assert.Equal([1], objects[0].CrossedFormalAttributeIds); // Alpha: a-y
        Assert.Equal([0], objects[1].CrossedFormalAttributeIds); // Beta: a-x
    }

    [Fact]
    public async Task Unordered_WhenPresentMissingInterleaved_ThenMissingPolicyApplies()
    {
        // Interleaved rows for one subject union; a present-missing value fires missing_policy on the
        // unordered path too (§10.5 / §5.3.1). s1's SQL (record 0) and ? (record 2) are non-contiguous.
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [ConversionFixtures.PredicateNominal("skill", "skill", ["SQL", "Python"], missing: MissingPolicy.AsAttribute)]);

        var (objects, diagnostics) = await RunAsync(spec, "s1,skill,SQL\ns2,skill,Python\ns1,skill,?", TripleOrdering.Unordered);

        Assert.Empty(diagnostics);
        Assert.Equal(["s1", "s2"], objects.Select(o => o.Name));
        Assert.Equal([0, 2], objects[0].CrossedFormalAttributeIds); // s1: skill-SQL AND skill-missing
        Assert.Equal([1], objects[1].CrossedFormalAttributeIds);    // s2: skill-Python
    }

    [Fact]
    public async Task Unordered_WhenSubjectInvalid_ThenObjectKeyValueInvalid()
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);

        var (_, diagnostics) = await RunAsync(spec, ",a,x", TripleOrdering.Unordered); // empty subject

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(0, diagnostic.Location?.RecordIndex);
    }

    [Fact]
    public async Task Unordered_WhenLaterRowFollowsInvalidSubject_ThenHaltsAtErrorAndDropsLaterRows()
    {
        // Codex halt-ordering: a structural subject error halts at that source record; a later row
        // must not be reordered ahead of it and must not influence output. Record 2 (Sam,job) must
        // not reach Sam before the invalid subject at record 1 halts the stream.
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [ConversionFixtures.PredicateNominal("job", "job", ["Clerical"])]);

        var (objects, diagnostics) = await RunAsync(spec, "Sam,age,39\n?,age,50\nSam,job,Clerical", TripleOrdering.Unordered);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, diagnostic.Code);
        Assert.Equal(1, diagnostic.Location?.RecordIndex); // halt at the invalid subject, record 1
        Assert.Empty(objects);                             // Sam never closes → record 2's job cross never leaks
    }

    [Fact]
    public async Task UnorderedTripleRowSource_WhenSubjectInvalidMidStream_ThenTruncatesAtErrorDroppingLaterRows()
    {
        // The decorator stops reading at the first unusable subject (inclusive); rows after it are
        // never yielded, so no later row can be reordered ahead of the structural error.
        var inner = ConversionFixtures.TripleSourceOver(
            "Sam,age,39\n?,age,50\nSam,job,Clerical", ConversionFixtures.Triple(TripleOrdering.Unordered));
        var decorated = new UnorderedTripleRowSource(inner, GroupingOptions.Default, new GroupingReports());

        var rows = new List<TripleRow>();
        await foreach (var row in decorated.ReadRowsAsync())
        {
            rows.Add(row);
        }

        Assert.Equal([0, 1], rows.Select(r => r.RecordIndex)); // record 2 (Sam,job) dropped
        Assert.Null(rows[^1].Subject);                         // the boundary row (? → null) ranks last
    }

    [Fact]
    public async Task Unordered_WhenRunTwice_ThenIdenticalObjectsAndCrosses()
    {
        var spec = ConversionFixtures.MushroomTripleSpec();

        var (first, _) = await RunAsync(spec, ConversionFixtures.MushroomTripleDataInterleaved, TripleOrdering.Unordered);
        var (second, _) = await RunAsync(spec, ConversionFixtures.MushroomTripleDataInterleaved, TripleOrdering.Unordered);

        Assert.Equal(first.Select(o => o.Name), second.Select(o => o.Name));
        Assert.Equal(
            first.Select(o => o.CrossedFormalAttributeIds),
            second.Select(o => o.CrossedFormalAttributeIds));
    }

    [Fact]
    public async Task CxtWriter_WhenUnorderedWrittenTwice_ThenIdenticalBytesInFirstAppearanceOrder()
    {
        // The .cxt two-pass calls the emit factory twice; the unordered grouping re-derives the same
        // first-appearance order each pass, so bytes are identical without a persistent spool (P-16).
        // Object names appear in first-appearance order (Sam before Alice/Tim), not sorted.
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [ConversionFixtures.PredicateNominal("role", "role", ["dev", "ops"])]);
        const string data =
            "Sam,role,dev\nJenny,role,ops\nAlice,role,dev\nTim,role,ops\n" +
            "Sam,role,ops\nJenny,role,dev\nAlice,role,ops\nTim,role,dev";

        var first = await WriteCxtUnorderedAsync(spec, data);
        var second = await WriteCxtUnorderedAsync(spec, data);

        Assert.Equal(first, second);
        var text = Encoding.UTF8.GetString(first);
        Assert.True(text.IndexOf("Sam", StringComparison.Ordinal) < text.IndexOf("Alice", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Sam", StringComparison.Ordinal) < text.IndexOf("Tim", StringComparison.Ordinal));
    }

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunAsync(
        BedrockSpec spec, string tripleData, TripleOrdering ordering)
    {
        // EmitTripleAsync owns ordering from plan.Execution (D-082), so the spec's binding must carry
        // the intended ordering; rebuild it here (all these fixtures use the default triple binding).
        var orderedSpec = new BedrockSpec(ConversionFixtures.Triple(ordering), spec.Attributes);
        var source = ConversionFixtures.TripleSourceOver(tripleData, ConversionFixtures.Triple(ordering));
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionPlanner.Plan(orderedSpec, schema).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (objects, diagnostics);
    }

    private static async Task<byte[]> WriteCxtUnorderedAsync(BedrockSpec spec, string data)
    {
        var source = ConversionFixtures.TripleSourceOver(data, ConversionFixtures.Triple(TripleOrdering.Unordered));
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionPlanner.Plan(spec, schema).TryGetValue(out var plan));

        using var stream = new MemoryStream();
        await CxtWriter.WriteAsync(
            plan,
            () => Emitter.EmitTripleAsync(plan, source, new List<BedrockDiagnostic>()),
            WriterOptions.Native,
            stream);
        return stream.ToArray();
    }
}
