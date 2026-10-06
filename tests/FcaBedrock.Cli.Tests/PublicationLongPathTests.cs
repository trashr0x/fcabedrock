using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Publication whose private files sit at full paths of 260 characters or more. In a process
/// without long-path support, a Win32 file call refuses such a path as not found even when the file
/// exists, so a removal that spelled the path the short way would report the file removed and leave
/// it. A successful run would then strand its private files, and a later run to the same base would
/// refuse because of them. The removal is handle-bound and must reach the object it proves (D-125).
/// <para>
/// Each case builds its output directory to an exact length under its own temporary directory, so
/// the lengths hold wherever that directory is. Where the limit does not apply (Unix, or a Windows
/// process with long paths enabled) these cases pass with or without the spelling rule they guard.
/// </para>
/// </summary>
public sealed class PublicationLongPathTests
{
    // The shortest full path a Win32 call refuses without the extended prefix.
    private const int Win32PathLimit = 260;

    // A private file name is the base file name plus a role suffix: 48 characters for identity
    // evidence, the shortest, and 120 for the intent descriptor, the longest.
    private const int ShortestPrivateSuffix = 48;
    private const int IntentSuffix = 120;

    private const string BaseName = "out";

    // Puts every private file of the base "out" past the limit: 210 + 1 + 3 + 48 = 262.
    private const int DeepDirectoryLength = 210;

    // Puts a short file name past the limit on its own, for the removal seam.
    private const int SeamDirectoryLength = 250;

    // ---- whole runs ------------------------------------------------------------------------------

    [Fact]
    public async Task Convert_WhenEveryPrivatePathIsPastTheLimit_ThenTheRunLeavesOnlyItsOutputs()
    {
        using var location = LongLocation.Create(DeepDirectoryLength);
        Assert.True(location.Base.Length + ShortestPrivateSuffix >= Win32PathLimit);
        var harness = new CliTestHarness();

        Assert.Equal(0, await location.ConvertAsync(harness));

        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(["out.cxt", "out.manifest.toml"], location.Entries());
    }

    [Fact]
    public async Task Convert_WhenForcedTwiceMoreAtTheSameBase_ThenEachRunLeavesOnlyItsOutputs()
    {
        // A forced run renames the existing outputs aside as backups and removes those once it has
        // committed, so the backups, also past the limit, must go too: a run that left them would
        // make the next one refuse.
        using var location = LongLocation.Create(DeepDirectoryLength);

        Assert.Equal(0, await location.ConvertAsync(new CliTestHarness()));
        Assert.Equal(["out.cxt", "out.manifest.toml"], location.Entries());

        Assert.Equal(0, await location.ConvertAsync(new CliTestHarness(), "--force"));
        Assert.Equal(["out.cxt", "out.manifest.toml"], location.Entries());

        Assert.Equal(0, await location.ConvertAsync(new CliTestHarness(), "--force"));
        Assert.Equal(["out.cxt", "out.manifest.toml"], location.Entries());
    }

    [Fact]
    public async Task Convert_WhenAForcedReplacementFailsAtItsLastCommit_ThenRollbackRestoresThePreviousOutputs()
    {
        // The outputs themselves sit past the limit here, so rollback must remove the .cxt it has
        // already committed before it can rename the previous one back. A removal that left it would
        // leave the previous manifest restored beside a .cxt from the failed run.
        var baseName = new string('r', 50);
        using var location = LongLocation.Create(DeepDirectoryLength, baseName);
        Assert.True(location.Base.Length + ".cxt".Length >= Win32PathLimit);
        Assert.Equal(0, await location.ConvertAsync(new CliTestHarness()));
        var previous = location.Outputs();

        // Different data, so the failed run's .cxt differs from the previous one.
        var other = location.Input("other.csv", "colour,size\ngreen,1\nred,2\n");
        var failing = new CliTestHarness();
        failing.PublicationFiles.FailKind = "Move";
        failing.PublicationFiles.FailMoveTo = baseName + ".manifest.toml";
        var failures = 0;
        failing.PublicationFiles.FailWith = () =>
        {
            failures++;
            return new IOException("injected failure");
        };

        Assert.Equal(1, await failing.RunAsync(
            "convert", location.Spec, other, "--out", location.Base, "--format", "cxt", "--force"));

        // Exactly the one injected failure: the manifest's commit rename, which follows the .cxt
        // commit. An earlier failure would also exit 1 and could leave the previous outputs.
        Assert.Equal(1, failures);
        Assert.Equal(previous, location.Outputs());
        Assert.Equal([baseName + ".cxt", baseName + ".manifest.toml"], location.Entries());
    }

