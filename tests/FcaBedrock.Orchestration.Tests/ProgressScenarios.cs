using System.Globalization;
using System.Text;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The host's input opener for the progress and cancellation suites: every file opens through it, and
/// each open of the data file is a <see cref="ScriptedStream"/> that counts what it served and lets a
/// test fail, cancel or hold one chosen read. Opens are recorded in order, so a test can assert that
/// an operation opened nothing.
/// </summary>
internal sealed class ScriptedInput(string dataPath)
{
    private readonly string _data = Path.GetFullPath(dataPath);
    private readonly Lock _gate = new();
    private readonly List<ScriptedStream> _dataOpens = [];
    private readonly List<string> _opened = [];

    /// <summary>Every data open, in order.</summary>
    public IReadOnlyList<ScriptedStream> DataOpens
    {
        get
        {
            lock (_gate)
            {
                return [.. _dataOpens];
            }
        }
    }

    /// <summary>Every path opened, data or spec, in order.</summary>
    public IReadOnlyList<string> Opened
    {
        get
        {
            lock (_gate)
            {
                return [.. _opened];
            }
        }
    }

    /// <summary>An exception to throw instead of opening <c>path</c>, or null to open it.</summary>
    public Func<string, Exception?>? FailOpen { get; set; }

    /// <summary>Runs before each data read: the stream and its read call, counting from 0.</summary>
    public Action<ScriptedStream, int>? BeforeRead { get; set; }

    /// <summary>Runs after each data read: the stream, its read call, what the read returned and what was requested.</summary>
    public Action<ScriptedStream, int, int, int>? AfterRead { get; set; }

    /// <summary>Runs when a data open is disposed, after its file closed.</summary>
    public Action<ScriptedStream>? AfterDispose { get; set; }

    /// <summary>The host every operation of a test uses, with <paramref name="files"/> as its publication filesystem.</summary>
    public OrchestrationHost Host(RecordingPublicationFileSystem files) => new(Open) { PublicationFiles = files };

    public Stream Open(string path)
    {
        lock (_gate)
        {
            _opened.Add(path);
        }

        if (FailOpen?.Invoke(path) is { } failure)
        {
            throw failure;
        }

        var inner = File.OpenRead(path);
        if (!string.Equals(Path.GetFullPath(path), _data, StringComparison.Ordinal))
        {
            return inner;
        }

        lock (_gate)
        {
            var stream = new ScriptedStream(inner, this, _dataOpens.Count);
            _dataOpens.Add(stream);
            return stream;
        }
    }

    internal void Before(ScriptedStream stream, int call) => BeforeRead?.Invoke(stream, call);

    internal void After(ScriptedStream stream, int call, int read, int requested) =>
        AfterRead?.Invoke(stream, call, read, requested);

    internal void Released(ScriptedStream stream) => AfterDispose?.Invoke(stream);
}

/// <summary>A seekable pass-through over one open of the data file that counts the bytes and read calls it served.</summary>
internal sealed class ScriptedStream(FileStream inner, ScriptedInput owner, int index) : Stream
{
    private readonly long _fileLength = inner.Length;
    private long _bytes;
    private int _reads;

    /// <summary>This open's position among the data opens, counting from 0.</summary>
    public int Index => index;

    /// <summary>The bytes this open served.</summary>
    public long Bytes => Volatile.Read(ref _bytes);

    /// <summary>The read calls this open served.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>The file's length, taken when it was opened.</summary>
    public long FileLength => _fileLength;

    /// <summary>True once disposed.</summary>
    public bool Disposed { get; private set; }

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var call = _reads;
        owner.Before(this, call);
        var read = inner.Read(buffer);
        Interlocked.Add(ref _bytes, read);
        Interlocked.Increment(ref _reads);
        owner.After(this, call, read, buffer.Length);
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    protected override void Dispose(bool disposing)
    {
        if (disposing && !Disposed)
        {
            Disposed = true;
            inner.Dispose();
            owner.Released(this);
        }

        base.Dispose(disposing);
    }
}

/// <summary>The specs and data the progress and cancellation suites run.</summary>
internal static class ProgressSpecs
{
    /// <summary>A fully declared wide spec over <c>colour,size</c>: no calibration pass.</summary>
    public const string WideDeclared = PublicationRun.Spec;

