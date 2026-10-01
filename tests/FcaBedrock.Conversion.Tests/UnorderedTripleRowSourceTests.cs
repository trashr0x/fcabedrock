using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using static FcaBedrock.Conversion.Tests.EmitDrain;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The unordered triple source's truncation at the first unusable subject, driven through the real
/// emitter. The truncation is a successful shortened enumeration: it selects no result (the emitter
/// halts on the offending row it now holds), so a failure to close the upstream after it fails the
/// operation; a read failure already in flight is kept over a close failure. The upstream is closed
/// exactly once.
/// </summary>
public sealed class UnorderedTripleRowSourceTests
{
    internal const string TripleOk = "s1,p,v\ns2,p,w\n";
    internal const string TripleBadSubject = "s1,p,v\n,p,w\ns3,p,v\n";

    internal static async Task<Func<UpstreamFault, string, IReadOnlyList<TripleRow>?, GroupingOptions, Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>>>> UnorderedEmitAsync()
    {
        var binding = ConversionFixtures.Triple(TripleOrdering.Unordered);
        var spec = new BedrockSpec(binding, [ConversionFixtures.PredicateNominal("a", "p", ["v", "w"])]);
        var schema = await ConversionFixtures.TripleSourceOver(TripleOk, binding).GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return (fault, data, listed, options) => diagnostics => Emitter.EmitTripleAsync(
            plan, new FaultyTripleSource(ConversionFixtures.TripleSourceOver(data, binding), fault, listed), diagnostics, options);
    }

    [Fact]
    public async Task ReadRows_WhenTheTruncationAtAnUnusableSubjectHasACleanClose_ThenTheEmitterHaltsOnIt()
    {
        var emit = await UnorderedEmitAsync();
        var fault = new UpstreamFault();

        var (objects, diagnostics, error) = await RunAsync(emit(fault, TripleBadSubject, null, GroupingOptions.Default));

        Assert.Equal(0, objects);
        Assert.Equal(["ObjectKeyValueInvalid"], ErrorCodes(diagnostics));
        Assert.Null(error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task ReadRows_WhenTheTruncationIsFollowedByASoleCloseFailure_ThenTheCloseFailureFailsTheOperation()
    {
        var emit = await UnorderedEmitAsync();
        var close = UpstreamFault.CloseFailure();
        var fault = new UpstreamFault { DisposeFailure = close };

        var (objects, diagnostics, error) = await RunAsync(emit(fault, TripleBadSubject, null, GroupingOptions.Default));

        Assert.Equal(0, objects);
        Assert.Empty(ErrorCodes(diagnostics));
        Assert.Same(close, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task ReadRows_WhenAReadFailsAndTheCloseFails_ThenTheReadFailureIsKept()
    {
        var emit = await UnorderedEmitAsync();
        var read = UpstreamFault.ReadFailure();
        var fault = new UpstreamFault { ThrowOnMove = 1, MoveFailure = read, DisposeFailure = UpstreamFault.CloseFailure() };

        var (objects, _, error) = await RunAsync(emit(
            fault, TripleOk, [new TripleRow(0, "s1", "p", "v"), new TripleRow(1, "s2", "p", "w")], GroupingOptions.Default));

        Assert.Equal(0, objects);
        Assert.Same(read, error);
        Assert.Equal(1, fault.DisposeCalls);
    }
}
