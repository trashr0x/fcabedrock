namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The public relocation route and its closed family (D-140): a verified decision carries the
/// reference to write, the authored text, the destination and the loaded base it was verified
/// against; a recheck probes afresh and returns the same decision while it holds; and the host's
/// identity seam is the one both use. Relocation produces no diagnostics.
/// </summary>
public sealed class SpecRelocationTests
{
    [Fact]
    public void Relocate_WhenTheAuthoredReferenceStillHolds_ThenAVerifiedDecisionKeepsItAndRecheckReturnsTheSameInstance()
    {
        using var temp = TempDirectory.Create();
        var immediateBase = Base(temp);
        var host = new OrchestrationHost(File.OpenRead);
        var destination = temp.Resolve("copy.toml");

        var decision = Assert.IsType<OperationCompleted<VerifiedRelocation>>(
            SpecRelocation.Relocate("base.toml", destination, immediateBase, host)).Value;

        Assert.Equal("base.toml", decision.Reference);
        Assert.Equal("base.toml", decision.Authored);
        Assert.Equal(destination, decision.Destination);
        Assert.Same(immediateBase, decision.ImmediateBase);

        var rechecked = SpecRelocation.Recheck(decision, host);
        Assert.Same(decision, Assert.IsType<OperationCompleted<RelocationDecision>>(rechecked).Value);
        Assert.Empty(rechecked.Diagnostics);
    }

    [Fact]
    public void Relocate_WhenTheReferenceMustBeRebuilt_ThenTheVerifiedDecisionCarriesTheRebuiltTextAndTheImmediateBase()
    {
        using var temp = TempDirectory.Create();
        var immediateBase = Base(temp);
        var destination = temp.Resolve("nested/copy.toml");

        var outcome = SpecRelocation.Relocate("base.toml", destination, immediateBase, new OrchestrationHost(File.OpenRead));

        var decision = Assert.IsType<OperationCompleted<VerifiedRelocation>>(outcome).Value;
        Assert.Equal("../base.toml", decision.Reference);
        Assert.Equal("base.toml", decision.Authored);
        Assert.Same(immediateBase, decision.ImmediateBase);
        Assert.Empty(outcome.Diagnostics);
    }

    [Fact]
    public void Recheck_WhenTheBaseWasReplacedAfterTheDecision_ThenItIsRefusedAsUnverified()
    {
        // A recheck is a new observation: a different object at the base's path does not hold.
        using var temp = TempDirectory.Create();
        var immediateBase = Base(temp);
        var host = new OrchestrationHost(File.OpenRead);
        var destination = temp.Resolve("nested/copy.toml");
        var decision = Assert.IsType<OperationCompleted<VerifiedRelocation>>(
            SpecRelocation.Relocate("base.toml", destination, immediateBase, host)).Value;

        // The loaded base is moved aside, not deleted. While it exists, no other file can have its
        // identity, so the replacement is a distinguishable object. A deleted file's identifier can
        // be handed to the next file created (on ext4 it often is, D-125), and the identity check
        // could then not tell the replacement from the loaded base. The two assertions check that
        // precondition on the platform the test runs on.
        var aside = temp.Resolve("base.toml.aside");
        File.Move(immediateBase.FullPath, aside);
        File.WriteAllText(immediateBase.FullPath, "[spec]\nversion = 1\n");
        var identities = FileIdentity.CreateDefault();
        Assert.Equal(immediateBase.Identity, identities.KeyFor(aside));
        Assert.NotEqual(immediateBase.Identity, identities.KeyFor(immediateBase.FullPath));

        var error = Assert.IsType<OperationHostFailure<RelocationDecision>>(SpecRelocation.Recheck(decision, host)).Error;
        Assert.Equal(HostErrorKind.RelocationUnverified, error.Kind);
        Assert.Equal(ExtendsRelocation.UnverifiedMessage(destination, "base.toml"), error.Message);
        Assert.Equal(destination, error.Subject);
        Assert.Equal("base.toml", error.Counterpart);
    }

    [Fact]
    public void Relocate_WhenTheHostIdentitySeamIsReplaced_ThenTheDecisionUsesIt()
    {
        // The base was loaded with an OS identity; a host whose seam can only answer the path
        // fallback cannot verify it, because keys of different kinds never match. The default
        // host verifies the same reference, so the seam, not a fixed service, decided.
        using var temp = TempDirectory.Create();
        var immediateBase = Base(temp);
        var destination = temp.Resolve("copy.toml");
        var fallbackOnly = new OrchestrationHost(File.OpenRead)
        {
            SpecIdentities = static () => new FileIdentity(new UnavailableFileIdentityProbe()),
        };

        var refused = SpecRelocation.Relocate("base.toml", destination, immediateBase, fallbackOnly);
        var verified = SpecRelocation.Relocate("base.toml", destination, immediateBase, new OrchestrationHost(File.OpenRead));

        Assert.Equal(HostErrorKind.RelocationUnverified, Assert.IsType<OperationHostFailure<VerifiedRelocation>>(refused).Error.Kind);
        Assert.IsType<OperationCompleted<VerifiedRelocation>>(verified);
    }

    // A base loaded with this platform's OS identity.
    private static SpecChainFile Base(TempDirectory temp)
    {
        var path = temp.Write("base.toml", "[spec]\nversion = 1\n");
        var identity = FileIdentity.CreateDefault().KeyFor(path);
        Assert.SkipUnless(identity.IsOperatingSystemIdentity, "this host supplies no OS file identity.");
        return new SpecChainFile(path, "base.toml", "sha256:unused", identity);
    }
}