    [Theory]
    [InlineData(259)]
    [InlineData(260)]
    public async Task Convert_WhenTheIntentPathIsAtTheLimit_ThenTheIntentIsRemovedLikeEveryOtherPrivateFile(
        int intentPathLength)
    {
        // At these lengths the intent descriptor, the longest private name, is the only private file
        // at or past the limit: 259 is the control, and 260 is the first length the limit refuses.
        using var location = LongLocation.Create(intentPathLength - 1 - BaseName.Length - IntentSuffix);
        Assert.Equal(intentPathLength, location.Base.Length + IntentSuffix);

        Assert.Equal(0, await location.ConvertAsync(new CliTestHarness(), "--no-manifest"));

        Assert.Equal(["out.cxt"], location.Entries());
    }

    [Fact]
    public async Task Calibrate_WhenEveryPrivatePathIsPastTheLimit_ThenTheRunLeavesOnlyTheFrozenSpec()
    {
        // The single-file family: the same transaction and removals, with the target as the base.
        using var location = LongLocation.Create(
            DeepDirectoryLength, spec: CliFixtures.CalibrateCutsSpec, data: CliFixtures.CalibrateWideData);
        var harness = new CliTestHarness();

        Assert.Equal(0, await location.CalibrateAsync(harness));

        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(["out"], location.Entries());
    }

    [Fact]
    public async Task Convert_WhenTheIntentNameWouldPassTheNameLimit_ThenTheRefusedStartLeavesNothingBehind()
    {
        // A base of 136 characters gives the intent descriptor a 256-character name, one more than
        // the 255 characters NTFS allows in a file name, so publication cannot start. The pending
        // record is created first and sits past the path limit here; the refusal must still remove it.
        using var location = LongLocation.Create(DeepDirectoryLength, baseName: new string('b', 136));
        var harness = new CliTestHarness();

        Assert.Equal(1, await location.ConvertAsync(harness));

        Assert.Contains("cannot start publication for the output base", harness.StdErr, StringComparison.Ordinal);
        Assert.Empty(location.Entries());
    }

    // ---- recovery by a later run -----------------------------------------------------------------

    [Fact]
    public async Task Convert_WhenARunStoppedBeforeItsRecordIsRetried_ThenRecoveryRemovesItsIntentAndPendingRecord()
    {
        // The stop leaves a complete intent descriptor and the empty pending record it names: the
        // state a later run recovers from the descriptor alone, before it begins its own.
        using var location = LongLocation.Create(DeepDirectoryLength);
        var stopped = new CliTestHarness();
        stopped.PublicationFiles.CrashAfter = "StreamClose:out.fcabedrock-intent-T-05-T-T";

        await location.ConvertAsync(stopped);

        Assert.True(stopped.PublicationFiles.Crashed, "the stop after the intent descriptor never fired");
        Assert.Collection(
            location.Entries(),
            name => Assert.StartsWith("out.fcabedrock-intent-", name, StringComparison.Ordinal),
            name => Assert.StartsWith("out.fcabedrock-pending-", name, StringComparison.Ordinal));

        Assert.Equal(0, await location.ConvertAsync(new CliTestHarness()));
        Assert.Equal(["out.cxt", "out.manifest.toml"], location.Entries());
    }

    [Fact]
    public async Task Convert_WhenARunStoppedAfterItsCommitIsRetried_ThenRecoveryFinishesItAndRemovesItsPrivateFiles()
    {
        // The stop comes right after the manifest commits, before the run cleans up. The retry is an
        // ordinary invocation: recovery finishes the interrupted run forward, the retry then finds
        // the outputs in place and refuses without --force, and the outputs are all that remain.
        using var location = LongLocation.Create(DeepDirectoryLength);
        var stopped = new CliTestHarness();
        stopped.PublicationFiles.CrashAfter = "Move:out.manifest.toml.fcabedrock-stage-T->out.manifest.toml";

        await location.ConvertAsync(stopped);

        Assert.True(stopped.PublicationFiles.Crashed, "the stop after the manifest commit never fired");
        Assert.Contains(location.Entries(), name => name!.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));

        var retry = new CliTestHarness();
        Assert.Equal(1, await location.ConvertAsync(retry));

