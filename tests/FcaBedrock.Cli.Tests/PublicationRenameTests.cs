using System.Runtime.InteropServices;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The publication rename itself (D-125): the exclusive primitive first, an <b>exact</b> gate on
/// which native result may fall back to a checked classic rename, and the guarded protocol that
/// fallback runs.
/// <para>
/// The capability table is exercised as the pure rule it is, so both platforms' rows are provable
/// from either host. The protocol around it runs the <em>real</em> primitives — the classic rename
/// a fallback ends in is the production one, injected only in which capability result reaches it —
/// because a fallback proved against a fake primitive proves nothing about a filesystem.
/// </para>
/// </summary>
public sealed class PublicationRenameTests
{
    // The errno values, spelled out so the two platforms' tables are readable side by side rather
    // than hidden behind a constant that could quietly be the wrong one.
    private const int EPerm = 1;
    private const int ENoEnt = 2;
    private const int EIo = 5;
    private const int EAccess = 13;
    private const int EExist = 17;
    private const int EInval = 22;
    private const int ERofs = 30;
    private const int EXDev = 18;
    private const int LinuxENoSys = 38;
    private const int LinuxEOpNotSupp = 95;
    private const int DarwinENotSup = 45;
    private const int DarwinENoSys = 78;
    private const int DarwinEOpNotSupp = 102;

    // ---- the capability table, both platforms ------------------------------------------------------

    [Theory]

    // The one collision answer, on either platform, and never a capability signal.
    [InlineData(EExist, false, "Collision")]
    [InlineData(EExist, true, "Collision")]

    // Linux: the VFS contract requires EINVAL for a flag a filesystem does not support, ENOTSUP and
    // EOPNOTSUPP are one errno (95) reached through a server's own reply, and ENOSYS is a kernel
    // without the call.
    [InlineData(EInval, false, "CapabilityAbsent")]
    [InlineData(LinuxEOpNotSupp, false, "CapabilityAbsent")]
    [InlineData(LinuxENoSys, false, "CapabilityAbsent")]

    // macOS: ONLY the documented ENOTSUP. Apple documents EINVAL as an invalid flag rather than an
    // unsupported one, and neither its ENOSYS nor its distinct modern EOPNOTSUPP is documented as
    // this call's capability answer — so Linux's readings are not copied across.
    [InlineData(DarwinENotSup, true, "CapabilityAbsent")]
    [InlineData(EInval, true, "Failed")]
    [InlineData(DarwinENoSys, true, "Failed")]
    [InlineData(DarwinEOpNotSupp, true, "Failed")]

    // Ordinary failures, on both: permission, access, I/O, read-only, cross-device, missing source.
    // None of them is permission to try a second, weaker move.
    [InlineData(EPerm, false, "Failed")]
    [InlineData(EPerm, true, "Failed")]
    [InlineData(EAccess, false, "Failed")]
    [InlineData(EAccess, true, "Failed")]
    [InlineData(EIo, false, "Failed")]
    [InlineData(EIo, true, "Failed")]
    [InlineData(ERofs, false, "Failed")]
    [InlineData(ERofs, true, "Failed")]
    [InlineData(EXDev, false, "Failed")]
    [InlineData(EXDev, true, "Failed")]
    [InlineData(ENoEnt, false, "Failed")]
    [InlineData(ENoEnt, true, "Failed")]
    public void Classify_WhenTheExclusiveRenameFails_ThenOnlyTheNamedResultsPermitFallback(
        int error, bool darwin, string expected) =>
        Assert.Equal(expected, PublicationNative.ClassifyUnixRename(error, darwin).ToString());

    // ---- what each outcome does -------------------------------------------------------------------

    [Fact]
    public void Move_WhenTheExclusiveRenameSucceeds_ThenNothingElseIsCalled()
    {
        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Resolve("destination");
        var primitives = new RecordingRenamePrimitives();

        PublicationRename.Move(primitives, source, destination, null);

        // The real fast path, witnessed by the calls rather than inferred from the outcome: the
        // exclusive primitive ran, and no absence check or classic rename followed it.
        Assert.Equal(["exclusive"], primitives.Calls);
        Assert.False(File.Exists(source));
        Assert.Equal("payload", File.ReadAllText(destination));
    }

