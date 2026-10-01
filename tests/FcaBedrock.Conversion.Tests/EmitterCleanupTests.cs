using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using static FcaBedrock.Conversion.Tests.EmitDrain;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// How the emitter closes its upstream (D-041). A result the emitter has selected (a halting
/// diagnostic, a storage failure, or an exception already in flight) is never replaced by a failure
/// to close the upstream. Without one, a close failure is the operation's failure: after the
/// upstream reported its end, after a truncation that selects nothing, and when the emitter is
/// itself disposed while suspended at a yield. Every scenario closes the upstream exactly once.
/// </summary>
public sealed class EmitterCleanupTests
{
    private const string TripleOk = "s1,p,v\ns2,p,w\n";
    private const string TripleBadSubject = "s1,p,v\n,p,w\ns3,p,v\n"; // the second row's subject is empty
    private const string WideOk = "k1,x\nk2,y\nk3,x\n";
    private const string WideBadKey = "k1,x\n,y\nk3,x\n"; // the second record's key is empty
    private const string WideDuplicateKey = "k1,x\nk1,y\nk3,x\n";

    private static async Task<Func<UpstreamFault, string, IReadOnlyList<TripleRow>?, Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>>>> TripleEmitAsync(
        TripleOrdering ordering, GroupingOptions? options = null)
    {
        var binding = ConversionFixtures.Triple(ordering);
        var spec = new BedrockSpec(binding, [ConversionFixtures.PredicateNominal("a", "p", ["v", "w"])]);
        var schema = await ConversionFixtures.TripleSourceOver(TripleOk, binding).GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return (fault, data, listed) => diagnostics => Emitter.EmitTripleAsync(
            plan, new FaultyTripleSource(ConversionFixtures.TripleSourceOver(data, binding), fault, listed), diagnostics,
            options ?? GroupingOptions.Default);
    }

    private static async Task<Func<UpstreamFault, string, IReadOnlyList<ObjectRecord>?, Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>>>> WideEmitAsync(
        DuplicateObjectPolicy policy, GroupingOptions? options = null)
    {
        var binding = ConversionFixtures.WideWithKey(0, policy);
        var spec = new BedrockSpec(binding, [ConversionFixtures.Nominal("a", 1, "x", "y")]);
        var schema = await ConversionFixtures.SourceOver(WideOk, binding).GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return (fault, data, listed) => diagnostics => Emitter.EmitAsync(
            plan, new FaultyRecordSource(ConversionFixtures.SourceOver(data, binding), fault, listed), diagnostics,
            options ?? GroupingOptions.Default);
    }

    // ---- EmitTripleAsync, subject_grouped ---------------------------------------------------

