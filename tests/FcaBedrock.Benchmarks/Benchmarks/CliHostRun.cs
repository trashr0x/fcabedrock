using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Cli;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// A clock that never moves, so a run manifest's <c>timestamp</c> is a constant rather than an input
/// that varies per iteration.
/// </summary>
internal sealed class FixedClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow { get; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
}

/// <summary>A signal source that is never signalled: nothing here cancels a benchmark.</summary>
internal sealed class QuietSignalSource : ISignalSource
{
    private readonly CancellationTokenSource _source = new();

    /// <inheritdoc/>
    public CancellationToken Token => _source.Token;

    /// <inheritdoc/>
    public void Dispose() => _source.Dispose();
}

/// <summary>
/// The shared body of every CLI-host benchmark: one complete <c>fcabedrock convert</c> driven at the
/// argv boundary, in process.
/// <para>
/// <b>What is measured.</b> <see cref="CliHost.RunAsync"/> from the raw argv through parsing,
/// resolution, calibration, planning, emission, export, inline input and output hashing, the real
/// staged publication transaction against the real filesystem, and the manifest sidecar. Only the
/// clock, the signal source, and the two text sinks are injected; the input opener and the
/// publication filesystem are the production ones, so the publication really does create, rename,
/// commit, and fsync on disk.
/// </para>
/// <para>
/// <b>What is not, and why the label matters.</b> Process start, host resolution, the tool shim,
/// runtime startup, JIT warmup of a cold process, console attachment, and terminal rendering are all
/// outside — they belong to the operating system and the .NET host, not to FcaBedrock. So this is
/// <b>CLI-host throughput</b> and is never to be quoted as installed-command latency. The packaging
/// smokes cover the real executable boundary; this covers what the tool does once it is running.
/// </para>
/// <para>
/// The argv is explicitly synthetic and says so: the audit argv the manifest records is a fixed
/// label, not a captured process command line, because a benchmark has no honest claim to one.
/// </para>
/// </summary>
internal sealed class CliHostRun(string label, CorpusCase corpus)
{
    /// <summary>The audit argv[0] a manifest produced by this suite records.</summary>
    public const string SyntheticArgv0 = "fcabedrock-benchmark-host";

    private PreparedCorpus _prepared = null!;
    private string[] _argv = [];
    private string _outputBase = string.Empty;
    private StringWriter _out = new();
    private StringWriter _error = new();
    private int _exitCode = -1;

    /// <summary>The prepared corpus, for a case that needs its measured record count.</summary>
    public PreparedCorpus Prepared => _prepared;

    /// <summary>The exit code the last completed iteration produced.</summary>
    public int ExitCode => _exitCode;

    /// <summary>Everything the run wrote to its primary sink.</summary>
    public string StandardOutput => _out.ToString();

    /// <summary>Everything the run wrote to its diagnostic sink.</summary>
    public string StandardError => _error.ToString();

    /// <summary>The output base the run was given; the ruled extension is appended to it.</summary>
    public string OutputBase => _outputBase;

    /// <summary>The primary artifact's path.</summary>
    public string ArtifactPath => _outputBase + Extension;

    /// <summary>The run manifest's path, whether or not this case asks for one (§15).</summary>
    public string ManifestPath => _outputBase + ManifestExtension;

    /// <summary>The run manifest's sidecar extension, as the publication targets define it.</summary>
    public const string ManifestExtension = ".manifest.toml";

    /// <summary>The output format this run requests.</summary>
    public required string Format { get; init; }

    /// <summary>Whether the run suppresses the manifest sidecar.</summary>
    public bool NoManifest { get; init; }

    /// <summary>
    /// Diagnostic codes this command is <b>expected</b> to report, by name.
    /// <para>
    /// A convert under a fully declared spec says nothing at all, so the default is an empty list and
    /// any output on the diagnostic sink fails the case. An auto-calibrated one legitimately reports
    /// that its column set came from the data — a statement about the mode it was asked to run, not
    /// a finding about the input, and one it would make on every run over every corpus. Naming the
    /// codes per case keeps that from becoming a general licence to ignore stderr.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ExpectedDiagnosticCodes { get; init; } = [];

