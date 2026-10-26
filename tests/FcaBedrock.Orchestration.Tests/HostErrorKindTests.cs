using System.Text;
using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The closed set of host-failure conditions (D-140): one kind per existing message builder, each
/// error carrying the structured facts its builder received beside the unchanged text, and the
/// occupant outcome reported only after the target's own residue was recovered and after the
/// input-collision and directory checks.
/// </summary>
public sealed class HostErrorKindTests
{
    private const string Token = "aaaaaaaabbbbbbbbccccccccdddddddd";

    [Theory]
    [InlineData("ExistingTarget")]
    [InlineData("StaleManifest")]
    [InlineData("TargetDirectory")]
    [InlineData("ReadOnlyTarget")]
    [InlineData("ReadOnlyManifest")]
    [InlineData("InputCollision")]
    [InlineData("OutputCollision")]
    [InlineData("UnknownResidue")]
    [InlineData("RecoveryFailed")]
    [InlineData("RecordFailed")]
    [InlineData("StageFailed")]
    [InlineData("CommitFailed")]
    public void Factory_WhenEachPublicationBuilderIsClassified_ThenItsKindAndMessageAreTheBuildersOwn(string builder)
    {
        var blocker = new CleanupFact(CleanupStep.Restore, PublicationCause.InUse, "out.cxt.fcabedrock-backup-" + Token);

        (PublicationError Error, HostErrorKind Kind, string Message, string? Subject, string? Counterpart,
            PublicationCause? Cause, CleanupFact? Blocker) expected = builder switch
        {
            "ExistingTarget" => (PublicationErrors.ExistingTarget("out.cxt"), HostErrorKind.TargetOccupied,
                PublicationMessages.ExistingTarget("out.cxt"), "out.cxt", null, null, null),
            "StaleManifest" => (PublicationErrors.StaleManifest("out.manifest.toml"), HostErrorKind.StaleManifest,
                PublicationMessages.StaleManifest("out.manifest.toml"), "out.manifest.toml", null, null, null),
            "TargetDirectory" => (PublicationErrors.TargetDirectory("out.cxt"), HostErrorKind.TargetIsDirectory,
                PublicationMessages.TargetDirectory("out.cxt"), "out.cxt", null, null, null),
            "ReadOnlyTarget" => (PublicationErrors.ReadOnlyTarget("out.cxt"), HostErrorKind.TargetReadOnly,
                PublicationMessages.ReadOnlyTarget("out.cxt"), "out.cxt", null, PublicationCause.ReadOnly, null),
            "ReadOnlyManifest" => (PublicationErrors.ReadOnlyManifest("out.manifest.toml"), HostErrorKind.ManifestReadOnly,
                PublicationMessages.ReadOnlyManifest("out.manifest.toml"), "out.manifest.toml", null, PublicationCause.ReadOnly, null),
            "InputCollision" => (PublicationErrors.InputCollision("out.cxt", "data.csv"), HostErrorKind.InputCollision,
                PublicationMessages.InputCollision("out.cxt", "data.csv"), "out.cxt", "data.csv", null, null),
            "OutputCollision" => (PublicationErrors.OutputCollision("out.cxt", "out.dat"), HostErrorKind.OutputCollision,
                PublicationMessages.OutputCollision("out.cxt", "out.dat"), "out.cxt", "out.dat", null, null),
            "UnknownResidue" => (PublicationErrors.UnknownResidue("out"), HostErrorKind.UnknownResidue,
                PublicationMessages.UnknownResidue("out"), "out", null, null, null),
            "RecoveryFailed" => (PublicationErrors.RecoveryFailed("out", blocker), HostErrorKind.RecoveryFailed,
                PublicationMessages.RecoveryFailed("out", blocker), "out", null, null, blocker),
            "RecordFailed" => (PublicationErrors.RecordFailed("out", PublicationCause.FolderMissing), HostErrorKind.PublicationControlFailed,
                PublicationMessages.RecordFailed("out", PublicationCause.FolderMissing), "out", null, PublicationCause.FolderMissing, null),
            "StageFailed" => (PublicationErrors.StageFailed("out.cxt"), HostErrorKind.StageFailed,
                PublicationMessages.StageFailed("out.cxt"), "out.cxt", null, null, null),
            _ => (PublicationErrors.CommitFailed("out.cxt", PublicationCause.InUse), HostErrorKind.CommitFailed,
                PublicationMessages.CommitFailed("out.cxt", PublicationCause.InUse), "out.cxt", null, PublicationCause.InUse, null),
        };

        var host = expected.Error.ToHostError();
        Assert.Equal(expected.Kind, host.Kind);
        Assert.Equal(expected.Message, host.Message);
        Assert.Equal(expected.Subject, host.Subject);
        Assert.Equal(expected.Counterpart, host.Counterpart);
        Assert.Equal(expected.Cause, host.Cause);
        Assert.Same(expected.Blocker, host.Blocker);
    }