        Assert.Contains("already exists; use --force to replace it.", retry.StdErr, StringComparison.Ordinal);
        Assert.Equal(["out.cxt", "out.manifest.toml"], location.Entries());
    }

    // ---- the removal seam ------------------------------------------------------------------------

    [Fact]
    public void Remove_WhenTheObjectIsPastTheLimit_ThenItIsRemovedThroughItsOwnHandleAndTheNameIsFreed()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(DirectoryOfLength(temp, SeamDirectoryLength), "object-past-the-limit");
        Assert.True(path.Length >= Win32PathLimit);
        var files = PublicationFileSystem.Instance;

        var created = files.CreateNew(path);
        Assert.NotNull(created.Reference);
        using (created.Content)
        {
            created.Content.Write("payload"u8);
            files.Flush(created.Content);
        }

        Assert.True(files.Remove(path, (identity, _) => identity == created.Reference!.Identity));

        // As at any length, the deletion completes when the reference, the last handle, closes.
        created.Reference!.Dispose();
        Assert.False(File.Exists(path), "an object past the limit survived its removal");

        File.WriteAllText(path, "the next occupant");
        Assert.Equal("the next occupant", File.ReadAllText(path));
    }

    [Fact]
    public void Remove_WhenTheProofRefusesAnObjectPastTheLimit_ThenTheObjectIsUntouchedAndTheRemovalSaysSo()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(DirectoryOfLength(temp, SeamDirectoryLength), "object-past-the-limit");
        File.WriteAllText(path, "keep me");
        var proofs = 0;

        var removed = PublicationFileSystem.Instance.Remove(path, (_, _) =>
        {
            proofs++;
            return false;
        });

        // Only an object the removal opened can be refused, so the proof must have been asked.
        Assert.False(removed);
        Assert.Equal(1, proofs);
        Assert.Equal("keep me", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Remove_WhenNothingIsAtAPathPastTheLimit_ThenTheRemovalReportsAbsenceWithoutAProof(bool parentExists)
    {
        using var temp = TempDirectory.Create();
        var directory = DirectoryOfLength(temp, SeamDirectoryLength);
        var path = parentExists
            ? Path.Combine(directory, "object-past-the-limit")
            : Path.Combine(directory, "no-such-directory", "object");

        Assert.True(PublicationFileSystem.Instance.Remove(
            path, (_, _) => throw new InvalidOperationException("nothing is there to prove")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Remove_WhenANameIsLongerThanTheFileSystemAllows_ThenTheRemovalFailsRatherThanReportingAbsence()
    {
        // No object can have this name, and the file system answers with an error of its own. That
        // answer is not "not found", so it must not read as removed.
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, new string('n', 256));

        Assert.ThrowsAny<IOException>(() => PublicationFileSystem.Instance.Remove(path, (_, _) => false));
    }

    // ---- helpers ---------------------------------------------------------------------------------

    // A directory of exactly `length` characters under the test's temporary directory, built from
    // components of at most 100 characters. A temporary directory that is already too long skips.
    private static string DirectoryOfLength(TempDirectory temp, int length)
    {
        var directory = temp.Path;
        Assert.SkipUnless(
            directory.Length + 2 <= length,
            $"the temporary directory has {directory.Length} characters, too many to build one of {length}.");

        while (directory.Length < length)
        {
            var remaining = length - directory.Length;
            var part = Math.Min(100, remaining - 1);
            if (remaining - 1 - part == 1)
            {
                // A last component needs at least one character after its separator.
                part--;
            }

            directory = Path.Combine(directory, new string('d', part));
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>A spec and its data in a short directory, and an output base in a long one.</summary>
    private sealed class LongLocation : IDisposable
    {
        private readonly TempDirectory _temp;
        private readonly string _data;
        private readonly string _directory;

        private LongLocation(TempDirectory temp, string spec, string data, string directory, string baseName)
        {
            _temp = temp;
            Spec = spec;
            _data = data;
            _directory = directory;
            Base = Path.Combine(directory, baseName);
        }

        public string Spec { get; }

        /// <summary>The <c>--out</c> operand: an absolute path in the long directory.</summary>
        public string Base { get; }

        public static LongLocation Create(
            int directoryLength, string baseName = BaseName, string? spec = null, string? data = null)
        {
            var temp = TempDirectory.Create();
            try
            {
                return new LongLocation(
                    temp,
                    temp.Write("spec.toml", spec ?? CliFixtures.IndexBoundSpec),
                    temp.Write("data.csv", data ?? CliFixtures.WideData),
                    DirectoryOfLength(temp, directoryLength),
                    baseName);
            }
            catch
            {
                temp.Dispose();
                throw;
            }
        }

        public Task<int> ConvertAsync(CliTestHarness harness, params string[] options) =>
            harness.RunAsync(["convert", Spec, _data, "--out", Base, "--format", "cxt", .. options]);

        public Task<int> CalibrateAsync(CliTestHarness harness) =>
            harness.RunAsync("calibrate", Spec, _data, "--out", Base);

        /// <summary>Every file in the long directory, by name, in ordinal order.</summary>
        public IEnumerable<string?> Entries() =>
            Directory.GetFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal);

        /// <summary>Writes another input beside the spec and returns its full path.</summary>
        public string Input(string name, string content) => _temp.Write(name, content);

        /// <summary>Every output in the long directory (no private file), by name, with its bytes.</summary>
        public Dictionary<string, byte[]> Outputs()
        {
            var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(_directory))
            {
                var name = Path.GetFileName(path);
                if (!name.Contains(".fcabedrock-", StringComparison.Ordinal))
                {
                    outputs[name] = File.ReadAllBytes(path);
                }
            }

            return outputs;
        }

        public void Dispose() => _temp.Dispose();
    }
}
