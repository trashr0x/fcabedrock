using System.Text;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>A clock that never moves unless a test moves it.</summary>
internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
}

/// <summary>A signal source a test cancels directly, without raising a real signal.</summary>
internal sealed class TestSignalSource : ISignalSource
{
    private readonly CancellationTokenSource _source = new();

    public CancellationToken Token => _source.Token;

    public bool Disposed { get; private set; }

    public void Cancel() => _source.Cancel();

    public void Dispose()
    {
        Disposed = true;
        _source.Dispose();
    }
}

/// <summary>Records the progress events the host brackets each command with.</summary>
internal sealed class RecordingProgressObserver : IProgressObserver
{
    public List<string> Events { get; } = [];

    public void CommandStarted(string command) => Events.Add($"started:{command}");

    public void CommandCompleted(string command, int exitCode) => Events.Add($"completed:{command}:{exitCode}");
}

/// <summary>
/// Drives <see cref="CliHost.RunAsync"/> with a fully injected world: captured writers, a
/// fixed clock and version, a cancellable signal source, and a recording input opener.
/// Nothing here touches the console, and no test depends on wall-clock time, the machine's
/// culture, or a real signal.
/// </summary>
internal sealed class CliTestHarness
{
    private readonly StringWriter _out = new(new StringBuilder());
    private readonly StringWriter _error = new(new StringBuilder());

    /// <summary>The version string the injected environment reports.</summary>
    public string ToolVersion { get; set; } = "fcabedrock-vnext 9.9.9-test";

    /// <summary>The complete audit argv, argv[0] included.</summary>
    public IReadOnlyList<string> AuditArgv { get; set; } = ["fcabedrock"];

    /// <summary>Replaces the input opener; the default opens real files.</summary>
    public Func<string, Stream>? OpenInput { get; set; }

    /// <summary>Replaces the captured stdout writer — used to inject sink failures.</summary>
    public TextWriter? OutOverride { get; set; }

    /// <summary>Replaces the captured stderr writer — used to inject sink failures.</summary>
    public TextWriter? ErrorOverride { get; set; }

    /// <summary>Every path the CLI asked to open, in order — one entry per open, not per distinct path.</summary>
    public List<string> Opened { get; } = [];

    /// <summary>
    /// The publication filesystem the run uses. It is the real one, wrapped so a test can watch
    /// the exact sequence of operations and fail any single boundary.
    /// </summary>
    public RecordingPublicationFileSystem PublicationFiles { get; } = new();

    public TestClock Clock { get; } = new();

    public TestSignalSource Signals { get; } = new();

    public RecordingProgressObserver Progress { get; } = new();

    /// <summary>Everything written to stdout.</summary>
    public string StdOut => _out.ToString();

    /// <summary>Everything written to stderr.</summary>
    public string StdErr => _error.ToString();

    /// <summary>The injected environment.</summary>
    public CliEnvironment Environment => new()
    {
        Out = OutOverride ?? _out,
        Error = ErrorOverride ?? _error,
        Clock = Clock,
        Signals = Signals,
        ToolVersion = ToolVersion,
        AuditArgv = AuditArgv,
        OpenInput = Open,
        PublicationFiles = PublicationFiles,
        Progress = Progress,
    };

    /// <summary>Runs the CLI at the argv boundary.</summary>
    public Task<int> RunAsync(params string[] argv) => CliHost.RunAsync(argv, Environment);

    private Stream Open(string path)
    {
        Opened.Add(path);
        return (OpenInput ?? CliEnvironment.OpenFile)(path);
    }
}

/// <summary>
/// A text sink that fails the way a closed pipe or released handle does — immediately on
/// write, or only when the buffered bytes are finally flushed.
/// </summary>
internal sealed class ThrowingWriter(bool failOnWrite, bool failOnFlush) : TextWriter
{
    private readonly StringWriter _accepted = new();

    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Everything the sink accepted before failing.</summary>
    public string Accepted => _accepted.ToString();