    /// <summary>
    /// An alternative spec for the same corpus, written beside it at setup. Null uses the corpus's
    /// own committed spec.
    /// <para>
    /// It exists for one case the corpus spec cannot express: an <b>auto-calibrated</b> convert. The
    /// declared spec resolves its whole column set up front, so the command makes a single input
    /// pass; a spec whose cuts come from the data makes <em>two</em> — one to calibrate, one to
    /// emit — and each is hashed inline. That difference is the M7 input-stability cost at its real
    /// worst case, and it is only reachable by converting the same data under a different spec.
    /// </para>
    /// </summary>
    public string? SpecOverride { get; init; }

    private string Extension => Format switch
    {
        "dat" => ".dat",
        "cxt" => ".cxt",
        _ => throw new InvalidOperationException(
            $"'{Format}' publishes more than one artifact, so it has no single path; validate each one."),
    };

    /// <summary>The <c>.dat</c> artifact's path, for a run that publishes both formats.</summary>
    public string DatPath => _outputBase + ".dat";

    /// <summary>The <c>.cxt</c> artifact's path, for a run that publishes both formats.</summary>
    public string CxtPath => _outputBase + ".cxt";

    /// <summary>
    /// Validates a <c>--format both</c> run: <b>both</b> artifacts must be exactly right, because
    /// the transaction publishes them together and a partial commit is precisely what it exists to
    /// prevent.
    /// </summary>
    public void ValidateBoth(ContextExpectation dat, ContextExpectation cxt)
    {
        ArgumentNullException.ThrowIfNull(dat);
        ArgumentNullException.ThrowIfNull(cxt);

        RequireSuccess();
        OutputValidation.RequireFileMatches(DatPath, dat.ByteLength, dat.Sha256, label + " (.dat)");
        OutputValidation.RequireFileMatches(CxtPath, cxt.ByteLength, cxt.Sha256, label + " (.cxt)");
        RequireManifestState();
        DeleteArtifacts();
    }

    /// <summary>
    /// Requires an already-prepared corpus and composes the argv. Nothing is opened and nothing is
    /// converted here; the whole command is inside the measured call.
    /// </summary>
    public void Setup()
    {
        _prepared = CorpusPreparer.Require(corpus);
        _outputBase = Path.Combine(BenchmarkPaths.OutputDirectory, $"{corpus.Id}.{Sanitize(label)}");

        // An override spec is written once, in setup, beside the corpus it reads - never inside a
        // measured interval, and never over the committed spec the catalog's digest describes.
        var specPath = _prepared.SpecPath;
        if (SpecOverride is { } specText)
        {
            specPath = Path.Combine(BenchmarkPaths.CorpusDirectory, $"{corpus.Id}.{Sanitize(label)}.toml");
            File.WriteAllBytes(
                specPath, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(specText));
        }

        var argv = new List<string>
        {
            "convert", specPath, _prepared.DataPath, "--out", _outputBase, "--format", Format,

            // The command's own option for the same seam every library case uses, so a CLI run
            // spills onto the same volume as the corpora it reads and the artifacts it publishes.
            "--temp-dir", BenchmarkPaths.SpoolDirectory,
        };

        if (NoManifest)
        {
            argv.Add("--no-manifest");
        }

        _argv = [.. argv];
    }

    /// <summary>Removes every artifact the previous iteration published and takes fresh sinks.</summary>
    public void Reset()
    {
        DeleteArtifacts();
        _out = new StringWriter();
        _error = new StringWriter();
        _exitCode = -1;
    }

