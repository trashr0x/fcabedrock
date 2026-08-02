using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The cross-family boundary (D-123 point 7): a caller completes only its <b>own</b> record family.
/// At one exact output base a convert record and a single-file record are both well formed, and
/// each names files the other command never asked to write, so a valid foreign record is refused
/// and preserved byte-for-byte rather than recovered.
/// <para>
/// <b>What makes the refusal meaningful.</b> Every foreign-family row is paired with the identical
/// residue offered to its <em>own</em> family, which recovers and proceeds. Without that pairing a
/// family check that simply refused everything would look correct here.
/// </para>
/// <para>
/// Residue is authored as literal bytes through the established test-only <see cref="Residue"/>
/// writer; no production serializer or parser produces an expectation, and nothing in this file
/// removes a file or derives authority from a name.
/// </para>
/// </summary>
public sealed class PublicationFamilyBoundaryTests
{
    private const string OldBytes = "the previous artifact";
    private const string NewBytes = "the published artifact";
    private const string StageBytes = "half-written";

    // The four durable states a discovered transaction can present.
    public static TheoryData<string> States => ["preparing", "staged", "rollingback", "committed"];

    // ---- direction 1: a single-file caller meets convert residue at the same base ----------------

    [Fact]
    public void SingleCaller_WhenAConvertRecordExistsAtTheSameBase_ThenItIsRefusedAndPreservedByteIdentically() =>
        AssertForeignRefusal(family: "convert", state: "preparing");

    [Fact]
    public void SingleCaller_WhenAStagedConvertTransactionExistsAtTheSameBase_ThenItIsRefusedAndPreservedByteIdentically() =>
        AssertForeignRefusal(family: "convert", state: "staged");

    [Fact]
    public void SingleCaller_WhenARollingBackConvertTransactionExistsAtTheSameBase_ThenItIsRefusedAndPreservedByteIdentically() =>
        AssertForeignRefusal(family: "convert", state: "rollingback");

    [Fact]
    public void SingleCaller_WhenACommittedConvertTransactionAwaitsCleanupAtTheSameBase_ThenItIsRefusedAndPreservedByteIdentically() =>
        AssertForeignRefusal(family: "convert", state: "committed");

    // ---- direction 2: a convert caller meets single-file residue at the same base -----------------

    [Fact]
    public void ConvertCaller_WhenASingleRecordExistsAtTheSameBase_ThenItIsRefusedAndPreservedByteIdentically() =>
        AssertForeignRefusal(family: "single", state: "preparing");

    [Fact]
    public void ConvertCaller_WhenAStagedSingleTransactionExistsAtTheSameBase_ThenItIsRefusedAndPreservedByteIdentically() =>
        AssertForeignRefusal(family: "single", state: "staged");

    [Fact]
    public void ConvertCaller_WhenARollingBackSingleTransactionExistsAtTheSameBase_ThenItIsRefusedAndPreservedByteIdentically() =>
        AssertForeignRefusal(family: "single", state: "rollingback");

    [Fact]
    public void ConvertCaller_WhenACommittedSingleTransactionAwaitsCleanupAtTheSameBase_ThenItIsRefusedAndPreservedByteIdentically() =>
        AssertForeignRefusal(family: "single", state: "committed");

    /// <summary>
    /// The shared exact-base oracle. <paramref name="family"/> names the family that <b>owns</b> the
    /// residue; the caller is always the other one, at the identical base.
    /// </summary>
    private static void AssertForeignRefusal(string family, string state)
    {
        using var run = Boundary.Create();
        Author(run, family, state);
        var before = run.Snapshot();

        var preparation = string.Equals(family, "convert", StringComparison.Ordinal)
            ? run.Single()
            : run.Convert();

        // Refused as unrecognized residue, naming only the caller's own operand; not one mutating
        // operation was even attempted; and every file in the directory is byte-identical.
        var refused = Assert.IsType<PublicationRefused>(preparation);
        Assert.Equal(
            $"the output base '{run.Base}' has unrecognized fcabedrock transaction residue; remove it and retry.",
            refused.Message);
        run.AssertNoMutation();
        Assert.Equal(before, run.Snapshot());

        // The committed rows additionally keep the foreign run's published final.
        if (string.Equals(state, "committed", StringComparison.Ordinal))
        {
            Assert.Equal(NewBytes, File.ReadAllText(run.Resolve(Final(family))));
        }
    }

