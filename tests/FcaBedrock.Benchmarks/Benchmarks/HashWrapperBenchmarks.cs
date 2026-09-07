using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Cli;
using FcaBedrock.Cli.Publication;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// What the M7 input-stability guarantee costs: one complete source pass through the real
/// <see cref="InputHashTracker"/> against the identical pass without it.
/// <para>
/// <b>A pair, not a switch.</b> Inline raw-byte hashing of every complete input pass is a correctness
/// guarantee (D-122 part 5 / §17) and cannot be turned off; there is no product option here to
/// measure. So the unwrapped arm is a <em>component experiment</em> — the same reader over the same
/// bytes with the wrapper absent — and the difference between the two arms is the wrapper's cost.
/// It is not a configuration a user can select, and the report says so.
/// </para>
/// <para>
/// The wrapper's construction and finalization are inside the measured interval, because a per-pass
/// cost that excluded them would understate a real one. The digest is verified <b>outside</b> timing,
/// against the corpus catalog's recorded SHA-256 — which is a genuinely independent expectation,
/// since the catalog's digest was computed by the preparer over the same file long before this run.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Hash, BenchmarkCategories.Source)]
public abstract class InputHashPairBenchmark
{
    private PreparedCorpus _corpus = null!;
    private SourceReadSettings _settings = null!;
    private string? _digest;
    private IReadOnlyList<long> _opens = [];

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Resolves the read settings. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _corpus = CorpusPreparer.Require(Corpus);
        var session = await ConversionPipeline.OpenSessionAsync(_corpus.SpecPath, _corpus.DataPath)
            .ConfigureAwait(false);
        _settings = ((WideCsvSession)session).ReadSettings;
    }

    /// <summary>Discards the previous iteration's observations. Outside timing.</summary>
    [IterationSetup]
    public void Reset()
    {
        _digest = null;
        _opens = [];
    }

    /// <summary>One complete source pass with the real hashing wrapper in the stream chain.</summary>
    [Benchmark(Description = "source pass, hashed")]
    public async Task<long> Hashed()
    {
        var tracker = new InputHashTracker(CliEnvironment.OpenFile, _corpus.DataPath);
        var read = await DrainAsync(tracker.OpenHashed).ConfigureAwait(false);
        _digest = tracker.Digest;
        return read;
    }

    /// <summary>The identical pass with no wrapper: the baseline arm of the pair.</summary>
    [Benchmark(Description = "source pass, unhashed", Baseline = true)]
    public async Task<long> Unhashed() =>
        await DrainAsync(() => CliEnvironment.OpenFile(_corpus.DataPath)).ConfigureAwait(false);

    /// <summary>
    /// Verifies the digest outside timing, against the byte length and SHA-256 the corpus catalog
    /// recorded at preparation. A hashed pass that produced the wrong digest — or none, because it
    /// never reached end of stream — has no cost worth reporting.
    /// </summary>
    [IterationCleanup(Target = nameof(Hashed))]
    public void ValidateHashed()
    {
        RequireOneCompletePass("hashed");

        if (!string.Equals(_digest, _corpus.Entry.Data.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"input hash pair ({Corpus.Id}): the tracker reported {_digest ?? "no digest"}, "
                + $"expected {_corpus.Entry.Data.Sha256}. The catalog's digest was computed by the "
                + "preparer over the same file, so a disagreement is the tracker's.");
        }
    }

    /// <summary>Checks that the unhashed arm read the same bytes, so the two arms are comparable.</summary>
    [IterationCleanup(Target = nameof(Unhashed))]
    public void ValidateUnhashed() => RequireOneCompletePass("unhashed");

    /// <summary>
    /// Requires that exactly one of the iteration's stream opens consumed the whole file.
    /// <para>
    /// A wide session opens its input <b>twice</b>: once to read the schema, which stops after the
    /// first buffer, and once to stream every record. That is the production shape, and it is
    /// precisely why the tracker only completes a pass on end of stream — a digest over the schema
    /// probe would describe a prefix. So the assertion is "one open consumed the file", not "the
    /// iteration read the file's byte count", which a short probe would break for the right reason.
    /// </para>
    /// </summary>
    private void RequireOneCompletePass(string arm)
    {
        var complete = _opens.Count(bytes => bytes == _corpus.InputBytes);
        if (complete != 1)
        {
            throw new InvalidOperationException(
                $"input hash pair ({Corpus.Id}, {arm}): {complete} of {_opens.Count} stream open(s) "
                + $"consumed the file's {_corpus.InputBytes} bytes, expected exactly 1 "
                + $"(opens read [{string.Join(", ", _opens)}]).");
        }
    }

    // The real parser over the real chunking: the wrapper's cost depends on how the reader asks for
    // bytes, so a hand-rolled loop with a convenient buffer size would measure a different thing.
    private async Task<long> DrainAsync(Func<Stream> open)
    {
        var counting = new CountingOpen(open);
        var session = (IWideSourceSession)ConversionPipeline.CreateSession(_settings, counting.Open);
        _ = await session.GetSchemaAsync().ConfigureAwait(false);

        var fields = 0L;
        await foreach (var record in session.ReadAsync().ConfigureAwait(false))
        {
            fields += record.FieldCount;
        }

        _opens = counting.Opens;
        return fields;
    }

    // Counts what the reader actually consumed, PER OPEN, so "one pass reached end of stream over
    // the whole file" is asserted rather than assumed. It wraps the stream in BOTH arms, so its own
    // cost is common to the pair and cancels out of the difference.
    private sealed class CountingOpen(Func<Stream> open)
    {
        private readonly List<long> _opens = [];

        public IReadOnlyList<long> Opens => _opens;

        public Stream Open()
        {
            _opens.Add(0);
            return new CountingStream(open(), this, _opens.Count - 1);
        }

        private void Add(int index, int count) => _opens[index] += count;

        private sealed class CountingStream(Stream inner, CountingOpen owner, int index) : Stream
        {
            public override bool CanRead => inner.CanRead;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => inner.Flush();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = inner.Read(buffer, offset, count);
                owner.Add(index, read);
                return read;
            }

            public override int Read(Span<byte> buffer)
            {
                var read = inner.Read(buffer);
                owner.Add(index, read);
                return read;
            }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                owner.Add(index, read);
                return read;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}

