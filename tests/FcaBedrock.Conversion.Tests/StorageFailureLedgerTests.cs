using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

// The bounded aggregation logic behind both the emitter's per-enumeration flush and the session's
// cross-pass promotion (D-082): one aggregate per (operation, kind), worst severity, ≤ 3 samples in
// first-occurrence order.
public sealed class StorageFailureLedgerTests
{
    [Fact]
    public void Record_WhenSameIdentityWarningThenError_ThenOneErrorWithCombinedCountAndSamples()
    {
        var ledger = new StorageFailureLedger();
        ledger.Record(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, "a", DiagnosticSeverity.Warning);
        ledger.Record(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, "b", DiagnosticSeverity.Error);

        var (failure, severity) = Assert.Single(ledger.Aggregates());
        Assert.Equal(DiagnosticSeverity.Error, severity); // worst severity wins
        Assert.Equal(2, failure.Count);
        Assert.Equal(["a", "b"], failure.PathSamples);
    }

    [Fact]
    public void Aggregates_AreInFirstOccurrenceOrder()
    {
        var ledger = new StorageFailureLedger();
        ledger.Record(GroupingOperation.Spill, SpoolFailureKind.StorageExhausted, null, DiagnosticSeverity.Error);
        ledger.Record(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, null, DiagnosticSeverity.Warning);
        ledger.Record(GroupingOperation.Spill, SpoolFailureKind.StorageExhausted, null, DiagnosticSeverity.Error);

        var aggregates = ledger.Aggregates();

        Assert.Equal(2, aggregates.Count);
        Assert.Equal(GroupingOperation.Spill, aggregates[0].Failure.Operation); // first-seen identity leads
        Assert.Equal(2, aggregates[0].Failure.Count);
        Assert.Equal(GroupingOperation.CleanupDelete, aggregates[1].Failure.Operation);
    }

    [Fact]
    public void Record_CapsSamplesAtThreeButCountsAll()
    {
        var ledger = new StorageFailureLedger();
        for (var i = 0; i < 6; i++)
        {
            ledger.Record(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, $"p{i}", DiagnosticSeverity.Warning);
        }

        var (failure, _) = Assert.Single(ledger.Aggregates());
        Assert.Equal(6, failure.Count);
        Assert.Equal(["p0", "p1", "p2"], failure.PathSamples);
    }

    [Fact]
    public void Merge_CombinesPreAggregatedFailuresAcrossPasses()
    {
        var ledger = new StorageFailureLedger();
        ledger.Merge(new GroupingStorageFailure(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, 3, ["a", "b", "c"]), DiagnosticSeverity.Warning);
        ledger.Merge(new GroupingStorageFailure(GroupingOperation.CleanupDelete, SpoolFailureKind.DeleteFailed, 2, ["d"]), DiagnosticSeverity.Error);

        var (failure, severity) = Assert.Single(ledger.Aggregates());
        Assert.Equal(5, failure.Count);
        Assert.Equal(DiagnosticSeverity.Error, severity);
        Assert.Equal(["a", "b", "c"], failure.PathSamples); // cap 3, first-occurrence
    }
}
