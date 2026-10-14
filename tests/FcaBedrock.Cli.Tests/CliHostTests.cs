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

    [Fact]
    public async Task RunAsync_WhenAUsageMessageQuotesADoubleQuote_ThenTheQuoteStaysLiteral()
    {
        var harness = new CliTestHarness();

        // A backslash followed by t is two characters, not a tab, so it is refused, and the
        // message names it with its backslash doubled.
        var exit = await harness.RunAsync("probe", "x", "--delimiter", "\\t");

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            "error: option '--delimiter' accepts a tab character, the word tab, or one character from U+001F to U+007E"
            + " other than '#' and the quote character '\"', not '\\\\t'.\n" + UsageText.For("probe"),
            harness.StdErr);
    }

    [Fact]
    public async Task RunAsync_WhenAUsageMessageEchoesControlCharacters_ThenTheyAreEscapedOnOneLine()
    {
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("a\u0085b" + (char)0x2028 + "c\\nd\ne");

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            "error: unknown command 'a\\u0085b\\u2028c\\\\nd\\ne'.\n" + UsageText.General, harness.StdErr);
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

    // ---- usage failures come before any input is opened ------------------------------------
    //
    // Every input below is missing, so a run that read one would fail with exit 1: exit 2 with
    // nothing opened and no publication operation proves the value was refused first.

    [Theory]
    [InlineData("convert")]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("calibrate")]
    [InlineData("fingerprint")]
    public async Task RunAsync_WhenTempDirIsBlank_ThenUsageExit2BeforeAnyInputIsOpened(string command)
    {
        using var temp = TempDirectory.Create();
        string[] output = command switch
        {
            "convert" => ["--out", temp.Resolve("out"), "--format", "cxt"],
            "calibrate" => ["--out", temp.Resolve("out.toml")],
            _ => [],
        };
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            [command, temp.Resolve("missing.toml"), temp.Resolve("missing.csv"), .. output, "--temp-dir", " "]);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            "error: the --temp-dir value must be a non-empty, non-whitespace path.\n" + UsageText.For(command),
            harness.StdErr);
        Assert.Empty(harness.Opened);
        Assert.Empty(harness.PublicationFiles.Operations);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    // One writing command over missing inputs, with --out last.
    private static string[] Writer(string command, TempDirectory temp, string output) => command switch
    {
        "convert" => ["convert", temp.Resolve("missing.toml"), temp.Resolve("missing.csv"), "--format", "cxt", "--out", output],
        "calibrate" => ["calibrate", temp.Resolve("missing.toml"), temp.Resolve("missing.csv"), "--out", output],
        "probe" => ["probe", temp.Resolve("missing.csv"), "--shape", "wide", "--out", output],
        "migrate" => ["migrate", temp.Resolve("missing.bed"), "--out", output],
        _ => ["fingerprint", temp.Resolve("missing.toml"), temp.Resolve("missing.csv"), "--write", "--out", output],
    };

    [Theory]
    [InlineData("convert")]
    [InlineData("calibrate")]
    [InlineData("probe")]
    [InlineData("migrate")]
    [InlineData("fingerprint")]
    public async Task RunAsync_WhenOutNamesNoFile_ThenUsageExit2BeforeAnyInputIsOpenedAndNothingIsWritten(string command)
    {
        using var temp = TempDirectory.Create();
        var separator = Path.DirectorySeparatorChar;
        static string Names(string value) => $"the --out value '{value}' names a directory; it must end in a file name.";
        static string Trimmed(string value) =>
            $"the --out value '{value}' ends in a dot or a space, which Windows removes from a file name.";

        // Absolute spellings under the run's own directory, so any file a run wrote would show
        // there, plus the relative forms whose meaning depends on the working directory.
        var cases = new List<(string Output, string Message)>
        {
            (string.Empty, "the --out value must be a non-empty, non-whitespace path."),
            (".", Names(".")),
            ("..", Names("..")),
            (temp.Path + separator, Names(temp.Path + separator)),
            (temp.Path + separator + ".", Names(temp.Path + separator + ".")),
            (temp.Resolve("sub") + separator + "..", Names(temp.Resolve("sub") + separator + "..")),
        };

        if (OperatingSystem.IsWindows())
        {
            cases.Add((temp.Resolve("foo."), Trimmed(temp.Resolve("foo."))));
            cases.Add((temp.Resolve("foo "), Trimmed(temp.Resolve("foo "))));
            cases.Add((temp.Resolve("..."), Trimmed(temp.Resolve("..."))));
            cases.Add((temp.Resolve(@"sub\..."), Trimmed(temp.Resolve(@"sub\..."))));
        }

        foreach (var (output, message) in cases)
        {
            var harness = new CliTestHarness();

            var exit = await harness.RunAsync(Writer(command, temp, output));

            Assert.Equal(2, exit);
            Assert.Equal(string.Empty, harness.StdOut);
            Assert.Equal(DiagnosticRenderer.RenderHostError(message) + UsageText.For(command), harness.StdErr);
            Assert.Empty(harness.Opened);
            Assert.Empty(harness.PublicationFiles.Operations);
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task RunAsync_WhenConvertOutIsADash_ThenUsageExit2BeforeAnyInputIsOpened()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(Writer("convert", temp, "-"));

        Assert.Equal(2, exit);
        Assert.Equal(
            "error: command 'convert' has no stdout output, so --out cannot be '-'.\n" + UsageText.For("convert"),
            harness.StdErr);
        Assert.Empty(harness.Opened);
        Assert.Empty(harness.PublicationFiles.Operations);
    }

    [Fact]
    public async Task RunAsync_WhenOutEndsInADotFromSeveralWorkingDirectories_ThenEveryRunIsAUsageFailure()
    {
        // The refused spelling is judged as text, so the working directory cannot change the
        // outcome. Only a child process has a working directory of its own, so the built tool
        // runs in one at three depths; the in-process harness shares the test host's.
        var tool = Path.Combine(AppContext.BaseDirectory, "FcaBedrock.Cli.dll");
        Assert.SkipUnless(
            File.Exists(tool) && File.Exists(Path.ChangeExtension(tool, ".runtimeconfig.json")),
            "the CLI assembly and its runtime configuration are not copied into this test's output.");

        using var temp = TempDirectory.Create();
        var middle = Directory.CreateDirectory(Path.Combine(temp.Path, "a")).FullName;
        var deepest = Directory.CreateDirectory(Path.Combine(middle, "b")).FullName;

        foreach (var directory in new[] { temp.Path, middle, deepest })
        {
            foreach (var output in new[] { ".", "..", Path.Combine("sub", ".") })
            {
                var result = await ToolProcess.RunAsync(
                    ToolProcess.DotnetHost(),
                    [tool, "convert", "missing.toml", "missing.csv", "--out", output, "--format", "cxt"],
                    directory,
                    timeout: TimeSpan.FromMinutes(1));

                Assert.Equal(2, result.ExitCode);
                Assert.Equal(string.Empty, result.StandardOutput);
                Assert.StartsWith(
                    DiagnosticRenderer.RenderHostError($"the --out value '{output}' names a directory; it must end in a file name."),
                    result.StandardError,
                    StringComparison.Ordinal);
            }
        }

        // Nothing was written anywhere in the tree, at any depth.
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*", SearchOption.AllDirectories));
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
        // The signal lands after the command has started, while the spec is being read,
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
        // The user received no usage text at all, so this is a host output failure, not an
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
        // Redirected output is buffered, so this is where a closed pipe usually surfaces:
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