    /// <summary>The same wide spec with the domain omitted, so calibration reads the data once.</summary>
    public const string WideCalibrated = """
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

    /// <summary>A wide <c>dedupe</c> key over <c>key,colour</c>: emission groups its input.</summary>
    public const string WideDedupe = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [defaults]
        duplicate_object_policy = "dedupe"

        [binding.object_key]
        mode = "column"
        column = 0

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>A declared triple spec, <c>unordered</c>: emission groups its input.</summary>
    public const string TripleUnordered = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "unordered"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "colour"
        source = { kind = "predicate", name = "colour" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>The same triple spec, <c>subject_grouped</c>: emission streams.</summary>
    public const string TripleGrouped = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "subject_grouped"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "colour"
        source = { kind = "predicate", name = "colour" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>An unordered triple count-sensitive spec: calibration makes a raw pass and a grouped pass.</summary>
    public const string TripleCountSensitive = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "unordered"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "age"
        source = { kind = "predicate", name = "age", value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 2 }
        scale = { kind = "nominal" }
        """;

    /// <summary>A wide source of <paramref name="records"/> records for <see cref="WideDeclared"/> and <see cref="WideCalibrated"/>.</summary>
    public static string WideData(int records)
    {
        var text = new StringBuilder("colour,size\n");
        for (var i = 0; i < records; i++)
        {
            text.Append(i % 2 == 0 ? "red," : "green,").Append(i.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A <see cref="WideDedupe"/> source: <paramref name="prefix"/> records over repeated keys, then,
    /// when <paramref name="offender"/>, one record whose key is empty (unusable), then
    /// <paramref name="suffix"/> more valid records.
    /// </summary>
    public static string DedupeData(int prefix, bool offender, int suffix)
    {
        var text = new StringBuilder("key,colour\n");
        for (var i = 0; i < prefix + suffix; i++)
        {
            if (offender && i == prefix)
            {
                text.Append(",red\n");
            }

            text.Append('k').Append((i % 50).ToString(CultureInfo.InvariantCulture)).Append(i % 3 == 0 ? ",green\n" : ",red\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// A triple source over the <c>colour</c> predicate: <paramref name="prefix"/> interleaved rows,
    /// then, when <paramref name="offender"/>, one row with an empty subject, then
    /// <paramref name="suffix"/> more rows. With <paramref name="contiguous"/> each subject's rows are
    /// adjacent, as <c>subject_grouped</c> requires.
    /// </summary>
    public static string TripleData(int prefix, bool offender, int suffix, bool contiguous = false)
    {
        var text = new StringBuilder();
        for (var i = 0; i < prefix + suffix; i++)
        {
            if (offender && i == prefix)
            {
                text.Append(",colour,red\n");
            }

            var subject = contiguous ? i : i % 40;
            text.Append('s').Append(subject.ToString(CultureInfo.InvariantCulture)).Append(i % 2 == 0 ? ",colour,red\n" : ",colour,green\n");
        }

        return text.ToString();
    }

    /// <summary>A <see cref="TripleCountSensitive"/> source: one numeric <c>age</c> observation per subject.</summary>
    public static string AgeData(int subjects)
    {
        var text = new StringBuilder();
        for (var i = 0; i < subjects; i++)
        {
            text.Append('s').Append(i.ToString(CultureInfo.InvariantCulture)).Append(",age,").Append((i % 97).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A stand-in run: <paramref name="real"/>'s parts with <paramref name="emit"/> as its emitter, built
    /// through the internal constructor, so a test controls what the emit enumeration does without a
    /// Conversion seam. It shares <paramref name="real"/>'s tracker, so the passes it opens are real
    /// tracker passes.
    /// </summary>
    public static PreparedFileRun StandIn(PreparedFileRun real, PreparedRunEmitter emit) =>
        new(
            real.RootDocument,
            real.RootKey,
            real.Resolved,
            real.Calibrated,
            real.Shape,
            real.Plan,
            real.Fingerprints,
            real.Output,
            real.SpecChain,
            emit,
            real.Input,
            real.SpecPath,
            real.DataPath);

    /// <summary>An emitted object named <paramref name="name"/> that crosses nothing.</summary>
    public static EmittedObject Object(string name) => new(name, []);
}

/// <summary>A one-shot hold: the held side signals that it arrived and waits to be released.</summary>
internal sealed class Hold : IDisposable
{
    private readonly ManualResetEventSlim _arrived = new();
    private readonly ManualResetEventSlim _released = new();

    /// <summary>Called on the operation's thread: signals arrival, then blocks until released.</summary>
    public void Wait()
    {
        _arrived.Set();
        _released.Wait(TimeSpan.FromMinutes(2));
    }

    /// <summary>Called on the test's thread: waits until the operation arrived.</summary>
    public void WaitForArrival() => Assert.True(_arrived.Wait(TimeSpan.FromMinutes(2)), "the operation never reached the hold");

    /// <summary>Releases the held operation.</summary>
    public void Release() => _released.Set();

    public void Dispose()
    {
        _released.Set();
        _arrived.Dispose();
        _released.Dispose();
    }
}

/// <summary>
/// One temporary directory holding a spec and its data, with the scripted opener, the recording
/// publication filesystem, a cancellable host token and, when observed, a progress with its recorder.
/// The convert writes <c>out.cxt</c>, <c>out.dat</c> and <c>out.manifest.toml</c> in the directory.
/// </summary>
internal sealed class ProgressRun : IDisposable
{
    private readonly TempDirectory _temp;

    private ProgressRun(TempDirectory temp, string specPath, string dataPath, bool observed)
    {
        _temp = temp;
        SpecPath = specPath;
        DataPath = dataPath;
        Input = new ScriptedInput(dataPath);
        Host = Input.Host(Files);
        if (observed)
        {
            (Progress, Recorder) = ProgressRecorder.Create();
        }
    }

    public string Directory => _temp.Path;

    public string SpecPath { get; }

    public string DataPath { get; }

    public ScriptedInput Input { get; }

    public RecordingPublicationFileSystem Files { get; } = new();

    public OrchestrationHost Host { get; }

    public OperationProgress? Progress { get; }

    public ProgressRecorder? Recorder { get; }

    public CancellationTokenSource Signals { get; } = new();

    public string Base => Path.Combine(Directory, "out");

    public ManifestHostFacts Facts { get; } = new()
    {
        ToolVersion = "fcabedrock-vnext test",
        CommandLine = ["fcabedrock", "convert"],
        UtcNow = static () => new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
    };

    public static ProgressRun Create(string spec, string data, bool observed)
    {
        var temp = TempDirectory.Create();
        return new ProgressRun(temp, temp.Write("spec.toml", spec), temp.Write("data.csv", data), observed);
    }

    public Task<OperationOutcome<PreparedFileRun>> PrepareOutcomeAsync() =>
        RunPreparation.PrepareAsync(new FileRunRequest { SpecPath = SpecPath, DataPath = DataPath }, Host, Progress, Signals.Token);

    public async Task<(PreparedFileRun Run, IReadOnlyList<BedrockDiagnostic> Diagnostics)> PrepareAsync()
    {
        var completed = Assert.IsType<OperationCompleted<PreparedFileRun>>(await PrepareOutcomeAsync());
        return (completed.Value, completed.Diagnostics);
    }

    public ConvertOutput Output(bool cxt = true, bool dat = true, bool manifest = true, bool replace = false) =>
        new() { Base = Base, WritesCxt = cxt, WritesDat = dat, WritesManifest = manifest, ReplaceExisting = replace };

    public PreflightedConvertOutput Preflight(PreparedFileRun run, ConvertOutput output) =>
        Assert.IsType<OperationCompleted<PreflightedConvertOutput>>(
            ConvertPublication.Preflight(output, run, Host, Signals.Token)).Value;

    public static EffectiveConversion Plan(PreparedFileRun run, IReadOnlyList<BedrockDiagnostic> diagnostics, bool v2 = false) =>
        Assert.IsType<OperationCompleted<EffectiveConversion>>(ConvertPublication.PlanConversion(run, v2, diagnostics)).Value;

    public Task<OperationOutcome<Published>> PublishAsync(
        EffectiveConversion conversion, PreflightedConvertOutput handle, IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        ConvertPublication.PublishAsync(conversion, handle, Facts, diagnostics, Signals.Token);

    /// <summary>Completes the progress, as the owner does after its last call, and checks the recorded grammar.</summary>
    public void CompleteAndAssertGrammar(PreparedFileRun? run)
    {
        if (Progress is null)
        {
            return;
        }

        Progress.Complete();
        Recorder!.AssertGrammar(observedOpens: run?.Input.ObservedOpens ?? 0);
    }

    public List<string> Names() =>
        [.. System.IO.Directory.EnumerateFiles(Directory).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    public List<string> Residue() => [.. Names().Where(name => name.Contains(".fcabedrock-", StringComparison.Ordinal))];

    public void Dispose()
    {
        Signals.Dispose();
        _temp.Dispose();
    }
}
