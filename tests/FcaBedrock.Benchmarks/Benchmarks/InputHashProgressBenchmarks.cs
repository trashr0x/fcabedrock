using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Cli;
using FcaBedrock.Core.Spec;
using FcaBedrock.Orchestration;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// What observing passes costs on the path an operation progress touches per read: the hashed source
/// drain of <see cref="InputHashPairBenchmark"/> with its tracker unobserved, against the same drain
/// with the tracker observed by an <see cref="OperationProgress"/> (D-141).
/// <para>
/// <b>A pair over one build.</b> The unobserved arm is the off path every unobserved operation takes; the
/// observed arm adds the progress's construction, a call scope around the schema read and one around
/// the record pass, and its completion, all inside the measured interval. Nothing reads
/// <see cref="OperationProgress.Current"/> during it, and no emission adapter is involved, because the
/// drain enumerates records, not emitted objects.
/// </para>
/// <para>
/// The drain duplicates the hash pair's rather than sharing it, so no measured path of an existing case
/// moves. Every check runs after disposal and outside timing (D-124).
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Hash, BenchmarkCategories.Source, BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class InputHashProgressWorking
{
    private static readonly CorpusCase Corpus = CorpusCases.W16(CorpusTier.Working);

    private PreparedCorpus _corpus = null!;
    private SourceReadSettings _settings = null!;
    private string? _digest;
    private OperationProgress? _progress;
    private IReadOnlyList<(long Bytes, int Reads)> _opens = [];
    private (int Operation, int CompletePass)? _launchReads;

    /// <summary>
    /// Resolves the read settings, then drains once through a progress whose sink records the pass ends,
    /// to show the schema pass ends closed before end of input on this file. Outside every measured
    /// interval, and in both cases' processes, so the two arms meet the same warm file.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _corpus = CorpusPreparer.Require(Corpus);
        var session = await ConversionPipeline.OpenSessionAsync(_corpus.SpecPath, _corpus.DataPath)
            .ConfigureAwait(false);
        _settings = ((WideCsvSession)session).ReadSettings;

        var ends = new PassEnds();
        var progress = new OperationProgress { Events = ends };
        var tracker = new InputHashTracker(CliEnvironment.OpenFile, _corpus.DataPath, progress);
        _ = await DrainObservedAsync(tracker).ConfigureAwait(false);
        progress.Complete();
        if (!ends.Recorded.SequenceEqual([(PassKind.Schema, PassEnd.ClosedBeforeEndOfInput), (PassKind.Statistics, PassEnd.EndOfInput)]))
        {
            throw new InvalidOperationException(
                $"input hash progress ({Corpus.Id}): the observed drain recorded the passes "
                + $"[{string.Join(", ", ends.Recorded)}], expected a schema pass closed before end of input, "
                + "then a statistics pass that reached it.");
        }
    }

    /// <summary>Discards the previous iteration's observations. Outside timing.</summary>
    [IterationSetup]
    public void Reset()
    {
        _digest = null;
        _progress = null;
        _opens = [];
    }

    /// <summary>One hashed source pass with the tracker unobserved: the baseline arm.</summary>
    [Benchmark(Description = "source pass, hashed", Baseline = true)]
    public async Task<long> Hashed()
    {
        var tracker = new InputHashTracker(CliEnvironment.OpenFile, _corpus.DataPath);
        var read = await DrainAsync(tracker.OpenHashed).ConfigureAwait(false);
        _digest = tracker.Digest;
        return read;
    }

    /// <summary>The identical pass with the tracker observed by a fresh operation progress.</summary>
    [Benchmark(Description = "source pass, hashed and observed")]
    public async Task<long> HashedObserved()
    {
        var progress = new OperationProgress();
        var tracker = new InputHashTracker(CliEnvironment.OpenFile, _corpus.DataPath, progress);
        var read = await DrainObservedAsync(tracker).ConfigureAwait(false);
        progress.Complete();
        _digest = tracker.Digest;
        _progress = progress;
        return read;
    }

    /// <summary>Validates the unobserved arm's digest and read calls.</summary>
    [IterationCleanup(Target = nameof(Hashed))]
    public void ValidateHashed() => Validate("hashed");

    /// <summary>
    /// Validates the observed arm's digest and read calls, then its final snapshot: the second pass, of
    /// kind statistics, ended at end of input with every byte consumed, and completed, the same
    /// instance on every read.
    /// </summary>
    [IterationCleanup(Target = nameof(HashedObserved))]
    public void ValidateHashedObserved()
    {
        Validate("hashed and observed");

        var final = _progress?.Current;
        if (final is not { PassSequence: 2, PassKind: PassKind.Statistics, PassEnd: PassEnd.EndOfInput, IsCompleted: true }
            || final.BytesConsumed != _corpus.InputBytes
            || !ReferenceEquals(final, _progress!.Current))
        {
            throw new InvalidOperationException(
                $"input hash progress ({Corpus.Id}, hashed and observed): the final snapshot was "
                + $"{final?.ToString() ?? "absent"}, expected pass 2 of kind Statistics ended EndOfInput "
                + $"with {_corpus.InputBytes} bytes consumed, completed, and the same instance on every read.");
        }
    }

    /// <summary>
    /// The existing checks (the catalog's digest, one open that consumed the whole file), then the read
    /// calls: the first validation of a launch records the operation's count and its complete pass's,
    /// and every later one requires the same two numbers, so the arm read the same way every iteration.
    /// The counts go to standard output for the log; they are never parsed as a measurement.
    /// </summary>
    private void Validate(string arm)
    {
        var what = $"input hash progress ({Corpus.Id}, {arm})";
        if (!string.Equals(_digest, _corpus.Entry.Data.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{what}: the tracker reported {_digest ?? "no digest"}, expected {_corpus.Entry.Data.Sha256}.");
        }

        var complete = _opens.Where(open => open.Bytes == _corpus.InputBytes).ToList();
        if (complete.Count != 1)
        {
            throw new InvalidOperationException(
                $"{what}: {complete.Count} of {_opens.Count} stream open(s) consumed the file's "
                + $"{_corpus.InputBytes} bytes, expected exactly 1 (opens read [{string.Join(", ", _opens)}]).");
        }

        var reads = (Operation: _opens.Sum(open => open.Reads), CompletePass: complete[0].Reads);
        _launchReads ??= reads;
        if (reads != _launchReads)
        {
            throw new InvalidOperationException(
                $"{what}: {reads.Operation} wrapper read calls per operation, {reads.CompletePass} in the "
                + $"complete pass, where this launch's first iteration made {_launchReads.Value.Operation} "
                + $"and {_launchReads.Value.CompletePass}.");
        }

        Console.WriteLine(
            $"{what}: {reads.Operation} wrapper read calls per operation, {reads.CompletePass} in the complete pass");
    }

    // The observed arm's drain: the schema read under a call scope of kind Schema, and the record pass
    // under one of kind Statistics, a label with no effect on the read path.
    private async Task<long> DrainObservedAsync(InputHashTracker tracker)
    {
        var counting = new CountingOpen(tracker.OpenHashed);
        var session = (IWideSourceSession)ConversionPipeline.CreateSession(_settings, counting.Open);
        var schema = tracker.EnterScope(PassKind.Schema, postInputStep: 0, CancellationToken.None);
        try
        {
            _ = await session.GetSchemaAsync().ConfigureAwait(false);
        }
        finally
        {
            schema.Exit();
        }

        var fields = 0L;
        var records = tracker.EnterScope(PassKind.Statistics, postInputStep: 0, CancellationToken.None);
        try
        {
            await foreach (var record in session.ReadAsync().ConfigureAwait(false))
            {
                fields += record.FieldCount;
            }
        }
        finally
        {
            records.Exit();
        }

        _opens = counting.Opens;
        return fields;
    }

    // The unobserved arm's drain: the same production calls as the hash pair's.
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

    // Records each pass's kind and end, for the setup's check.
    private sealed class PassEnds : IProgressEvents
    {
        private readonly List<PassKind> _kinds = [];
        private readonly Dictionary<int, PassEnd> _ends = [];

        public List<(PassKind Kind, PassEnd End)> Recorded =>
            [.. _kinds.Select((kind, index) => (kind, _ends.GetValueOrDefault(index + 1)))];

        public void PassStarted(int sequence, PassKind kind, long? length) => _kinds.Add(kind);

        public void Consumed(int sequence, long total)
        {
        }

        public void Emitted(int sequence, long count)
        {
        }

        public void Closed(int sequence, bool endOfInput)
        {
        }

        public void PassEnded(int sequence, PassEnd reason) => _ends[sequence] = reason;

        public void Step(WorkingStep step)
        {
        }

        public void ScopeExited(int sequence, PassEnd end, WorkingStep step)
        {
            if (end != 0)
            {
                _ends[sequence] = end;
            }
        }

        public void Completed()
        {
        }
    }

    // Counts what the reader consumed and how many read calls it made, PER OPEN. It sits above the
    // hashing wrapper in both arms, so each of its reads is exactly one wrapper read, and its own cost is
    // common to the pair.
    private sealed class CountingOpen(Func<Stream> open)
    {
        private readonly List<(long Bytes, int Reads)> _opens = [];

        public IReadOnlyList<(long Bytes, int Reads)> Opens => _opens;

        public Stream Open()
        {
            _opens.Add((0, 0));
            return new CountingStream(open(), this, _opens.Count - 1);
        }

        private void Add(int index, int count)
        {
            var (bytes, reads) = _opens[index];
            _opens[index] = (bytes + count, reads + 1);
        }

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
