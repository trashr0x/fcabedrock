using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Benchmarks;

/// <summary>Which exporter a conversion case writes through.</summary>
internal enum ExportFormat
{
    /// <summary>FIMI <c>.dat</c>: streams the object enumeration once, ids only.</summary>
    Dat,

    /// <summary>
    /// Burmeister <c>.cxt</c>: takes its normal object-name pass and then replays the emission
    /// (§18.1), so a case measured here costs two complete passes by design, not by accident.
    /// </summary>
    Cxt,
}

/// <summary>
/// The shared body of every "convert a prepared corpus to a real artifact" benchmark: the setup that
/// stops exactly at the measured boundary, the per-iteration reset, the timed write, and the
/// post-iteration validation.
/// <para>
/// It exists because that body is <b>identical</b> across families and must stay identical. The
/// measured interval is a contract (D-124), and a per-family copy of it is a per-family opportunity
/// for one case to start its clock a little earlier or validate a little less than another. What
/// varies between families — which corpus, which expectation, which label — is what each case
/// supplies; nothing else.
/// </para>
/// <para>
/// It is a plain helper rather than a base class on purpose: BenchmarkDotNet reads
/// <c>[Benchmark(Description = …)]</c> per method, so a shared base would force every family to
/// share one label in the report. Composition keeps the label with the case.
/// </para>
/// </summary>
internal sealed class ConversionRun(string label, CorpusCase corpus, ExportFormat format = ExportFormat.Dat)
{
    private PreparedCorpus _prepared = null!;
    private PreparedConversion _conversion = null!;
    private ContextExpectation _expected = null!;
    private List<BedrockDiagnostic> _diagnostics = [];
    private string _outputPath = string.Empty;

    /// <summary>The prepared corpus, available to a case that needs its measured record count.</summary>
    public PreparedCorpus Prepared => _prepared;

    /// <summary>The resolved, calibrated plan, available for a setup-time shape assertion.</summary>
    public ConversionPlan Plan => _conversion.Plan;

    /// <summary>The expectation this run validates against, when it has a derivable one.</summary>
    public ContextExpectation Expected => _expected;

    /// <summary>The diagnostics the last completed iteration produced.</summary>
    public IReadOnlyList<BedrockDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Diagnostic codes that are legitimate for this case beyond the degenerate-shape set — the
    /// aggregated <c>DuplicateObjectKey</c> a deduping conversion reports, for instance, which is an
    /// Info stating exactly what the case exists to do.
    /// </summary>
    public IReadOnlyList<DiagnosticCode> AlsoAllowed { get; init; } = [];

    /// <summary>The grouping options the conversion runs under; null means the production defaults.</summary>
    public GroupingOptions? Grouping { get; init; }

    /// <summary>
    /// Requires an already-prepared corpus, drives the real spec-read/resolve/calibrate/plan
    /// sequence once, and derives the expectation. All of it is outside every measured interval.
    /// </summary>
    /// <param name="expect">
    /// Derives the independent expectation from the prepared corpus, or null for a case whose
    /// expected bytes are not derivable ahead of the run and which validates against a baseline.
    /// </param>
    /// <param name="expectedFormalAttributes">
    /// The formal-attribute count the expectation's frozen layout assumes, checked against the plan
    /// that was actually produced. A planner change that moved a column would otherwise invalidate
    /// every expectation silently; here it stops the case before it measures anything.
    /// </param>
    public async Task SetupAsync(
        Func<PreparedCorpus, ContextExpectation>? expect, int? expectedFormalAttributes = null)
    {
        _prepared = CorpusPreparer.Require(corpus);
        _conversion = await ConversionPipeline.FromSpecFileAsync(
            _prepared.SpecPath, _prepared.DataPath, grouping: Grouping).ConfigureAwait(false);
        _expected = expect is null ? new ContextExpectation(0, 0, 0, 0, string.Empty) : expect(_prepared);

        // Named from the case rather than from the runtime type: BenchmarkDotNet's out-of-process
        // toolchain runs each case inside a generated `Runnable_0`, so a type-derived name would be
        // both unreadable and identical across cases.
        _outputPath = Path.Combine(
            BenchmarkPaths.OutputDirectory,
            $"{corpus.Id}.{Sanitize(label)}.{(format == ExportFormat.Dat ? "dat" : "cxt")}");

        if (expectedFormalAttributes is { } expectedCount && _conversion.Plan.FormalAttributes.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"{label}: the plan has {_conversion.Plan.FormalAttributes.Count} formal attributes, "
                + $"but the oracle's frozen layout expects {expectedCount}.");
        }
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    public void Reset()
    {
        Delete();
        _diagnostics = [];
    }

