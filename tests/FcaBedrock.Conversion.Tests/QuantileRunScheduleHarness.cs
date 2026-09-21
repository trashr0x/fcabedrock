using System.Globalization;

namespace FcaBedrock.Conversion.Tests;

/// <summary>One observed run write, split by the phase the driver knew it was in.</summary>
internal readonly record struct ObservedQuantileRunWrite(QuantileRunWriteKind Kind, long Bytes)
{
    /// <summary>The row count the byte size implies, at the fixed framed record size.</summary>
    public int Rows => checked((int)(Bytes / QuantileRunScheduleModel.RecordBytes));
}

/// <summary>
/// Everything one observed calibration transition sequence yields: the ordered writes with their
/// phase, the catalogue count after every transition, and the tier-2 peaks. Nothing here is derived
/// from the production schedule policy — the phase labels come from the driver, which knows when it
/// called <c>EndIntake</c> and <c>PrepareReplay</c>.
/// </summary>
internal sealed record QuantileRunObservation(
    IReadOnlyList<ObservedQuantileRunWrite> Writes,
    IReadOnlyList<int> CatalogueAfterEachTransition,
    int PeakCatalogue,
    int QuiescentCatalogue,
    int PeakPendingDeletions,
    long FinalRunBytes,
    int LiveRunsAfterPrepare)
{
    /// <summary>How many original spills were observed.</summary>
    public int OriginalWrites => Writes.Count(write => write.Kind == QuantileRunWriteKind.Original);
}

/// <summary>
/// Drives the <b>real</b> <see cref="QuantileAccumulator"/> and <see cref="SpoolWorkspace{TRow}"/>
/// over the counting filesystem with literal leaf populations, and reports what actually happened.
/// It is deliberately not a handle simulator: the transitions it records are the product's own.
/// </summary>
internal sealed class QuantileRunScheduleDriver : IDisposable
{
    private readonly SpoolWorkspace<ValueCount> _workspace;
    private readonly QuantileAccumulator _accumulator;
    private readonly PhaseObserver _observer;
    private readonly CountingSpoolFileSystem _files;

    private QuantileRunScheduleDriver(
        SpoolWorkspace<ValueCount> workspace,
        QuantileAccumulator accumulator,
        PhaseObserver observer,
        CountingSpoolFileSystem files)
    {
        _workspace = workspace;
        _accumulator = accumulator;
        _observer = observer;
        _files = files;
    }

    /// <summary>
    /// The counting filesystem, for per-run byte, reader/writer-peak and read-pass assertions.
    /// </summary>
    public CountingSpoolFileSystem Files => _files;

    /// <summary>The live accumulator, for the extraction and bound assertions.</summary>
    public QuantileAccumulator Accumulator => _accumulator;

    /// <summary>Builds a driver at the requested budget and fan-in over a counting filesystem.</summary>
    public static QuantileRunScheduleDriver Create(
        long budget, int fanIn, ISpoolFileSystem? inner = null, string attribute = "score")
    {
        var files = new CountingSpoolFileSystem(inner);
        var observer = new PhaseObserver();
        var options = new GroupingOptions(budget, fanIn, tempDirectory: null, files, observer);
        var workspace = new SpoolWorkspace<ValueCount>(
            options, ValueCountCodec.Instance, new GroupingReports(),
            QuantileAccumulator.MaxPendingDeletions(fanIn));
        var calibrationBudget = new CalibrationBudget(budget, 1, observer);
        var accumulator = new QuantileAccumulator(
            attribute, CultureInfo.InvariantCulture, calibrationBudget, workspace, options, observer,
            CancellationToken.None);
        return new QuantileRunScheduleDriver(workspace, accumulator, observer, files);
    }

    /// <summary>
    /// Feeds every leaf's values in order, so that with an accepted capacity equal to each leaf's
    /// distinct-value count, original spill <c>k</c> holds exactly leaf <c>k</c>.
    /// </summary>
    public void Feed(IReadOnlyList<QuantileSpillLeaf> leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        var tally = new DiagnosticTally();
        foreach (var leaf in leaves)
        {
            foreach (var row in leaf.Rows)
            {
                for (long i = 0; i < row.Count; i++)
                {
                    _accumulator.Observe(row.Value.ToString("R", CultureInfo.InvariantCulture), tally);
                }
            }
        }
    }