    public override void Write(char value)
    {
        if (failOnWrite)
        {
            throw new IOException("the pipe has been ended.");
        }

        _accepted.Write(value);
    }

    public override void Write(string? value)
    {
        if (failOnWrite)
        {
            throw new IOException("the pipe has been ended.");
        }

        _accepted.Write(value);
    }

    public override void Flush()
    {
        if (failOnFlush)
        {
            throw new IOException("the pipe has been ended.");
        }

        _accepted.Flush();
    }
}

/// <summary>
/// The real publication filesystem with two test affordances: it records every operation in
/// order, and it can be told to fail one boundary — the Nth operation of a given kind, optionally
/// only for a chosen file name.
/// <para>
/// It delegates to the production implementation rather than simulating a filesystem, so a test
/// that injects a commit failure still observes the real bytes, the real renames, and the real
/// residue afterwards.
/// </para>
/// </summary>
internal sealed class RecordingPublicationFileSystem : IPublicationFileSystem
{
    private readonly IPublicationFileSystem _real = PublicationFileSystem.Instance;
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>Every mutating or observing operation, as <c>kind:fileName</c>, in order.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>The operation kind to fail — <c>CreateNew</c>, <c>Flush</c>, <c>Move</c>, or <c>Delete</c>.</summary>
    public string? FailKind { get; set; }

    /// <summary>When set, only operations whose (source) file name matches this are counted and failed.</summary>
    public string? FailName { get; set; }

    /// <summary>
    /// As <see cref="FailName"/>, but matched as a prefix — how a test names a control file or a
    /// stage whose token it cannot predict.
    /// </summary>
    public string? FailNamePrefix { get; set; }

    /// <summary>When set, only moves whose destination file name matches this are counted and failed.</summary>
    public string? FailMoveTo { get; set; }

    /// <summary>Which matching occurrence to fail, counting from 1.</summary>
    public int FailOccurrence { get; set; } = 1;

    /// <summary>
    /// Fail <b>every</b> matching occurrence rather than only <see cref="FailOccurrence"/>. A
    /// commit rename and the rollback restore that follows it share a destination, so failing one
    /// occurrence exercises only the commit — the interesting case is when the restore fails too.
    /// </summary>
    public bool FailEveryMatch { get; set; }

    /// <summary>The exception a matching injected failure raises; an I/O failure by default.</summary>
    public Func<Exception> FailWith { get; set; } = static () => new IOException("injected failure");

    /// <summary>
    /// Fails creation of any file whose name starts with this, independently of
    /// <see cref="FailKind"/> — so a commit failure and a phase-marker failure can be injected in
    /// the same run.
    /// </summary>
    public string? FailCreateNewPrefix { get; set; }

    /// <summary>
    /// Fails <b>removal</b> of any file whose name starts with this, independently of
    /// <see cref="FailKind"/> — how a test reaches one specific control-file removal whose
    /// position in the run it cannot count.
    /// <para>
    /// A removal is one seam call, <c>Remove(path, proof)</c>, so this fails that call by the name
    /// of the object it would have removed.
    /// </para>
    /// </summary>
    public string? FailDeletePrefix { get; set; }

    /// <summary>The exception a failing stream write raises; an I/O failure by default.</summary>
    public Func<Exception> FailStreamWith { get; set; } =
        static () => new IOException("there is not enough space on the disk.");

    /// <summary>Invoked immediately after a successful move to <see cref="CancelAfterMoveTo"/>.</summary>
    public Action? CancelAfterMove { get; set; }

    /// <summary>The exact destination file name whose successful move triggers <see cref="CancelAfterMove"/>.</summary>
    public string? CancelAfterMoveTo { get; set; }

    /// <summary>
    /// The destination file-name <b>prefix</b> whose successful move triggers
    /// <see cref="CancelAfterMove"/> — how a test names a backup, whose token is unpredictable.
    /// </summary>
    public string? CancelAfterMoveToPrefix { get; set; }