    [Fact]
    public void Move_WhenTheDestinationExists_ThenItIsACollisionAndNeverAFallback()
    {
        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Write("destination", "keep me");
        var primitives = new RecordingRenamePrimitives();

        Assert.Throws<IOException>(() => PublicationRename.Move(primitives, source, destination, null));

        // EEXIST is a refusal, not a capability answer — so no second, weaker attempt happens even
        // though a classic rename would have succeeded at replacing it.
        Assert.Equal(["exclusive"], primitives.Calls);
        Assert.Equal("keep me", File.ReadAllText(destination));
        Assert.Equal("payload", File.ReadAllText(source));
    }

    [Fact]
    public void Move_WhenAnOrdinaryFailureIsReported_ThenNoSecondPrimitiveIsTried()
    {
        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Resolve("destination");
        var primitives = new RecordingRenamePrimitives
        {
            // Cross-device: the one failure a copy would "fix", and precisely the one this contract
            // refuses to fix that way.
            Injected = new ExclusiveRenameResult(ExclusiveRename.Failed, EXDev),
        };

        Assert.Throws<IOException>(() => PublicationRename.Move(primitives, source, destination, null));

        Assert.Equal(["exclusive"], primitives.Calls);
        Assert.False(File.Exists(destination));
        Assert.Equal("payload", File.ReadAllText(source));
    }

    [Fact]
    public void Move_WhenTheDirectoriesDiffer_ThenItIsAContractFaultBeforeAnyNativeCall()
    {
        // Same-directory is what makes "same filesystem" true by construction, and what makes the
        // absence of any copy flag safe. A cross-directory request is a defect in the transaction,
        // not a user-facing failure, so it never reaches a native call at all.
        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var nested = Directory.CreateDirectory(temp.Resolve("nested"));
        var primitives = new RecordingRenamePrimitives();

        Assert.Throws<NotSupportedException>(
            () => PublicationRename.Move(primitives, source, Path.Combine(nested.FullName, "source"), null));

        Assert.Empty(primitives.Calls);
    }

    // ---- the guarded fallback ---------------------------------------------------------------------

    [Fact]
    public void Move_WhenTheCapabilityIsAbsent_ThenExactlyOneClassicRenameFollowsTheAbsenceCheck()
    {
        SkipWithoutUnixPrimitives();

        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Resolve("destination");
        var primitives = new RecordingRenamePrimitives
        {
            Injected = new ExclusiveRenameResult(ExclusiveRename.CapabilityAbsent, EInval),
        };

        PublicationRename.Move(primitives, source, destination, null);

        // Exactly this, in exactly this order: one exclusive attempt, one entry-absence check, one
        // classic rename. No pre-delete, no placeholder, no retry, and no third primitive.
        Assert.Equal(["exclusive", "lookup", "classic"], primitives.Calls);
        Assert.False(File.Exists(source));
        Assert.Equal("payload", File.ReadAllText(destination));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("directory")]
    [InlineData("dangling-symlink")]
    public void Move_WhenAnEntryAppearsBeforeTheAbsenceCheck_ThenItIsACollisionAndNothingMoves(string kind)
    {
        SkipWithoutUnixPrimitives();

        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Resolve("destination");

        var primitives = new RecordingRenamePrimitives
        {
            Injected = new ExclusiveRenameResult(ExclusiveRename.CapabilityAbsent, EInval),

            // The entry arrives after the capability answer and before the absence check — the
            // interval the check exists for.
            BeforeLookup = () => Create(destination, kind),
        };

        Assert.Throws<IOException>(() => PublicationRename.Move(primitives, source, destination, null));

        // A dangling symbolic link is the sharp case: `File.Exists` answers false for it, so an
        // absence check built on that would have renamed straight over the link entry. `lstat` does
        // not follow the final link, and any entry at all is a collision.
        Assert.Equal(["exclusive", "lookup"], primitives.Calls);
        Assert.Equal("payload", File.ReadAllText(source));
    }