    // ---- the positive guards that make the refusals meaningful ------------------------------------

    [Theory]
    [MemberData(nameof(States))]
    public void SingleCaller_WhenTheResidueIsItsOwnFamily_ThenItIsRecoveredAndTheRunProceeds(string state)
    {
        using var run = Boundary.Create();
        Author(run, "single", state);

        Assert.IsType<PublicationReady>(run.Single());
        Assert.Empty(run.Residue());
    }

    [Theory]
    [MemberData(nameof(States))]
    public void ConvertCaller_WhenTheResidueIsItsOwnFamily_ThenItIsRecoveredAndTheRunProceeds(string state)
    {
        using var run = Boundary.Create();
        Author(run, "convert", state);

        Assert.IsType<PublicationReady>(run.Convert());
        Assert.Empty(run.Residue());
    }

    [Fact]
    public void ConvertCaller_WhenOnlyConvertResidueExists_ThenRecoveryBehavesExactlyAsBeforeTheFamilyBoundary()
    {
        // The merged rollback-restore row, asserted from this file without editing the suite that
        // owns it: a backed-up but uncommitted transaction is rolled back, the old artifact comes
        // home byte-identically, and it then blocks an unforced run exactly as it always did.
        using var run = Boundary.Create();
        var residue = Residue.Create(run.Directory, "out", Boundary.Token);
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", OldBytes);
        residue.WritePrivate("stage", "out.cxt", StageBytes);
        residue.WriteEvidence(
            "c", "out.cxt", residue.Identity("backup", "out.cxt"), residue.Identity("stage", "out.cxt"));
        residue.WriteMarker("staged");

        var refused = Assert.IsType<PublicationRefused>(run.Convert(force: false));

        Assert.Equal($"the output '{run.Base}.cxt' already exists; use --force to replace it.", refused.Message);
        Assert.Equal(OldBytes, File.ReadAllText(run.Resolve("out.cxt")));
        Assert.Empty(run.Residue());
    }

    // ---- unequal bases whose private namespaces overlap -------------------------------------------

    [Fact]
    public void SingleCallerAtAnArtifactName_WhenAConvertRunOwnsTheEnclosingBase_ThenBothRefuseAndNothingIsDestroyed()
    {
        // `single --out out.cxt` beside `convert --out out`. The single-file caller's prefix is
        // `out.cxt`, so it never sees the convert record at all — only a private name inside its
        // own namespace that no record of ITS base accounts for.
        using var run = Boundary.Create();
        Overlap(run);
        var before = run.Snapshot();

        var refused = Assert.IsType<PublicationRefused>(run.Single(run.Resolve("out.cxt")));

        Assert.Equal(
            $"the output base '{run.Resolve("out.cxt")}' has unrecognized fcabedrock transaction residue;"
            + " remove it and retry.",
            refused.Message);
        run.AssertNoMutation();
        Assert.Equal(before, run.Snapshot());
    }

    [Fact]
    public void ConvertCaller_WhenASingleRunOwnsAnArtifactName_ThenBothRefuseAndNothingIsDestroyed()
    {
        // The converse perspective on the same location: the convert caller's prefix is `out`, so
        // it does enumerate `out.cxt.fcabedrock-stage-…` and meets the single-file run's stage,
        // which its own record cannot account for.
        using var run = Boundary.Create();
        Overlap(run);
        var before = run.Snapshot();

        var refused = Assert.IsType<PublicationRefused>(run.Convert());

        Assert.Equal(
            $"the output base '{run.Base}' has unrecognized fcabedrock transaction residue; remove it and retry.",
            refused.Message);
        run.AssertNoMutation();
        Assert.Equal(before, run.Snapshot());
    }

    // ---- a foreign descriptor that never became a record ------------------------------------------

