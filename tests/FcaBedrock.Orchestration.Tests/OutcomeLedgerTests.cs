using FcaBedrock.Diagnostics;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// Who owns an outcome's lists (D-140): a continuing operation copies the run's prior list at
/// entry and returns the complete run list, every returned list is an immutable snapshot a caller
/// cannot change, and a prepared run's chain is a copy no later read can change. Completion is a
/// produced value, so an operation whose list holds an Error can still complete, and a diagnostic
/// failure is an ok result with no value (D-140 part 4).
/// </summary>
public sealed class OutcomeLedgerTests
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
            "statistics" => (await ContextStatistics.CountAsync(run.Run, given, CancellationToken.None)).Value.Diagnostics,
            "conversion planning" => ConvertPublication.PlanConversion(run.Run, v2Compatible: false, given).Diagnostics,
            _ => (await ConvertPublication.PublishAsync(
                run.Conversion, (PreflightedConvertOutput)run.Preflight(), run.Facts, given, run.Signals.Token)).Value.Diagnostics,
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
        var prepared = OutcomeAssert.Completed(await RunPreparation.PrepareAsync(
            new FileRunRequest { SpecPath = spec, DataPath = data }, new OrchestrationHost(File.OpenRead), CancellationToken.None));

        var frozen = OutcomeAssert.Completed(SpecFreezing.Freeze(prepared.Value, prepared.Diagnostics));

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

    [Fact]
    public async Task Validate_WhenResolveReportsAnError_ThenItCompletesWithItsValueAndTheError()
    {
        // A binding by header name without DATA: resolve reports SourceBindingInvalid, and validation
        // still completes, because it reached resolve. The completion carries the Error.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "colour"
            source = { kind = "column", name = "colour" }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["red", "green"]
            """);

        var outcome = await SpecValidation.ValidateAsync(
            spec, dataPath: null, new OrchestrationHost(File.OpenRead), CancellationToken.None);

        Assert.True(outcome.IsOk);
        var (validated, diagnostics) = OutcomeAssert.Completed(outcome);
        Assert.NotNull(validated.Composed);
        Assert.True(outcome.Value.HasErrors);
        Assert.Equal(DiagnosticCode.SourceBindingInvalid, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task Count_WhenEmissionReportsAnError_ThenItCompletesWithItsCountsAndTheError()
    {
        // unknown_value_policy = "fail" makes the undeclared `green` an Error at emit, and the counts
        // are still produced. The CLI prints the same bytes for this state as for a diagnostic
        // failure, so only a carrier-level test sees a completion misread.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["red", "blue"]
            unknown_value_policy = "fail"
            """);
        var data = temp.Write("data.csv", "colour,size\nred,1\ngreen,2\n");
        var prepared = OutcomeAssert.Completed(await RunPreparation.PrepareAsync(
            new FileRunRequest { SpecPath = spec, DataPath = data }, new OrchestrationHost(File.OpenRead), CancellationToken.None));

        var counted = await ContextStatistics.CountAsync(prepared.Value, prepared.Diagnostics, CancellationToken.None);

        Assert.True(counted.IsOk);
        var (counts, diagnostics) = OutcomeAssert.Completed(counted);
        Assert.Equal(2, counts.FormalAttributes);
        Assert.True(counted.Value.HasErrors);
        Assert.Equal(prepared.Diagnostics, diagnostics.Take(prepared.Diagnostics.Count));
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownValueObserved && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task PlanConversion_WhenThePriorListHoldsAnError_ThenTheNativeRouteCompletes()
    {
        // The native route plans nothing new: it continues the given list and completes even when
        // that list already holds an Error. No CLI path feeds it one, because a completed preparation
        // carries no Error and the stale-fingerprint additions are Warnings.
        using var run = await PublicationRun.CreateAsync(PublicationRun.Convert);
        var inherited = new BedrockDiagnostic(
            DiagnosticCode.UnknownValueObserved, DiagnosticSeverity.Error, "an error the host carried");
        IReadOnlyList<BedrockDiagnostic> given = [.. run.Diagnostics, inherited];

        var planned = ConvertPublication.PlanConversion(run.Run, v2Compatible: false, given);

        var (conversion, diagnostics) = OutcomeAssert.Completed(planned);
        Assert.Same(run.Run, conversion.Run);
        Assert.True(planned.HasErrors);
        Assert.Equal(given, diagnostics);
    }

    [Fact]
    public async Task Prepare_WhenTheSpecDoesNotParse_ThenTheDiagnosticFailureIsInsideAnOkResult()
    {
        // A diagnostic failure is not a host failure: the result is ok, with no value and the parse
        // diagnostics that explain why.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", "this is = = not toml\n");
        var data = temp.Write("data.csv", PublicationRun.Data);

        var outcome = await RunPreparation.PrepareAsync(
            new FileRunRequest { SpecPath = spec, DataPath = data }, new OrchestrationHost(File.OpenRead), CancellationToken.None);

        Assert.True(outcome.IsOk);
        var diagnostics = OutcomeAssert.Failed(outcome);
        Assert.Contains(
            diagnostics, diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
    }

    [Fact]
    public async Task HostFailure_WhenAContinuingOperationFailsOnTheHost_ThenItCarriesTheCompleteImmutableRunList()
    {
        // The stage write fails after the emit pass began, so the publish fails on the host. The
        // failure carries the run's whole list, the given one first, as an immutable snapshot that a
        // later change to the caller's list cannot reach.
        using var run = await PublicationRun.CreateAsync(PublicationRun.Convert);
        run.Files.FailStreamWritePrefix = Path.GetFileName(run.Target) + ".fcabedrock-stage-";
        List<BedrockDiagnostic> given = [.. run.Diagnostics, Sentinel];

        var failure = OutcomeAssert.HostFailed(await ConvertPublication.PublishAsync(
            run.Conversion, (PreflightedConvertOutput)run.Preflight(), run.Facts, given, run.Signals.Token));
        var returned = failure.Diagnostics.ToList();

        Assert.Equal(HostErrorKind.StageFailed, failure.Error.Kind);
        Assert.Equal(given, failure.Diagnostics.Take(given.Count));
        Assert.False(failure.Diagnostics is List<BedrockDiagnostic>);
        Assert.False(failure.Diagnostics is BedrockDiagnostic[]);
        var collection = Assert.IsAssignableFrom<ICollection<BedrockDiagnostic>>(failure.Diagnostics);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(Sentinel));

        given.Clear();
        given.Add(Sentinel);
        Assert.Equal(returned, failure.Diagnostics);
    }

    [Fact]
    public void HostFailure_WhenConstructedWithoutAnError_ThenItThrows()
    {
        var thrown = Assert.Throws<ArgumentNullException>(() => new HostFailure(null!, [Sentinel]));

        Assert.Equal("error", thrown.ParamName);
    }
}
