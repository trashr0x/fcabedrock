using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The crash/retry transition matrix (CX-M7H-008): for <b>every</b> material filesystem
/// transition a publication performs, stop the run immediately after it and prove that a plain
/// retry converges.
/// <para>
/// <b>Why after, not before.</b> Failing before an operation proves the transaction handles a
/// refusal; it cannot produce the state a crash leaves, which is the state that decides whether a
/// retry rolls back or finishes forward. Here the real operation completes and every later one
/// then fails — so even rollback cannot run, and the directory holds exactly what an abrupt
/// termination at that instant would leave.
/// </para>
/// <para>
/// <b>What convergence means.</b> After the retry the location holds the complete previous set or
/// the complete new set — never a mixture, never a marker certifying bytes it does not describe,
/// and never surviving transaction residue.
/// </para>
/// </summary>
public sealed class PublicationCrashMatrixTests
{
    [Fact]
    public async Task Publication_WhenAForcedReplacementCrashesAtEveryTransition_ThenARetryConverges()
    {
        // The richest shape: three pre-existing artifacts, three backups, three commits, and a
        // manifest to demote and republish.
        await AssertConvergesAtEveryTransitionAsync(
            format: "both", manifest: true, preexisting: true, force: true);
    }

    [Fact]
    public async Task Publication_WhenAFirstRunCrashesAtEveryTransition_ThenARetryConverges()
    {
        await AssertConvergesAtEveryTransitionAsync(
            format: "both", manifest: true, preexisting: false, force: false);
    }

    [Fact]
    public async Task Publication_WhenAForcedNoManifestReplacementCrashesAtEveryTransition_ThenARetryConverges()
    {
        // The no-manifest parity case: the old marker is demoted but never republished, so the
        // retry must not resurrect it and must not treat its absence as an incomplete run.
        await AssertConvergesAtEveryTransitionAsync(
            format: "cxt", manifest: false, preexisting: true, force: true);
    }

    [Fact]
    public async Task Publication_WhenASingleArtifactRunCrashesAtEveryTransition_ThenARetryConverges()
    {
        await AssertConvergesAtEveryTransitionAsync(
            format: "dat", manifest: true, preexisting: true, force: true);
    }

    [Fact]
    public async Task Publication_WhenOnlySomeTargetsPreExistAndTheRunCrashesEverywhere_ThenARetryConverges()
    {
        // The CX-M7H-001 shape generalized: a subset pre-exists, so a crash before staging leaves
        // "some finals present, no stages" — the state that must never read as a partial commit.
        await AssertConvergesAtEveryTransitionAsync(
            format: "both", manifest: true, preexisting: true, force: true, only: ".cxt");
    }

    [Theory]
    [InlineData("both", true, "out.dat")]
    [InlineData("both", true, "out.manifest.toml")]
    [InlineData("cxt", false, "out.cxt")]
    public async Task Publication_WhenARollbackCrashesAtEveryTransition_ThenARetryFinishesIt(
        string format, bool manifest, string failCommitTo)
    {
        // CX-M7H-014. A successful run never rolls back, so a matrix derived from one cannot reach
        // a single rollback transition. This one drives a real rollback — a forced replacement
        // whose commit fails part-way — and stops at every step of the undo: deleting a stage,
        // deleting the artifact it had already published, renaming each backup home, and clearing
        // the markers and the record.
        var transitions = await RollbackTransitionsAsync(format, manifest, failCommitTo);
        Assert.NotEmpty(transitions);

        for (var index = 0; index < transitions.Count; index++)
        {
            using var scenario = Scenario.Create(preexisting: true, only: null);
            var old = scenario.Snapshot();

            var crashing = new CliTestHarness();
            crashing.PublicationFiles.FailKind = "Move";
            crashing.PublicationFiles.FailMoveTo = failCommitTo;
            crashing.PublicationFiles.CrashAfter = transitions[index];
            await scenario.RunAsync(crashing, format, manifest, force: true);

            Assert.True(
                crashing.PublicationFiles.Crashed,
                $"the rollback crash after '{transitions[index]}' never fired");

            AssertRecoverableInterruption(
                scenario, old, format, manifest, transitions[index], index, transitions.Count);

            // A plain retry, with no commit failure this time. Whatever the interrupted rollback
            // had done, the location must end as one coherent run — or refuse deterministically
            // with the previous set intact, where the interruption fell inside an unacknowledged
            // acquisition.
            var retry = new CliTestHarness();
            var exit = await scenario.RunAsync(retry, format, manifest, force: true);

            var context = $"rollback crash after '{transitions[index]}' ({index + 1}/{transitions.Count})";
            await AssertRetryOutcomeAsync(scenario, old, format, manifest, force: true, exit, context);
        }
    }