    [Fact]
    public async Task Preflight_WhenAFileOccupiesTheTargetWithoutReplacement_ThenTheOccupantIsReportedAfterItsResidueIsRecovered()
    {
        // A prior single-file run left a provable stage: recovery removes it first, and only then is
        // the occupant refused, so an occupant reported this way is not residue of its own.
        using var run = await PublicationRun.CreateAsync(PublicationRun.SpecFile);
        PublicationRun.Occupy(run.Target);
        LeaveProvableStage(run);

        var error = run.Refused();

        Assert.Equal(HostErrorKind.TargetOccupied, error.Kind);
        Assert.Equal(run.Target, error.Subject);
        Assert.Empty(run.Residue());
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
    }

    [Theory]
    [InlineData(PublicationRun.Convert)]
    [InlineData(PublicationRun.SpecFile)]
    public async Task Preflight_WhenReplacementIsAuthorized_ThenAnOccupantIsReplacedAsBefore(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);

        Assert.IsType<OperationCompleted<Published>>(await run.PublishAsync(run.Preflight(replace: true)));

        Assert.NotEqual(PublicationRun.Old, File.ReadAllText(run.Target));
        Assert.Empty(run.Residue());
    }

    [Theory]
    [InlineData(PublicationRun.Convert)]
    [InlineData(PublicationRun.SpecFile)]
    public async Task Preflight_WhenADirectoryIsAtTheTarget_ThenItIsNotReportedAsAnOccupant(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        Directory.CreateDirectory(run.Target);

        var error = run.Refused();

        Assert.Equal(HostErrorKind.TargetIsDirectory, error.Kind);
        Assert.Equal(run.Target, error.Subject);
    }

    [Fact]
    public async Task Preflight_WhenTheTargetIsAnInputByIdentity_ThenItIsNotReportedAsAnOccupant()
    {
        // The collision check precedes the occupant refusal: an input at the target is a collision,
        // with or without replacement.
        using var run = await PublicationRun.CreateAsync(PublicationRun.SpecFile);

        var outcome = SpecPublication.Preflight(
            new SpecDelivery { Target = run.DataPath, ReplaceExisting = false, Operands = [run.DataPath] },
            run.Host,
            run.Signals.Token);

        var error = Assert.IsType<OperationHostFailure<PreflightedSpecFile>>(outcome).Error;
        Assert.Equal(HostErrorKind.InputCollision, error.Kind);
        Assert.Equal(run.DataPath, error.Subject);
        Assert.Equal(run.DataPath, error.Counterpart);
        Assert.Empty(run.Mutations());
    }

    [Theory]
    [InlineData(PublicationRun.Convert)]
    [InlineData(PublicationRun.SpecFile)]
    public async Task Preflight_WhenTheOccupantIsReported_ThenItsMessageIsTheExistingTargetText(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);

        var error = run.Refused();

        Assert.Equal(HostErrorKind.TargetOccupied, error.Kind);
        Assert.Equal($"the output '{run.Target}' already exists; use --force to replace it.", error.Message);
        Assert.Null(error.Cause);
        Assert.Null(error.Blocker);
    }

    [Fact]
    public async Task Commit_WhenARenameIsRefusedWithAnEstablishedCause_ThenTheErrorCarriesThatCause()
    {
        // A refusal every platform classifies the same way: access denied.
        using var run = await PublicationRun.CreateAsync(PublicationRun.SpecFile);
        run.Files.FailKind = "Move";
        run.Files.FailMoveTo = Path.GetFileName(run.Target);
        run.Files.FailWith = static () => new UnauthorizedAccessException("injected refusal");

        var failed = Assert.IsType<OperationHostFailure<Published>>(await run.PublishAsync(run.Preflight()));

        Assert.Equal(HostErrorKind.CommitFailed, failed.Error.Kind);
        Assert.Equal(PublicationCause.AccessDenied, failed.Error.Cause);
        Assert.Equal(run.Target, failed.Error.Subject);
        Assert.Equal(PublicationMessages.CommitFailed(run.Target, PublicationCause.AccessDenied), failed.Error.Message);
        Assert.False(File.Exists(run.Target));
    }

    [Fact]
    public async Task Preflight_WhenRecoveryCannotFinish_ThenTheErrorCarriesTheFirstBlocker()
    {
        // The prior stage's removal fails for an environment reason, so recovery cannot finish: the
        // refusal names the step that stopped it, by file name only, and the stage is preserved.
        using var run = await PublicationRun.CreateAsync(PublicationRun.SpecFile);
        LeaveProvableStage(run);
        var stage = Path.GetFileName(run.Target) + ".fcabedrock-stage-" + Token;
        run.Files.FailDeletePrefix = stage;

        var error = run.Refused();

        Assert.Equal(HostErrorKind.RecoveryFailed, error.Kind);
        var blocker = Assert.IsType<CleanupFact>(error.Blocker);
        Assert.Equal(CleanupStep.Remove, blocker.Step);
        Assert.Equal(stage, blocker.EntryName);
        Assert.Equal(PublicationMessages.RecoveryFailed(run.Base, blocker), error.Message);
        Assert.True(File.Exists(Path.Combine(run.Directory, stage)));
    }

    [Theory]
    [InlineData("temporary directory")]
    [InlineData("spec unreadable")]
    [InlineData("data unreadable")]
    [InlineData("data changed")]
    public async Task Preparation_WhenEachHostFailureOccurs_ThenItsKindAndSubjectAreSet(string failure)
    {
        using var temp = TempDirectory.Create();
        const string Observed = """
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
        var spec = temp.Write("spec.toml", Observed);
        var data = temp.Write("data.csv", "colour\nred\n");
        var passes = 0;
        var host = new OrchestrationHost(path =>
        {
            // For the changing input: each data pass reads different bytes.
            if (failure == "data changed" && path == data)
            {
                var text = passes++ == 0 ? "colour\nred\n" : "colour\ngreen\n";
                return new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false);
            }

            return File.OpenRead(path);
        });

        var request = failure switch
        {
            "temporary directory" => new FileRunRequest { SpecPath = spec, DataPath = data, TempDirectory = " " },
            "spec unreadable" => new FileRunRequest { SpecPath = temp.Resolve("missing.toml"), DataPath = data },
            "data unreadable" => new FileRunRequest { SpecPath = spec, DataPath = temp.Resolve("missing.csv") },
            _ => new FileRunRequest { SpecPath = spec, DataPath = data },
        };

        var outcome = await RunPreparation.PrepareAsync(request, host, CancellationToken.None);

        var error = Assert.IsType<OperationHostFailure<PreparedFileRun>>(outcome).Error;
        var (kind, subject) = failure switch
        {
            "temporary directory" => (HostErrorKind.TemporaryDirectoryInvalid, (string?)null),
            "spec unreadable" => (HostErrorKind.SpecUnreadable, request.SpecPath),
            "data unreadable" => (HostErrorKind.DataUnreadable, request.DataPath),
            _ => (HostErrorKind.DataChanged, request.DataPath),
        };
        Assert.Equal(kind, error.Kind);
        Assert.Equal(subject, error.Subject);
        Assert.Null(error.Counterpart);
        Assert.Null(error.Cause);
        Assert.Null(error.Blocker);
    }

    [Theory]
    [InlineData("other root")]
    [InlineData("unverified")]
    public void Relocation_WhenRefused_ThenTheKindDistinguishesAnotherRootFromAnUnverifiedReference(string refusal)
    {
        // Another drive or share has no relative reference at all; a reference that does not reach
        // the loaded object is unverified.
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("base.toml", "[spec]\nversion = 1\n");
        var immediateBase = new SpecChainFile(basePath, "base.toml", "sha256:unused", FileIdentity.CreateDefault().KeyFor(basePath));
        var host = new OrchestrationHost(File.OpenRead);

        string destination;
        if (refusal == "other root")
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows has a second root to name without creating it.");
            var root = Path.GetPathRoot(basePath)!;
            var other = char.ToUpperInvariant(root[0]) == 'Z' ? "Y:" : "Z:";
            destination = other + "\\elsewhere\\out.toml";
        }
        else
        {
            // The loaded base's object is replaced at its path, so neither reference verifies. It is
            // moved aside, not deleted: a deleted file's identifier can be handed to the replacement
            // (on ext4 it often is, D-125), which would then verify as the loaded base. While the
            // original exists no other file can have its identity, and the assertions check that.
            var aside = temp.Resolve("base.toml.aside");
            File.Move(basePath, aside);
            File.WriteAllText(basePath, "[spec]\nversion = 1\n");
            var identities = FileIdentity.CreateDefault();
            Assert.Equal(immediateBase.Identity, identities.KeyFor(aside));
            Assert.NotEqual(immediateBase.Identity, identities.KeyFor(basePath));
            destination = temp.Resolve("nested/out.toml");
        }

        var outcome = SpecRelocation.Relocate("base.toml", destination, immediateBase, host);

        var error = Assert.IsType<OperationHostFailure<VerifiedRelocation>>(outcome).Error;
        Assert.Equal(refusal == "other root" ? HostErrorKind.RelocationOtherRoot : HostErrorKind.RelocationUnverified, error.Kind);
        Assert.Equal(destination, error.Subject);
        Assert.Equal("base.toml", error.Counterpart);
        Assert.Empty(outcome.Diagnostics);
    }

    [Fact]
    public void HostErrorKind_ShouldHaveNoMemberWithTheValueZero()
    {
        Assert.DoesNotContain(0, Enum.GetValues<HostErrorKind>().Select(kind => (int)kind));
        Assert.Equal(18, Enum.GetValues<HostErrorKind>().Length);
    }

    // A prior single-file run that crashed after creating its stage: the record, the stage and the
    // stage's own valid claim, so the stage is provable and recovery may remove it.
    private static void LeaveProvableStage(PublicationRun run)
    {
        var name = Path.GetFileName(run.Target);
        var residue = Residue.Create(run.Directory, name, Token);
        residue.WriteRecord([("stage", name)]);
        residue.WritePrivate("stage", name, "half-written");
        residue.WriteStageClaim("s", name);
    }
}
