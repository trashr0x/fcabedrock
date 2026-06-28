using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Tests.Discretization;

public sealed class IdentityDiscretizerTests
{
    [Fact]
    public void Discretize_WhenGivenValue_ThenReturnsValueAsBin()
    {
        var discretizer = new IdentityDiscretizer();

        // The raw value is its own bin; domain membership is decided later by the planner/emitter.
        Assert.Equal(BinResult.Bin("broad"), discretizer.Discretize("broad"));
    }

    [Fact]
    public void Kind_WhenIdentity_ThenIsTheIdentityString()
    {
        Assert.Equal("identity", new IdentityDiscretizer().Kind);
    }
}
