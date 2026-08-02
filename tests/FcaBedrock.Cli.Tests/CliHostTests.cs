namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The argv boundary: the process-exit contract (D-122 part 2), stdout/stderr ownership,
/// and the interceptions. Every case drives <see cref="CliHost.RunAsync"/> exactly as the
/// real entry point does.
/// </summary>
public sealed class CliHostTests
{
    [Fact]
    public async Task RunAsync_WhenHelpIsRequested_ThenHelpGoesToStdoutAndExitsZero()
    {
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("--help");

        Assert.Equal(0, exit);
        Assert.Equal(UsageText.Help, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenVersionIsRequested_ThenTheInjectedVersionGoesToStdoutAndExitsZero()
    {
        var harness = new CliTestHarness { ToolVersion = "fcabedrock-vnext 4.5.6" };

        var exit = await harness.RunAsync("--version");

        Assert.Equal(0, exit);
        Assert.Equal("fcabedrock-vnext 4.5.6\n", harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenInvokedBare_ThenUsageGoesToStderrAndExitsTwo()
    {
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync();

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal("error: no command given.\n" + UsageText.General, harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenTheCommandIsUnknown_ThenTheGeneralUsageGoesToStderrAndExitsTwo()
    {
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("frobnicate", "x");

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal("error: unknown command 'frobnicate'.\n" + UsageText.General, harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenACommandsGrammarIsViolated_ThenThatCommandsUsageGoesToStderrAndExitsTwo()
    {
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate");

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            "error: command 'validate' requires the SPEC operand.\n" + UsageText.For("validate"), harness.StdErr);
    }

    [Theory]
    [InlineData("--sample")]
    [InlineData("--gzip")]
    [InlineData("--color")]
    [InlineData("--progress")]
    [InlineData("--machine")]
    public async Task RunAsync_WhenAnExcludedFlagIsSupplied_ThenExitTwo(string flag)
    {
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("convert", "s.toml", "d.csv", "--out", "b", "--format", "cxt", flag);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.StartsWith($"error: unknown option '{flag}'", harness.StdErr, StringComparison.Ordinal);
    }

    // The commands whose grammar is settled but whose behaviour is still to come. `plan`,
    // `stats`, `fingerprint`, `convert`, and now `probe` and `migrate` left this list when their
    // handlers landed; `calibrate` leaves it with its own slice.
    public static TheoryData<string[], string> LaterSliceCommands() => new()
    {
        { ["calibrate", "s.toml", "d.csv", "--out", "o.toml"], "calibrate" },
    };

    [Theory]
    [MemberData(nameof(LaterSliceCommands))]
    public async Task RunAsync_WhenALaterSliceCommandIsInvoked_ThenItParsesAndReportsWithoutDoingAnyWork(
        string[] argv, string command)
    {
        // The grammar is settled and accepted; the behaviour lands in a later slice. The
        // operands above name files that do not exist, and the run must not try to touch
        // them — so nothing is opened and the failure is the honest "not implemented",
        // not a missing-file error.
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(argv);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal($"error: the '{command}' command is not implemented yet.\n", harness.StdErr);
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task RunAsync_WhenACommandRuns_ThenProgressIsObservedAroundIt()
    {
        var harness = new CliTestHarness();

        await harness.RunAsync("plan", "s.toml", "d.csv");

        Assert.Equal(["started:plan", "completed:plan:1"], harness.Progress.Events);
    }

    [Fact]
    public async Task RunAsync_WhenParsingFails_ThenNoCommandProgressIsObserved()
    {
        var harness = new CliTestHarness();

        await harness.RunAsync("plan");

        Assert.Empty(harness.Progress.Events);
    }

    [Fact]
    public async Task RunAsync_WhenAnUnexpectedFaultOccurs_ThenExactlyOneSanitizedErrorLineAndExitFour()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);

        // An input opener that fails in a way no expected-input filter absorbs: the CLI
        // must not translate a programmer-level fault into an ordinary input error.
        var harness = new CliTestHarness
        {
            OpenInput = _ => throw new InvalidOperationException(
                "internal detail C:\\secret\\path with a stack-worthy cause"),
        };

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(4, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal($"error: {CliHost.UnexpectedFaultMessage}\n", harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenAnUnexpectedFaultOccurs_ThenNothingAboutTheExceptionLeaks()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var harness = new CliTestHarness
        {
            OpenInput = _ => throw new InvalidOperationException("C:\\secret\\path"),
        };

        await harness.RunAsync("validate", spec);

        Assert.DoesNotContain("InvalidOperationException", harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("at FcaBedrock", harness.StdErr, StringComparison.Ordinal);
        Assert.Single(harness.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task RunAsync_WhenCancellationIsAlreadyRequested_ThenExitThreeWithNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);

        var harness = new CliTestHarness();
        harness.Signals.Cancel();

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationArrivesMidRun_ThenExitThreeWithNoOutputAtAll()
    {
        // The signal lands after the command has started — while the spec is being read —
        // so it is observed by the source seam during schema acquisition, not by the
        // host's command-boundary check, and the run still ends at exit 3 in silence.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);

        CliTestHarness? harness = null;
        harness = new CliTestHarness
        {
            OpenInput = path =>
            {
                if (string.Equals(path, spec, StringComparison.Ordinal))
                {
                    harness!.Signals.Cancel();
                }

                return File.OpenRead(path);
            },
        };

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);

        // The command really had begun: the spec was read and composed before the token
        // was observed.
        Assert.Contains(spec, harness.Opened);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationArrivesMidRunWithNoData_ThenExitThreeWithNoOutputAtAll()
    {
        // The no-DATA vertical has no asynchronous stage to observe the token for it, so the
        // phase boundaries must. Without them the first signal is suppressed and then
        // ignored, and a cancelled run reports success.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);

        CliTestHarness? harness = null;
        harness = new CliTestHarness
        {
            OpenInput = path =>
            {
                harness!.Signals.Cancel();
                return File.OpenRead(path);
            },
        };

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal([spec], harness.Opened);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationArrivesBeforeRenderingDiagnostics_ThenNothingIsRendered()
    {
        // A run that WOULD have reported problems still reports nothing once cancelled: the
        // last boundary sits before the write, not after it.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.ErrorSpec);

        CliTestHarness? harness = null;
        harness = new CliTestHarness
        {
            OpenInput = path =>
            {
                harness!.Signals.Cancel();
                return File.OpenRead(path);
            },
        };

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenAnUnrelatedCancellationRacesTheHostToken_ThenItIsNotReportedAsUserCancellation()
    {
        // Exit 3 asserts one specific outcome: cooperative cleanup after the user's signal.
        // A different operation's cancellation, merely concurrent with a real signal, is a
        // defect and must not be silently relabelled as a cancelled run.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        using var unrelated = new CancellationTokenSource();
        await unrelated.CancelAsync();

        CliTestHarness? harness = null;
        harness = new CliTestHarness
        {
            OpenInput = _ =>
            {
                harness!.Signals.Cancel();
                throw new OperationCanceledException(unrelated.Token);
            },
        };

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(4, exit);
        Assert.Equal($"error: {CliHost.UnexpectedFaultMessage}\n", harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenTheCancellationCarriesTheHostToken_ThenItIsSilentExitThree()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);

        CliTestHarness? harness = null;
        harness = new CliTestHarness
        {
            OpenInput = _ =>
            {
                harness!.Signals.Cancel();
                throw new OperationCanceledException(harness.Signals.Token);
            },
        };

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public async Task RunAsync_WhenStandardOutputFails_ThenItIsACodeLessHostErrorAndExitOne(string option)
    {
        // A broken pipe is an environment failure, not an internal bug: it keeps exit 1 and
        // reports through the channel that still works.
        var harness = new CliTestHarness { OutOverride = new ThrowingWriter(failOnWrite: true, failOnFlush: false) };

        var exit = await harness.RunAsync(option);

        Assert.Equal(1, exit);
        Assert.Equal($"error: {CliHost.OutputFailureMessage}\n", harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenStandardErrorAlsoFails_ThenNothingEscapesAndTheExitCodeStands()
    {
        var harness = new CliTestHarness
        {
            OutOverride = new ThrowingWriter(failOnWrite: true, failOnFlush: false),
            ErrorOverride = new ThrowingWriter(failOnWrite: true, failOnFlush: false),
        };

        var exit = await harness.RunAsync("--help");

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_WhenStandardErrorFailsWritingUsage_ThenExitOneRatherThanTwo()
    {
        // The user received no usage text at all, so this is a host output failure — not an
        // invalid invocation the user could act on.
        var harness = new CliTestHarness { ErrorOverride = new ThrowingWriter(failOnWrite: true, failOnFlush: false) };

        var exit = await harness.RunAsync();

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
    }

    [Fact]
    public async Task RunAsync_WhenStandardErrorFailsWritingDiagnostics_ThenExitOneRatherThanZero()
    {
        // Warning-only validation would otherwise report success while its warnings were
        // lost on the way to the user.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.WarningOnlySpec);
        var harness = new CliTestHarness { ErrorOverride = new ThrowingWriter(failOnWrite: true, failOnFlush: false) };

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Complete_WhenTheDeferredStandardErrorFlushFails_ThenSuccessAndUsageBecomeTheOutputFailureExit(int chosen)
    {
        // stdout is fine; only the buffered stderr never reaches the user.
        var error = new ThrowingWriter(failOnWrite: false, failOnFlush: true);

        Assert.Equal(1, CliHost.Complete(chosen, new StringWriter(), error));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Complete_WhenTheDeferredStandardErrorFlushFails_ThenAMoreSpecificOutcomeStands(int chosen)
    {
        // 1 is already the output-failure exit; a cancelled run owed stderr nothing, and an
        // internal fault is the more specific reason the run failed.
        var error = new ThrowingWriter(failOnWrite: false, failOnFlush: true);

        Assert.Equal(chosen, CliHost.Complete(chosen, new StringWriter(), error));
    }

    [Fact]
    public void Complete_WhenTheDeferredFlushFails_ThenTheOutputFailureExitReplacesTheChosenCode()
    {
        // Redirected output is buffered, so this is where a closed pipe usually surfaces —
        // after the host already chose 0. It must not escape, and it must not stay 0.
        var output = new ThrowingWriter(failOnWrite: false, failOnFlush: true);
        var error = new StringWriter();

        var exit = CliHost.Complete(0, output, error);

        Assert.Equal(1, exit);
        Assert.Equal($"error: {CliHost.OutputFailureMessage}\n", error.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Complete_WhenFlushingSucceeds_ThenTheChosenExitCodeIsPreserved(int chosen)
    {
        var error = new StringWriter();

        Assert.Equal(chosen, CliHost.Complete(chosen, new StringWriter(), error));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Complete_WhenBothChannelsFail_ThenItStillReturnsWithoutThrowing()
    {
        var exit = CliHost.Complete(
            0,
            new ThrowingWriter(failOnWrite: false, failOnFlush: true),
            new ThrowingWriter(failOnWrite: true, failOnFlush: true));

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_WhenSuccessful_ThenDiagnosticsNeverReachStdout()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.WarningOnlySpec);

        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.NotEqual(string.Empty, harness.StdErr);
    }
}