    /// <summary>Ends intake, consolidates, and reports the complete observation.</summary>
    public QuantileRunObservation Complete()
    {
        _observer.Phase = QuantileRunWriteKind.Original;
        _accumulator.EndIntake();
        var quiescent = _observer.LastCatalogue;
        _observer.Phase = QuantileRunWriteKind.Final;
        _accumulator.PrepareReplay();

        // Measured from the workspace rather than from the last write: when the catalogue already
        // holds exactly one run the merger returns it untouched and writes nothing, and the final
        // run is still whatever is live. It is also an independent reading of the same fact.
        var finalBytes = _workspace.LiveBytes;

        return new QuantileRunObservation(
            [.. _observer.Writes],
            [.. _observer.CatalogueCounts],
            _observer.PeakCatalogue,
            quiescent,
            _observer.PeakPendingDeletions,
            finalBytes,
            _workspace.LiveRunCount);
    }

    /// <summary>Releases the accumulator and reports the workspace's live-run count afterwards.</summary>
    public int Release()
    {
        _accumulator.Release();
        return _workspace.LiveRunCount;
    }

    public void Dispose() => _workspace.Cleanup();

    // Labels each observed write with the phase the driver is in. During intake an original spill
    // and a merge are distinguished by the backend's own isInitial flag, which is a property of the
    // write site rather than of any schedule policy.
    private sealed class PhaseObserver : ICalibrationObserver
    {
        public QuantileRunWriteKind Phase { get; set; } = QuantileRunWriteKind.Original;

        public List<ObservedQuantileRunWrite> Writes { get; } = [];

        public List<int> CatalogueCounts { get; } = [];

        public int PeakCatalogue { get; private set; }

        public int LastCatalogue { get; private set; }

        public int PeakPendingDeletions { get; private set; }

        public void AccumulatorSized(string attribute, int capacity, long modeledBytes)
        {
        }

        public void AggregateResident(long modeledBytes)
        {
        }

        public void RunCatalog(string attribute, int liveRuns)
        {
            CatalogueCounts.Add(liveRuns);
            LastCatalogue = liveRuns;
            PeakCatalogue = Math.Max(PeakCatalogue, liveRuns);
        }

        public void RunWritten(string path, long sizeBytes, bool isInitial)
        {
            Writes.Add(new ObservedQuantileRunWrite(
                isInitial ? QuantileRunWriteKind.Original
                    : Phase == QuantileRunWriteKind.Final ? QuantileRunWriteKind.Final : QuantileRunWriteKind.Carry,
                sizeBytes));
        }

        public void RunOpenedForRead(string path)
        {
        }

        public void RunClosed(string path)
        {
        }

        public void RunDeleted(string path, long sizeBytes)
        {
        }

        public void LiveBytes(long liveBytes)
        {
        }

        public void PendingDeletions(int count) => PeakPendingDeletions = Math.Max(PeakPendingDeletions, count);

        public void BufferSpilled(long residentBytes)
        {
        }
    }
}

/// <summary>
/// Accepts or rejects an observed transition sequence against a modelled schedule, and classifies an
/// observation against both models at once. Wherever the two models predict different schedules an
/// observation must match exactly one of them; the permanent tests then require that one to be the
/// generation-tiered schedule, which makes the whole-catalogue model a standing negative control.
/// </summary>
internal static class QuantileRunScheduleOracle
{
    /// <summary>Whether <paramref name="observed"/> conforms to <paramref name="expected"/> in every checked respect.</summary>
    public static bool Accepts(QuantileRunSchedule expected, QuantileRunObservation observed) => Explain(expected, observed) is null;

    /// <summary>The first disagreement, or <see langword="null"/> when the observation conforms.</summary>
    public static string? Explain(QuantileRunSchedule expected, QuantileRunObservation observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);

        if (expected.Writes.Count != observed.Writes.Count)
        {
            return $"expected {expected.Writes.Count} run writes, observed {observed.Writes.Count}";
        }

        for (var i = 0; i < expected.Writes.Count; i++)
        {
            if (expected.Writes[i].Kind != observed.Writes[i].Kind)
            {
                return $"write {i}: expected {expected.Writes[i].Kind}, observed {observed.Writes[i].Kind}";
            }

            if (expected.Writes[i].Rows != observed.Writes[i].Rows)
            {
                return $"write {i} ({expected.Writes[i].Kind}): expected {expected.Writes[i].Rows} rows, observed {observed.Writes[i].Rows}";
            }

            var expectedBytes = QuantileRunScheduleModel.RunBytes(expected.Writes[i].Rows);
            if (expectedBytes != observed.Writes[i].Bytes)
            {
                return $"write {i}: expected {expectedBytes} bytes, observed {observed.Writes[i].Bytes}";
            }
        }

