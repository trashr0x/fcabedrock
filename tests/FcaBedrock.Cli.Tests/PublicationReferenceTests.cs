using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The lifetime reference that makes an identity mean something (D-125), and the whole sequence it
/// has to survive: created while the writer is open, still true after the writer closes, still
/// true after a rename, released around a handle-bound removal so the name becomes reusable.
/// <para>
/// <b>The hazard these prove is not hypothetical.</b> On ext4 the inode of a just-deleted file is
/// immediately free, so a replacement can be handed the very identifier the transaction recorded —
/// which is exactly how a substituted stage came to be published, and a foreign file came to be
/// deleted, on every Linux job of the first native run. The reference removes that by keeping the
/// original allocated; the cases below hold it to that on the platform they run on.
/// </para>
/// </summary>
public sealed class PublicationReferenceTests
{
    // ---- the hazard, and its removal ---------------------------------------------------------------

    [Fact]
    public void Reference_WhenTheOriginalIsClosed_ThenAReplacementCanInheritItsIdentifier()
    {
        // The control that gives every other case its meaning. It asserts a POSSIBILITY, not a
        // certainty — whether the identifier is reissued is the allocator's business, and on some
        // filesystems it never is — so it records what happened rather than requiring reuse.
        using var temp = TempDirectory.Create();
        var path = temp.Write("object", "original");

        var before = Identity(path);
        File.Delete(path);
        File.WriteAllText(path, "an impostor");
        var after = Identity(path);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"closed original: {before}; replacement: {after}; identifier reused: {before == after}");

