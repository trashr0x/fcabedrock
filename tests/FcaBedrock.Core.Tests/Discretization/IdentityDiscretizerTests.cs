using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Tests.Discretization;

public sealed class IdentityDiscretizerTests
{
    [Fact]
    public void Discretize_WhenGivenValue_ThenReturnsValueUnchanged()
    {
        var discretizer = new IdentityDiscretizer();

        Assert.Equal("broad", discretizer.Discretize("broad"));
    }

    [Fact]
    public void Kind_WhenIdentity_ThenIsTheIdentityString()
    {
        Assert.Equal("identity", new IdentityDiscretizer().Kind);
    }
}