    [Fact]
    public void Move_WhenACompensationDestinationIsOccupied_ThenTheReverseRenameIsRefusedToo()
    {
        SkipWithoutUnixPrimitives();

        // Compensation moves an object BACK to the name it came from, and that name is a
        // destination like any other: the same gate, the same absence check, the same refusal.
        using var temp = TempDirectory.Create();
        var staged = temp.Write("staged", "payload");
        var published = temp.Resolve("published");
        var primitives = new RecordingRenamePrimitives
        {
            Injected = new ExclusiveRenameResult(ExclusiveRename.CapabilityAbsent, EInval),
        };

        PublicationRename.Move(primitives, staged, published, null);
        primitives.Calls.Clear();

        // Something has taken the source name back in the meantime.
        File.WriteAllText(staged, "someone else");

        Assert.Throws<IOException>(() => PublicationRename.Move(primitives, published, staged, null));

        Assert.Equal(["exclusive", "lookup"], primitives.Calls);
        Assert.Equal("someone else", File.ReadAllText(staged));
        Assert.Equal("payload", File.ReadAllText(published));
    }

    [Fact]
    public void Move_WhenTheSourceIsNoLongerTheOwnedObject_ThenTheFallbackRefusesIt()
    {
        SkipWithoutUnixPrimitives();

        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Resolve("destination");

        using var reference = PublicationObjectReference.TryAcquire(source)
            ?? throw new InvalidOperationException("this host supplied no lifetime reference.");

        var primitives = new RecordingRenamePrimitives
        {
            Injected = new ExclusiveRenameResult(ExclusiveRename.CapabilityAbsent, EInval),

            // A failed exclusive attempt is not evidence that the namespace stayed still, so the
            // source is revalidated against the reference before anything else happens — which is
            // why this substitution is placed the instant that attempt reports back.
            AfterExclusive = () =>
            {
                File.Delete(source);
                File.WriteAllText(source, "an impostor");
            },
        };

        Assert.Throws<IOException>(() => PublicationRename.Move(primitives, source, destination, reference));

        Assert.Equal(["exclusive"], primitives.Calls);
        Assert.False(File.Exists(destination));
        Assert.Equal("an impostor", File.ReadAllText(source));
    }

    [Fact]
    public void Move_WhenAnEntryAppearsAfterTheAbsenceCheck_ThenTheDisclosedRaceIsWhatHappens()
    {
        SkipWithoutUnixPrimitives();

        // THE EXCLUDED RACE, witnessed rather than claimed safe. Between the last absence check and
        // the classic rename an actor that violates the exclusive-namespace precondition can create
        // the destination, and the flagless rename replaces it. The post-move identity match still
        // proves which SOURCE object arrived — and that is all it proves: the foreign entry is gone
        // and nothing here can restore it. Windows never reaches this path at all.
        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Resolve("destination");

        using var reference = PublicationObjectReference.TryAcquire(source)
            ?? throw new InvalidOperationException("this host supplied no lifetime reference.");

        var primitives = new RecordingRenamePrimitives
        {
            Injected = new ExclusiveRenameResult(ExclusiveRename.CapabilityAbsent, EInval),
            BeforeClassic = () => File.WriteAllText(destination, "a foreign entry"),
        };

        PublicationRename.Move(primitives, source, destination, reference);

        Assert.Equal(["exclusive", "lookup", "classic"], primitives.Calls);

        // The source object arrived, and its identity proves that much.
        Assert.Equal("payload", File.ReadAllText(destination));
        Assert.True(reference.IsStillAt(destination));

        // And the foreign entry did not survive ANYWHERE. This is the disclosed limit of the
        // fallback — an excluded race under the exclusive-namespace precondition — rather than a
        // guarantee the implementation fails to keep.
        Assert.DoesNotContain(
            Directory.GetFiles(temp.Path),
            path => string.Equals(File.ReadAllText(path), "a foreign entry", StringComparison.Ordinal));
    }

    // ---- no copy, and the identity that proves it -------------------------------------------------