    /// <summary>
    /// The measured operation: open the destination, run the production emission to completion,
    /// serialize, flush, and close. Nothing is open when this is entered.
    /// </summary>
    public async Task ConvertAsync()
    {
        await using var output = new FileStream(_outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (format == ExportFormat.Dat)
        {
            await DatWriter.WriteAsync(_conversion.Emit(_diagnostics), WriterOptions.Native, output)
                .ConfigureAwait(false);
        }
        else
        {
            await CxtWriter.WriteAsync(
                    _conversion.Plan, () => _conversion.Emit(_diagnostics), WriterOptions.Native, output)
                .ConfigureAwait(false);
        }

        await output.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Validates the completed iteration once the writer's stream is disposed and the emit
    /// diagnostics are final, then removes the artifact so only one iteration's output ever exists.
    /// </summary>
    public void Validate() => ValidateAgainst(_expected.ByteLength, _expected.Sha256);

    /// <summary>
    /// Validates the diagnostics and the artifact's exact length and digest against a supplied
    /// expectation, for a case whose expected bytes are not derivable ahead of the run.
    /// </summary>
    public void ValidateAgainst(long expectedBytes, string expectedSha256)
    {
        OutputValidation.RequireCleanEmit(_diagnostics, label, AlsoAllowed);
        OutputValidation.RequireFileMatches(_outputPath, expectedBytes, expectedSha256, label);
        Delete();
    }

    /// <summary>The artifact's current length and digest, for a case that is recording a baseline.</summary>
    public (long Bytes, string Sha256) Observe() =>
        (new FileInfo(_outputPath).Length, CorpusCatalog.HashFile(_outputPath));

    /// <summary>
    /// The first completed iteration's observed length and digest — this run's <b>baseline</b>.
    /// </summary>
    public (long Bytes, string Sha256)? Baseline { get; private set; }

    /// <summary>
    /// Validates a case whose expected bytes are <em>not</em> derivable ahead of the run, because
    /// its schema comes from the data: real data with discovered domains.
    /// <para>
    /// It asserts three things and is honest about what each is worth. The diagnostics must be
    /// clean. The object count must equal the count independently measured from the input, which is
    /// a genuine semantic check — it catches a lost, duplicated, or spuriously invented object. And
    /// every iteration after the first must reproduce the first one's bytes exactly, which is a
    /// determinism check within the run. The recorded baseline digest is <b>regression evidence</b>
    /// for later runs; it is not, and is never presented as, independent proof that the semantics
    /// are right.
    /// </para>
    /// </summary>
    public void ValidateBaseline(long expectedObjects)
    {
        OutputValidation.RequireCleanEmit(_diagnostics, label, AlsoAllowed);
        OutputValidation.RequireLineCount(_outputPath, expectedObjects, label);

        var observed = Observe();
        if (Baseline is { } first)
        {
            if (first.Bytes != observed.Bytes || !string.Equals(first.Sha256, observed.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{label}: iteration produced {observed.Bytes} bytes / {observed.Sha256}, "
                    + $"but the first iteration of this run produced {first.Bytes} / {first.Sha256}. "
                    + "The same input under the same spec must convert identically every time.");
            }
        }
        else
        {
            Baseline = observed;
        }

        Delete();
    }

    /// <summary>Removes the output directory entry this case owned.</summary>
    public void Cleanup() => Delete();

    private void Delete()
    {
        if (_outputPath.Length > 0 && File.Exists(_outputPath))
        {
            File.Delete(_outputPath);
        }
    }

    // The label reaches a file name, so it is reduced to the characters a path may carry. It is
    // authored text, never user input, but a name derived from it should still be obviously safe.
    private static string Sanitize(string text) =>
        string.Concat(text.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-'));
}
