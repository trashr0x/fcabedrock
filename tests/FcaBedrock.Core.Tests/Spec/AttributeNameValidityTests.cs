using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Spec;

/// <summary>
/// The §10.1 attribute-name rule: any non-empty string without CR, LF or the TOML key-quoting
/// character <c>"</c>. Each character is built from its code point, so no control character
/// appears raw in this file.
/// </summary>
public sealed class AttributeNameValidityTests
{
    [Theory]
    [InlineData(13)] // CR
    [InlineData(10)] // LF
    [InlineData(34)] // the TOML key-quoting character
    public void IsValid_WhenTheNameContainsAForbiddenCharacter_ThenFalse(int codePoint)
    {
        var forbidden = new string((char)codePoint, 1);

        Assert.False(AttributeNameValidity.IsValid(forbidden));
        Assert.False(AttributeNameValidity.IsValid("a" + forbidden));
        Assert.False(AttributeNameValidity.IsValid(forbidden + "a"));
        Assert.False(AttributeNameValidity.IsValid("a" + forbidden + "b"));
    }

    [Theory]
    [InlineData(0)]    // NUL
    [InlineData(9)]    // TAB
    [InlineData(11)]   // VERTICAL TAB
    [InlineData(12)]   // FORM FEED
    [InlineData(27)]   // ESCAPE
    [InlineData(127)]  // DELETE
    [InlineData(133)]  // NEXT LINE, which is neither CR nor LF
    [InlineData(8232)] // LINE SEPARATOR
    [InlineData(8233)] // PARAGRAPH SEPARATOR
    public void IsValid_WhenTheNameContainsAnyOtherCharacter_ThenTrue(int codePoint)
    {
        // Deliberately permissive, and deliberately not ObjectNameValidity: a control character
        // other than CR and LF is harmless in a TOML string.
        var other = new string((char)codePoint, 1);

        Assert.True(AttributeNameValidity.IsValid(other));
        Assert.True(AttributeNameValidity.IsValid("a" + other + "b"));
    }

    [Theory]
    [InlineData("bruises?")]
    [InlineData("feature.1")]
    [InlineData("days@home")]
    [InlineData("   ")]
    [InlineData("a\\b")]
    [InlineData("it's")]
    public void IsValid_WhenTheNameIsAPermissiveHeader_ThenTrue(string name) =>
        Assert.True(AttributeNameValidity.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsValid_WhenTheNameIsNullOrEmpty_ThenFalse(string? name) =>
        Assert.False(AttributeNameValidity.IsValid(name));
}
