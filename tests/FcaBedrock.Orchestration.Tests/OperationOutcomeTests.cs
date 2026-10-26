using FcaBedrock.Diagnostics;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// Who owns an outcome's lists (D-140): a continuing operation copies the run's prior list at
/// entry and returns the complete run list, every returned list is an immutable snapshot a caller
/// cannot change, and a prepared run's chain is a copy no later read can change.
/// </summary>
public sealed class OperationOutcomeTests
{
    private static readonly BedrockDiagnostic Sentinel = new(
        DiagnosticCode.UnknownValueObserved, DiagnosticSeverity.Warning, "a warning the host added");

    [Fact]
    public async Task Outcome_WhenTheCallerChangesItsListAfterTheCall_ThenTheReturnedListIsUnchanged()
    {
        using var run = await PublicationRun.CreateAsync(PublicationRun.Convert);
        List<BedrockDiagnostic> diagnostics = [.. run.Diagnostics, Sentinel];

        var planned = ConvertPublication.PlanConversion(run.Run, v2Compatible: false, diagnostics);
        var returned = planned.Diagnostics.ToList();

        diagnostics.Clear();
        diagnostics.Add(Sentinel);
        diagnostics.Add(Sentinel);

        Assert.Equal(returned, planned.Diagnostics);
        Assert.Equal([.. run.Diagnostics, Sentinel], planned.Diagnostics);
    }

    [Fact]
    public async Task Outcome_ShouldExposeDiagnosticsThatCannotBeChangedThroughACast()
    {
        using var run = await PublicationRun.CreateAsync(PublicationRun.Convert);

        var planned = ConvertPublication.PlanConversion(run.Run, v2Compatible: false, [Sentinel]);

        Assert.False(planned.Diagnostics is List<BedrockDiagnostic>);
        Assert.False(planned.Diagnostics is BedrockDiagnostic[]);
        var collection = Assert.IsAssignableFrom<ICollection<BedrockDiagnostic>>(planned.Diagnostics);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(Sentinel));
    }

    [Theory]
    [InlineData("statistics")]
    [InlineData("conversion planning")]
    [InlineData("publication")]
    public async Task ContinuingOperation_WhenGivenTheRunList_ThenItReturnsTheCompleteRunList(string operation)
    {
        // The run's list in, the run's list out: what the host passed comes first, in order, and the
        // operation's own diagnostics follow it, so its validity decisions see the whole run.
        using var run = await PublicationRun.CreateAsync(PublicationRun.Convert);
        IReadOnlyList<BedrockDiagnostic> given = [.. run.Diagnostics, Sentinel];

        IReadOnlyList<BedrockDiagnostic> returned = operation switch
        {
            "statistics" => (await ContextStatistics.CountAsync(run.Run, given, CancellationToken.None)).Diagnostics,
            "conversion planning" => ConvertPublication.PlanConversion(run.Run, v2Compatible: false, given).Diagnostics,
            _ => (await ConvertPublication.PublishAsync(
                run.Conversion, (PreflightedConvertOutput)run.Preflight(), run.Facts, given, run.Signals.Token)).Diagnostics,
        };

        Assert.Equal(given, returned.Take(given.Count));
    }

    [Fact]
    public async Task Freeze_WhenGivenThePreparationList_ThenTheStaleWarningsFollowItAndPrecedeThePostFreezeDiagnostics()
    {
        // A stored fingerprint that no longer matches warns once, after the preparation's own list;
        // the freeze then creates a shadowed-matcher warning of its own, which comes after both.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1
            schema_fingerprint = "sha256:stale"

            [binding]
            shape = "wide"
            has_header = true

            [[template]]
            id = "cuts"
            discretizer = { kind = "equal_width", bins = 2, range = "min_max" }

            [[matcher]]
            match = { name_regex = "age" }
            template = "cuts"

            [[attribute]]
            name = "age"
            source = { kind = "column", index = 1, value_type = "number" }
            scale = { kind = "nominal" }
            """);
        var data = temp.Write("data.csv", "colour,age\nred,10\ngreen,20\nblue,30\n");
        var prepared = Assert.IsType<OperationCompleted<PreparedFileRun>>(await RunPreparation.PrepareAsync(
            new FileRunRequest { SpecPath = spec, DataPath = data }, new OrchestrationHost(File.OpenRead), CancellationToken.None));

        var frozen = Assert.IsType<OperationCompleted<Spec.Toml.SpecDocument>>(
            SpecFreezing.Freeze(prepared.Value, prepared.Diagnostics));

        var codes = frozen.Diagnostics.Select(diagnostic => diagnostic.Code).ToList();
        Assert.Equal(prepared.Diagnostics, frozen.Diagnostics.Take(prepared.Diagnostics.Count));
        var stale = codes.IndexOf(DiagnosticCode.SchemaFingerprintStale);
        var shadowed = codes.IndexOf(DiagnosticCode.MatcherFullyShadowed);
        Assert.True(stale >= prepared.Diagnostics.Count, "the stale warning precedes the preparation's list");
        Assert.True(shadowed > stale, "the post-freeze warning precedes the stale warning");
        Assert.Single(codes, code => code == DiagnosticCode.MatcherFullyShadowed);
    }

    [Fact]
    public async Task PreparedRun_ShouldExposeAChainThatLaterReadsCannotChange()
    {
        using var run = await PublicationRun.CreateAsync(PublicationRun.Convert);

        var chain = run.Run.SpecChain;

        Assert.Single(chain);
        Assert.False(chain is List<SpecChainFile>);
        var collection = Assert.IsAssignableFrom<ICollection<SpecChainFile>>(chain);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(chain[0]));
        Assert.Same(chain, run.Run.SpecChain);
    }
}
