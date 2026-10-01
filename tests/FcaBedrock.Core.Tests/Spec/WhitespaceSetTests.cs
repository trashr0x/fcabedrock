namespace FcaBedrock.Core.Tests.Spec;

// Spec §5.1 defines whitespace W as an explicit list of 25 UTF-16 code units. The reading rules
// (Sources) and numeric parsing (CanonicalNumber.TryParse) rely on the runtime's
// char.IsWhiteSpace and span Trim for that set, so both are locked here against the literal
// list, over the whole char domain.
public sealed class WhitespaceSetTests
{
    private static readonly int[] DocumentedWhitespace =
    [
        0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x0020, 0x0085, 0x00A0, 0x1680,
        0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A,
        0x2028, 0x2029, 0x202F, 0x205F, 0x3000,
    ];

    [Fact]
    public void CharIsWhiteSpace_OverTheCharDomain_ThenEqualsTheDocumented25()
    {
        var documented = new HashSet<int>(DocumentedWhitespace);
        Assert.Equal(25, documented.Count);

        for (var code = 0; code <= char.MaxValue; code++)
        {
            Assert.True(documented.Contains(code) == char.IsWhiteSpace((char)code), $"U+{code:X4}");
        }
    }

    [Fact]
    public void SpanTrim_OverTheCharDomain_ThenRemovesExactlyTheDocumented25()
    {
        var documented = new HashSet<int>(DocumentedWhitespace);

        for (var code = 0; code <= char.MaxValue; code++)
        {
            var c = (char)code;
            var trimmed = $"{c}x{c}".AsSpan().Trim();
            Assert.True(trimmed.Length == (documented.Contains(code) ? 1 : 3), $"U+{code:X4}");
        }
    }
}