    [Fact]
    public void Caller_WhenOnlyAForeignFamilyIntentDescriptorExists_ThenItIsRefusedAndPreserved()
    {
        // No record — only the intent descriptor a single-file run writes before publishing one.
        // Its shape byte `41` reconstructs a single-file record, so the convert caller refuses it
        // on family authority alone, before any state is assembled.
        using var run = Boundary.Create();
        var residue = Residue.Create(run.Directory, "out", Boundary.Token);
        residue.WriteRecord([("stage", "out")]);
        var intent = run.Resolve(
            $"out.fcabedrock-intent-{Boundary.Token}-41-{residue.RecordDigest}-{Boundary.Identity}");
        File.WriteAllBytes(intent, residue.ControlBody("intent"));
        File.Delete(residue.RecordPath);
        var before = run.Snapshot();

        var refused = Assert.IsType<PublicationRefused>(run.Convert());

        Assert.Equal(
            $"the output base '{run.Base}' has unrecognized fcabedrock transaction residue; remove it and retry.",
            refused.Message);
        run.AssertNoMutation();
        Assert.Equal(before, run.Snapshot());
        Assert.True(File.Exists(intent));
    }

    [Fact]
    public void Caller_WhenForeignResidueIsRefused_ThenTheMessageNamesOnlyTheCallersOwnOperand()
    {
        using var run = Boundary.Create();
        Author(run, "convert", "staged");

        var refused = Assert.IsType<PublicationRefused>(run.Single());

        Assert.Contains(run.Base, refused.Message, StringComparison.Ordinal);
        foreach (var fragment in new[]
            { "fcabedrock-stage", "fcabedrock-backup", "fcabedrock-transaction", "fcabedrock-intent", "-e-", "-sc-", ".cxt" })
        {
            Assert.DoesNotContain(fragment, refused.Message, StringComparison.Ordinal);
        }

        // The operand's own temp directory carries a 32-hex name, so the token check is made on
        // what is left after the directory the caller itself named.
        Assert.DoesNotMatch(
            "[0-9a-f]{32}", refused.Message.Replace(run.Directory, string.Empty, StringComparison.Ordinal));
    }

    // ---- residue authoring ------------------------------------------------------------------------

    /// <summary>
    /// Writes a valid transaction of <paramref name="family"/> at base <c>out</c>, in the durable
    /// <paramref name="state"/>. Convert publishes <c>out.cxt</c>; single-file publishes <c>out</c>.
    /// </summary>
    private static void Author(Boundary run, string family, string state)
    {
        var single = string.Equals(family, "single", StringComparison.Ordinal);
        var target = single ? "out" : "out.cxt";
        var kind = single ? "s" : "c";
        var residue = Residue.Create(run.Directory, "out", Boundary.Token);

        if (string.Equals(state, "preparing", StringComparison.Ordinal))
        {
            // The record alone: nothing owned exists yet, so no evidence and no marker.
            residue.WriteRecord([("stage", target)]);
            return;
        }

        if (string.Equals(state, "committed", StringComparison.Ordinal))
        {
            // Past the commit point, awaiting cleanup: the old object is in its backup and the new
            // one is published at the target.
            residue.WriteRecord([("backup", target), ("stage", target)]);
            residue.WritePrivate("backup", target, OldBytes);
            File.WriteAllText(run.Resolve(target), NewBytes);
            residue.WriteEvidence(
                kind,
                target,
                residue.Identity("backup", target),
                residue.IdentityOf(run.Resolve(target), "stage", target));
            residue.WriteMarker("staged");
            residue.WriteMarker("committed");
            return;
        }

        // Staged, and — for the rolling-back row — with the durable rollback intent beside it.
        residue.WriteRecord([("stage", target)]);
        residue.WritePrivate("stage", target, StageBytes);
        residue.WriteEvidence(kind, target, IdentityEvidence.NotApplicable, residue.Identity("stage", target));
        residue.WriteMarker("staged");
        if (string.Equals(state, "rollingback", StringComparison.Ordinal))
        {
            residue.WriteMarker("rollback");
        }
    }

