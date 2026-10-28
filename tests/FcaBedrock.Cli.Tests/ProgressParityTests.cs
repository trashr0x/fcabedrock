using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Orchestration;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Parity through the CLI (EP-7, D-141): every command run with the internal operation-progress seam
/// unset and set writes the same stdout, stderr, exit code, file bytes and manifest bytes under the
/// harness clock, and performs the same publication filesystem operations. The CLI passes no progress
/// in production; the seam only proves that observing changes nothing. Every observed run ends with a
/// completed progress whose recorded effects satisfy the ordering guarantees.
/// </summary>
public sealed class ProgressParityTests
{
    /// <summary>A wide spec that calibrates: its domain is omitted.</summary>
    private const string CalibratedSpec = """
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
        """;

    private sealed record Observed(string Text, ProgressRecorder? Recorder, OperationProgress? Progress);

    // ---- every active golden through convert --format both --------------------------------------

    /// <summary>The nine active v2 goldens: family, variant, migrate flags and data variant.</summary>
    private static readonly (string Family, string Variant, string[] Flags, string Data)[] Goldens =
    [
        ("mini-mushroom", "mini-mushroom", ["--shape", "wide", "--delimiter", ",", "--header", "true"], "mini-mushroom"),
        ("mini-mushroom", "mini-mushroom_tabbed_noheader", ["--shape", "wide", "--delimiter", "\t", "--header", "false"], "mini-mushroom_tabbed_noheader"),
        ("mini-adult", "mini-adult", ["--shape", "wide", "--delimiter", ",", "--header", "true"], "mini-adult"),
        ("mini-adult", "mini-adult_noheader", ["--shape", "wide", "--delimiter", ",", "--header", "false"], "mini-adult_noheader"),
        ("mini-adult", "mini-adult_employment_ordinal_discrete", ["--shape", "wide", "--delimiter", ",", "--header", "true", "--scaling", "discrete"], "mini-adult"),
        ("mini-adult", "mini-adult_employment_ordinal_progressive", ["--shape", "wide", "--delimiter", ",", "--header", "true", "--scaling", "progressive"], "mini-adult"),
        ("mini-mushroom", "mini-mushroom_triples", ["--shape", "triple", "--delimiter", ",", "--header", "false"], "mini-mushroom_triples"),
        ("mini-adult", "mini-adult_triples", ["--shape", "triple", "--delimiter", ",", "--header", "false"], "mini-adult_triples"),
        ("mini-adult", "mini-adult_triples_named", ["--shape", "triple", "--delimiter", ",", "--header", "false"], "mini-adult_triples_named"),
    ];

