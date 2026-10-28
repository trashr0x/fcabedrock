using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// One prepared, fully declared run in a temporary directory, published through either
/// publication family's <b>public</b> operations, with the recording publication filesystem and a
/// cancellable host token. The artifacts family writes <c>out.cxt</c> (and, when asked,
/// <c>out.dat</c>) with a manifest; the single-file family writes the run's root document to
/// <c>out.toml</c>.
/// </summary>
internal sealed class PublicationRun : IDisposable
{
    /// <summary>The artifacts family.</summary>
    public const string Convert = "convert";

    /// <summary>The single-file family.</summary>
    public const string SpecFile = "spec";

    /// <summary>The bytes an existing target holds before a replacement, so a restore can be checked.</summary>
    public const string Old = "the old file";

    /// <summary>A fully declared wide spec: it plans with no data pass beyond the schema read.</summary>
    public const string Spec = """
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
        declared_domain = ["red", "green"]
        """;

    /// <summary>The data the spec binds.</summary>
    public const string Data = "colour,size\nred,1\ngreen,2\n";

    private readonly TempDirectory _temp;

    private PublicationRun(
        TempDirectory temp,
        string family,
        RecordingPublicationFileSystem files,
        OrchestrationHost host,
        string specPath,
        string dataPath,
        (PreparedFileRun Value, IReadOnlyList<BedrockDiagnostic> Diagnostics) prepared,
        OperationProgress? progress)
    {
        _temp = temp;
        Family = family;
        Files = files;
        Host = host;
        SpecPath = specPath;
        DataPath = dataPath;
        Progress = progress;
        Run = prepared.Value;
        Diagnostics = prepared.Diagnostics;
        Base = family == Convert ? temp.Resolve("out") : temp.Resolve("out.toml");
        Conversion = OutcomeAssert.Completed(ConvertPublication.PlanConversion(Run, v2Compatible: false, Diagnostics)).Value;
    }

    /// <summary><see cref="Convert"/> or <see cref="SpecFile"/>.</summary>
    public string Family { get; }

    /// <summary>The injected publication filesystem.</summary>
    public RecordingPublicationFileSystem Files { get; }

    /// <summary>The host every operation of this run uses.</summary>
    public OrchestrationHost Host { get; }

    /// <summary>The exact host token's source.</summary>
    public CancellationTokenSource Signals { get; } = new();

    /// <summary>The directory every file of the run lives in.</summary>
    public string Directory => _temp.Path;

    /// <summary>The spec the run read.</summary>
    public string SpecPath { get; }

    /// <summary>The data the run read.</summary>
    public string DataPath { get; }

    /// <summary>The operation progress the run's chain carries, or null when it is not observed (D-141).</summary>
    public OperationProgress? Progress { get; }

    /// <summary>The prepared run.</summary>
    public PreparedFileRun Run { get; }

    /// <summary>The preparation's diagnostics.</summary>
    public IReadOnlyList<BedrockDiagnostic> Diagnostics { get; }

    /// <summary>The native conversion of <see cref="Run"/>.</summary>
    public EffectiveConversion Conversion { get; }

    /// <summary>The output base, or the single file's own path: what the private names start with.</summary>
    public string Base { get; }

    /// <summary>The base's file name, the prefix of every private name of this run.</summary>
    public string BaseName => Path.GetFileName(Base);

    /// <summary>The first file this run publishes: the <c>.cxt</c>, or the spec file itself.</summary>
    public string Target => Family == Convert ? Base + ".cxt" : Base;

    /// <summary>The manifest host facts every convert publish of this run uses.</summary>
    public ManifestHostFacts Facts { get; } = new()
    {
        ToolVersion = "fcabedrock-vnext test",
        CommandLine = ["fcabedrock", "convert"],
        UtcNow = static () => new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
    };

    /// <summary>
    /// Prepares a run of <paramref name="family"/>, reading through <paramref name="open"/> when one is
    /// given. A <paramref name="progress"/> observes the preparation and, for the single-file family,
    /// the preflight; the artifacts family's handle carries it from the run.
    /// </summary>
    public static async Task<PublicationRun> CreateAsync(
        string family, Func<string, Stream>? open = null, OperationProgress? progress = null)
    {
        var temp = TempDirectory.Create();
        var files = new RecordingPublicationFileSystem();
        var host = new OrchestrationHost(open ?? File.OpenRead) { PublicationFiles = files };
        var specPath = temp.Write("spec.toml", Spec);
        var dataPath = temp.Write("data.csv", Data);
        var prepared = OutcomeAssert.Completed(
            await RunPreparation.PrepareAsync(
                new FileRunRequest { SpecPath = specPath, DataPath = dataPath }, host, progress, CancellationToken.None));
        return new PublicationRun(temp, family, files, host, specPath, dataPath, prepared, progress);
    }

    /// <summary>Writes <see cref="Old"/> at <paramref name="path"/>: an existing target.</summary>
    public static void Occupy(string path) => File.WriteAllText(path, Old);

