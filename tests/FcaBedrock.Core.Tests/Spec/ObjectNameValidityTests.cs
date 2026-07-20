using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Spec;

// The D-085 data-derived object-name predicate, hoisted to Core so probe and calibrate/emit
// share one authority. Usable = non-null, not empty/whitespace-only, no control
// characters.
public sealed class ObjectNameValidityTests
{
    [Theory]
    [InlineData("s1")]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData(" leading and trailing ")]      // interior/edge spaces are fine — it is not blank.
    [InlineData("punctuation-,.;:!?")]
    [InlineData("quote\"inside")]               // §10.1 name validity is a DIFFERENT predicate.
    [InlineData("café")]
    [InlineData("日本語")]
    [InlineData("0")]
    [InlineData("?")]                           // the missing token as a literal subject value.
    public void IsUsable_WhenPresentAndPrintable_ThenTrue(string name) =>
        Assert.True(ObjectNameValidity.IsUsable(name));

    [Fact]
    public void IsUsable_WhenNull_ThenFalse() =>
        Assert.False(ObjectNameValidity.IsUsable(null));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsUsable_WhenEmptyOrWhitespaceOnly_ThenFalse(string name) =>
        Assert.False(ObjectNameValidity.IsUsable(name));

    // Control characters are given as code points rather than literals, so no raw control byte
    // is embedded in this source file. A newline is the load-bearing case — it would corrupt the
    // line-structured .cxt (§18.1) — but the predicate rejects the whole class.
    [Theory]
    [InlineData(0)]         // NUL
    [InlineData(7)]         // bell
    [InlineData(9)]         // tab
    [InlineData(10)]        // line feed
    [InlineData(13)]        // carriage return
    [InlineData(27)]        // escape
    [InlineData(127)]       // delete
    [InlineData(133)]       // NEL, a C1 control
    public void IsUsable_WhenInteriorControlCharacter_ThenFalse(int codePoint) =>
        Assert.False(ObjectNameValidity.IsUsable("a" + (char)codePoint + "b"));

    [Theory]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(0)]
    public void IsUsable_WhenTrailingControlCharacter_ThenFalse(int codePoint) =>
        Assert.False(ObjectNameValidity.IsUsable("name" + (char)codePoint));
}