    public static TheoryData<string, bool> GoldenCases()
    {
        var cases = new TheoryData<string, bool>();
        foreach (var golden in Goldens)
        {
            cases.Add(golden.Variant, true);
            cases.Add(golden.Variant, false);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public async Task Convert_WhenAGoldenIsConvertedObservedOrNot_ThenEveryByteIsEqual(string variant, bool manifest)
    {
        var golden = Goldens.Single(row => row.Variant == variant);
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "v2", golden.Family);
        var data = Path.Combine(root, golden.Data + ".data");
        using var temp = TempDirectory.Create();
        var spec = temp.Resolve(golden.Variant + ".toml");
        Assert.Equal(0, await new CliTestHarness().RunAsync(["migrate", Path.Combine(root, golden.Variant + ".bed"), "--out", spec, .. golden.Flags]));
        string[] argv = manifest
            ? ["convert", spec, data, "--out", temp.Resolve("out"), "--format", "both", "--v2-compat"]
            : ["convert", spec, data, "--out", temp.Resolve("out"), "--format", "both", "--v2-compat", "--no-manifest"];

        await AssertParityAsync(temp.Path, argv, data, trackerOpens: true, expectedExit: 0);
    }

    // ---- every command that reaches the seam, and validate, which reaches nothing ------------------

    public static TheoryData<string> Commands() =>
    [
        "plan", "stats", "stats dedupe", "convert", "calibrate --out", "fingerprint", "fingerprint --write --out",
        "probe --out", "migrate --out", "validate",
    ];

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task Command_WhenRunObservedOrNot_ThenEveryByteIsEqual(string command)
    {
        using var temp = TempDirectory.Create();
        var (spec, data) = command switch
        {
            "stats dedupe" => (CliFixtures.DedupeSpec, CliFixtures.DedupeData),
            "fingerprint" or "fingerprint --write --out" => (CliFixtures.FrozenSpec, CliFixtures.FrozenData),
            "validate" or "probe --out" => (CliFixtures.IndexBoundSpec, CliFixtures.WideData),
            _ => (CalibratedSpec, CliFixtures.CalibrateWideData),
        };
        var specPath = temp.Write("spec.toml", spec);
        var dataPath = temp.Write("data.csv", data);
        var bed = Path.Combine(AppContext.BaseDirectory, "fixtures", "v2", "mini-mushroom", "mini-mushroom.bed");
        string[] argv = command switch
        {
            "plan" => ["plan", specPath, dataPath],
            "stats" or "stats dedupe" => ["stats", specPath, dataPath],
            "convert" => ["convert", specPath, dataPath, "--out", temp.Resolve("out"), "--format", "both"],
            "calibrate --out" => ["calibrate", specPath, dataPath, "--out", temp.Resolve("frozen.toml")],
            "fingerprint" => ["fingerprint", specPath, dataPath],
            "fingerprint --write --out" => ["fingerprint", specPath, dataPath, "--write", "--out", temp.Resolve("corrected.toml")],
            "probe --out" => ["probe", dataPath, "--shape", "wide", "--out", temp.Resolve("draft.toml")],
            "migrate --out" => ["migrate", bed, "--out", temp.Resolve("migrated.toml"), "--shape", "wide", "--delimiter", ",", "--header", "true"],
            _ => ["validate", specPath, dataPath],
        };

        // probe, migrate and validate read no prepared run, so their data opens are not tracker passes.
        var trackerOpens = command is not ("probe --out" or "migrate --out" or "validate");
        var observed = await AssertParityAsync(temp.Path, argv, dataPath, trackerOpens, expectedExit: 0);

        var recorder = observed.Recorder!;
        if (command is "probe --out" or "migrate --out" or "calibrate --out" or "fingerprint --write --out")
        {
            Assert.Equal(
                [WorkingStep.PreparingOutput, WorkingStep.Staging, WorkingStep.Sealing, WorkingStep.Committing, WorkingStep.Cleaning],
                recorder.NamedSteps().Where(step => step != WorkingStep.Calibrating));
        }

        if (command == "validate")
        {
            Assert.Equal([ProgressEvent.Completed], recorder.Events.Select(e => e.Name));
            Assert.Equal(new ProgressSnapshot(0, 0, null, 0, 0, 0, 0, isCompleted: true), observed.Progress!.Current);
        }
    }

    // ---- publication refusal, rollback and recovery -----------------------------------------------

    public static TheoryData<string> PublicationCases() =>
    [
        "existing target", "stale manifest", "residue recovered", "stage write failure", "cancelled at a data read",
        "cancelled after the commit point",
    ];

    [Theory]
    [MemberData(nameof(PublicationCases))]
    public async Task Publication_WhenRunObservedOrNot_ThenEveryByteIsEqual(string publication)
    {
        using var temp = TempDirectory.Create();
        var specPath = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var dataPath = temp.Write("data.csv", CliFixtures.WideData);
        var outputBase = temp.Resolve("out");
        string[] argv = publication switch
        {
            "stale manifest" => ["convert", specPath, dataPath, "--out", outputBase, "--format", "cxt", "--no-manifest"],
            "residue recovered" => ["convert", specPath, dataPath, "--out", outputBase, "--format", "both", "--force"],
            _ => ["convert", specPath, dataPath, "--out", outputBase, "--format", "both"],
        };
        Action? arrange = null;
        Action<CliTestHarness>? configure = null;
        var exit = 1;
        switch (publication)
        {
            case "existing target":
                arrange = () => File.WriteAllText(outputBase + ".cxt", "an earlier file");
                break;
            case "stale manifest":
                arrange = () => File.WriteAllText(outputBase + ".manifest.toml", "an earlier manifest");
                break;
            case "residue recovered":
                // A run stopped right after its manifest committed leaves its record and private files;
                // the next run's preflight finishes it forward, then replaces its outputs.
                exit = 0;
                arrange = () =>
                {
                    var crashing = new CliTestHarness();
                    crashing.PublicationFiles.CrashAfter = "Move:out.manifest.toml.fcabedrock-stage-T->out.manifest.toml";
                    _ = crashing.RunAsync(argv).GetAwaiter().GetResult();
                    Assert.True(crashing.PublicationFiles.Crashed, "the interrupted run never crashed");
                    Assert.NotEmpty(Directory.EnumerateFiles(temp.Path, "out.fcabedrock-*"));
                };
                break;
            case "stage write failure":
                configure = harness => harness.PublicationFiles.FailStreamWritePrefix = "out.dat.fcabedrock-stage-";
                break;
            case "cancelled at a data read":
                exit = 3;
                configure = harness =>
                {
                    var opens = 0;
                    harness.OpenInput = path =>
                        path == dataPath && opens++ == 1
                            ? new CancelOnRead(File.ReadAllBytes(path), harness.Signals.Cancel)
                            : File.OpenRead(path);
                };
                break;
            default:
                exit = 0;
                configure = harness =>
                {
                    harness.PublicationFiles.MutateBefore = "CreateNew:out.fcabedrock-committed-T";
                    harness.PublicationFiles.Mutate = harness.Signals.Cancel;
                };
                break;
        }

        var observed = await AssertParityAsync(temp.Path, argv, dataPath, trackerOpens: true, expectedExit: exit, configure, arrange);

        var steps = observed.Recorder!.NamedSteps();
        Assert.Equal(publication is "existing target" or "stale manifest" ? 0 : 1, steps.Count(step => step == WorkingStep.Cleaning));
        if (publication == "cancelled at a data read")
        {
            Assert.Equal(PassEnd.Cancelled, observed.Recorder.EndOf(2));
        }
    }

    // ---- the comparison -----------------------------------------------------------------------------

    // Runs argv unobserved, then observed, from the same starting directory, and asserts that the two
    // renderings are equal. Returns the observed one.
    private static async Task<Observed> AssertParityAsync(
        string directory,
        string[] argv,
        string dataPath,
        bool trackerOpens,
        int expectedExit,
        Action<CliTestHarness>? configure = null,
        Action? arrange = null)
    {
        var plain = await ObserveAsync(directory, argv, dataPath, observed: false, trackerOpens, configure, arrange);
        var watched = await ObserveAsync(directory, argv, dataPath, observed: true, trackerOpens, configure, arrange);

        Assert.StartsWith($"exit {expectedExit}\n", plain.Text, StringComparison.Ordinal);
        Assert.Equal(plain.Text, watched.Text);
        Assert.True(watched.Progress!.Current.IsCompleted);
        return watched;
    }

    private static async Task<Observed> ObserveAsync(
        string directory,
        string[] argv,
        string dataPath,
        bool observed,
        bool trackerOpens,
        Action<CliTestHarness>? configure,
        Action? arrange)
    {
        var inputs = Directory.EnumerateFiles(directory).ToHashSet(StringComparer.Ordinal);
        arrange?.Invoke();
        var harness = new CliTestHarness();
        configure?.Invoke(harness);
        ProgressRecorder? recorder = null;
        if (observed)
        {
            (harness.OperationProgress, recorder) = ProgressRecorder.Create();
        }

        var progress = harness.OperationProgress;
        var exit = await harness.RunAsync(argv);

        var text = new StringBuilder();
        text.Append("exit ").Append(exit).Append('\n');
        text.Append("stdout ").Append(harness.StdOut).Append('\n');
        text.Append("stderr ").Append(harness.StdErr).Append('\n');
        foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            text.Append("file ").Append(RecordingPublicationFileSystem.Fold(Path.GetFileName(path))).Append(' ')
                .Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))).Append('\n');
        }

        foreach (var operation in harness.PublicationFiles.Operations)
        {
            text.Append("op ").Append(RecordingPublicationFileSystem.Fold(operation)).Append('\n');
        }

        if (recorder is not null)
        {
            var dataOpens = trackerOpens ? harness.Opened.Count(path => path == dataPath) : 0;
            recorder.AssertGrammar(observedOpens: dataOpens);
        }

        // Back to the starting directory, so the next rendering meets the same location.
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (!inputs.Contains(path))
            {
                File.Delete(path);
            }
        }

        return new Observed(text.ToString(), recorder, progress);
    }

    /// <summary>An in-memory copy of a data file that requests cancellation on every read.</summary>
    private sealed class CancelOnRead(byte[] bytes, Action cancel) : MemoryStream(bytes, writable: false)
    {
        // A derived MemoryStream's span read is routed to this array read.
        public override int Read(byte[] buffer, int offset, int count)
        {
            cancel();
            return base.Read(buffer, offset, count);
        }
    }
}