    [Fact]
    public async Task Publication_WhenRollbackCannotRestoreABackup_ThenARetryRestoresItAndKeepsTheOldBytes()
    {
        // The register's own example: the commit publishes the new CXT, the DAT rename fails,
        // rollback deletes the new CXT — and then cannot rename the old CXT backup home. Failing
        // EVERY matching move is what reaches it; failing only the first exercises the commit
        // alone and lets the restore succeed.
        using var scenario = Scenario.Create(preexisting: true, only: null);
        var old = scenario.Snapshot();

        var failing = new CliTestHarness();
        failing.PublicationFiles.FailKind = "Move";
        failing.PublicationFiles.FailMoveTo = "out.cxt";
        failing.PublicationFiles.FailEveryMatch = true;
        Assert.Equal(1, await scenario.RunAsync(failing, "both", manifest: true, force: true));

        // Before the retry: the record and the rollback marker are still there, and the old CXT is
        // held safely in its backup rather than lost.
        var residue = scenario.Residue();
        Assert.Contains(residue, name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains(".fcabedrock-rollback-", StringComparison.Ordinal));
        Assert.True(scenario.BackupHolds(old["out.cxt"]), "the old CXT was neither in place nor backed up");

        var retry = new CliTestHarness();
        Assert.Equal(0, await scenario.RunAsync(retry, "both", manifest: true, force: true));

        Assert.Empty(scenario.Residue());
        Assert.True(
            Scenario.IsCompleteNewRun(scenario.Snapshot(), "both", manifest: true),
            "the retry did not converge on a complete new run");
    }

    // The transitions a real ROLLBACK performs, learned from a run whose commit fails part-way.
    private static async Task<List<string>> RollbackTransitionsAsync(
        string format, bool manifest, string failCommitTo)
    {
        using var scenario = Scenario.Create(preexisting: true, only: null);
        var harness = new CliTestHarness();
        harness.PublicationFiles.FailKind = "Move";
        harness.PublicationFiles.FailMoveTo = failCommitTo;
        await scenario.RunAsync(harness, format, manifest, force: true);

        // Everything AFTER the failed commit rename is rollback work. The failing move itself is
        // excluded: it never completes, so there is no state to stop at.
        var transitions = new List<string>();
        var rolling = false;
        foreach (var operation in harness.PublicationFiles.Operations)
        {
            var folded = RecordingPublicationFileSystem.Fold(operation);
            if (folded.EndsWith("->" + failCommitTo, StringComparison.Ordinal))
            {
                rolling = true;
                continue;
            }

            if (rolling
                && (folded.StartsWith("Move:", StringComparison.Ordinal)
                    || folded.StartsWith("Confidential:", StringComparison.Ordinal)
                    || folded.StartsWith("Delete:", StringComparison.Ordinal)
                    || folded.StartsWith("CreateNew:", StringComparison.Ordinal)))
            {
                transitions.Add(folded);
            }
        }

        return transitions;
    }

