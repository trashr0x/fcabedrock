using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Cancellation inside the per-candidate routines, over a real Sep row of a long candidate. A test
/// budget wraps the real <see cref="CancellationBudget"/> and requests cancellation only once the
/// units of the earlier stages have been charged, so the checkpoint that throws can only lie in the
/// stage under test; the units charged at the throw locate it.
/// </summary>
public sealed class CsvReadPipelineCancellationTests
{
    /// <summary>A raw provider over one candidate, opened as the pipeline opens it.</summary>
    private static SepReader RowOf(string candidate)
    {
        var reader = DelimitedSourceReader.ProviderOptions(',').FromText(candidate + "\n");
        Assert.True(reader.MoveNext());
        return reader;
    }

    [Fact]
    public void TripleCandidate_WhenAQuoteFreeCandidateHasManyUnusedColumnsAfterItsRoles_ThenTheTraversalIsCancellable()
    {
        using var reader = RowOf("s2,p2,v2" + new string(',', 299_997));
        var row = reader.Current;
        using var source = new CancellationTokenSource();
        // The quote scan charges the candidate's length and each two-unit role three; cancellation is
        // requested at the first unit charged after them, which is the traversal's.
        var beforeTraversal = row.Span.Length + (3 * 3);
        var budget = new CancelAfterUnits(new CancellationBudget(source.Token), source, beforeTraversal + 1);
        var lines = default(CsvReadPipeline.LineState);
        lines.StartCandidate();
        var needHeader = false;
        string? subject = null, predicate = null, value = null;
        OperationCanceledException? canceled = null;

        try
        {
            CsvReadPipeline.TripleCandidate(
                row, row.ColCount, row.Span, ',', ref needHeader, "?", new TripleColumns(0, 1, 2), 0, ref lines, ref budget,
                out subject, out predicate, out value);
        }
        catch (OperationCanceledException ex)
        {
            canceled = ex;
        }

        Assert.NotNull(canceled);
        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.InRange(budget.Units - beforeTraversal, 1, 299_996); // one unit per unused column passed over
        Assert.Equal("s2", subject);
        Assert.Equal("p2", predicate);
        Assert.Equal("v2", value);
    }

    [Fact]
    public void TripleCandidate_WhenALateRoleFollowsManyUnusedColumns_ThenTheTraversalIsCancellableBeforeTheRole()
    {
        using var reader = RowOf("s2,p2" + new string(',', 299_998) + "v2");
        var row = reader.Current;
        using var source = new CancellationTokenSource();
        var beforeTraversal = row.Span.Length + (2 * 3);
        var budget = new CancelAfterUnits(new CancellationBudget(source.Token), source, beforeTraversal + 1);
        var lines = default(CsvReadPipeline.LineState);
        lines.StartCandidate();
        var needHeader = false;
        string? subject = null, predicate = null, value = null;
        OperationCanceledException? canceled = null;

        try
        {
            CsvReadPipeline.TripleCandidate(
                row, row.ColCount, row.Span, ',', ref needHeader, "?", new TripleColumns(0, 1, 299_999), 0, ref lines, ref budget,
                out subject, out predicate, out value);
        }
        catch (OperationCanceledException ex)
        {
            canceled = ex;
        }

        Assert.NotNull(canceled);
        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.InRange(budget.Units - beforeTraversal, 1, 299_997);
        Assert.Equal("s2", subject);
        Assert.Equal("p2", predicate);
        Assert.Null(value); // the late role was never reached
    }

    [Fact]
    public void TripleCandidate_WhenTheBudgetNeverCancels_ThenTheLateRoleIsDecodedExactly()
    {
        using var reader = RowOf("s2,p2" + new string(',', 299_998) + "v2");
        var row = reader.Current;
        var budget = new CancellationBudget(CancellationToken.None);
        var lines = default(CsvReadPipeline.LineState);
        lines.StartCandidate();
        var needHeader = false;

        var retained = CsvReadPipeline.TripleCandidate(
            row, row.ColCount, row.Span, ',', ref needHeader, "?", new TripleColumns(0, 1, 299_999), 0, ref lines, ref budget,
            out var subject, out var predicate, out var value);

        Assert.True(retained);
        Assert.Equal("s2", subject);
        Assert.Equal("p2", predicate);
        Assert.Equal("v2", value);
    }

    [Fact]
    public void RecordCandidate_WhenAMalformedFieldFollowsLongQuotedFields_ThenTheDiagnosticRescanIsCancellable()
    {
        // Five valid quoted fields of 100,000 units, then a malformed field. Without cancellation the
        // routine charges T units and throws the refusal; the rescan that locates the defect's line
        // re-examines the five fields, so at least their 500,000 units are charged after the defect
        // is found. Requesting cancellation 300,000 units before T therefore falls inside the rescan,
        // and at least one checkpoint (they are at most two quanta apart) follows it.
        using var reader = RowOf(string.Join(",", Enumerable.Repeat("\"" + new string('y', 100_000) + "\"", 5)) + ",x\"z");
        var row = reader.Current;

        using var counting = new CancellationTokenSource();
        var baseline = new CancelAfterUnits(new CancellationBudget(counting.Token), counting, long.MaxValue);
        var baselineLines = default(CsvReadPipeline.LineState);
        baselineLines.StartCandidate();
        var baselineHeader = false;
        SourceReadException? refused = null;
        try
        {
            _ = CsvReadPipeline.RecordCandidate(row, row.ColCount, row.Span, ',', ref baselineHeader, "?", 0, ref baselineLines, ref baseline);
        }
        catch (SourceReadException ex)
        {
            refused = ex;
        }

        Assert.NotNull(refused);
        Assert.Contains("data record 0, column 5", refused.Message, StringComparison.Ordinal);
        var total = baseline.Units;

        using var source = new CancellationTokenSource();
        var cancelAt = total - 300_000;
        var budget = new CancelAfterUnits(new CancellationBudget(source.Token), source, cancelAt);
        var lines = default(CsvReadPipeline.LineState);
        lines.StartCandidate();
        var needHeader = false;
        OperationCanceledException? canceled = null;
        try
        {
            _ = CsvReadPipeline.RecordCandidate(row, row.ColCount, row.Span, ',', ref needHeader, "?", 0, ref lines, ref budget);
        }
        catch (OperationCanceledException ex)
        {
            canceled = ex;
        }

        Assert.NotNull(canceled);
        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.InRange(budget.Units, cancelAt, total - 1);
    }
}