    /// <summary>The created file-name prefix whose stream fails on its first write.</summary>
    public string? FailStreamWritePrefix { get; set; }

    /// <summary>The created file-name prefix whose stream fails when it is closed.</summary>
    public string? FailStreamClosePrefix { get; set; }

    /// <summary>The exception a failing stream close raises; an I/O failure by default.</summary>
    public Func<Exception> FailStreamCloseWith { get; set; } =
        static () => new IOException("the file could not be closed.");

    /// <summary>
    /// The operation to run <see cref="Mutate"/> immediately <b>before</b>, in the folded
    /// <c>kind:fileName</c> form used in <see cref="Operations"/>.
    /// <para>
    /// This is how a test creates a genuine race: something else changes the location between the
    /// transaction's last look and its next move — a target that appears, one that disappears, one
    /// replaced by a different object — and the run must survive it without destroying anything it
    /// does not own.
    /// </para>
    /// </summary>
    public string? MutateBefore { get; set; }

    /// <summary>What to do at <see cref="MutateBefore"/>; it runs at most once.</summary>
    public Action? Mutate { get; set; }

    /// <summary>
    /// How many times a deterministic substitution actually fired.
    /// <para>
    /// A substitution test that silently never raced proves nothing, and an <em>outcome</em> cannot
    /// tell the two apart — before the lifetime correction, whether a given variant failed depended
    /// on the allocator's history rather than on whether the hook ran. So the tests that place a
    /// race assert that it happened.
    /// </para>
    /// </summary>
    public int MutationsFired { get; private set; }

    /// <summary>
    /// As <see cref="Mutate"/>, but handed the <b>unfolded</b> operation — so a test can place a
    /// race at a path whose token this run generated and has not written anywhere yet.
    /// <para>
    /// That is a deliberately stronger adversary than the filesystem affords: it learns the name at
    /// the instant of the call rather than by reading the directory. It is what keeps a refused
    /// acquisition testable now that nothing durable precedes it.
    /// </para>
    /// </summary>
    public Action<string>? MutateWith { get; set; }

    /// <summary>
    /// The exception a residue READ raises, and which operation raises it — how a test reaches the
    /// classification path's own failure families. The name is matched as a prefix, so
    /// a test can name a control file whose token it cannot predict.
    /// </summary>
    public Func<Exception>? FailReadWith { get; set; }

    /// <summary>The read operation to fail: <c>EnumerateFiles</c>, <c>ReadBounded</c>, or <c>Exists</c>.</summary>
    public string? FailReadKind { get; set; }

    /// <summary>When set, only reads whose file name starts with this are failed.</summary>
    public string? FailReadNamePrefix { get; set; }

    /// <summary>
    /// Report no identity for a created stage, as a host with no filesystem-identity capability
    /// does. Everything else stays real, so the run meets exactly the situation such a host
    /// creates: it can still create the file, but it can prove nothing about what it created.
    /// </summary>
    public bool SuppressStageIdentity { get; set; }

    /// <summary>
    /// The same capability absence for <b>control</b> creations — the pending transaction record
    /// and each pending evidence file — so the root of the transaction can be shown to fail closed
    /// rather than publishing something it cannot prove.
    /// </summary>
    public bool SuppressControlIdentity { get; set; }

    /// <summary>
    /// Report no lifetime reference for an <b>existing</b> participant, as a host that cannot hold
    /// one does. Identity that cannot be anchored is identity this protocol will not act on, so a
    /// forced replacement fails closed before anything is created or moved.
    /// </summary>
    public bool SuppressReferences { get; set; }

    /// <summary>
    /// Report no lifetime reference for <b>one</b> participant — the objects whose file name starts
    /// with this — while every other acquisition, path observation and removal open still succeeds.
    /// <para>
    /// A run-wide suppression can only show a pass refusing before it began. What a recovery pass
    /// has to be held to is narrower and harder: <em>this</em> object cannot be held, everything
    /// else can, and the pass must still mutate nothing — including the participants it could have
    /// held and would otherwise have reached first.
    /// </para>
    /// </summary>
    public string? SuppressReferenceNamePrefix { get; set; }