    private static async Task AssertConvergesAtEveryTransitionAsync(
        string format, bool manifest, bool preexisting, bool force, string? only = null)
    {
        // One clean run first, purely to enumerate the transitions this configuration performs.
        var transitions = await TransitionsAsync(format, manifest, preexisting, force, only);
        Assert.NotEmpty(transitions);

        for (var index = 0; index < transitions.Count; index++)
        {
            using var scenario = Scenario.Create(preexisting, only);
            var old = scenario.Snapshot();

            var crashing = new CliTestHarness();
            crashing.PublicationFiles.CrashAfter = transitions[index];
            await scenario.RunAsync(crashing, format, manifest, force);

            Assert.True(
                crashing.PublicationFiles.Crashed,
                $"the crash after '{transitions[index]}' never fired");

            // The interruption state is inspected BEFORE the retry, so a retry cannot conceal an
            // unsafe intermediate state by publishing over it (CX-M7H-014).
            AssertRecoverableInterruption(scenario, old, format, manifest, transitions[index], index, transitions.Count);

            // The retry is an ordinary invocation — no residue knowledge, no special flags beyond
            // the ones the original run had.
            var retry = new CliTestHarness();
            var exit = await scenario.RunAsync(retry, format, manifest, force);

            var context = $"crash after '{transitions[index]}' (transition {index + 1}/{transitions.Count})";
            await AssertRetryOutcomeAsync(scenario, old, format, manifest, force, exit, context);
        }
    }

    /// <summary>
    /// What a plain retry is allowed to do, and nothing else.
    /// <para>
    /// <b>Either it converges</b> — no residue at all, and the location holding the complete
    /// previous set or the complete new run — <b>or it refuses, deterministically, having
    /// destroyed nothing.</b>
    /// </para>
    /// <para>
    /// The second outcome is not a weakening; it is where the ownership rule lands. An
    /// interruption between a successful create-new and the durable statement that acknowledges it
    /// leaves a state in which "this run created that object" and "an object was already there and
    /// refused this run" are the same bytes under the same name. Nothing on disk distinguishes
    /// them, so nothing is removed on a guess (CX-M7H-036/037): the previous set stays exactly as
    /// it was, the record survives so the state is classifiable, and every retry reports the same
    /// sanitized refusal.
    /// </para>
    /// </summary>
    private static async Task AssertRetryOutcomeAsync(
        Scenario scenario,
        Dictionary<string, string> old,
        string format,
        bool manifest,
        bool force,
        int exit,
        string context)
    {
        var state = scenario.Snapshot();
        var isOld = Scenario.IsCompleteOldRun(state, old);
        var isNew = Scenario.IsCompleteNewRun(state, format, manifest);
        var residue = scenario.Residue();

        if (residue.Count == 0)
        {
            Assert.True(
                isOld || isNew,
                $"{context}: the location is neither the complete previous set nor a complete new run: "
                + string.Join(", ", state.Keys));

            if (exit == 0)
            {
                Assert.True(isNew, $"{context}: exit 0 but the published run is incomplete");
            }

            return;
        }

        Assert.Equal(1, exit);

        // Nothing of the previous run was destroyed: every old file is either still at its target
        // or held in a transaction-owned backup waiting to be renamed home.
        foreach (var (name, content) in old)
        {
            var survives = state.TryGetValue(name, out var current)
                && string.Equals(current, content, StringComparison.Ordinal);

            Assert.True(
                survives || scenario.BackupHolds(content),
                $"{context}: the retry refused and '{name}' is neither in place nor held in a backup");
        }

        // And it is deterministic: a second retry reaches the same answer over the same bytes.
        var again = new CliTestHarness();
        Assert.Equal(1, await scenario.RunAsync(again, format, manifest, force));
        Assert.Equal(residue, scenario.Residue());
        Assert.Equal(state, scenario.Snapshot());
    }