    /// <summary>The measured operation: one complete command at the argv boundary.</summary>
    public async Task<int> RunAsync()
    {
        using var signals = new QuietSignalSource();
        _exitCode = await CliHost.RunAsync(
            _argv,
            new CliEnvironment
            {
                Out = _out,
                Error = _error,
                Clock = new FixedClock(),
                Signals = signals,
                ToolVersion = "fcabedrock-vnext benchmark",

                // Explicitly synthetic, and recorded as such: `command_line` is an audit record of
                // what a process actually received, and a benchmark has none to report.
                AuditArgv = [SyntheticArgv0, .. _argv],
                OpenInput = CliEnvironment.OpenFile,
            }).ConfigureAwait(false);

        return _exitCode;
    }

    /// <summary>
    /// Validates the completed command after every stream is closed: the exit status, an empty
    /// diagnostic sink, the exact artifact bytes, and the manifest's presence or absence.
    /// </summary>
    public void Validate(ContextExpectation expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        RequireSuccess();
        OutputValidation.RequireFileMatches(ArtifactPath, expected.ByteLength, expected.Sha256, label);
        RequireManifestState();
        DeleteArtifacts();
    }

    /// <summary>
    /// Validates a command whose expected bytes are not derivable ahead of the run.
    /// <para>
    /// Three assertions, and each is worth exactly what it claims. The published <c>.dat</c> must
    /// have one line per expected object, which is a genuine semantic check: it catches a lost,
    /// duplicated, or invented object. Every iteration after the first must reproduce the first
    /// one's bytes, which is a determinism check within the run. And the digest it records is
    /// <b>regression evidence</b> for a later run — not proof that the semantics are right.
    /// </para>
    /// </summary>
    public void ValidateBaseline(long expectedObjects)
    {
        RequireSuccess();
        OutputValidation.RequireLineCount(ArtifactPath, expectedObjects, label);

        var observed = (Bytes: new FileInfo(ArtifactPath).Length, Sha256: CorpusCatalog.HashFile(ArtifactPath));
        if (Baseline is { } first)
        {
            if (first.Bytes != observed.Bytes || !string.Equals(first.Sha256, observed.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{label}: iteration published {observed.Bytes} bytes / {observed.Sha256}, "
                    + $"but the first iteration published {first.Bytes} / {first.Sha256}.");
            }
        }
        else
        {
            Baseline = observed;
        }

        RequireManifestState();
        DeleteArtifacts();
    }

    /// <summary>The first completed iteration's published length and digest.</summary>
    public (long Bytes, string Sha256)? Baseline { get; private set; }

    /// <summary>Removes every artifact this case published.</summary>
    public void Cleanup() => DeleteArtifacts();

    private void RequireSuccess()
    {
        if (_exitCode != 0)
        {
            throw new InvalidOperationException(
                $"""
                {label}: the command exited {_exitCode}, so it published no result worth timing.
                stderr: {StandardError}
                stdout: {StandardOutput}
                """);
        }

        // A successful convert reports nothing on the diagnostic sink unless the case named the
        // codes it expects. Every reported line must carry one of those names; anything else is a
        // warning the run would oblige its caller to read, which is not a clean measured command.
        var unexpected = StandardError
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => !ExpectedDiagnosticCodes.Any(code => line.Contains(code, StringComparison.Ordinal)))
            .ToList();

        if (unexpected.Count > 0)
        {
            throw new InvalidOperationException(
                $"{label}: the command reported unexpected diagnostics: {string.Join(" | ", unexpected)}");
        }
    }

    // `--no-manifest` suppresses the sidecar and nothing else: the input and output hashing still
    // happen, so the pair of cases measures the sidecar's own cost rather than "hashing off".
    private void RequireManifestState()
    {
        var present = File.Exists(ManifestPath);
        if (present == NoManifest)
        {
            throw new InvalidOperationException(
                NoManifest
                    ? $"{label}: --no-manifest still produced a manifest at '{ManifestPath}'."
                    : $"{label}: no manifest was produced at '{ManifestPath}'.");
        }
    }

    private void DeleteArtifacts()
    {
        if (_outputBase.Length == 0)
        {
            return;
        }

        foreach (var path in (string[])[DatPath, CxtPath, ManifestPath])
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string Sanitize(string text) =>
        string.Concat(text.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-'));
}
