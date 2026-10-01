namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Cancellation inside each owned grammar routine on a field longer than the quantum. A test
/// either cancels the token first and calls the routine whose work is the target, so the routine's
/// first checkpoint falls inside that work, or wraps a live token so that cancellation is requested
/// only once the earlier stages have been charged, so the checkpoint that throws can only lie in
/// the later stage under test. Each asserts the operation's token and how far the routine had got.
/// No test pre-cancels a routine whose earlier stage would throw first and calls that proof of a
/// later stage.
/// </summary>
public sealed class DelimitedFieldGrammarCancellationTests
{
    private const int Q = CancellationBudget.Quantum;

    private static (CancelAfterUnits Budget, CancellationTokenSource Source) CancelledBudget()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return (new CancelAfterUnits(new CancellationBudget(source.Token), source, long.MaxValue), source);
    }

    [Fact]
    public void IsQuoteFree_WhenALongCandidateHasNoQuote_ThenTheFirstWindowThrows()
    {
        var (budget, source) = CancelledBudget();
        var text = new string('x', 200_000);

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => DelimitedFieldGrammar.IsQuoteFree(text.AsSpan(), ref budget));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(Q, budget.Units); // exactly one window searched
    }

    [Fact]
    public void Check_WhenALongQuotedFieldIsValidated_ThenValidationThrows()
    {
        var (budget, source) = CancelledBudget();
        var raw = "\"" + new string('x', 200_000) + "\"";

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => DelimitedFieldGrammar.Check(raw.AsSpan(), ',', ref budget));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.InRange(budget.Units, Q, 2 * Q); // inside the search for the closing quote, not at its entry
    }

    [Fact]
    public void CountLineBreaks_WhenLongQuotedContentIsCounted_ThenTheCountThrows()
    {
        var (budget, source) = CancelledBudget();
        var content = new string('x', 100_000) + "\r\n" + new string('y', 100_000);

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => DelimitedFieldGrammar.CountLineBreaks(content.AsSpan(), ref budget));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(Q, budget.Units);
    }

    [Fact]
    public void IsBlankCandidate_WhenALongWhitespaceCandidateIsTested_ThenTheTestThrows()
    {
        var (budget, source) = CancelledBudget();
        var candidate = new string(' ', 200_000);

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => DelimitedFieldGrammar.IsBlankCandidate(candidate.AsSpan(), ',', ref budget));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(Q, budget.Units); // the entry unit and 65,535 whitespace units
    }

    [Fact]
    public void MaterializeUnquoted_WhenALongPaddedFieldIsTrimmed_ThenTheTrimThrows()
    {
        var (budget, source) = CancelledBudget();
        var raw = new string(' ', 100_000) + "x" + new string(' ', 100_000);

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => DelimitedFieldGrammar.MaterializeUnquoted(raw.AsSpan(), ',', ref budget));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(Q, budget.Units); // one unit per leading whitespace unit trimmed
    }

    [Fact]
    public void MaterializeUnquoted_WhenALongUnpaddedFieldIsCancelled_ThenTheCopyEntryChargeThrows()
    {
        // Entry proof only. An unpadded field has no trim work, so its first charge is the copy's
        // entry charge, its length plus one, made before the one runtime copy into the new string.
        // The checkpoint that throws is that charge; the copy itself is a primitive operation that
        // no checkpoint interrupts, and this test makes no claim about it.
        var (budget, source) = CancelledBudget();
        var raw = new string('x', 300_000);
        string? materialized = null;

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => materialized = DelimitedFieldGrammar.MaterializeUnquoted(raw.AsSpan(), ',', ref budget));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(300_001, budget.Units); // the copy's entry charge and nothing before it
        Assert.Null(materialized);
    }

    [Fact]
    public void CopyUnescaped_WhenCancelledInTheLongRunBeforeTheFirstEscapedQuote_ThenNothingIsWritten()
    {
        var (budget, source) = CancelledBudget();
        var content = new string('x', 300_000) + "\"\"" + new string('y', 300_000);
        var destination = new char[600_001]; // all zero: no decoded unit is zero

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => DelimitedFieldGrammar.CopyUnescaped(content.AsSpan(), destination.AsSpan(), ref budget));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(Q, budget.Units); // inside the copy's first windowed search
        Assert.True(destination.AsSpan().IndexOfAnyExcept('\0') < 0, "the destination must be untouched");
    }

    [Fact]
    public void CopyUnescaped_WhenCancelledInTheLongRunAfterAnEscapedQuote_ThenTheCopiedPrefixIsKept()
    {
        using var source = new CancellationTokenSource();
        var content = new string('x', 300_000) + "\"\"" + new string('y', 300_000);
        var destination = new char[600_001]; // all zero: no decoded unit is zero

        // The first search passes four full windows and finds the pair's first quote as the
        // 37,857th unit of the fifth: 4 x 65,536 + 37,857 = 300,001 units, after which the run and
        // one quote are copied. Cancellation is requested by the next charge, the first window of
        // the search after the pair, and that charge also spends the units left before the
        // checkpoint.
        const int throughPair = 300_001;
        var budget = new CancelAfterUnits(new CancellationBudget(source.Token), source, throughPair + 1);

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => DelimitedFieldGrammar.CopyUnescaped(content.AsSpan(), destination.AsSpan(), ref budget));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(throughPair + Q, budget.Units); // the first window after the pair
        Assert.True(destination.AsSpan(0, 300_000).IndexOfAnyExcept('x') < 0, "the run before the pair must be copied");
        Assert.Equal('"', destination[300_000]); // one quote for the escaped pair
        Assert.True(destination.AsSpan(300_001).IndexOfAnyExcept('\0') < 0, "the run after the pair must be untouched");
    }

    [Fact]
    public void CopyUnescaped_WhenCancelledAmongManyEscapedQuotes_ThenOnlyACorrectPrefixIsWritten()
    {
        var (budget, source) = CancelledBudget();
        var content = string.Concat(Enumerable.Repeat("\"\"", 200_000));
        var destination = new char[200_000];

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => DelimitedFieldGrammar.CopyUnescaped(content.AsSpan(), destination.AsSpan(), ref budget));

        // Each pair's search charges one unit; the 65,536th search reaches the checkpoint before its
        // pair is copied, so 65,535 quotes are written and the rest is untouched.
        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(Q, budget.Units);
        Assert.True(destination.AsSpan(0, Q - 1).IndexOfAnyExcept('"') < 0, "the written prefix must be one quote per pair");
        Assert.True(destination.AsSpan(Q - 1).IndexOfAnyExcept('\0') < 0, "the rest must be untouched");
    }

    [Fact]
    public void DecodeField_WhenTheEscapedCopyRunsOnAMeteredField_ThenTheDecodedValueIsExactAcrossWindows()
    {
        // The metered paths at window boundaries: in the escaped copy, whose first window starts at the
        // content's start, the pair's first quote is that window's last unit and its twin the next
        // window's first; line breaks sit either side. A budget that never cancels decodes it exactly.
        var budget = new CancellationBudget(CancellationToken.None);
        var before = new string('x', Q - 3) + "\r\n";
        var after = "\r" + new string('y', Q + 7) + "\n";
        var raw = " \"" + before + "\"\"" + after + "\" ";

        var decoded = DelimitedFieldGrammar.DecodeField(raw.AsSpan(), 1, ',', ref budget, out var fault, out var lineBreaks);

        Assert.True(fault.IsValid);
        Assert.Equal(before + "\"" + after, decoded);
        Assert.Equal(3, lineBreaks);
    }

    [Fact]
    public void DecodeField_WhenCancelledInsideTheEscapedCopy_ThenTheCallbackChargesTheCallersBudgetAndNoValueIsReturned()
    {
        // The whole quoted content is 40,000 escaped pairs. Before the copy, DecodeField charges
        // 40,002 units to validate (its entry, one per pair search and one for the closing quote),
        // 80,000 to count line breaks (passing one checkpoint) and 40,001 at the copy's entry:
        // 160,003 in all, leaving 11,071 units before the next checkpoint. Cancellation is requested
        // by the next unit charged, the first pair search of the escaped copy inside
        // string.Create's callback, and is observed when the callback's 11,071st search spends the
        // units left. A callback given a copy of the budget would leave the caller's count at
        // 160,003, and one whose budget started again at the quantum would reach no checkpoint in
        // 40,000 one-unit searches, so the decode would succeed.
        using var source = new CancellationTokenSource();
        var raw = "\"" + string.Concat(Enumerable.Repeat("\"\"", 40_000)) + "\"";
        const int beforeCopy = 40_002 + 80_000 + 40_001;
        var budget = new CancelAfterUnits(new CancellationBudget(source.Token), source, beforeCopy + 1);
        string? decoded = null;

        var canceled = Assert.ThrowsAny<OperationCanceledException>(
            () => decoded = DelimitedFieldGrammar.DecodeField(raw.AsSpan(), 0, ',', ref budget, out _, out _));

        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(beforeCopy + 11_071, budget.Units); // the caller's own budget, charged inside the callback
        Assert.Null(decoded);
    }
}