    /// <summary>
    /// Two runs at different bases whose private namespaces overlap: a convert transaction at
    /// <c>out</c> and a single-file transaction at <c>out.cxt</c>. Each caller then finds, inside
    /// its own namespace, a private name no record of its own base accounts for.
    /// </summary>
    private static void Overlap(Boundary run)
    {
        var convert = Residue.Create(run.Directory, "out", Boundary.Token);
        convert.WriteRecord([("stage", "out.cxt")]);
        convert.WritePrivate("stage", "out.cxt", StageBytes);

        var single = Residue.Create(run.Directory, "out.cxt", Boundary.Other);
        single.WriteRecord([("stage", "out.cxt")]);
        single.WritePrivate("stage", "out.cxt", StageBytes);
    }

    private static string Final(string family) =>
        string.Equals(family, "single", StringComparison.Ordinal) ? "out" : "out.cxt";
}

/// <summary>
/// One temporary location shared by both families: an input, the base operand <c>out</c>, and the
/// injected filesystem each caller is driven with. Both callers are the real preflight entry
/// points, so the family authority under test is the production one.
/// </summary>
internal sealed class Boundary : IDisposable
{
    /// <summary>The token every hand-authored residue uses; a real run's own is unpredictable.</summary>
    public const string Token = "aaaaaaaabbbbbbbbccccccccdddddddd";

    /// <summary>A second token, for the run whose namespace overlaps the first's.</summary>
    public const string Other = "bbbbbbbbccccccccddddddddeeeeeeee";

    /// <summary>A stand-in acknowledged identity for a hand-authored intent descriptor.</summary>
    public const string Identity = "ccccccccddddddddeeeeeeeeffffffff";

    private readonly TempDirectory _temp;

    private Boundary(TempDirectory temp, string input)
    {
        _temp = temp;
        Input = input;
    }

    /// <summary>The injected publication filesystem for the current attempt.</summary>
    public RecordingPublicationFileSystem Files { get; } = new();

    /// <summary>The file both callers read.</summary>
    public string Input { get; }

    /// <summary>The directory every target and all residue live in.</summary>
    public string Directory => _temp.Path;

    /// <summary>The verbatim base operand both callers are given.</summary>
    public string Base => _temp.Resolve("out");

    public static Boundary Create()
    {
        var temp = TempDirectory.Create();
        return new Boundary(temp, temp.Write("input.csv", "colour\nred\n"));
    }

    /// <summary>The full path of <paramref name="name"/> in this location.</summary>
    public string Resolve(string name) => _temp.Resolve(name);

    /// <summary>The single-file caller, at <paramref name="outPath"/> or the shared base.</summary>
    public PublicationPreparation Single(string? outPath = null, bool force = true) =>
        PublicationTransaction.PreflightSingle(
            Files, FileIdentity.CreateDefault, outPath ?? Base, Inputs, force, CancellationToken.None);

    /// <summary>The convert caller, publishing <c>BASE.cxt</c> from the shared base.</summary>
    public PublicationPreparation Convert(bool force = true) =>
        PublicationTransaction.Preflight(
            Files,
            FileIdentity.CreateDefault,
            Base,
            [PublicationTargetKind.Cxt],
            Inputs,
            force,
            CancellationToken.None);

    /// <summary>Every file name paired with its exact bytes — the before/after preservation oracle.</summary>
    public Dictionary<string, byte[]> Snapshot() =>
        System.IO.Directory.GetFiles(Directory)
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes, StringComparer.Ordinal);

    /// <summary>Every file that claims a private transaction namespace.</summary>
    public IReadOnlyList<string> Residue() =>
        [.. Snapshot().Keys.Where(name => name.Contains(".fcabedrock-", StringComparison.Ordinal))];

    /// <summary>
    /// Asserts that not one mutating filesystem operation was even attempted. A foreign-family
    /// refusal is settled during classification, before recovery, so unlike the same-family
    /// unusable-stage retry there is no removal boundary to reach at all.
    /// </summary>
    public void AssertNoMutation() =>
        Assert.DoesNotContain(
            Files.Operations,
            operation => operation.StartsWith("CreateNew:", StringComparison.Ordinal)
                || operation.StartsWith("Confidential:", StringComparison.Ordinal)
                || operation.StartsWith("Move:", StringComparison.Ordinal)
                || operation.StartsWith("Delete:", StringComparison.Ordinal));

    public void Dispose() => _temp.Dispose();

    private PublicationInput[] Inputs => [new PublicationInput(Input, Input)];
}
