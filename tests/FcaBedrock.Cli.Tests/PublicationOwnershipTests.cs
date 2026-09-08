using System.Text;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The exact-object protocol: a derived private name predicts a
/// <em>path</em>, and only a successful create-new, a verified rename result, or a durable identity
/// digest says which <em>object</em> is at it.
/// <para>
/// Every case here is driven through the real argv boundary and the real filesystem, with the
/// injected seam used only to place a genuine race — a file that appears at a destination between
/// the transaction's last look and its next move, or in the middle of the move itself. What is
/// asserted is the files, their bytes, the exit code, the stderr line, and what a plain retry then
/// does; never merely the order of the calls.
/// </para>
/// </summary>
public sealed class PublicationOwnershipTests
{
    private const string Keep = "keep me";

    // ---- a refused stage acquisition writes nothing that names the occupant ----------------------

    [Fact]
    public async Task Publication_WhenAStageAcquisitionIsRefused_ThenNoDurableStateNamesTheOccupant()
    {
        // The claim is written AFTER the create-new succeeds and carries that object's identity. A
        // refusal therefore writes no claim at all — which is what stops a later recovery from
        // deleting the very file whose presence caused the refusal.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Confidential:out.cxt.fcabedrock-stage-T";
        run.Harness.PublicationFiles.MutateWith = operation =>
        {
            var stage = operation["Confidential:".Length..];
            File.Delete(Path.Combine(run.Directory, stage));
            File.WriteAllText(Path.Combine(run.Directory, stage), Keep);
        };

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));

        Assert.DoesNotContain(
            run.Harness.PublicationFiles.Operations,
            operation => operation.StartsWith("CreateNew:out.fcabedrock-sc-", StringComparison.Ordinal));

        var occupant = Single(run.Directory, "out.cxt.fcabedrock-stage-*");
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));

        // The record stays, because a path the transaction cannot account for is still occupied:
        // the state is classifiable rather than looking like a clean base with a stray file in it.
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));

        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
    }

    [Fact]
    public async Task Publication_WhenTheProcessStopsBetweenAStageAndItsClaim_ThenNothingIsRemovedOnAGuess()
    {
        // THE undecidable interval: the acquisition has succeeded and nothing durable says so yet.
        // On disk this is indistinguishable from "an object was already there and refused the
        // create-new" — the two histories leave the same bytes at the same name. Ownership is not
        // inferred from the length, the name, or the token: nothing is removed, the record survives
        // so the state stays classifiable, and the run says plainly that it cannot finish the
        // clean-up. The previous set is intact throughout.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
        var before = run.Snapshot();

        var crashing = new CliTestHarness();
        crashing.PublicationFiles.CrashAfter = "Confidential:out.cxt.fcabedrock-stage-T";
        await crashing.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");
        Assert.True(crashing.PublicationFiles.Crashed, "the crash after the stage creation never fired");

        var stage = Single(run.Directory, "out.cxt.fcabedrock-stage-*");
        Assert.DoesNotContain(
            crashing.PublicationFiles.Operations,
            operation => operation.StartsWith("CreateNew:out.fcabedrock-sc-", StringComparison.Ordinal));

        // Deterministic, and destructive of nothing: every retry reaches the same answer.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                1,
                await retry.RunAsync(
                    "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
                retry.StdErr);

            Assert.True(File.Exists(stage), "the unprovable object was removed on a guess");
            AssertSameFiles(before, run.Snapshot());
        }
    }

    [Fact]
    public async Task Publication_WhenAStageClaimCannotBeCreated_ThenTheStageItAcquiredIsTakenBackOut()
    {
        // The claim's own name carries a runtime identity digest, so no later run could predict it.
        // A stage acquired but never claimed must therefore be removed by the run that made it,
        // through the same ownership-safe path everything else uses.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailCreateNewPrefix = "out.fcabedrock-sc-";

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot write the output '{run.Target(".cxt")}'."),
            run.Harness.StdErr);

        Assert.Empty(run.Residue());
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    // ---- pending control acquisitions -------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenThePendingRecordPathIsOccupied_ThenTheOccupantIsPreserved()
    {
        // The descriptor is written AFTER the acquisition and names the object that creation
        // produced. A refusal therefore writes no descriptor at all, and nothing left behind can
        // authorize removing the occupant that caused it.
        //
        // Reaching this at all needs the operation-aware race hook: at the instant of the call,
        // this run's token exists nowhere on disk, so an ordinary adversary could not name the path
        // it is about to create. The test is handed the name anyway — a deliberately stronger
        // adversary than the filesystem affords.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "CreateNew:out.fcabedrock-pending-T";
        run.Harness.PublicationFiles.MutateWith = operation =>
            File.WriteAllText(Path.Combine(run.Directory, operation["CreateNew:".Length..]), Keep);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));

        Assert.DoesNotContain(
            run.Harness.PublicationFiles.Operations,
            operation => operation.StartsWith("CreateNew:out.fcabedrock-intent-", StringComparison.Ordinal));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot start publication for the output base '{run.Base}'."),
            run.Harness.StdErr);

        var occupant = Single(run.Directory, "out.fcabedrock-pending-*");
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
        Assert.False(File.Exists(run.Target(".cxt")));

        // A plain retry meets it as an unknown lookalike and still refuses to touch it.
        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
    }

    [Theory]
    [InlineData(Keep)]

    // An occupant that is EXACTLY the document the descriptor's own digest names — the sharpest
    // form of "grammar and prefix knowledge are not proof". Nothing about its content is wrong;
    // only its identity is.
    [InlineData(null)]
    public async Task Publication_WhenASurvivingIntentDoesNotNameTheOccupant_ThenItIsPreserved(string? content)
    {
        // A descriptor outliving its run authorizes removing exactly one thing: the object whose
        // identity its name states. A different object at that path — however plausible its bytes —
        // is not that object, so it stays and the run says the clean-up cannot finish.
        using var run = ConvertRun.Wide();
        var token = new string('a', 32);
        var intent = WriteIntent(
            run.Directory, "out", token, [("stage", "out.cxt")], new string('b', 32));

        var pending = Path.Combine(run.Directory, PublicationTargets.PendingRecordName("out", token));
        var record = TransactionRecord.Create(token, "out", [new TransactionFileEntry("stage", "out.cxt")]);
        await File.WriteAllBytesAsync(
            pending, content is null ? record.ToBytes() : Encoding.UTF8.GetBytes(content));

        var expected = await File.ReadAllBytesAsync(pending);

        // Deterministic: the answer is the same every time, and it destroys nothing.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var harness = new CliTestHarness();
            Assert.Equal(
                1, await harness.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
                harness.StdErr);

            Assert.Equal(expected, await File.ReadAllBytesAsync(pending));
            Assert.True(File.Exists(intent));
            Assert.False(File.Exists(run.Target(".cxt")));
        }
    }

    [Fact]
    public async Task Publication_WhenThePendingEvidencePathIsOccupied_ThenRollbackLeavesItAlone()
    {
        // Reachable without any crash at all: the collision refuses the create-new, evidence
        // publication fails, and the rollback that follows must not delete the collision.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "CreateNew:out.fcabedrock-ep-c-T";
        run.Harness.PublicationFiles.Mutate = () =>
            File.WriteAllText(Path.Combine(run.Directory, $"out.fcabedrock-ep-c-{TokenOf(run.Directory)}"), Keep);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));

        var occupant = Single(run.Directory, "out.fcabedrock-ep-c-*");
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
        Assert.False(File.Exists(run.Target(".cxt")));

        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
    }

    // ---- authoritative evidence and phase markers -------------------------------------------------

    [Fact]
    public async Task Publication_WhenTheAuthoritativeEvidencePathIsOccupied_ThenTheOccupantIsPreserved()
    {
        // The publishing rename refuses the destination; cleanup must not reverse that safety by
        // deleting the object that refused it.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Move:out.fcabedrock-ep-c-T->out.fcabedrock-e-c-T";
        run.Harness.PublicationFiles.Mutate = () =>
            File.WriteAllText(Path.Combine(run.Directory, $"out.fcabedrock-e-c-{TokenOf(run.Directory)}"), Keep);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));

        var occupant = Single(run.Directory, "out.fcabedrock-e-c-*");
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenTheCommittedMarkerPathIsOccupied_ThenTheSuccessfulRunLeavesItAlone()
    {
        // The sharpest form: every artifact commits, the run exits 0, and forward cleanup must
        // still not delete a file whose presence its own create-new refused.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "CreateNew:out.fcabedrock-committed-T";
        run.Harness.PublicationFiles.Mutate = () =>
            File.WriteAllText(
                Path.Combine(run.Directory, $"out.fcabedrock-committed-{TokenOf(run.Directory)}"), Keep);

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.True(File.Exists(run.Target(".manifest.toml")));

        var occupant = Single(run.Directory, "out.fcabedrock-committed-*");
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
    }

    [Theory]
    [InlineData("staged")]
    [InlineData("rollback")]
    public async Task Publication_WhenAPhaseMarkerPathIsOccupied_ThenTheOccupantSurvivesTheRollback(string phase)
    {
        // `staged` stops the seal; `rollback` stops the rollback from starting at all.
        // Either way the occupant is preserved.
        using var run = ConvertRun.Wide();
        if (string.Equals(phase, "rollback", StringComparison.Ordinal))
        {
            run.Harness.PublicationFiles.FailKind = "Move";
            run.Harness.PublicationFiles.FailMoveTo = "out.cxt";
        }

        run.Harness.PublicationFiles.MutateBefore = $"CreateNew:out.fcabedrock-{phase}-T";
        run.Harness.PublicationFiles.Mutate = () =>
            File.WriteAllText(
                Path.Combine(run.Directory, $"out.fcabedrock-{phase}-{TokenOf(run.Directory)}"), Keep);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));

        var occupant = Single(run.Directory, $"out.fcabedrock-{phase}-*");
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    // ---- the stage claim is authoritative by its exact content ------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_WhenAnAcknowledgedClaimIsReplacedByAnEmptyObject_ThenNeitherItNorItsStageGoes(
        bool alsoEmptyStage)
    {
        // The accepted counterexample. A valid Preparing record, its surviving stage, and its
        // acknowledged claim — and then the claim OBJECT is replaced by an unrelated empty file at
        // the same path, before discovery ever runs.
        //
        // The digest in that name is the only thing that would authorize deleting the stage beside
        // it, and the replacement carries none of the claim's authority. So the stage is not
        // removed, the replacement is not removed, and the run refuses with the location exactly
        // as it was found.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", new string('a', 32));
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", alsoEmptyStage ? string.Empty : "half-written");
        var claim = residue.WriteStageClaim("c", "out.cxt");

        // Same name, same length, different object and no authority.
        File.Delete(claim);
        await File.WriteAllBytesAsync(claim, []);

        var stage = residue.PrivatePath("stage", "out.cxt");
        var stageBytes = await File.ReadAllBytesAsync(stage);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var harness = new CliTestHarness();
            Assert.Equal(
                1, await harness.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Equal(string.Empty, harness.StdOut);
            Assert.Contains(
                "unrecognized fcabedrock transaction residue", harness.StdErr, StringComparison.Ordinal);

            Assert.True(File.Exists(claim), $"attempt {attempt + 1} removed the replacement claim");
            Assert.Empty(await File.ReadAllBytesAsync(claim));
            Assert.True(File.Exists(stage), $"attempt {attempt + 1} removed the stage on a substituted claim");
            Assert.Equal(stageBytes, await File.ReadAllBytesAsync(stage));
            Assert.True(File.Exists(residue.RecordPath));
            Assert.False(File.Exists(run.Target(".cxt")));
        }
    }

    [Fact]
    public async Task Publication_WhenAClaimIsReplacedInsideItsOwnRemoval_ThenTheReplacementSurvives()
    {
        // The same substitution at the other boundary: inside the claim's actual Delete. The
        // removal's proof is read from the handle it holds, so the fresh empty object is what the
        // proof sees — and it is not this transaction's claim.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Delete:out.fcabedrock-sc-c-T-T";
        run.Harness.PublicationFiles.MutateWith = operation =>
        {
            var path = Path.Combine(run.Directory, operation["Delete:".Length..]);
            File.Delete(path);
            File.WriteAllBytes(path, []);
        };

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
        Assert.Null(run.Harness.PublicationFiles.MutateWith);

        var occupant = Single(run.Directory, "out.fcabedrock-sc-c-*");
        Assert.Empty(await File.ReadAllBytesAsync(occupant));

        // The published run stands, and what survives is classifiable: the record is still there.
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("token")]
    [InlineData("base")]
    [InlineData("record")]
    [InlineData("control")]
    [InlineData("stage")]
    [InlineData("partial")]
    public async Task Publication_WhenAClaimsBodyIsNotItsOwnAuthority_ThenItIsRefusedAndPreserved(string field)
    {
        // Every field the claim binds, and a body that simply did not land whole. None of them is
        // this transaction's claim for this target kind and this identity, so none is trusted or
        // removed — and the stage beside it stays put.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", new string('a', 32));
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");

        var digest = residue.Identity("stage", "out.cxt");
        var claim = residue.StageClaimPath("c", digest);
        var body = new UTF8Encoding(false).GetString(residue.ControlBody("claim-c", digest));

        var forged = field switch
        {
            "token" => body.Replace(new string('a', 32), new string('b', 32), StringComparison.Ordinal),
            "base" => body.Replace("base = \"out\"", "base = \"other\"", StringComparison.Ordinal),
            "record" => body.Replace(residue.RecordDigest, new string('c', 32), StringComparison.Ordinal),
            "control" => body.Replace("\"claim-c\"", "\"claim-d\"", StringComparison.Ordinal),
            "stage" => body.Replace($"stage = \"{digest}\"", $"stage = \"{new string('d', 32)}\"", StringComparison.Ordinal),
            _ => body[..(body.Length / 2)],
        };

        await File.WriteAllTextAsync(claim, forged, new UTF8Encoding(false));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var harness = new CliTestHarness();
            Assert.Equal(
                1, await harness.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Contains(
                "unrecognized fcabedrock transaction residue", harness.StdErr, StringComparison.Ordinal);

            Assert.Equal(forged, await File.ReadAllTextAsync(claim));
            Assert.Equal("half-written", await File.ReadAllTextAsync(residue.PrivatePath("stage", "out.cxt")));
        }
    }

    [Theory]

    // Created, nothing written: not a claim, and nothing else says the acquisition happened.
    [InlineData("CreateNew:out.fcabedrock-sc-c-T-T", false)]

    // The body is one write, so from that call onwards it is on disk whole — the claim is genuine
    // at each of these, and a retry finishes the interrupted run rather than refusing it.
    [InlineData("StreamWrite:out.fcabedrock-sc-c-T-T", true)]
    [InlineData("StreamFlush:out.fcabedrock-sc-c-T-T", true)]
    [InlineData("StreamClose:out.fcabedrock-sc-c-T-T", true)]
    public async Task Publication_WhenAClaimIsInterruptedAtEachBoundary_ThenOnlyTheAcknowledgedOneConverges(
        string transition, bool acknowledged)
    {
        // Create, write, flush and close are four distinct crash boundaries. A state whose body
        // landed whole is a claim and converges automatically; one whose body did not is
        // unacknowledged and falls to the fail-closed interval — preserved, refused, and
        // byte-identical on every retry.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CrashAfter = transition;

        await run.ConvertAsync("--format", "cxt");
        Assert.True(run.Harness.PublicationFiles.Crashed, $"the crash after '{transition}' never fired");

        var claim = Single(run.Directory, "out.fcabedrock-sc-c-*");
        var bytes = await File.ReadAllBytesAsync(claim);

        if (acknowledged)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                0, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Empty(run.Residue());
            return;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(claim));
            Assert.False(File.Exists(run.Target(".cxt")));
        }
    }

    [Fact]
    public async Task Publication_WhenAClaimRemovalFailsWithIo_ThenTheCommittedRunStandsAndRetryFinishes()
    {
        // The environment family at the claim's removal: best-effort tidy-up past the commit point,
        // so the run publishes and what could not go stays owned until a retry finishes it.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailDeletePrefix = "out.fcabedrock-sc-c-";

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-sc-c-", StringComparison.Ordinal));

        var retry = new CliTestHarness();
        Assert.Equal(
            0,
            await retry.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAClaimRemovalRaisesAContractFault_ThenItIsAnInternalFault()
    {
        // And the contract family at the same boundary reaches the sanitized unexpected-fault exit.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailDeletePrefix = "out.fcabedrock-sc-c-";
        run.Harness.PublicationFiles.FailWith = static () => new ObjectDisposedException("publication");

        Assert.Equal(4, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError("an unexpected internal error occurred."),
            run.Harness.StdErr);
    }

    [Fact]
    public async Task Publication_WhenTheSignalArrivesAtAClaimRemoval_ThenTheResidueStaysResumable()
    {
        // Cancellation at the same boundary: nothing reported, exit 3, and the state a later run
        // can still finish.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailKind = "Move";
        run.Harness.PublicationFiles.FailMoveTo = "out.cxt";
        run.Harness.PublicationFiles.MutateBefore = "Delete:out.fcabedrock-sc-c-T-T";
        run.Harness.PublicationFiles.Mutate = run.Harness.Signals.Cancel;

        Assert.Equal(3, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.False(File.Exists(run.Target(".cxt")));

        var retry = new CliTestHarness();
        Assert.Equal(
            0, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAnEmptyOccupantRefusesAStageClaim_ThenNoRunEverRemovesIt()
    {
        // A stage claim's name states the identity of the stage it acknowledges, so a resumed run
        // can ask whether that object is actually there. A claim raced in as an empty file names an
        // object no longer present — its own create-new having been refused, the stage was taken
        // back out — so nothing proves it and no run removes it.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "CreateNew:out.fcabedrock-sc-c-T-T";
        run.Harness.PublicationFiles.MutateWith = operation =>
            File.WriteAllBytes(Path.Combine(run.Directory, operation["CreateNew:".Length..]), []);

        await run.ConvertAsync("--format", "cxt");
        Assert.Null(run.Harness.PublicationFiles.MutateWith);

        var occupant = Single(run.Directory, "out.fcabedrock-sc-c-*");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");
            Assert.True(
                File.Exists(occupant),
                $"recovery attempt {attempt + 1} removed an occupant nothing proves it created");
        }
    }

    [Theory]

    // The folded transition that creates each control, and the glob that finds the occupant.
    // `05` is the shape of a cxt-plus-manifest record.
    [InlineData("out.fcabedrock-intent-T-05-T-T", "out.fcabedrock-intent-*", false)]
    [InlineData("out.fcabedrock-staged-T", "out.fcabedrock-staged-*", false)]
    [InlineData("out.fcabedrock-rollback-T", "out.fcabedrock-rollback-*", true)]
    [InlineData("out.fcabedrock-committed-T", "out.fcabedrock-committed-*", false)]
    public async Task Publication_WhenAnEmptyOccupantRefusesAControlCreate_ThenNoRunEverRemovesIt(
        string control, string glob, bool rollback)
    {
        // The empty-occupant race, at every role-only control. The create-new is refused,
        // so nothing successful and nothing durable names the occupant.
        //
        // A zero-byte control could not answer this: an empty file at the name is exactly what the
        // control this run would have made there looks like. So these controls now carry a
        // canonical body binding the run token, this base, the control's own role, and the digest
        // of the authoritative record — and removal requires those exact bytes. The occupant is
        // empty, so it is not this transaction's control, and no run removes it.
        using var run = ConvertRun.Wide();
        if (rollback)
        {
            run.Harness.PublicationFiles.FailKind = "Move";
            run.Harness.PublicationFiles.FailMoveTo = "out.cxt";
        }

        run.Harness.PublicationFiles.MutateBefore = $"CreateNew:{control}";
        run.Harness.PublicationFiles.MutateWith = operation =>
            File.WriteAllBytes(Path.Combine(run.Directory, operation["CreateNew:".Length..]), []);

        await run.ConvertAsync("--format", "cxt");
        Assert.Null(run.Harness.PublicationFiles.MutateWith);

        var occupant = Single(run.Directory, glob);
        Assert.True(File.Exists(occupant), "the run that met the occupant removed it");

        // Recover twice: the second is what would catch a resumed run accepting an expected-name
        // zero-byte occupant as its own state.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");
            Assert.True(
                File.Exists(occupant),
                $"recovery attempt {attempt + 1} removed an occupant nothing proves it created");
            Assert.Empty(await File.ReadAllBytesAsync(occupant));
        }
    }

    [Theory]

    // A control of the right name and the right shape, but bound to a DIFFERENT transaction: the
    // wrong token, the wrong record digest, or the wrong role. None of them is this run's control,
    // so none may be trusted to select a recovery direction or removed as its residue.
    [InlineData("token")]
    [InlineData("record")]
    [InlineData("control")]
    public async Task Publication_WhenAMarkersBodyBindsAnotherTransaction_ThenItIsRefusedAndPreserved(string field)
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", new string('a', 32));
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");

        var body = new UTF8Encoding(false).GetString(residue.ControlBody("staged"));
        var forged = field switch
        {
            "token" => body.Replace(new string('a', 32), new string('b', 32), StringComparison.Ordinal),
            "record" => body.Replace(residue.RecordDigest, new string('c', 32), StringComparison.Ordinal),
            _ => body.Replace("\"staged\"", "\"committed\"", StringComparison.Ordinal),
        };

        await File.WriteAllTextAsync(residue.MarkerPath("staged"), forged, new UTF8Encoding(false));

        // Refused with the location exactly as it was found — twice over.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var harness = new CliTestHarness();
            Assert.Equal(
                1, await harness.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Contains(
                "unrecognized fcabedrock transaction residue", harness.StdErr, StringComparison.Ordinal);
            Assert.Equal(forged, await File.ReadAllTextAsync(residue.MarkerPath("staged")));
            Assert.Equal("half-written", await File.ReadAllTextAsync(residue.PrivatePath("stage", "out.cxt")));
        }
    }

    [Fact]
    public async Task Publication_WhenAControlBodyIsSubstitutedInsideItsRemoval_ThenItSurvives()
    {
        // The removal's proof is read from the handle it holds, so a body swapped in at the delete
        // boundary is what the proof sees — and it fails. On Windows the deletion is requested
        // against that handle and there is no interval at all; on Unix the proof is the last thing
        // before the unlink. Either way the substitute survives.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Delete:out.fcabedrock-committed-T";
        run.Harness.PublicationFiles.MutateWith = operation =>
            File.WriteAllText(Path.Combine(run.Directory, operation["Delete:".Length..]), Keep);

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
        Assert.Null(run.Harness.PublicationFiles.MutateWith);

        var occupant = Single(run.Directory, "out.fcabedrock-committed-*");
        Assert.Equal(Keep, await File.ReadAllTextAsync(occupant));
        Assert.True(File.Exists(run.Target(".cxt")));
    }

    // ---- an object the host cannot name is never reclaimed ----------------------------------------

    [Fact]
    public async Task Publication_WhenThePendingRecordCannotBeIdentified_ThenItIsNeverReclaimed()
    {
        // The host cannot say which object its own create-new produced, so the transaction fails
        // closed — and it does NOT take the object back out. Removal is bound to the exact created
        // object; where the seam cannot name it there is no proof, and "it is zero bytes" is a
        // length, not a proof. So no removal is even attempted, and the object stays.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.SuppressControlIdentity = true;

        // Armed at the removal that must not happen: if anything tried to reclaim the pending
        // record, this would fire and swap a fresh empty object in underneath it.
        run.Harness.PublicationFiles.MutateBefore = "Delete:out.fcabedrock-pending-T";
        run.Harness.PublicationFiles.MutateWith = operation =>
        {
            var path = Path.Combine(run.Directory, operation["Delete:".Length..]);
            File.Delete(path);
            File.WriteAllBytes(path, []);
        };

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot start publication for the output base '{run.Base}'."),
            run.Harness.StdErr);

        Assert.NotNull(run.Harness.PublicationFiles.MutateWith);
        Assert.DoesNotContain(
            run.Harness.PublicationFiles.Operations,
            operation => operation.StartsWith("Delete:out.fcabedrock-pending-", StringComparison.Ordinal));

        var pending = Single(run.Directory, "out.fcabedrock-pending-*");
        Assert.False(File.Exists(run.Target(".cxt")));

        // Now the substitution the removal would have destroyed, placed before discovery: a fresh
        // empty object at the same name. No later run authorizes or deletes it.
        File.Delete(pending);
        await File.WriteAllBytesAsync(pending, []);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Equal(string.Empty, retry.StdOut);
            Assert.Contains(
                "unrecognized fcabedrock transaction residue", retry.StdErr, StringComparison.Ordinal);

            Assert.True(File.Exists(pending), $"attempt {attempt + 1} removed an object nothing names");
            Assert.Empty(await File.ReadAllBytesAsync(pending));
            Assert.Equal([Path.GetFileName(pending)], run.Residue());
            Assert.False(File.Exists(run.Target(".cxt")));
        }
    }

    [Fact]
    public async Task Publication_WhenAStageCannotBeIdentified_ThenItIsNeverReclaimedAndTheOldSetIsExact()
    {
        // The same rule at the stage. The run stops before its writer, before DATA is enumerated
        // for it, before the second format is begun, and before any target is renamed aside — and
        // the object it created stays beside its record rather than being reclaimed on a length.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        var before = run.Snapshot();

        var second = new CliTestHarness();
        second.PublicationFiles.SuppressStageIdentity = true;
        second.PublicationFiles.MutateBefore = "Delete:out.cxt.fcabedrock-stage-T";
        second.PublicationFiles.MutateWith = operation =>
        {
            var path = Path.Combine(run.Directory, operation["Delete:".Length..]);
            File.Delete(path);
            File.WriteAllBytes(path, []);
        };

        Assert.Equal(
            1,
            await second.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));
        Assert.Equal(string.Empty, second.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
            second.StdErr);

        // The identity branch itself reclaims nothing. The rollback that follows does REACH the
        // stage's removal — and that is the point of the substitution: the proof it takes from the
        // handle is of the fresh empty object, which is not the stage this run created, so the
        // removal refuses and the replacement survives.
        var operations = second.PublicationFiles.Operations;
        Assert.Null(second.PublicationFiles.MutateWith);
        Assert.DoesNotContain(
            operations,
            operation => operation.StartsWith("StreamWrite:out.cxt.fcabedrock-stage-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            operations,
            operation => operation.StartsWith("Confidential:out.dat.fcabedrock-stage-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            operations,
            operation => operation.StartsWith("Move:", StringComparison.Ordinal)
                && operation.Contains(".fcabedrock-backup-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            operations,
            operation => operation.StartsWith("CreateNew:out.fcabedrock-sc-", StringComparison.Ordinal));

        // The replacement survives that removal, stays beside its record, and the previous public
        // set is byte-identical.
        var stage = Single(run.Directory, "out.cxt.fcabedrock-stage-*");
        Assert.Empty(await File.ReadAllBytesAsync(stage));
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
        AssertSameFiles(before, run.Snapshot());

        // And nothing later authorizes or deletes it either: two deterministic retries.
        var residue = run.Residue();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                1,
                await retry.RunAsync(
                    "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));
            Assert.Equal(string.Empty, retry.StdOut);
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
                retry.StdErr);

            Assert.True(File.Exists(stage), $"attempt {attempt + 1} removed an object nothing names");
            Assert.Empty(await File.ReadAllBytesAsync(stage));
            Assert.Equal(residue, run.Residue());
            AssertSameFiles(before, run.Snapshot());
        }
    }

    [Fact]
    public async Task Publication_WhenAnUnidentifiableStageRunIsInterrupted_ThenTheStateStaysByteIdentical()
    {
        // Interruption on the same branch: the rollback that follows the refusal never runs, so
        // what survives is the record and the object the host could not name. Retries reproduce it.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.SuppressStageIdentity = true;
        run.Harness.PublicationFiles.CrashAfter = "Confidential:out.cxt.fcabedrock-stage-T";

        await run.ConvertAsync("--format", "cxt");
        Assert.True(run.Harness.PublicationFiles.Crashed, "the crash after the stage creation never fired");

        var residue = run.Residue();
        Assert.Contains(residue, name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains(".fcabedrock-stage-", StringComparison.Ordinal));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Equal(string.Empty, retry.StdOut);
            Assert.Equal(residue, run.Residue());
            Assert.False(File.Exists(run.Target(".cxt")));
        }
    }

    // ---- the restoring rename's own result --------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_WhenTheBackupIsSubstitutedInsideItsRestore_ThenItIsNotPublishedAsTheOldTarget(
        bool alias)
    {
        // Rollback's last destructive act is a rename home. Its result is proved before any evidence
        // can be discarded: a different object moved into the target path is not the prior run, and
        // declaring the restoration complete over it would erase the only state that discloses it.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
        var oldCxt = await File.ReadAllBytesAsync(run.Target(".cxt"));

        var second = new CliTestHarness();
        var linked = false;
        second.PublicationFiles.FailKind = "Move";
        second.PublicationFiles.FailMoveTo = "out.manifest.toml";
        second.PublicationFiles.MutateBefore = "Move:out.cxt.fcabedrock-backup-T->out.cxt";
        second.PublicationFiles.Mutate = () =>
        {
            var backup = Single(run.Directory, "out.cxt.fcabedrock-backup-*");
            File.Delete(backup);
            if (alias)
            {
                linked = PlatformLinks.TryCreateHardLink(backup, run.Data, out _);
            }
            else
            {
                File.WriteAllText(backup, Keep);
            }
        };

        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");

        if (alias && !linked)
        {
            Assert.Skip("hard links are unavailable on this host.");
        }

        Assert.Equal(1, exit);

        // The substitute is not at the target — the restoring rename was undone — the record
        // survives so the state stays classifiable rather than looking finished, and the object
        // itself is preserved somewhere rather than deleted.
        var expected = alias ? CliFixtures.WideData : Keep;
        Assert.False(
            File.Exists(run.Target(".cxt")) && string.Equals(run.Text(".cxt"), expected, StringComparison.Ordinal),
            "the substitute was published as the restored prior target");
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
        Assert.Contains(
            Directory.GetFiles(run.Directory),
            path => string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal));

        // And neither the input nor the old CXT was destroyed by the attempt.
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(run.Data));
        Assert.NotEmpty(oldCxt);
    }

    // ---- nothing is deleted where it stands -------------------------------------------------------

    [Theory]

    // A superseded backup, during forward cleanup past the commit point.
    [InlineData("out.cxt.fcabedrock-backup-T", "out.cxt.fcabedrock-backup-")]

    // The authoritative evidence of a committed run.
    [InlineData("out.fcabedrock-e-c-T", "out.fcabedrock-e-c-")]

    // The committed phase marker.
    [InlineData("out.fcabedrock-committed-T", "out.fcabedrock-committed-")]

    // The transaction record itself — the very last removal a run performs.
    [InlineData("out.fcabedrock-transaction-T.toml", "out.fcabedrock-transaction-")]
    public async Task Publication_WhenAnObjectIsSubstitutedInsideItsRemoval_ThenTheSubstituteSurvives(
        string transition, string pattern)
    {
        // A delete names a path, not an object, and leaves no result whose identity could be
        // checked afterwards. So the proof is not taken before the delete and acted on by it: the
        // removal opens the object, reads its identity and bytes from that handle, and deletes
        // through the same handle. A file that took the name in between is what the proof sees, and
        // it fails — so the deletion never happens at all.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        var second = new CliTestHarness();
        var substituted = string.Empty;
        second.PublicationFiles.MutateBefore = $"Delete:{transition}";
        second.PublicationFiles.Mutate = () =>
        {
            substituted = Single(run.Directory, pattern + "*");
            File.Delete(substituted);
            File.WriteAllText(substituted, Keep);
        };

        await second.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");

        Assert.Equal(1, second.PublicationFiles.MutationsFired);
        Assert.NotEqual(string.Empty, substituted);
        Assert.True(File.Exists(substituted), $"'{substituted}' was deleted rather than preserved");
        Assert.Equal(Keep, await File.ReadAllTextAsync(substituted));
    }

    [Fact]
    public async Task Publication_WhenAPublishedFinalIsSubstitutedInsideItsRemoval_ThenTheSubstituteSurvives()
    {
        // The same boundary on the destructive side of a rollback: this run's own published artifact
        // is about to be withdrawn, and something else takes its place inside the very rename that
        // begins the withdrawal.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", new string('a', 32));
        residue.WriteRecord([("stage", "out.cxt"), ("stage", "out.dat")]);
        await File.WriteAllTextAsync(run.Target(".cxt"), "this run's cxt");
        await File.WriteAllTextAsync(run.Target(".dat"), "this run's dat");
        residue.WriteEvidence(
            "c", "out.cxt", IdentityEvidence.NotApplicable, residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteEvidence(
            "d", "out.dat", IdentityEvidence.NotApplicable, residue.IdentityOf(run.Target(".dat"), "stage", "out.dat"));
        residue.WriteMarker("staged");
        residue.WriteMarker("rollback");

        run.Harness.PublicationFiles.MutateBefore = "Delete:out.cxt";
        run.Harness.PublicationFiles.Mutate = () =>
        {
            File.Delete(run.Target(".cxt"));
            File.WriteAllText(run.Target(".cxt"), Keep);
        };

        Assert.Equal(1, await run.ConvertAsync("--format", "both"));

        Assert.Equal(1, run.Harness.PublicationFiles.MutationsFired);
        Assert.Equal(Keep, await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.True(File.Exists(residue.RecordPath), "the record was removed over an unaccounted object");
    }

    [Fact]
    public async Task Publication_WhenAnInputIsSubstitutedInsideARemoval_ThenTheInputSurvives()
    {
        // The same boundary with the substitute aliased to DATA: the object a removal would destroy
        // is one of the run's own inputs, and it must come through untouched.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        var second = new CliTestHarness();
        var linked = false;
        var backup = string.Empty;
        second.PublicationFiles.MutateBefore = "Delete:out.cxt.fcabedrock-backup-T";
        second.PublicationFiles.Mutate = () =>
        {
            backup = Single(run.Directory, "out.cxt.fcabedrock-backup-*");
            File.Delete(backup);
            linked = PlatformLinks.TryCreateHardLink(backup, run.Data, out _);
        };

        await second.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");

        if (!linked)
        {
            Assert.Skip("hard links are unavailable on this host.");
        }

        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(run.Data));
        Assert.True(File.Exists(backup), "the aliased input was deleted rather than preserved");
    }

    [Fact]
    public async Task Publication_WhenAnUnrelatedObjectOccupiesThePendingRecordPathAtItsRemoval_ThenTheProofRefusesAndPreservesIt()
    {
        // The pending record's own removal, at the tail of a resumed cleanup. Its descriptor
        // authorizes removing exactly one thing — the object whose identity that name states — and
        // a file that took the path afterwards is not that object. The proof is read from the
        // handle the deletion acts through, so the occupant is what it judges: the removal is
        // attempted, refused, and the teardown stops before the record it would otherwise erase.
        using var run = ConvertRun.Wide();
        var token = new string('a', 32);
        var residue = Residue.Create(run.Directory, "out", token);
        residue.WriteRecord([("stage", "out.cxt")]);

        // Well formed and naming no object that exists: the descriptor agrees with the record
        // above, so classification accepts the state whole while the pending path is still empty.
        var intent = WriteIntent(run.Directory, "out", token, [("stage", "out.cxt")], new string('b', 32));
        var pending = Path.Combine(run.Directory, PublicationTargets.PendingRecordName("out", token));
        var recordBytes = await File.ReadAllBytesAsync(residue.RecordPath);

        // The first existence probe of that path is the prior-collision check's, which runs after
        // classification accepted the state and before recovery mutates anything — so the occupant
        // arrives inside exactly the window this proof exists for.
        run.Harness.PublicationFiles.MutateBefore = "Exists:out.fcabedrock-pending-T";
        run.Harness.PublicationFiles.Mutate = () => File.WriteAllText(pending, Keep);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
            run.Harness.StdErr);

        // The removal really was attempted: the seam records that call only when the proof is
        // evaluated against the object's own handle, so a surviving occupant beside it cannot be an
        // existence or guard short-circuit.
        Assert.Contains(
            run.Harness.PublicationFiles.Operations,
            operation => operation.StartsWith("Delete:out.fcabedrock-pending-", StringComparison.Ordinal));

        var planted = Encoding.UTF8.GetBytes(Keep);
        Assert.True(File.Exists(pending), "the occupant was removed by a descriptor that never named it");
        Assert.Equal(planted, await File.ReadAllBytesAsync(pending));

        // The descriptor immediately before it in the teardown order DID go, which pins the refusal
        // to the pending record's own removal rather than to the preparatory-intent closure.
        Assert.False(File.Exists(intent));
        Assert.True(File.Exists(residue.RecordPath), "the record was removed over an unaccounted object");
        Assert.Equal(recordBytes, await File.ReadAllBytesAsync(residue.RecordPath));
        Assert.False(File.Exists(run.Target(".cxt")));

        // Deterministic, and destructive of nothing: the preserved record and the occupant now form
        // the coexistence classification refuses, and every plain retry says exactly that.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Contains(
                "unrecognized fcabedrock transaction residue", retry.StdErr, StringComparison.Ordinal);

            Assert.True(File.Exists(pending), $"attempt {attempt + 1} removed the occupant");
            Assert.Equal(planted, await File.ReadAllBytesAsync(pending));
            Assert.True(File.Exists(residue.RecordPath), $"attempt {attempt + 1} removed the record");
            Assert.Equal(recordBytes, await File.ReadAllBytesAsync(residue.RecordPath));
        }

        // The remedy that message names. With the unrelated object gone the preserved authority is
        // classifiable again, and an ordinary run clears it and publishes.
        File.Delete(pending);

        var resumed = new CliTestHarness();
        Assert.Equal(
            0, await resumed.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    // ---- origin-aware failure taxonomy at every mutation seam -------------------------------------

    [Theory]

    // Acquiring a control path.
    [InlineData("CreateNew", "out.fcabedrock-intent-", "start")]

    // The record's own publishing rename.
    [InlineData("Move", "out.fcabedrock-pending-", "start")]

    // The stage-to-final commit rename.
    [InlineData("Move", "out.cxt.fcabedrock-stage-", "publish")]
    public async Task Publication_WhenAMutationSeamRaisesAContractFault_ThenItIsAnInternalFault(
        string kind, string prefix, string family)
    {
        // D-122 separates an ordinary host/publication failure (exit 1) from an unexpected internal
        // fault (exit 4). An ObjectDisposedException from a create, a rename, or a delete of a path
        // this code derived cannot describe anything the user typed: it is a product bug, and which
        // method of the same internal seam exposed it must not decide the exit code.
        using var contract = ConvertRun.Wide();
        Configure(contract.Harness, kind, prefix, () => new ObjectDisposedException("publication"));

        Assert.Equal(4, await contract.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError("an unexpected internal error occurred."),
            contract.Harness.StdErr);
        Assert.False(File.Exists(contract.Target(".cxt")));

        // The same operation failing for a genuine environment reason stays the established exit 1
        // with the message that names the user's own output.
        using var io = ConvertRun.Wide();
        Configure(io.Harness, kind, prefix, () => new IOException("there is not enough space on the disk."));

        Assert.Equal(1, await io.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                family == "start"
                    ? $"cannot start publication for the output base '{io.Base}'."
                    : $"cannot publish the output '{io.Target(".cxt")}'."),
            io.Harness.StdErr);
        Assert.False(File.Exists(io.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenARemovalsUnlinkRaisesAContractFault_ThenItIsAnInternalFault()
    {
        // The delete half of a removal, reached at the first one a run performs: withdrawing the
        // intent descriptor once the record is published.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailKind = "Delete";
        run.Harness.PublicationFiles.FailWith = static () => new ObjectDisposedException("publication");

        Assert.Equal(4, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError("an unexpected internal error occurred."),
            run.Harness.StdErr);
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenARemovalsUnlinkFailsWithIo_ThenItStaysAnOrdinaryBestEffortOutcome()
    {
        // The same operation failing for a genuine environment reason is the established
        // best-effort tidy-up: the run publishes, and what could not be removed stays owned.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailKind = "Delete";
        run.Harness.PublicationFiles.FailEveryMatch = true;

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.NotEmpty(run.Residue());

        var retry = new CliTestHarness();
        Assert.Equal(
            0,
            await retry.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAPostCommitRemovalRaisesAContractFault_ThenTheCommittedRunStands()
    {
        // Past the commit point the run is public. The fault still reaches exit 4 — it is a product
        // bug wherever it happens — but nothing unwinds what was published.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailDeletePrefix = "out.fcabedrock-committed-";
        run.Harness.PublicationFiles.FailWith = static () => new ObjectDisposedException("publication");

        Assert.Equal(4, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError("an unexpected internal error occurred."),
            run.Harness.StdErr);

        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.True(File.Exists(run.Target(".manifest.toml")));
    }

    [Fact]
    public async Task Publication_WhenACancelledRunAlsoMeetsARollbackFault_ThenItStaysACancellation()
    {
        // Cancellation is exit 3 by contract and reports nothing. A contract defect met while
        // undoing must not be allowed to promote it.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CancelAfterMoveToPrefix = "out.fcabedrock-e-c-";
        run.Harness.PublicationFiles.CancelAfterMove = run.Harness.Signals.Cancel;
        run.Harness.PublicationFiles.FailDeletePrefix = "out.cxt.fcabedrock-stage-";
        run.Harness.PublicationFiles.FailWith = static () => new ObjectDisposedException("publication");

        Assert.Equal(3, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    // ---- a forced participant that cannot be identified ------------------------------------------

    [Theory]
    [InlineData(".cxt")]
    [InlineData(".manifest.toml")]
    public void Publication_WhenAForcedParticipantCannotBeIdentified_ThenPreflightRefusesBeforeAnything(
        string extension)
    {
        // The inability is already KNOWN at preflight: the target exists, its identity was asked
        // for, and the answer cannot authorize a replacement. Discovering that only at sealing
        // would mean a record, three stages, and a whole conversion pass first.
        //
        // Both faces of that inability are present, because they are one capability: the host
        // reports no filesystem identity for a path, and no live reference for an object. An
        // identity that cannot be anchored is not an identity this protocol acts on (D-125).
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var basePath = temp.Resolve("out");
        File.WriteAllText(basePath + extension, "the old one");

        var files = new RecordingPublicationFileSystem { SuppressReferences = true };
        var preparation = PublicationTransaction.Preflight(
            files,
            static () => new FileIdentity(new UnavailableFileIdentityProbe()),
            basePath,
            [PublicationTargetKind.Cxt],
            [new PublicationInput(data, data), new PublicationInput(spec, spec)],
            force: true,
            CancellationToken.None);

        var refused = Assert.IsType<PublicationRefused>(preparation);
        Assert.Equal($"cannot publish the output '{basePath + extension}'.", refused.Message);

        // Not one mutating operation: the location is exactly as it was found.
        Assert.DoesNotContain(
            files.Operations,
            operation => operation.StartsWith("CreateNew:", StringComparison.Ordinal)
                || operation.StartsWith("Confidential:", StringComparison.Ordinal)
                || operation.StartsWith("Move:", StringComparison.Ordinal)
                || operation.StartsWith("Delete:", StringComparison.Ordinal));

        Assert.Equal("the old one", File.ReadAllText(basePath + extension));
    }

    // ---- a stage that can never be proved is never written ---------------------------------------

    [Fact]
    public async Task Publication_WhenAStageReportsNoIdentity_ThenNoWriterPassAndNoBackupRenameRun()
    {
        // The host says, at the created handle, that it cannot identify the object. That is the
        // whole answer: this stage can never be committed, so the writer is never invoked, DATA is
        // never enumerated for it, the second format is never begun, and — the part a snapshot
        // taken after a successful rollback hides — no prior target is renamed aside.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        var before = run.Snapshot();

        var second = new CliTestHarness();
        second.PublicationFiles.SuppressStageIdentity = true;

        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
            second.StdErr);

        var operations = second.PublicationFiles.Operations;
        Assert.DoesNotContain(
            operations, operation => operation.StartsWith("StreamWrite:out.cxt.fcabedrock-stage-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            operations, operation => operation.StartsWith("Confidential:out.dat.fcabedrock-stage-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            operations,
            operation => operation.StartsWith("Move:", StringComparison.Ordinal)
                && operation.Contains(".fcabedrock-backup-", StringComparison.Ordinal));

        // Byte-identical. The object the host could not name stays beside its record rather than
        // being reclaimed on a length.
        AssertSameFiles(before, run.Snapshot());
        Assert.Contains(
            run.Residue(), name => name.Contains("out.cxt.fcabedrock-stage-", StringComparison.Ordinal));
    }

    // ---- the record's own publication -------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenThePendingRecordIsSubstitutedInsideItsPublish_ThenNothingBegins()
    {
        // The record is the root of every later authority. Accepting an unverified rename result
        // would let a successful command delete an unrelated object at the end of its cleanup, and
        // let a crash strand real residue behind a record that cannot describe it.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore =
            "Move:out.fcabedrock-pending-T->out.fcabedrock-transaction-T.toml";
        run.Harness.PublicationFiles.Mutate = () =>
        {
            var pending = Single(run.Directory, "out.fcabedrock-pending-*");
            File.Delete(pending);
            File.WriteAllText(pending, Keep);
        };

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot start publication for the output base '{run.Base}'."),
            run.Harness.StdErr);

        // Nothing was staged, nothing published, and the substitute is back where the rename took
        // it from rather than sitting at the authoritative record name.
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Empty(Directory.GetFiles(run.Directory, "out.fcabedrock-transaction-*"));
        Assert.Equal(Keep, await File.ReadAllTextAsync(Single(run.Directory, "out.fcabedrock-pending-*")));

        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Equal(Keep, await File.ReadAllTextAsync(Single(run.Directory, "out.fcabedrock-pending-*")));
    }

    [Fact]
    public async Task Publication_WhenThePendingRecordCannotBeIdentified_ThenNothingBegins()
    {
        // The same rule from the other side: a host that cannot say which object it just created
        // cannot bind the record to it, so the transaction fails closed instead of publishing an
        // authority it could not prove.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.SuppressControlIdentity = true;

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot start publication for the output base '{run.Base}'."),
            run.Harness.StdErr);

        Assert.False(File.Exists(run.Target(".cxt")));

        // The pending object the host could not name is the only thing left, and it is left
        // untouched: no descriptor was ever written, so nothing names it.
        Assert.Single(run.Residue(), name => name.Contains(".fcabedrock-pending-", StringComparison.Ordinal));
    }

    // ---- the public commit marker's own rename ----------------------------------------------------

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Publication_WhenTheManifestStageIsSubstitutedAtItsCommit_ThenNoPublicMarkerSurvives(
        bool stale, bool forced)
    {
        // The manifest's final rename IS the run's public commit point. Detecting the substitution
        // afterwards is not enough on its own: the non-overwriting rename has already put an
        // unrelated object at the marker path, and an ordinary rollback would preserve it there
        // while erasing every private control that could classify it — a failed run wearing a
        // success marker, with no residue to reveal it.
        using var run = ConvertRun.Wide();
        byte[] impostor;
        if (stale)
        {
            // A perfectly parseable manifest from a different run — the sharpest case, because
            // nothing about its CONTENT is wrong. Only its identity is.
            Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
            impostor = await File.ReadAllBytesAsync(run.Target(".manifest.toml"));
            if (!forced)
            {
                File.Delete(run.Target(".cxt"));
                File.Delete(run.Target(".manifest.toml"));
            }
        }
        else
        {
            impostor = Encoding.UTF8.GetBytes("not a manifest at all");
            if (forced)
            {
                Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
            }
        }

        var before = run.Snapshot();

        var second = new CliTestHarness();
        second.PublicationFiles.MutateBefore =
            "Move:out.manifest.toml.fcabedrock-stage-T->out.manifest.toml";
        second.PublicationFiles.Mutate = () =>
        {
            var stage = Single(run.Directory, "out.manifest.toml.fcabedrock-stage-*");
            File.Delete(stage);
            File.WriteAllBytes(stage, impostor);
        };

        var argv = new List<string> { "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt" };
        if (forced)
        {
            argv.Add("--force");
        }

        Assert.Equal(1, await second.RunAsync([.. argv]));

        // The race really happened. Before the lifetime correction this case could pass or fail on
        // the same code depending on which inode the allocator handed the impostor, so the run has
        // to say that the substitution fired, not merely that the outcome looks right.
        Assert.Equal(1, second.PublicationFiles.MutationsFired);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".manifest.toml")}'."),
            second.StdErr);

        // The impostor is preserved — never deleted as owned — but it is NOT the public marker, and
        // the previous state is exactly what it was.
        Assert.Contains(
            Directory.GetFiles(run.Directory),
            path => Path.GetFileName(path).Contains(".fcabedrock-", StringComparison.Ordinal)
                && File.ReadAllBytes(path).AsSpan().SequenceEqual(impostor));

        AssertSameFiles(before, run.Snapshot());

        // And the record survives, because the substitute is still sitting on a private path this
        // transaction cannot account for: the state stays classifiable rather than looking clean.
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publication_WhenTheManifestSubstituteCannotBeMovedBack_ThenTheAuthorityIsKept()
    {
        // Compensation itself failing is the last case. The impostor cannot be deleted and cannot
        // be moved off the marker path — so the one thing left that must hold is that nothing
        // erases the private state a later run needs to recognize it, and that a retry keeps
        // reaching the same answer.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore =
            "Move:out.manifest.toml.fcabedrock-stage-T->out.manifest.toml";
        run.Harness.PublicationFiles.Mutate = () =>
        {
            var stage = Single(run.Directory, "out.manifest.toml.fcabedrock-stage-*");
            File.Delete(stage);
            File.WriteAllText(stage, "an impostor");
        };

        // The reversing rename is the only move whose SOURCE is the published marker.
        run.Harness.PublicationFiles.FailKind = "Move";
        run.Harness.PublicationFiles.FailName = "out.manifest.toml";

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));

        Assert.Equal("an impostor", await File.ReadAllTextAsync(run.Target(".manifest.toml")));
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));

        // Deterministic: the retry reads the same state, refuses the same way, and destroys nothing.
        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
            retry.StdErr);
        Assert.Equal("an impostor", await File.ReadAllTextAsync(run.Target(".manifest.toml")));
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publication_WhenTheProcessStopsInsideTheManifestCompensation_ThenARetryConverges()
    {
        // The compensation is itself a durable transition, so it gets the same treatment as every
        // other: stop the process the instant it completes, inspect what is on disk, and prove a
        // plain retry reaches one coherent run rather than a mixture.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore =
            "Move:out.manifest.toml.fcabedrock-stage-T->out.manifest.toml";
        run.Harness.PublicationFiles.Mutate = () =>
        {
            var stage = Single(run.Directory, "out.manifest.toml.fcabedrock-stage-*");
            File.Delete(stage);
            File.WriteAllText(stage, "an impostor");
        };

        run.Harness.PublicationFiles.CrashAfter =
            "Move:out.manifest.toml->out.manifest.toml.fcabedrock-stage-T";

        await run.ConvertAsync("--format", "cxt");
        Assert.True(run.Harness.PublicationFiles.Crashed, "the crash after the compensation never fired");

        // At the instant the process disappeared: no public marker, and the impostor is back on the
        // private path the rename took it from.
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Equal(
            "an impostor",
            await File.ReadAllTextAsync(Single(run.Directory, "out.manifest.toml.fcabedrock-stage-*")));

        // The retry cannot remove an object it does not own, so it says so — every time, without
        // touching anything.
        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
            retry.StdErr);
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenAStageIsReplacedByAByteIdenticalObject_ThenItIsStillNotOurs()
    {
        // The sharpest substitution there is, and the one no content check can catch: the object at
        // the stage path is replaced by a DIFFERENT object holding exactly the bytes this run wrote.
        // Its hash is the one the manifest would certify, so only identity can refuse it — and
        // identity can only refuse it because the original is still held open, which is what stops
        // the replacement from being handed the original's identifier (D-125).
        using var run = ConvertRun.Wide();

        byte[] identical = [];
        run.Harness.PublicationFiles.MutateBefore = "Move:out.cxt.fcabedrock-stage-T->out.cxt";
        run.Harness.PublicationFiles.Mutate = () =>
        {
            var stage = Single(run.Directory, "out.cxt.fcabedrock-stage-*");
            identical = File.ReadAllBytes(stage);
            File.Delete(stage);
            File.WriteAllBytes(stage, identical);
        };

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));

        Assert.Equal(1, run.Harness.PublicationFiles.MutationsFired);
        Assert.NotEmpty(identical);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
            run.Harness.StdErr);

        // Nothing becomes public, and the impostor is preserved where the compensation put it back:
        // it is not this transaction's object, so it is neither certified nor deleted.
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));

        var occupant = Single(run.Directory, "out.cxt.fcabedrock-stage-*");
        Assert.Equal(identical, await File.ReadAllBytesAsync(occupant));
        Assert.Contains(run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publication_WhenTheManifestCommitSucceeds_ThenNoIntermediateMarkerIsEverVisible()
    {
        // The other half of the marker rule: on the happy path the marker appears exactly once, at
        // the very end, and never before the artifacts it certifies are published.
        using var run = ConvertRun.Wide();
        var seenEarly = false;
        run.Harness.PublicationFiles.MutateBefore = "Move:out.cxt.fcabedrock-stage-T->out.cxt";
        run.Harness.PublicationFiles.Mutate = () =>
            seenEarly = File.Exists(run.Target(".manifest.toml"));

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.False(seenEarly, "the public marker existed before the artifact it certifies");
        Assert.True(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static void Configure(CliTestHarness harness, string kind, string prefix, Func<Exception> failure)
    {
        harness.PublicationFiles.FailWith = failure;
        if (string.Equals(kind, "CreateNew", StringComparison.Ordinal))
        {
            harness.PublicationFiles.FailCreateNewPrefix = prefix;
            return;
        }

        harness.PublicationFiles.FailKind = "Move";
        harness.PublicationFiles.FailNamePrefix = prefix;
    }

    // An intent descriptor carrying the canonical intent body, whose name states the record
    // shape, that record's own digest, and the identity it claims for the pending object.
    private static string WriteIntent(
        string directory,
        string baseName,
        string token,
        IReadOnlyList<(string Role, string Target)> files,
        string pendingIdentity)
    {
        var entries = new List<TransactionFileEntry>();
        foreach (var (role, target) in files)
        {
            entries.Add(new TransactionFileEntry(role, target));
        }

        var record = TransactionRecord.Create(token, baseName, entries);
        var path = Path.Combine(
            directory,
            PublicationTargets.IntentName(baseName, token, record.ShapeCode, record.Digest, pendingIdentity));

        File.WriteAllBytes(path, PublicationRecoveryTests.IntentBody(token, baseName, record.Digest));
        return path;
    }

    // The run's token, read from whichever of its two name-bearing control files exists: the intent
    // descriptor before the record is published, the record itself afterwards.
    private static string TokenOf(string directory)
    {
        var intent = Directory
            .GetFiles(directory, "out.fcabedrock-intent-*")
            .Select(Path.GetFileName)
            .FirstOrDefault();

        if (intent is not null)
        {
            return intent["out.fcabedrock-intent-".Length..][..PublicationTargets.TokenLength];
        }

        var record = Path.GetFileName(Directory.GetFiles(directory, "out.fcabedrock-transaction-*.toml").Single());
        return record["out.fcabedrock-transaction-".Length..^".toml".Length];
    }

    private static string Single(string directory, string pattern) =>
        Directory.GetFiles(directory, pattern).Single();

    private static void AssertSameFiles(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        foreach (var (name, bytes) in before)
        {
            Assert.True(after.TryGetValue(name, out var actual), $"'{name}' no longer exists");
            Assert.Equal(bytes, actual);
        }
    }
}
