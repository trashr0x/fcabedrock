using FcaBedrock.Core.Spec;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Spec §5.1.1 checks every field of every record, including columns no attribute uses: a triple
/// read validates its unused columns (and counts their line breaks for later refusals) without
/// decoding them, and a wide read validates every column whether or not a spec binds it.
/// </summary>
public sealed class UnusedColumnTests
{
    [Fact]
    public async Task ReadRows_WhenAnUnusedColumnIsMalformed_ThenTheRowIsRefusedAtThatColumn()
    {
        var refused = await Assert.ThrowsAsync<SourceReadException>(
            () => DrainAsync(TripleSession(Opener("s,p,v,u,\"x\"y\n")).ReadRowsAsync(new TripleColumns(0, 1, 2))));

        Assert.Equal(
            DelimitedGrammarConformanceTests.Message(DelimitedGrammarConformanceTests.Defect.TextAfterClosingQuote, 0, 1, 4, 1),
            refused.Message);
    }

    [Fact]
    public async Task ReadRows_WhenAnUnusedColumnLiesBetweenRoles_ThenItIsValidatedButNotDecoded()
    {
        // Column 1 is unused and quoted with line breaks; the refusal in the next record must still
        // count them.
        const string Text = "s,\"u\n\nu\",p,v\ns2,u,p2,v\"2\n";

        var (rows, error) = await DrainCapturingAsync(TripleSession(Opener(Text)).ReadRowsAsync(new TripleColumns(0, 2, 3)));

        Assert.Equal(["0:[<s>,<p>,<v>]"], rows.Select(Render));
        var refused = Assert.IsType<SourceReadException>(error);
        Assert.Equal(
            DelimitedGrammarConformanceTests.Message(DelimitedGrammarConformanceTests.Defect.QuoteInUnquotedField, 1, 4, 3, 4),
            refused.Message);
    }

    [Fact]
    public async Task ReadRows_WhenRolesLeaveExtraColumnsAtTheEnd_ThenTheExtraColumnsAreValidatedToo()
    {
        var refused = await Assert.ThrowsAsync<SourceReadException>(
            () => DrainAsync(new TripleCsvSource(Opener("s,p,v\ns,p,v,,,,\"open\n"), TripleBinding()).ReadRowsAsync()));

        Assert.Contains("data record 1, column 6", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith("a quoted field is not closed before the end of the input.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_WhenAWideColumnIsMalformed_ThenEveryRouteRefusesItWhetherOrNotASpecBindsIt()
    {
        const string Text = "a,b,c\n1,2,x\"y\n";

        var unbound = await Assert.ThrowsAsync<SourceReadException>(() => DrainAsync(WideSession(Opener(Text), hasHeader: true).ReadAsync()));
        var bound = await Assert.ThrowsAsync<SourceReadException>(() => DrainAsync(new WideCsvSource(Opener(Text), WideBinding(hasHeader: true)).ReadAsync()));

        Assert.Contains("data record 0, column 2", unbound.Message, StringComparison.Ordinal);
        Assert.Equal(unbound.Message, bound.Message);
    }
}
