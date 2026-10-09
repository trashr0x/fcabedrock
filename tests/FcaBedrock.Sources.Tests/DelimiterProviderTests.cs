using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The v1 delimiter alphabet (spec §5.1.1) against the pinned Sep 0.17.1 reader, as three separate
/// exhaustive locks: what the provider accepts, that every usable v1 delimiter reads, and that the
/// usable set (the alphabet without the double quote) equals the provider's accepted set. The
/// alphabet itself is locked in Core. A Sep version change that moves any of these must be
/// reviewed; the alphabet does not widen with it.
/// </summary>
public sealed class DelimiterProviderTests
{
    // The 95 characters pinned Sep 0.17.1 accepts as a separator, written out literally.
    private static readonly string ProviderAccepted =
        "\t" + (char)0x1F + " !$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    private static bool ProviderAccepts(char c)
    {
        try
        {
            _ = Sep.New(c);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Fact]
    public void SepNew_OverEveryChar_ThenThePinnedProviderAcceptsExactlyTheLiteral95()
    {
        var expected = new HashSet<char>(ProviderAccepted);
        Assert.Equal(95, expected.Count);

        for (var code = 0; code <= char.MaxValue; code++)
        {
            var c = (char)code;
            Assert.True(expected.Contains(c) == ProviderAccepts(c), $"U+{code:X4}");
        }
    }

    [Fact]
    public void UsableDelimiters_WhenComparedWithThePinnedProvider_ThenTheyAreExactlyTheAcceptedSet()
    {
        for (var code = 0; code <= char.MaxValue; code++)
        {
            var c = (char)code;
            var usable = SourceReadSettings.IsInDelimiterAlphabet(c) && c != '"';
            Assert.True(usable == ProviderAccepts(c), $"U+{code:X4}");
        }

        // The double quote is in the alphabet, but neither usable nor accepted by the provider.
        Assert.True(SourceReadSettings.IsInDelimiterAlphabet('"'));
        Assert.False(ProviderAccepts('"'));
    }

    [Fact]
    public async Task Read_WhenEveryUsableDelimiterSeparatesValues_ThenTheSessionsReadThem()
    {
        foreach (var delimiter in ProviderAccepted)
        {
            var (left, right) = delimiter is 'x' or 'y' ? ("p", "q") : ("x", "y");
            var text = left + delimiter + right + "\n";

            var records = await DrainAsync(WideSession(Opener(text), delimiter).ReadAsync());

            Assert.Equal([$"0:[<{left}>,<{right}>]"], records.Select(Render));
        }
    }
}