    [Fact]
    public async Task EmitTriple_WhenAnInvalidSubjectHaltsAndTheCloseFails_ThenTheHaltIsKept()
    {
        var emit = await TripleEmitAsync(TripleOrdering.SubjectGrouped);
        var fault = new UpstreamFault { DisposeFailure = UpstreamFault.CloseFailure() };

        var (objects, diagnostics, error) = await RunAsync(emit(fault, TripleBadSubject, null));

        Assert.Equal(0, objects);
        Assert.Equal(["ObjectKeyValueInvalid"], ErrorCodes(diagnostics));
        Assert.Null(error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmitTriple_WhenAbandonedWhileSuspendedAtItsYield_ThenTheUpstreamIsClosedOnceAndACloseFailurePropagates(bool closeFails)
    {
        var emit = await TripleEmitAsync(TripleOrdering.SubjectGrouped);
        var close = UpstreamFault.CloseFailure();
        var fault = new UpstreamFault { DisposeFailure = closeFails ? close : null };

        var (objects, _, error) = await RunAsync(emit(fault, TripleOk + "s3,p,v\n", null), abandonAfter: 1);

        Assert.Equal(1, objects);
        Assert.Equal(closeFails ? close : null, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task EmitTriple_WhenAReadFailsMidStreamAndTheCloseFails_ThenTheReadFailureIsKept()
    {
        var emit = await TripleEmitAsync(TripleOrdering.SubjectGrouped);
        var read = UpstreamFault.ReadFailure();
        var fault = new UpstreamFault { ThrowOnMove = 2, MoveFailure = read, DisposeFailure = UpstreamFault.CloseFailure() };

        var (objects, _, error) = await RunAsync(emit(fault, TripleOk, [new TripleRow(0, "s1", "p", "v"), new TripleRow(1, "s2", "p", "w")]));

        Assert.Equal(1, objects);
        Assert.Same(read, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task EmitTriple_WhenAGenericUpstreamEndsAndOnlyItsCloseFails_ThenTheCloseFailureFailsTheStream()
    {
        var emit = await TripleEmitAsync(TripleOrdering.SubjectGrouped);
        var close = UpstreamFault.CloseFailure();
        var fault = new UpstreamFault { DisposeFailure = close };

        var (objects, _, error) = await RunAsync(emit(fault, TripleOk, [new TripleRow(0, "s1", "p", "v")]));

        Assert.Equal(0, objects);
        Assert.Same(close, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task EmitTriple_WhenDrainedNormally_ThenEveryObjectIsEmittedAndTheUpstreamClosedOnce()
    {
        var emit = await TripleEmitAsync(TripleOrdering.SubjectGrouped);
        var fault = new UpstreamFault();

        var (objects, diagnostics, error) = await RunAsync(emit(fault, TripleOk, null));

        Assert.Equal(2, objects);
        Assert.Empty(ErrorCodes(diagnostics));
        Assert.Null(error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    // ---- the wide streaming emission (column key, duplicate_object_policy = "fail") ----------

    [Theory]
    [InlineData(WideBadKey, "ObjectKeyValueInvalid")]
    [InlineData(WideDuplicateKey, "DuplicateObjectKey")]
    public async Task EmitWide_WhenAKeyHaltsTheStreamAndTheCloseFails_ThenTheHaltIsKept(string data, string code)
    {
        var emit = await WideEmitAsync(DuplicateObjectPolicy.Fail);
        var fault = new UpstreamFault { DisposeFailure = UpstreamFault.CloseFailure() };

        var (objects, diagnostics, error) = await RunAsync(emit(fault, data, null));

        Assert.Equal(1, objects);
        Assert.Equal([code], ErrorCodes(diagnostics));
        Assert.Null(error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task EmitWide_WhenAbandonedWhileSuspendedAtItsYieldAndTheCloseFails_ThenTheCloseFailurePropagates()
    {
        var emit = await WideEmitAsync(DuplicateObjectPolicy.Fail);
        var close = UpstreamFault.CloseFailure();
        var fault = new UpstreamFault { DisposeFailure = close };

        var (objects, _, error) = await RunAsync(emit(fault, WideOk, null), abandonAfter: 1);

        Assert.Equal(1, objects);
        Assert.Same(close, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task EmitWide_WhenAReadFailsAndTheCloseFails_ThenTheReadFailureIsKept()
    {
        var emit = await WideEmitAsync(DuplicateObjectPolicy.Fail);
        var read = UpstreamFault.ReadFailure();
        var fault = new UpstreamFault { ThrowOnMove = 1, MoveFailure = read, DisposeFailure = UpstreamFault.CloseFailure() };

        var (objects, _, error) = await RunAsync(emit(fault, WideOk, [new ObjectRecord("0", ["k1", "x"]), new ObjectRecord("1", ["k2", "y"])]));

        Assert.Equal(1, objects);
        Assert.Same(read, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task EmitWide_WhenAKeyHaltsTheStreamWithACleanClose_ThenOnlyTheHaltIsReported()
    {
        var emit = await WideEmitAsync(DuplicateObjectPolicy.Fail);
        var fault = new UpstreamFault();

        var (objects, diagnostics, error) = await RunAsync(emit(fault, WideBadKey, null));

        Assert.Equal(1, objects);
        Assert.Equal(["ObjectKeyValueInvalid"], ErrorCodes(diagnostics));
        Assert.Null(error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    // ---- the dedupe keyed prefix (a successful shortened enumeration) ------------------------

    [Fact]
    public async Task EmitDedupe_WhenThePrefixTruncatesAtAnUnusableKeyWithACleanClose_ThenTheEmitterHaltsOnIt()
    {
        var emit = await WideEmitAsync(DuplicateObjectPolicy.Dedupe);
        var fault = new UpstreamFault();

        var (objects, diagnostics, error) = await RunAsync(emit(fault, WideBadKey, null));

        Assert.Equal(0, objects);
        Assert.Equal(["ObjectKeyValueInvalid"], ErrorCodes(diagnostics));
        Assert.Null(error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task EmitDedupe_WhenThePrefixTruncatesAndOnlyTheCloseFails_ThenTheCloseFailureFailsTheOperation()
    {
        var emit = await WideEmitAsync(DuplicateObjectPolicy.Dedupe);
        var close = UpstreamFault.CloseFailure();
        var fault = new UpstreamFault { DisposeFailure = close };

        var (objects, diagnostics, error) = await RunAsync(emit(fault, WideBadKey, null));

        Assert.Equal(0, objects);
        Assert.Empty(ErrorCodes(diagnostics));
        Assert.Same(close, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task EmitDedupe_WhenAReadFailsAndTheCloseFails_ThenTheReadFailureIsKept()
    {
        var emit = await WideEmitAsync(DuplicateObjectPolicy.Dedupe);
        var read = UpstreamFault.ReadFailure();
        var fault = new UpstreamFault { ThrowOnMove = 1, MoveFailure = read, DisposeFailure = UpstreamFault.CloseFailure() };

        var (objects, _, error) = await RunAsync(emit(fault, WideOk, [new ObjectRecord("0", ["k1", "x"]), new ObjectRecord("1", ["k2", "y"])]));

        Assert.Equal(0, objects);
        Assert.Same(read, error);
        Assert.Equal(1, fault.DisposeCalls);
    }
}