    /// <summary>The preflight result of the run's family, boxed and unasserted.</summary>
    public object PreflightOutcome(bool replace = false, bool writesDat = false) =>
        Family == Convert
            ? ConvertPublication.Preflight(
                new ConvertOutput
                {
                    Base = Base,
                    WritesCxt = true,
                    WritesDat = writesDat,
                    WritesManifest = true,
                    ReplaceExisting = replace,
                },
                Run,
                Host,
                Signals.Token)
            : SpecPublication.Preflight(
                new SpecDelivery
                {
                    Target = Base,
                    ReplaceExisting = replace,
                    Operands = [SpecPath, DataPath],
                    Chain = Run.SpecChain,
                },
                Host,
                Progress,
                Signals.Token);

    /// <summary>A completed preflight's handle.</summary>
    public PreflightedPublication Preflight(bool replace = false, bool writesDat = false) =>
        PreflightOutcome(replace, writesDat) switch
        {
            Result<PreflightedConvertOutput, HostError> { IsOk: true } convert => convert.Value,
            Result<PreflightedSpecFile, HostError> { IsOk: true } spec => spec.Value,
            var other => throw new InvalidOperationException($"the preflight did not complete: {Describe(other)}"),
        };

    /// <summary>A refused preflight's error; a host-only result carries no diagnostic list.</summary>
    public HostError Refused(bool replace = false, bool writesDat = false)
    {
        switch (PreflightOutcome(replace, writesDat))
        {
            case Result<PreflightedConvertOutput, HostError> { IsError: true } convert:
                return convert.Error;
            case Result<PreflightedSpecFile, HostError> { IsError: true } spec:
                return spec.Error;
            case var other:
                throw new InvalidOperationException($"the preflight was not refused: {Describe(other)}");
        }
    }

    /// <summary>
    /// Publishes <paramref name="handle"/> through its family's public operation and returns that
    /// family's result, boxed: <see cref="OutcomeAssert.Committed"/> and
    /// <see cref="OutcomeAssert.PublishHostFailed"/> read it.
    /// </summary>
    public async Task<object> PublishAsync(PreflightedPublication handle) => handle switch
    {
        PreflightedConvertOutput convert =>
            await ConvertPublication.PublishAsync(Conversion, convert, Facts, Diagnostics, Signals.Token),
        PreflightedSpecFile spec => await SpecPublication.PublishAsync(Run.RootDocument, spec, Signals.Token),
        _ => throw new InvalidOperationException("Unknown handle."),
    };

    /// <summary>Every mutating operation the recording filesystem saw, in order.</summary>
    public List<string> Mutations() =>
        [.. Files.Operations.Where(operation =>
            operation.StartsWith("CreateNew:", StringComparison.Ordinal)
            || operation.StartsWith("Confidential:", StringComparison.Ordinal)
            || operation.StartsWith("Move:", StringComparison.Ordinal)
            || operation.StartsWith("Delete:", StringComparison.Ordinal))];

    /// <summary>The directory's file names, ordinal.</summary>
    public List<string> Names() =>
        [.. System.IO.Directory.EnumerateFiles(Directory).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    /// <summary>Every private entry of this run's namespace.</summary>
    public List<string> Residue() =>
        [.. Names().Where(name => name.Contains(".fcabedrock-", StringComparison.Ordinal))];

    /// <summary>
    /// The Windows release observation: the publication seam's own removal marks the object with the
    /// classic handle disposition, which takes effect when the object's <em>last</em> handle closes
    /// (D-125 part 3). While a reference is still open the name stays occupied and a create there
    /// is refused; once nothing holds it, the name is free at once. A plain path delete is no such
    /// observation: current Windows frees the name immediately whatever handles remain.
    /// </summary>
    public static void AssertNoReferenceHeld(string path)
    {
        Assert.True(PublicationFileSystem.Instance.Remove(path, static (_, _) => true));
        File.WriteAllText(path, "the next occupant");
        Assert.Equal("the next occupant", File.ReadAllText(path));
    }

    /// <summary>The same observation's positive control: a reference is still open on the object at <paramref name="path"/>.</summary>
    public static void AssertReferenceHeld(string path)
    {
        Assert.True(PublicationFileSystem.Instance.Remove(path, static (_, _) => true));
        Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(path, "the next occupant"));
    }

    /// <summary>The reason a release observation skips off Windows.</summary>
    public const string WindowsOnly =
        "the release observation needs the Windows disposition that keeps a deleted name occupied while a handle is open.";

    public void Dispose()
    {
        Signals.Dispose();
        _temp.Dispose();
    }

    private static string Describe(object outcome) => outcome switch
    {
        Result<PreflightedConvertOutput, HostError> { IsError: true } failure => failure.Error.Message,
        Result<PreflightedSpecFile, HostError> { IsError: true } failure => failure.Error.Message,
        _ => outcome.GetType().Name,
    };
}