    /// <summary>
    /// What must be true of the directory the instant the process disappeared, before anything
    /// tries to repair it.
    /// <para>
    /// The invariants that make recovery possible at all: no owned residue exists without a record
    /// or a pending record accounting for it, and no old file has been destroyed without a backup
    /// holding it. A retry that later succeeds proves neither.
    /// </para>
    /// </summary>
    private static void AssertRecoverableInterruption(
        Scenario scenario,
        Dictionary<string, string> old,
        string format,
        bool manifest,
        string transition,
        int index,
        int count)
    {
        var context = $"interrupted after '{transition}' (transition {index + 1}/{count})";
        var residue = scenario.Residue();
        var state = scenario.Snapshot();

        var hasAuthority = false;
        var owned = new List<string>();
        foreach (var name in residue)
        {
            // The files that can authorize cleaning up everything else: the record, the pending
            // record it is published from, and the intent descriptor that authorizes THAT.
            if (name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal)
                || name.Contains(".fcabedrock-pending-", StringComparison.Ordinal)
                || name.Contains(".fcabedrock-intent-", StringComparison.Ordinal))
            {
                hasAuthority = true;
                continue;
            }

            owned.Add(name);
        }

        Assert.True(
            owned.Count == 0 || hasAuthority,
            $"{context}: owned residue exists with no record to account for it: {string.Join(", ", owned)}");

        // Past the commit point the previous run is superseded on purpose — that is what dropping
        // the backups means — so the survival rule applies only before it. Crossing it is read
        // from the published files rather than from a marker, because marker cleanup runs after
        // the commit point and would make the signal disappear exactly when it is needed.
        if (Scenario.IsCompleteNewRun(state, format, manifest))
        {
            return;
        }

        // Before it, nothing of the previous run may be simply gone: either the file is still at
        // its target, or a backup of it is sitting there waiting to be renamed home.
        foreach (var (name, content) in old)
        {
            var survives = state.TryGetValue(name, out var current)
                && string.Equals(current, content, StringComparison.Ordinal);

            if (!survives)
            {
                Assert.True(
                    scenario.BackupHolds(content),
                    $"{context}: '{name}' is neither in place nor held in a backup");
            }
        }