        if (expected.QuiescentCatalogue != observed.QuiescentCatalogue)
        {
            return $"quiescent catalogue: expected {expected.QuiescentCatalogue}, observed {observed.QuiescentCatalogue}";
        }

        if (expected.PeakCatalogue != observed.PeakCatalogue)
        {
            return $"peak catalogue: expected {expected.PeakCatalogue}, observed {observed.PeakCatalogue}";
        }

        if (!expected.CatalogueAfterEachTransition.SequenceEqual(observed.CatalogueAfterEachTransition))
        {
            return "the per-transition catalogue counts differ: expected ["
                + string.Join(',', expected.CatalogueAfterEachTransition) + "], observed ["
                + string.Join(',', observed.CatalogueAfterEachTransition) + "]";
        }

        if (QuantileRunScheduleModel.RunBytes(expected.FinalRows) != observed.FinalRunBytes)
        {
            return $"final run: expected {QuantileRunScheduleModel.RunBytes(expected.FinalRows)} bytes, observed {observed.FinalRunBytes}";
        }

        return null;
    }

    /// <summary>
    /// Classifies an observation against both models. Exactly one must accept — unless the two
    /// models predict the <b>same</b> checked schedule for this population
    /// (<see cref="SameSchedule"/>), which is a real and expected case: below the fan-in neither
    /// schedule merges during intake, and from <c>F+1</c> through <c>2F-1</c> leaves both have
    /// merged exactly leaves <c>1..F</c> once (at <c>F = 16</c>, 17 leaves is indistinguishable). At
    /// exactly <c>F</c> leaves they differ: the generation-tiered schedule carries during intake and
    /// is quiescent at one run, while the whole-catalogue schedule leaves <c>F</c> runs to the final
    /// merge. Both accepting while the two schedules differ, or neither accepting, is a defect and
    /// throws.
    /// </summary>
    public static QuantileRunScheduleKind Discriminate(int fanIn, IQuantileSpillLeaves leaves, QuantileRunObservation observed)
    {
        var wholeCatalogue = QuantileRunScheduleModel.WholeCatalogue(fanIn, leaves);
        var generationTiered = QuantileRunScheduleModel.GenerationTiered(fanIn, leaves);
        var wholeCatalogueWhy = Explain(wholeCatalogue, observed);
        var generationTieredWhy = Explain(generationTiered, observed);

        return (wholeCatalogueWhy, generationTieredWhy) switch
        {
            (null, null) when SameSchedule(wholeCatalogue, generationTiered) => QuantileRunScheduleKind.Indistinguishable,
            (null, null) => throw new InvalidOperationException(
                "both recurrences accepted an observation although they predict different schedules."),
            (null, not null) => QuantileRunScheduleKind.WholeCatalogue,
            (not null, null) => QuantileRunScheduleKind.GenerationTiered,
            _ => throw new InvalidOperationException(
                "neither recurrence accepted the observation. whole-catalogue: " + wholeCatalogueWhy
                + " | generation-tiered: " + generationTieredWhy),
        };
    }

    /// <summary>Whether the two modelled schedules are the same in every checked respect.</summary>
    public static bool SameSchedule(QuantileRunSchedule left, QuantileRunSchedule right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.Kinds.SequenceEqual(right.Kinds)
            && left.Rows.SequenceEqual(right.Rows)
            && left.CatalogueAfterEachTransition.SequenceEqual(right.CatalogueAfterEachTransition)
            && left.QuiescentCatalogue == right.QuiescentCatalogue
            && left.FinalRows == right.FinalRows;
    }
}

/// <summary>Which modelled schedule an observation was found to follow.</summary>
internal enum QuantileRunScheduleKind
{
    /// <summary>The superseded whole-catalogue schedule — the negative control.</summary>
    WholeCatalogue,

    /// <summary>The generation-tiered schedule the product must follow.</summary>
    GenerationTiered,

    /// <summary>
    /// The two models coincide for this population, so neither can be told from the other.
    /// </summary>
    Indistinguishable,
}