    [Fact]
    public void Move_WhenAnObjectIsRenamed_ThenTheSameObjectArrivesRatherThanACopyOfIt()
    {
        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Resolve("destination");

        using var reference = PublicationObjectReference.TryAcquire(source)
            ?? throw new InvalidOperationException("this host supplied no lifetime reference.");

        PublicationRename.Move(new RecordingRenamePrimitives(), source, destination, reference);

        // A metadata rename preserves the object, so the still-open original is what is now at the
        // destination. A copy would be a different object however identical its bytes.
        Assert.True(reference.IsStillAt(destination));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void Reference_WhenAByteIdenticalCopyIsMade_ThenItIsNotTheSameObject()
    {
        // The negative that gives the proof above its meaning: identical bytes are not identity.
        using var temp = TempDirectory.Create();
        var original = temp.Write("original", "payload");
        var copy = temp.Resolve("copy");
        File.Copy(original, copy);

        using var reference = PublicationObjectReference.TryAcquire(original)
            ?? throw new InvalidOperationException("this host supplied no lifetime reference.");

        Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(copy));
        Assert.True(reference.IsStillAt(original));
        Assert.False(reference.IsStillAt(copy));
    }

    // ---- the real native fast path, on this host ---------------------------------------------------

    [Fact]
    public void Move_OnThisVolume_ThenTheExclusivePrimitiveIsWhatRan()
    {
        // The witness the platform evidence rests on: it records the volume actually under test and
        // shows the exclusive primitive succeeded there, with no fallback. "The move succeeded"
        // alone cannot say which primitive ran.
        using var temp = TempDirectory.Create();
        var source = temp.Write("source", "payload");
        var destination = temp.Resolve("destination");

        // The production primitives for this host, wrapped only to observe.
        var primitives = new RecordingRenamePrimitives(PublicationNative.Primitives);

        PublicationRename.Move(primitives, source, destination, null);

        Assert.Equal(["exclusive"], primitives.Calls);
        Assert.DoesNotContain("classic", primitives.Calls, StringComparer.Ordinal);
        Assert.Equal("payload", File.ReadAllText(destination));

        // Recorded so a CI log says WHERE this held, not merely that it held.
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"exclusive native rename on {RuntimeInformation.OSDescription} "
            + $"({RuntimeInformation.ProcessArchitecture}), volume of '{temp.Path}'");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static void Create(string path, string kind)
    {
        switch (kind)
        {
            case "directory":
                Directory.CreateDirectory(path);
                return;

            case "dangling-symlink":
                File.CreateSymbolicLink(path, path + ".absent");
                return;

            default:
                File.WriteAllText(path, "keep me");
                return;
        }
    }

    // The fallback has no Windows implementation because Windows has no capability gap: its
    // no-replace behaviour IS the primitive. Skipping there is a genuinely absent platform API, not
    // a waiver — the Windows path is covered by the fast-path witness above and by the whole
    // publication suite.
    private static void SkipWithoutUnixPrimitives()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("the guarded classic fallback is a Unix path; Windows has no capability gap.");
        }
    }

    /// <summary>
    /// The production primitives, observed — and, where a test says so, with the exclusive attempt's
    /// result replaced by an exact one. The absence check and the classic rename are always the
    /// real ones, so a fallback proved here is a fallback through actual native calls.
    /// </summary>
    private sealed class RecordingRenamePrimitives(IPublicationRenamePrimitives? inner = null)
        : IPublicationRenamePrimitives
    {
        private readonly IPublicationRenamePrimitives _inner = inner ?? PublicationNative.Primitives;

        public List<string> Calls { get; } = [];

        /// <summary>The exact result the exclusive attempt reports, when a test fixes one.</summary>
        public ExclusiveRenameResult? Injected { get; set; }

        /// <summary>Runs the instant the exclusive attempt reports back, before anything reacts.</summary>
        public Action? AfterExclusive { get; set; }

        /// <summary>Runs immediately before the absence check.</summary>
        public Action? BeforeLookup { get; set; }

        /// <summary>Runs immediately before the classic rename — after the absence check.</summary>
        public Action? BeforeClassic { get; set; }

        public ExclusiveRenameResult Exclusive(string source, string destination)
        {
            Calls.Add("exclusive");
            var result = Injected ?? _inner.Exclusive(source, destination);
            AfterExclusive?.Invoke();
            AfterExclusive = null;
            return result;
        }

        public EntryLookup Lookup(string path)
        {
            BeforeLookup?.Invoke();
            BeforeLookup = null;
            Calls.Add("lookup");
            return _inner.Lookup(path);
        }

        public int Classic(string source, string destination)
        {
            BeforeClassic?.Invoke();
            BeforeClassic = null;
            Calls.Add("classic");
            return _inner.Classic(source, destination);
        }
    }
}