        _ = format;
        _ = manifest;
    }

    // The mutating transitions of one clean run, in order, as `kind:name` operations the recording
    // filesystem can be told to crash after.
    private static async Task<List<string>> TransitionsAsync(
        string format, bool manifest, bool preexisting, bool force, string? only)
    {
        using var scenario = Scenario.Create(preexisting, only);
        var harness = new CliTestHarness();
        await scenario.RunAsync(harness, format, manifest, force);

        var transitions = new List<string>();
        foreach (var operation in harness.PublicationFiles.Operations)
        {
            if (operation.StartsWith("CreateNew:", StringComparison.Ordinal)
                || operation.StartsWith("Confidential:", StringComparison.Ordinal)
                || operation.StartsWith("Move:", StringComparison.Ordinal)
                || operation.StartsWith("Delete:", StringComparison.Ordinal)

                // Stream boundaries too: a record or a stage stops being empty and starts being
                // partial at a write, and becomes durable at a flush or a close (CX-M7H-012).
                || operation.StartsWith("StreamWrite:", StringComparison.Ordinal)
                || operation.StartsWith("StreamFlush:", StringComparison.Ordinal)
                || operation.StartsWith("StreamClose:", StringComparison.Ordinal))
            {
                // Folded: the enumeration run and the crashing run have different tokens, and the
                // transition being named is the same one either way.
                transitions.Add(RecordingPublicationFileSystem.Fold(operation));
            }
        }

        return transitions;
    }

    /// <summary>One output location, its inputs, and the state a run starts from.</summary>
    private sealed class Scenario : IDisposable
    {
        private const string OldCxt = "the old cxt";
        private const string OldDat = "the old dat";
        private const string OldManifest = "the old manifest";

        private readonly TempDirectory _temp;

        private Scenario(TempDirectory temp, string spec, string data, string basePath)
        {
            _temp = temp;
            Spec = spec;
            Data = data;
            Base = basePath;
        }

        public string Spec { get; }

        public string Data { get; }

        public string Base { get; }

        public static Scenario Create(bool preexisting, string? only)
        {
            var temp = TempDirectory.Create();
            var scenario = new Scenario(
                temp,
                temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
                temp.Write("data.csv", CliFixtures.WideData),
                temp.Resolve("out"));

            if (!preexisting)
            {
                return scenario;
            }

            // `only` narrows the prior state to a subset, which is what makes "some finals present,
            // none of them ours" reachable.
            foreach (var (extension, content) in
                new[] { (".cxt", OldCxt), (".dat", OldDat), (".manifest.toml", OldManifest) })
            {
                if (only is null || string.Equals(only, extension, StringComparison.Ordinal))
                {
                    File.WriteAllText(scenario.Base + extension, content, new UTF8Encoding(false));
                }
            }

            return scenario;
        }

        public Task<int> RunAsync(CliTestHarness harness, string format, bool manifest, bool force)
        {
            var argv = new List<string> { "convert", Spec, Data, "--out", Base, "--format", format };
            if (!manifest)
            {
                argv.Add("--no-manifest");
            }

            if (force)
            {
                argv.Add("--force");
            }

            return harness.RunAsync([.. argv]);
        }

        public Dictionary<string, string> Snapshot()
        {
            var state = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(_temp.Path, "out*"))
            {
                var name = Path.GetFileName(path);
                if (!name.Contains(".fcabedrock-", StringComparison.Ordinal))
                {
                    state[name] = File.ReadAllText(path);
                }
            }

            return state;
        }

        /// <summary>True when some transaction-owned backup currently holds <paramref name="content"/>.</summary>
        public bool BackupHolds(string content)
        {
            foreach (var path in Directory.GetFiles(_temp.Path))
            {
                if (Path.GetFileName(path).Contains(".fcabedrock-backup-", StringComparison.Ordinal)
                    && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<string> Residue()
        {
            var residue = new List<string>();
            foreach (var path in Directory.GetFiles(_temp.Path))
            {
                if (Path.GetFileName(path).Contains(".fcabedrock-", StringComparison.Ordinal))
                {
                    residue.Add(Path.GetFileName(path));
                }
            }

            return residue;
        }

        /// <summary>
        /// Every artifact this configuration publishes is present and carries this run's bytes,
        /// and any marker it refused to write is gone.
        /// <para>
        /// Files the configuration never touches — an old <c>.cxt</c> beside a <c>--format dat</c>
        /// run — are deliberately not consulted: leaving them is correct, and a manifest that
        /// lists only what was written certifies them truthfully.
        /// </para>
        /// </summary>
        public static bool IsCompleteNewRun(Dictionary<string, string> state, string format, bool manifest)
        {
            var published = new List<string>();
            if (format is "cxt" or "both")
            {
                published.Add(".cxt");
            }

            if (format is "dat" or "both")
            {
                published.Add(".dat");
            }

            if (manifest)
            {
                published.Add(".manifest.toml");
            }
            else if (state.ContainsKey("out.manifest.toml"))
            {
                // A --no-manifest run must not leave an old public marker beside its output.
                return false;
            }

            foreach (var extension in published)
            {
                if (!state.TryGetValue("out" + extension, out var content) || content is OldCxt or OldDat or OldManifest)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The location holds exactly what it held before the interrupted run started.</summary>
        public static bool IsCompleteOldRun(Dictionary<string, string> state, Dictionary<string, string> old)
        {
            if (state.Count != old.Count)
            {
                return false;
            }

            foreach (var (name, content) in old)
            {
                if (!state.TryGetValue(name, out var actual) || !string.Equals(actual, content, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public void Dispose() => _temp.Dispose();
    }
}
