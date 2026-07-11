using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion;

/// <summary>
/// The per-enumeration mutable storage-failure state for one grouping run (D-082). Created fresh by the
/// emitter at each enumeration start (never on <see cref="GroupingOptions"/>, which is immutable
/// configuration), so concurrent conversions cannot cross-contaminate. It is a <b>single
/// insertion-ordered ledger</b> holding both channels' events at their first-logical-occurrence position:
/// <b>cleanup-class</b> failures (consumed-run deletes, workspace teardown, reader close / open-cleanup)
/// as <b>Warning</b>, recorded as they occur while the enumeration continues; and <b>in-path</b> failures
/// as <b>Error</b>, recorded by the detecting site before the cleanup its unwinding triggers (the same
/// failure additionally throws <see cref="GroupingStorageException"/> so the emitter halts). The emitter
/// renders <see cref="Aggregates"/> after the stream — one per identity, worst severity, first-occurrence
/// order.
/// </summary>
internal sealed class GroupingReports
{
    private readonly StorageFailureLedger _storage = new();

    /// <summary>
    /// Records a cleanup-class storage failure as a <b>Warning</b> (bounded aggregation by
    /// <c>(operation, kind)</c>). Never throws; the enumeration continues.
    /// </summary>
    public void RecordCleanupFailure(GroupingOperation operation, SpoolFailureKind kind, string? pathSample) =>
        _storage.Record(operation, kind, pathSample, DiagnosticSeverity.Warning);

    /// <summary>
    /// Records an in-path storage failure as an <b>Error</b> at its first-occurrence position — called by
    /// the detecting site before the cleanup its unwinding triggers, so the ledger stays chronologically
    /// ordered (the same failure also throws to halt the stream).
    /// </summary>
    public void RecordInPathFailure(GroupingOperation operation, SpoolFailureKind kind, string? pathSample) =>
        _storage.Record(operation, kind, pathSample, DiagnosticSeverity.Error);

    /// <summary>The aggregated storage failures, one per identity, worst severity, in first-occurrence order.</summary>
    public IReadOnlyList<(GroupingStorageFailure Failure, DiagnosticSeverity Severity)> Aggregates() =>
        _storage.Aggregates();
}

/// <summary>
/// Bounded, insertion-ordered aggregation of storage failures by stable identity
/// <c>(operation, kind)</c> — a count, up to three path samples in first-occurrence order, and the
/// worst severity seen (D-082/D-059). Never a per-failure diagnostic (P-16): a delete storm over
/// 73M-record spool runs yields one aggregate, not millions. Shared by the emitter's per-enumeration
/// flush and the replay session's cross-pass promotion.
/// </summary>
internal sealed class StorageFailureLedger
{
    private const int SampleCap = 3;

    private readonly Dictionary<(GroupingOperation Operation, SpoolFailureKind Kind), Entry> _byIdentity = new();
    private readonly List<(GroupingOperation Operation, SpoolFailureKind Kind)> _order = new();

    public bool IsEmpty => _order.Count == 0;

    /// <summary>Records a single failure event.</summary>
    public void Record(GroupingOperation operation, SpoolFailureKind kind, string? pathSample, DiagnosticSeverity severity)
    {
        var entry = GetOrAdd(operation, kind);
        entry.Count++;
        entry.Severity = Worst(entry.Severity, severity);
        AddSample(entry, pathSample);
    }

    /// <summary>Merges a pre-aggregated failure (from another pass/ledger) into this one.</summary>
    public void Merge(GroupingStorageFailure failure, DiagnosticSeverity severity)
    {
        var entry = GetOrAdd(failure.Operation, failure.Kind);
        entry.Count += failure.Count;
        entry.Severity = Worst(entry.Severity, severity);
        foreach (var sample in failure.PathSamples)
        {
            AddSample(entry, sample);
        }
    }

    /// <summary>The aggregates, one per identity, in first-occurrence order (never hash-map order — D-059).</summary>
    public IReadOnlyList<(GroupingStorageFailure Failure, DiagnosticSeverity Severity)> Aggregates()
    {
        var result = new List<(GroupingStorageFailure, DiagnosticSeverity)>(_order.Count);
        foreach (var key in _order)
        {
            var entry = _byIdentity[key];
            result.Add((new GroupingStorageFailure(key.Operation, key.Kind, entry.Count, entry.Samples), entry.Severity));
        }

        return result;
    }

    private Entry GetOrAdd(GroupingOperation operation, SpoolFailureKind kind)
    {
        var key = (operation, kind);
        if (!_byIdentity.TryGetValue(key, out var entry))
        {
            entry = new Entry();
            _byIdentity[key] = entry;
            _order.Add(key);
        }

        return entry;
    }

    private static void AddSample(Entry entry, string? pathSample)
    {
        if (pathSample is not null && entry.Samples.Count < SampleCap)
        {
            entry.Samples.Add(pathSample);
        }
    }

    private static DiagnosticSeverity Worst(DiagnosticSeverity a, DiagnosticSeverity b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Fatal => 3,
        DiagnosticSeverity.Error => 2,
        DiagnosticSeverity.Warning => 1,
        _ => 0,
    };

    private sealed class Entry
    {
        public long Count { get; set; }

        public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;

        public List<string> Samples { get; } = [];
    }
}
