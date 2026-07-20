using FcaBedrock.Core.Spec;

namespace FcaBedrock.Conversion.Tests;

// Conversion's object-name usability must be the SAME authority probe will apply, or a draft
// could accept a subject the conversion it promises then rejects. The predicate now
// lives in Core; this pins that Conversion forwards to it and that its behavior is unchanged.
public sealed class ObjectNamesTests
{
    [Theory]
    [InlineData("s1")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("quote\"inside")]
    [InlineData("?")]
    public void IsUsable_AgreesWithTheCoreAuthority(string? name) =>
        Assert.Equal(ObjectNameValidity.IsUsable(name), ObjectNames.IsUsable(name));

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(127)]
    public void IsUsable_AgreesWithTheCoreAuthorityOnControlCharacters(int codePoint)
    {
        var name = "a" + (char)codePoint + "b";

        Assert.Equal(ObjectNameValidity.IsUsable(name), ObjectNames.IsUsable(name));
        Assert.False(ObjectNames.IsUsable(name));
    }

    [Fact]
    public void IsUsable_KeepsItsExistingBehavior()
    {
        // The pre-hoist behavior, restated directly so the delegation cannot silently loosen it.
        Assert.True(ObjectNames.IsUsable("s1"));
        Assert.False(ObjectNames.IsUsable(null));
        Assert.False(ObjectNames.IsUsable(" "));
    }
}
