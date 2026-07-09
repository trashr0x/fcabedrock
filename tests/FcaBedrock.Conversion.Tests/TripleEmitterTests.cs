using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

public sealed class TripleEmitterTests
{
    [Fact]
    public async Task EmitTripleAsync_WhenSubjectGroupedMushroom_ThenCrossesMatchWideIncidence()
    {
        // The triple encoding of the mushroom context reproduces the wide incidence exactly
        // (the same formal attributes; §17 rule 8). Object names are the subjects, in
        // first-appearance order (§17 rule 4).
        var (objects, diagnostics) = await RunAsync(
            ConversionFixtures.MushroomTripleSpec(), ConversionFixtures.MushroomTripleData, ConversionFixtures.Triple());

        Assert.Empty(diagnostics);
        Assert.Equal(["m0", "m1", "m2", "m3", "m4"], objects.Select(o => o.Name));
        Assert.Equal([0, 1, 3, 5], objects[0].CrossedFormalAttributeIds); // XX.X.X..
        Assert.Equal([0, 2, 3, 7], objects[1].CrossedFormalAttributeIds); // X.XX...X
        Assert.Equal([2, 3, 5], objects[2].CrossedFormalAttributeIds);    // ..XX.X..
        Assert.Equal([0, 1, 3, 6], objects[3].CrossedFormalAttributeIds); // XX.X..X.
        Assert.Equal([2, 3, 5], objects[4].CrossedFormalAttributeIds);    // ..XX.X..
    }

    [Fact]
    public async Task EmitTripleAsync_WhenPredicateAbsentVsPresentMissing_ThenOnlyPresentMissingCrossesMissing()
    {
        // §10.5 / D-082: an ABSENT predicate is no observation (no cross, never -missing); a
        // PRESENT matching-predicate row with a missing value fires missing_policy. s2 has no "a"
        // row at all (absent) and an unbound predicate — it stays a legal no-cross object.
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"], missing: MissingPolicy.AsAttribute)]);

        var (objects, diagnostics) = await RunAsync(spec, "s1,a,x\ns2,z,foo\ns3,a,?", ConversionFixtures.Triple());

        Assert.Empty(diagnostics);
        Assert.Equal(["s1", "s2", "s3"], objects.Select(o => o.Name));
        Assert.Equal([0], objects[0].CrossedFormalAttributeIds);       // a-x
        Assert.Empty(objects[1].CrossedFormalAttributeIds);            // absent "a" → no cross, no a-missing
        Assert.Equal([2], objects[2].CrossedFormalAttributeIds);       // a-missing (present-missing)
    }

    [Fact]
    public async Task EmitTripleAsync_WhenPredicateUnbound_ThenSubjectIsKeptWithNoCrosses()
    {
        // A subject whose only rows carry unbound predicates still forms its object (§10.1).
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);

        var (objects, diagnostics) = await RunAsync(spec, "lonely,unbound,v", ConversionFixtures.Triple());

        Assert.Empty(diagnostics);
        Assert.Empty(Assert.Single(objects).CrossedFormalAttributeIds);
        Assert.Equal("lonely", objects[0].Name);
    }

    [Fact]
    public async Task EmitTripleAsync_WhenPredicateArrivalOrderVaries_ThenCrossesIdentical()
    {
        // §17 rules 1–3, 8: predicate arrival order never changes the crosses or their order —
        // formal-attribute order is spec-driven and the object's crosses are a union.
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
        [
            ConversionFixtures.PredicateNominal("a", "a", ["x", "y"]),
            ConversionFixtures.PredicateNominal("b", "b", ["p", "q"]),
        ]);

        var (forward, _) = await RunAsync(spec, "s,a,x\ns,b,p", ConversionFixtures.Triple());
        var (reversed, _) = await RunAsync(spec, "s,b,p\ns,a,x", ConversionFixtures.Triple());

        Assert.Equal([0, 2], Assert.Single(forward).CrossedFormalAttributeIds);  // a-x, b-p
        Assert.Equal(
            Assert.Single(forward).CrossedFormalAttributeIds,
            Assert.Single(reversed).CrossedFormalAttributeIds);
    }

    [Fact]
    public async Task EmitTripleAsync_WhenMultiValued_ThenCrossesUnion()
    {
        // §5.3.1 / §17 rule 8: several values for one subject+predicate union their crosses.
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);

        var (objects, diagnostics) = await RunAsync(spec, "s,a,x\ns,a,y", ConversionFixtures.Triple());

        Assert.Empty(diagnostics);
        Assert.Equal([0, 1], Assert.Single(objects).CrossedFormalAttributeIds); // a-x AND a-y
    }

    [Fact]
    public async Task EmitTripleAsync_WhenPresentAndMissingSameAttribute_ThenCrossesValueAndMissing()
    {
        // Operator/Codex point 3: a concrete value row and a present-missing row for the same
        // subject+predicate (under as_attribute) union to both the value attribute and -missing.
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("skill", "skill", ["SQL", "Python"], missing: MissingPolicy.AsAttribute)]);

        var (objects, diagnostics) = await RunAsync(spec, "Sam,skill,SQL\nSam,skill,?", ConversionFixtures.Triple());

        Assert.Empty(diagnostics);
        Assert.Equal([0, 2], Assert.Single(objects).CrossedFormalAttributeIds); // skill-SQL AND skill-missing
    }

    [Fact]
    public async Task EmitTripleAsync_WhenSubjectsInterleaved_ThenTripleSubjectNotContiguous()
    {
        // §5.3 / D-082: subject_grouped requires contiguity; s1 recurs after s2 intervenes.
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);

        var (_, diagnostics) = await RunAsync(spec, "s1,a,x\ns2,a,y\ns1,b,z", ConversionFixtures.Triple());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.TripleSubjectNotContiguous, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Location?.RecordIndex); // §5.3.1: the offending record (s1 recurs at row 2)
    }

    [Theory]
    [InlineData("?,a,x")]         // subject equals missing_token → no object identity
    [InlineData(",a,x")]          // empty subject
    [InlineData("\" \",a,x")]     // quoted whitespace-only subject
    [InlineData("\"a\nb\",a,x")]  // subject bears a newline (would corrupt the .cxt)
    public async Task EmitTripleAsync_WhenSubjectInvalid_ThenObjectKeyValueInvalid(string data)
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);

        var (_, diagnostics) = await RunAsync(spec, data, ConversionFixtures.Triple());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(0, diagnostic.Location?.RecordIndex); // the invalid subject is the first record
    }

    [Fact]
    public async Task EmitTripleAsync_WhenRunTwice_ThenProducesIdenticalCrosses()
    {
        var spec = ConversionFixtures.MushroomTripleSpec();

        var (first, _) = await RunAsync(spec, ConversionFixtures.MushroomTripleData, ConversionFixtures.Triple());
        var (second, _) = await RunAsync(spec, ConversionFixtures.MushroomTripleData, ConversionFixtures.Triple());

        Assert.Equal(first.Select(o => o.Name), second.Select(o => o.Name));
        Assert.Equal(
            first.Select(o => o.CrossedFormalAttributeIds),
            second.Select(o => o.CrossedFormalAttributeIds));
    }

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunAsync(
        BedrockSpec spec, string tripleData, Binding binding)
    {
        var source = ConversionFixtures.TripleSourceOver(tripleData, binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionPlanner.Plan(spec, schema).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (objects, diagnostics);
    }
}