    /// <summary>
    /// How many acquisitions <see cref="SuppressReferences"/> or
    /// <see cref="SuppressReferenceNamePrefix"/> actually refused.
    /// <para>
    /// A test that suppresses a reference and then observes an unchanged location proves nothing
    /// unless the suppression fired, and an outcome cannot tell the two apart — the same reason
    /// <see cref="MutationsFired"/> exists.
    /// </para>
    /// </summary>
    public int ReferencesSuppressed { get; private set; }

    /// <summary>
    /// The operation to simulate a crash after, as the <c>kind:fileName</c> form used in
    /// <see cref="Operations"/>. The real operation completes, and every later operation then
    /// fails — which is what the on-disk state looks like when the process simply disappears:
    /// nothing that follows, including rollback, can change anything.
    /// </summary>
    public string? CrashAfter { get; set; }

    /// <summary>True once <see cref="CrashAfter"/> fired.</summary>
    public bool Crashed { get; private set; }

    /// <inheritdoc/>
    public bool Exists(string path)
    {
        var name = Path.GetFileName(path);
        Observe("Exists:" + name);
        FailRead("Exists", name);
        return _real.Exists(path);
    }

    /// <inheritdoc/>
    public CreatedFile CreateNew(string path)
    {
        var name = Path.GetFileName(path);
        Fail("CreateNew", path, label: "CreateNew");
        var file = _real.CreateNew(path);
        if (SuppressControlIdentity)
        {
            // A host with no identity capability reports none AND anchors nothing — the two go
            // together, because an identity is only reported when a reference holds it.
            file.Reference?.Dispose();
            file = file with { Identity = null, Reference = null };
        }

        CrashIfRequested($"CreateNew:{name}");
        return file with { Content = Wrap(file.Content, name) };
    }

    /// <inheritdoc/>
    public CreatedFile CreateNewConfidential(string path)
    {
        var name = Path.GetFileName(path);

        // Recorded under its own label so a test can prove WHICH creation the transaction asked
        // for — the confidentiality boundary is a property of the call, not of the bytes.
        // The injectable failure kind stays `CreateNew` so failure injection is
        // unaffected by the distinction.
        Fail("CreateNew", path, label: "Confidential");
        var stage = _real.CreateNewConfidential(path);
        if (SuppressStageIdentity)
        {
            stage.Reference?.Dispose();
            stage = stage with { Identity = null, Reference = null };
        }

        StageIdentities[name] = stage.Identity;
        CrashIfRequested($"Confidential:{name}");
        return stage with { Content = Wrap(stage.Content, name) };
    }

    /// <summary>
    /// The identity each confidential creation reported, by stage file name — how a test proves
    /// the evidence a run publishes describes the object that creation produced.
    /// </summary>
    public Dictionary<string, FileIdentityKey?> StageIdentities { get; } = new(StringComparer.Ordinal);

    // Every created stream is wrapped, so a crash can be placed at a genuine STREAM boundary —
    // partial write, flush, close — and not merely at the filesystem-seam calls around it.
    private Stream Wrap(Stream stream, string name) =>
        new FaultyStream(
            stream,
            this,
            name,
            Matches(FailStreamWritePrefix, name),
            Matches(FailStreamClosePrefix, name));

    private static bool Matches(string? prefix, string name) =>
        prefix is not null && name.StartsWith(prefix, StringComparison.Ordinal);

    /// <inheritdoc/>
    public void Flush(Stream stream)
    {
        Fail("Flush", string.Empty);
        _real.Flush(stream);
        CrashIfRequested("Flush:");
    }

    /// <inheritdoc/>
    public PublicationObjectReference? TryAcquire(string path)
    {
        var name = Path.GetFileName(path);
        Observe("Acquire:" + name);
        FailRead("Acquire", name);

        if (SuppressReferences || Matches(SuppressReferenceNamePrefix, name))
        {
            ReferencesSuppressed++;
            return null;
        }

        return _real.TryAcquire(path);
    }