        Assert.Equal("an impostor", File.ReadAllText(path));
    }

    [Fact]
    public void Reference_WhenTheOriginalIsHeld_ThenNoReplacementCanInheritItsIdentifier()
    {
        // And this is the invariant the correction rests on: while the reference is alive the
        // identifier cannot be reissued, so a substitute is ALWAYS distinguishable. Unlike the
        // control above, this is required rather than recorded.
        using var temp = TempDirectory.Create();
        var path = temp.Write("object", "original");

        using var reference = Acquire(path);

        File.Delete(path);
        File.WriteAllText(path, "an impostor");

        Assert.False(reference.IsStillAt(path), "a replacement was handed the held object's identifier");
        Assert.Equal("an impostor", File.ReadAllText(path));
    }

    [Fact]
    public void Reference_WhenTheReplacementIsByteIdentical_ThenItIsStillNotTheSameObject()
    {
        // The sharpest case: nothing about the content is wrong, and a content check would accept
        // it. Only identity — anchored — tells the two apart.
        using var temp = TempDirectory.Create();
        var path = temp.Write("object", "identical");

        using var reference = Acquire(path);

        File.Delete(path);
        File.WriteAllText(path, "identical");

        Assert.False(reference.IsStillAt(path));
    }

    // ---- the full lifecycle, on whichever platform this runs on ------------------------------------

    [Fact]
    public void Reference_WhenAcquiredWhileTheWriterIsOpen_ThenItSurvivesTheWholeSequence()
    {
        // Create → acquire while the creation handle is still open → flush and close the writer →
        // re-observe by path → rename → prove at the new name → remove through a proof-bound
        // handle → reuse the removed name. Every step of the sequence a publication actually
        // performs, in order, with the reference held across all of it.
        using var temp = TempDirectory.Create();
        var files = PublicationFileSystem.Instance;
        var staged = temp.Resolve("staged");
        var published = temp.Resolve("published");

        var created = files.CreateNew(staged);
        Assert.NotNull(created.Reference);
        Assert.NotNull(created.Identity);

        // Acquired while the creation handle was still open, and PROVED equal to what that handle
        // reported. That is the whole acquisition rule, and it is why an identity is reported at
        // all: an unproved one would be a number, not an anchor.
        Assert.Equal(created.Identity, created.Reference!.Identity);

        using (created.Content)
        {
            // It is a genuinely different kind of open, which is why it can be taken at this
            // instant at all: the reference asks only for attributes and is outside the share
            // check, while a path re-observation asks for data read and — on Windows — is refused
            // by the FileShare.None creation handle. What is asserted is the property both
            // platforms share: the anchor, not the path, is what names the object here.
            //
            // The writer is untouched by it and still owns its stream.
            created.Content.Write("payload"u8);
            files.Flush(created.Content);
        }

        // The writer's flush-and-close boundary is exactly where it was; the reference did not move
        // it, and once the writer is closed the path re-observation agrees with the anchor again.
        Assert.True(created.Reference.IsStillAt(staged));

        // A path re-observation, which on Windows opens for data read while the reference is held.
        Assert.Equal(created.Reference.Identity, FileIdentity.CreateDefault().KeyFor(staged));

        files.Move(staged, published, created.Reference);
        Assert.True(created.Reference.IsStillAt(published));
        Assert.False(File.Exists(staged));

        // The proof-bound removal, through a handle opened with no sharing at all while this
        // reference is still open — the combination that has to work for any of this to be usable.
        var removed = files.Remove(published, (identity, _) => identity == created.Reference.Identity);
        Assert.True(removed);

        // On Windows the disposition names the object and takes effect when its LAST handle closes,
        // so the name is still occupied until the reference is released. Releasing it completes the
        // deletion — and releasing it can delete nothing else, because the disposition is bound to
        // that object.
        created.Reference.Dispose();

        Assert.False(File.Exists(published), "the removed name was still occupied after its reference closed");

        // And the name is reusable, which is the property a rollback's restore depends on.
        File.WriteAllText(published, "the next occupant");
        Assert.Equal("the next occupant", File.ReadAllText(published));
    }

    [Fact]
    public void Reference_WhenTheRemovalIsRefused_ThenTheObjectAndItsNameSurvive()
    {
        // The other half: a proof that does not accept the object leaves everything untouched —
        // no disposition, no deletion, nothing pending.
        using var temp = TempDirectory.Create();
        var files = PublicationFileSystem.Instance;
        var path = temp.Write("object", "keep me");

        using var reference = Acquire(path);

        Assert.False(files.Remove(path, (_, _) => false));
        Assert.True(File.Exists(path));
        Assert.Equal("keep me", File.ReadAllText(path));
        Assert.True(reference.IsStillAt(path));
    }

    [Fact]
    public void Reference_WhenAConfidentialStageIsCreated_ThenItIsAnchoredWithoutDisturbingItsOwnHandle()
    {
        // The data-bearing creation, which is held FileShare.None and — on Windows — carries an
        // owner-only DACL. The reference has to coexist with both, because it is taken while that
        // very handle is open.
        using var temp = TempDirectory.Create();
        var files = PublicationFileSystem.Instance;
        var path = temp.Resolve("stage");

        var created = files.CreateNewConfidential(path);
        using (created.Content)
        {
            Assert.NotNull(created.Reference);
            Assert.NotNull(created.Identity);
            Assert.Equal(created.Identity, created.Reference!.Identity);

            // The writer is unaffected: it still owns the stream and can still write through it.
            created.Content.Write("payload"u8);
            files.Flush(created.Content);
        }

        Assert.True(created.Reference!.IsStillAt(path));
        created.Reference.Dispose();
        Assert.Equal("payload", File.ReadAllText(path));
    }

    [Fact]
    public void Reference_WhenTheObjectIsAbsent_ThenNothingIsAnchoredAndNothingIsAuthorized()
    {
        // A failed acquisition is not an error to route around: it is the answer "no authority",
        // and every caller fails closed on it rather than proceeding on an unanchored identity.
        using var temp = TempDirectory.Create();

        Assert.Null(PublicationObjectReference.TryAcquire(temp.Resolve("absent")));
    }

    [Fact]
    public void Reference_WhenTheProofDoesNotMatchTheCreation_ThenNoReferenceIsHandedBack()
    {
        // The paired acquisition proves the reference IS the object an identity names. Given an
        // identity that names something else, it hands back nothing at all.
        using var temp = TempDirectory.Create();
        var first = temp.Write("first", "one");
        var second = temp.Write("second", "two");

        using var other = Acquire(second);

        Assert.Null(PublicationObjectReference.TryAcquire(first, other.Identity));
        Assert.Null(PublicationObjectReference.TryAcquire(first, null));
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static PublicationObjectReference Acquire(string path) =>
        PublicationObjectReference.TryAcquire(path)
        ?? throw new InvalidOperationException($"this host supplied no lifetime reference for '{path}'.");

    // The raw identifier, rendered for the record rather than compared as text: this is the value
    // the protocol digests, so a trace that prints it is a trace about the actual mechanism.
    // `FileIdentityKey` deliberately does not render itself, so nothing can leak one by accident.
    private static string Identity(string path)
    {
        var key = FileIdentity.CreateDefault().KeyFor(path);

        return key.TryGetOperatingSystemIdentity(out var volume, out var low, out var high)
            ? $"volume={volume} id={low}:{high}"
            : "no operating-system identity";
    }
}