/// <summary>The input-hash pair at 730,000 records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class InputHashPairWorking : InputHashPairBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);
}

/// <summary>The input-hash pair at 7.3M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class InputHashPairScale7M : InputHashPairBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);
}

/// <summary>
/// What hashing a staged output costs: a real <c>.dat</c> export through the real
/// <see cref="HashingWriteStream"/> against the identical export without it.
/// <para>
/// The same pairing discipline as the input side, and the same caveat: the staged output's digest is
/// what a publication commits against, so this is not an option anyone can turn off. The wrapper's
/// construction and its explicit <c>Complete()</c> finalization are both inside the measured
/// interval, and the resulting digest is checked outside it against the independently derived
/// expectation for the same context.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Hash, BenchmarkCategories.Convert)]
public abstract class OutputHashPairBenchmark
{
    private PreparedCorpus _corpus = null!;
    private PreparedConversion _conversion = null!;
    private ContextExpectation _expected = null!;
    private List<BedrockDiagnostic> _diagnostics = [];
    private string _outputPath = string.Empty;
    private string? _digest;

    /// <summary>The corpus this class converts.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Prepares the plan and the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _corpus = CorpusPreparer.Require(Corpus);
        _conversion = await ConversionPipeline.FromSpecFileAsync(_corpus.SpecPath, _corpus.DataPath)
            .ConfigureAwait(false);
        _expected = W16DeclaredOracle.Expect(_corpus.Records);
        _outputPath = Path.Combine(BenchmarkPaths.OutputDirectory, $"{Corpus.Id}.hashpair.dat");
    }

    /// <summary>Removes the previous iteration's artifact and its sinks. Outside timing.</summary>
    [IterationSetup]
    public void Reset()
    {
        Delete();
        _diagnostics = [];
        _digest = null;
    }

    /// <summary>The export with the real hashing wrapper between the writer and the file.</summary>
    [Benchmark(Description = "dat export, hashed")]
    public async Task Hashed()
    {
        await using var file = new FileStream(_outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var hashing = new HashingWriteStream(file);
        await DatWriter.WriteAsync(_conversion.Emit(_diagnostics), WriterOptions.Native, hashing)
            .ConfigureAwait(false);
        await hashing.FlushAsync().ConfigureAwait(false);
        _digest = hashing.Complete();
    }

    /// <summary>The identical export straight to the file: the baseline arm of the pair.</summary>
    [Benchmark(Description = "dat export, unhashed", Baseline = true)]
    public async Task Unhashed()
    {
        await using var file = new FileStream(_outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await DatWriter.WriteAsync(_conversion.Emit(_diagnostics), WriterOptions.Native, file).ConfigureAwait(false);
        await file.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>Validates the bytes and the wrapper's digest against the independent expectation.</summary>
    [IterationCleanup(Target = nameof(Hashed))]
    public void ValidateHashed()
    {
        var what = $"output hash pair ({Corpus.Id})";
        OutputValidation.RequireCleanEmit(_diagnostics, what);
        OutputValidation.RequireFileMatches(_outputPath, _expected.ByteLength, _expected.Sha256, what);

        // The wrapper prefixes its digest, so the comparison is against the prefixed form the
        // publication actually records rather than against a bare hex string.
        if (!string.Equals(_digest, ContentHash.Format(_expected.Sha256), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{what}: the wrapper reported {_digest ?? "no digest"}, "
                + $"expected {ContentHash.Format(_expected.Sha256)}.");
        }

        Delete();
    }

    /// <summary>Validates the unhashed arm produced the same artifact, so the two arms are comparable.</summary>
    [IterationCleanup(Target = nameof(Unhashed))]
    public void ValidateUnhashed()
    {
        var what = $"output hash pair ({Corpus.Id}, unhashed)";
        OutputValidation.RequireCleanEmit(_diagnostics, what);
        OutputValidation.RequireFileMatches(_outputPath, _expected.ByteLength, _expected.Sha256, what);
        Delete();
    }

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => Delete();

    private void Delete()
    {
        if (_outputPath.Length > 0 && File.Exists(_outputPath))
        {
            File.Delete(_outputPath);
        }
    }
}

/// <summary>The output-hash pair at 730,000 records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class OutputHashPairWorking : OutputHashPairBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);
}

/// <summary>The output-hash pair at 7.3M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class OutputHashPairScale7M : OutputHashPairBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);
}