    /// <inheritdoc/>
    public void Move(string source, string destination, PublicationObjectReference? sourceReference = null)
    {
        var target = Path.GetFileName(destination);
        Fail("Move", source, target);
        _real.Move(source, destination, sourceReference);
        CrashIfRequested($"Move:{Path.GetFileName(source)}->{target}");

        if (string.Equals(CancelAfterMoveTo, target, StringComparison.Ordinal) || Matches(CancelAfterMoveToPrefix, target))
        {
            CancelAfterMove?.Invoke();
        }
    }

    /// <inheritdoc/>
    public bool Remove(string path, RemovalProof isExpected)
    {
        // Recorded and raced as `Delete:<name>` — this IS the deletion boundary, and the mutation
        // hook runs before the real removal opens anything, which is exactly the same-operation
        // substitution this rule is about. Production must then refuse the replacement, because
        // its proof is taken from the handle it deletes through rather than from an earlier look.
        Fail("Delete", path);
        var removed = _real.Remove(path, isExpected);
        CrashIfRequested($"Delete:{Path.GetFileName(path)}");
        return removed;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateFiles(string directory, string namePrefix)
    {
        Observe("EnumerateFiles:" + namePrefix);
        FailRead("EnumerateFiles", namePrefix);
        return _real.EnumerateFiles(directory, namePrefix);
    }

    /// <inheritdoc/>
    public byte[]? ReadBounded(string path, int maxBytes)
    {
        var name = Path.GetFileName(path);
        Observe("ReadBounded:" + name);
        FailRead("ReadBounded", name);
        return _real.ReadBounded(path, maxBytes);
    }

    private void FailRead(string kind, string name)
    {
        if (FailReadWith is { } failure
            && string.Equals(FailReadKind, kind, StringComparison.Ordinal)
            && (FailReadNamePrefix is null || name.StartsWith(FailReadNamePrefix, StringComparison.Ordinal)))
        {
            throw failure();
        }
    }

    private void Fail(string kind, string path, string? destination = null, string? label = null)
    {
        // Once the simulated crash has fired, nothing else reaches the disk — the process is
        // conceptually gone, so even rollback cannot run.
        if (Crashed)
        {
            throw new IOException("the process crashed");
        }

        var name = Path.GetFileName(path);
        var recorded = label ?? kind;
        var operation = destination is null ? $"{recorded}:{name}" : $"{recorded}:{name}->{destination}";
        Operations.Add(operation);
        MutateIfRequested(operation);

        // Independent of FailKind, so a marker-creation failure can be combined with a commit
        // failure in one run.
        if (string.Equals(kind, "CreateNew", StringComparison.Ordinal) && Matches(FailCreateNewPrefix, name))
        {
            throw FailWith();
        }

        if (string.Equals(kind, "Delete", StringComparison.Ordinal) && Matches(FailDeletePrefix, name))
        {
            throw FailWith();
        }

        if (!string.Equals(FailKind, kind, StringComparison.Ordinal)
            || (FailName is not null && !string.Equals(FailName, name, StringComparison.Ordinal))
            || (FailNamePrefix is not null && !Matches(FailNamePrefix, name))
            || (FailMoveTo is not null && !string.Equals(FailMoveTo, destination, StringComparison.Ordinal)))
        {
            return;
        }

        _counts.TryGetValue(kind, out var seen);
        _counts[kind] = ++seen;
        if (FailEveryMatch || seen == FailOccurrence)
        {
            throw FailWith();
        }
    }

    /// <summary>
    /// <paramref name="operation"/> with every run token folded to <c>T</c>, so a transition can
    /// be named — and asserted, and crashed after — without the test knowing an unpredictable
    /// value.
    /// </summary>
    public static string Fold(string operation)
    {
        var folded = new StringBuilder(operation.Length);
        for (var i = 0; i < operation.Length;)
        {
            if (i + PublicationTargets.TokenLength <= operation.Length
                && PublicationTargets.IsToken(operation.Substring(i, PublicationTargets.TokenLength)))
            {
                folded.Append('T');
                i += PublicationTargets.TokenLength;
                continue;
            }

            folded.Append(operation[i++]);
        }

        return folded.ToString();
    }

    /// <summary>
    /// Records a stream-level boundary — a first write, a flush, a close — and crashes after it if
    /// asked. These are transitions of their own: a record or a stage stops being empty and starts
    /// being partial at exactly one of them.
    /// </summary>
    internal void Observe(string operation)
    {
        Operations.Add(operation);
        MutateIfRequested(operation);
        CrashIfRequested(operation);
    }

    // Fires once: a race is something that happens at one instant, and re-running it at every
    // later matching operation would be a different scenario.
    private void MutateIfRequested(string operation)
    {
        if (!string.Equals(MutateBefore, Fold(operation), StringComparison.Ordinal))
        {
            return;
        }

        var mutate = Mutate;
        var mutateWith = MutateWith;
        Mutate = null;
        MutateWith = null;

        if (mutate is not null || mutateWith is not null)
        {
            MutationsFired++;
        }

        mutate?.Invoke();
        mutateWith?.Invoke(operation);
    }

    // Called after the real operation succeeded, so the directory holds exactly the state an
    // abrupt termination at that instant would leave.
    internal void CrashIfRequested(string operation)
    {
        if (string.Equals(CrashAfter, Fold(operation), StringComparison.Ordinal))
        {
            Crashed = true;
        }
    }
}

/// <summary>
/// A destination stream that can fail the way a full disk or a revoked handle does — on the first
/// write, or only when the buffered bytes are finally closed out — and that reports its own write,
/// flush, and close boundaries so a crash can be placed at one of them.
/// <para>
/// Once the owning filesystem has crashed, every operation on the stream fails too: a process that
/// disappeared cannot finish writing a file it had open.
/// </para>
/// </summary>
internal sealed class FaultyStream(
    Stream inner, RecordingPublicationFileSystem owner, string name, bool failWrite, bool failClose) : Stream
{
    private bool _written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush()
    {
        Refuse();
        inner.Flush();
        owner.Observe($"StreamFlush:{name}");
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Refuse();
        if (failWrite)
        {
            throw owner.FailStreamWith();
        }

        inner.Write(buffer, offset, count);
        Observe();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Refuse();
        if (failWrite)
        {
            throw owner.FailStreamWith();
        }

        inner.Write(buffer);
        Observe();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var crashed = owner.Crashed;

            // A crashed process stops WRITING but does not keep its handles: the operating system
            // reclaims them. Releasing here is what makes the simulation faithful — otherwise the
            // stage or record would stay locked and the retry would fail to clean it up for a
            // reason no real crash produces.
            try
            {
                inner.Dispose();
            }
            catch (IOException)
            {
            }

            if (!crashed)
            {
                owner.Observe($"StreamClose:{name}");
                if (failClose)
                {
                    throw owner.FailStreamCloseWith();
                }
            }
        }

        base.Dispose(disposing);
    }

    // The FIRST write is the interesting boundary: it is where a record or a stage stops being
    // empty and starts being partial.
    private void Observe()
    {
        if (!_written)
        {
            _written = true;
            owner.Observe($"StreamWrite:{name}");
        }
    }

    private void Refuse()
    {
        if (owner.Crashed)
        {
            throw new IOException("the process crashed");
        }
    }
}

/// <summary>A disposable temporary directory for tests that need real files on disk.</summary>
internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    /// <summary>The directory's full path.</summary>
    public string Path { get; }

    /// <summary>Creates a fresh, uniquely named temporary directory.</summary>
    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "fcabedrock-cli-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    /// <summary>Writes <paramref name="content"/> as UTF-8 without a byte-order mark and returns its full path.</summary>
    public string Write(string name, string content)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>The full path <paramref name="name"/> would have, without creating anything.</summary>
    public string Resolve(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
