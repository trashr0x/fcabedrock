using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The library text of <c>GroupingStorageFailed</c> (§16.4, D-082): what temporary storage was doing,
/// whether the operation stopped or continued, the cause the failure kind establishes, and, for a
/// stopping failure whose kind a different directory can address, a remedy that names no host option.
/// The stable <c>(operation, kind)</c> identity, count and path samples stay in the text and the
/// structured context.
/// </summary>
public sealed class GroupingStorageDiagnosticsTests
{
    public static TheoryData<string, string, DiagnosticSeverity, long, string[], string> Messages() => new()
    {
        {
            nameof(GroupingOperation.Workspace), nameof(SpoolFailureKind.WorkspaceCreation), DiagnosticSeverity.Error, 1, ["blocked"],
            "Temporary storage failed while creating its working directory, so the operation stopped: the directory " +
            "could not be created (Workspace/WorkspaceCreation, 1 occurrence, e.g. blocked; §16.4, D-082). " +
            "Choose an existing, writable directory for temporary storage."
        },
        {
            nameof(GroupingOperation.Spill), nameof(SpoolFailureKind.StorageExhausted), DiagnosticSeverity.Error, 2, ["r1", "r2"],
            "Temporary storage failed while writing sorted data, so the operation stopped: the storage was full " +
            "(Spill/StorageExhausted, 2 occurrences, e.g. r1, r2; §16.4, D-082). " +
            "Free space, or choose a directory with more free space for temporary storage."
        },
        {
            nameof(GroupingOperation.MergeWrite), nameof(SpoolFailureKind.AccessDenied), DiagnosticSeverity.Error, 1, ["m"],
            "Temporary storage failed while writing merged data, so the operation stopped: access was denied " +
            "(MergeWrite/AccessDenied, 1 occurrence, e.g. m; §16.4, D-082). " +
            "Choose an existing, writable directory for temporary storage."
        },
        {
            nameof(GroupingOperation.MergeRead), nameof(SpoolFailureKind.CorruptRun), DiagnosticSeverity.Error, 1, [],
            "Temporary storage failed while reading sorted data back, so the operation stopped: stored data was " +
            "inconsistent (MergeRead/CorruptRun, 1 occurrence; §16.4, D-082)."
        },
        {
            nameof(GroupingOperation.MergeRead), nameof(SpoolFailureKind.TruncatedRun), DiagnosticSeverity.Error, 3, ["t"],
            "Temporary storage failed while reading sorted data back, so the operation stopped: stored data ended " +
            "early (MergeRead/TruncatedRun, 3 occurrences, e.g. t; §16.4, D-082)."
        },
        {
            nameof(GroupingOperation.Spill), nameof(SpoolFailureKind.Other), DiagnosticSeverity.Error, 1, ["s"],
            "Temporary storage failed while writing sorted data, so the operation stopped: the cause is not known " +
            "(Spill/Other, 1 occurrence, e.g. s; §16.4, D-082)."
        },
        {
            nameof(GroupingOperation.CleanupDelete), nameof(SpoolFailureKind.DeleteFailed), DiagnosticSeverity.Error, 4, ["d"],
            "Temporary storage failed while deleting files it no longer needed, so the operation stopped: files " +
            "could not be deleted (CleanupDelete/DeleteFailed, 4 occurrences, e.g. d; §16.4, D-082)."
        },
        {
            nameof(GroupingOperation.CleanupDelete), nameof(SpoolFailureKind.DeleteFailed), DiagnosticSeverity.Warning, 2, ["d1", "d2"],
            "Temporary storage cleanup failed while deleting files it no longer needed: files could not be deleted; " +
            "the operation continued, but some temporary storage may remain " +
            "(CleanupDelete/DeleteFailed, 2 occurrences, e.g. d1, d2; §16.4, D-082)."
        },
        {
            nameof(GroupingOperation.CleanupClose), nameof(SpoolFailureKind.Other), DiagnosticSeverity.Warning, 1, ["c"],
            "Temporary storage cleanup failed while closing files it no longer needed: the cause is not known; " +
            "the operation continued, but some temporary storage may remain " +
            "(CleanupClose/Other, 1 occurrence, e.g. c; §16.4, D-082)."
        },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void Render_WhenAFailureIsAggregated_ThenTheMessageStatesTheActivityOutcomeCauseAndIdentity(
        string operation, string kind, DiagnosticSeverity severity, long count, string[] samples, string expected)
    {
        var failure = new GroupingStorageFailure(
            Enum.Parse<GroupingOperation>(operation), Enum.Parse<SpoolFailureKind>(kind), count, samples);

        var diagnostic = GroupingStorageDiagnostics.Render(failure, severity);

        Assert.Equal(DiagnosticCode.GroupingStorageFailed, diagnostic.Code);
        Assert.Equal(severity, diagnostic.Severity);
        Assert.Null(diagnostic.Location);
        Assert.Same(failure, diagnostic.Context);
        Assert.Equal(expected, diagnostic.Message);
    }

    [Fact]
    public async Task Emit_WhenTheTemporaryRootIsAnOrdinaryFile_ThenWorkspaceCreationStopsTheRunWithTheDirectoryRemedy()
    {
        // A real filesystem refusal, not an injected one: a spill must create its working directory
        // under the configured root, and an ordinary file there cannot hold one.
        var blocker = Path.Combine(Path.GetTempPath(), "fcab-blocked-root-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a directory");
        try
        {
            var options = new GroupingOptions(maxBufferedBytes: 1, tempDirectory: blocker);

            var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns0,a,y", options);

            Assert.Empty(objects);
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(
                "Temporary storage failed while creating its working directory, so the operation stopped: the directory " +
                $"could not be created (Workspace/WorkspaceCreation, 1 occurrence, e.g. {blocker}; §16.4, D-082). " +
                "Choose an existing, writable directory for temporary storage.",
                diagnostic.Message);
            Assert.Equal("not a directory", File.ReadAllText(blocker)); // the blocker itself is untouched
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public async Task Emit_WhenConsumedRunsCannotBeDeleted_ThenACleanupWarningSaysTheRunContinued()
    {
        var fs = new FakeSpoolFileSystem { OnDeleteRun = _ => StorageFaults.AccessDenied() };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y", options);

        Assert.Equal(["s0", "s1", "s2"], objects.Select(o => o.Name));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var failure = Assert.IsType<GroupingStorageFailure>(diagnostic.Context);
        Assert.Equal(
            "Temporary storage cleanup failed while deleting files it no longer needed: files could not be deleted; " +
            "the operation continued, but some temporary storage may remain " +
            $"(CleanupDelete/DeleteFailed, {Occurrences(failure.Count)}, e.g. {string.Join(", ", failure.PathSamples)}; " +
            "§16.4, D-082).",
            diagnostic.Message);
    }

    [Fact]
    public async Task Emit_WhenARunReaderCannotBeClosed_ThenACleanupWarningSaysTheRunContinued()
    {
        var fs = new FakeSpoolFileSystem { WrapReadStream = (_, stream) => new ThrowOnDisposeStream(stream) };
        var options = new GroupingOptions(maxBufferedBytes: 1, fileSystem: fs);

        var (objects, diagnostics) = await RunUnorderedAsync("s0,a,x\ns1,a,y\ns2,a,x\ns0,a,y", options);

        Assert.Equal(["s0", "s1", "s2"], objects.Select(o => o.Name));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var failure = Assert.IsType<GroupingStorageFailure>(diagnostic.Context);
        Assert.Equal(GroupingOperation.CleanupClose, failure.Operation);
        Assert.Equal(
            "Temporary storage cleanup failed while closing files it no longer needed: access was denied; " +
            "the operation continued, but some temporary storage may remain " +
            $"(CleanupClose/AccessDenied, {Occurrences(failure.Count)}, e.g. {string.Join(", ", failure.PathSamples)}; " +
            "§16.4, D-082).",
            diagnostic.Message);
    }

    private static string Occurrences(long count) => count == 1 ? "1 occurrence" : $"{count} occurrences";

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunUnorderedAsync(
        string data, GroupingOptions options)
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
            [ConversionFixtures.PredicateNominal("a", "a", ["x", "y"])]);
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
}
