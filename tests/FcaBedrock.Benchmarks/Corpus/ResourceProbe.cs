using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>What a calibration pass reported about its own resource use.</summary>
internal sealed record ResourceTrace(
    IReadOnlyList<long> ModelledResident,
    IReadOnlyList<(string Attribute, int LiveRuns)> RunCatalog,
    IReadOnlyList<(string Attribute, int Capacity, long ModelledBytes)> AccumulatorSizes);

/// <summary>
/// Runs a calibration with the internal resource observers attached, as a <b>separate, untimed
/// validation</b>.
/// <para>
/// The observers are deliberately never installed in a timing configuration: an observer that
/// recorded every spill and catalog change while the clock ran would be measuring itself as much as
/// the calibration. So the same corpus is calibrated twice for two different purposes — once
/// unobserved, to be timed, and once observed, to check that the bounds the timed run relies on
/// actually held.
/// </para>
/// <para>
/// It lives in the benchmark assembly rather than the benchmark test assembly because that is where
/// the D-124 friend grant is: the grant stays to exactly the two assemblies that entry names, and
/// the tests reach the observers through this seam instead of acquiring their own access.
/// </para>
/// </summary>
internal static class ResourceProbe
{
    /// <summary>Calibrates <paramref name="dataPath"/> under <paramref name="specText"/>, observing resources.</summary>
    public static async Task<(CalibratedSpec Calibrated, ResourceTrace Trace)> CalibrateAsync(
        string specText,
        string dataPath,
        long budgetBytes,
        int mergeFanIn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specText);
        ArgumentNullException.ThrowIfNull(dataPath);

        var recorder = new Recorder();
        var options = BenchmarkGrouping.Create(budgetBytes, mergeFanIn, recorder);

        var document = ConversionPipeline.RequireDocument(specText);
        var settings = ConversionPipeline.RequireReadSettings(document);
        var session = ConversionPipeline.CreateSession(settings, dataPath);
        var schema = await session.GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        var resolved = ConversionPipeline.RequireResolved(document, schema).Resolved;

        var result = session switch
        {
            TripleCsvSession triple => await Calibrator.CalibrateTripleAsync(
                resolved, triple.Bind(resolved), options, recorder, cancellationToken).ConfigureAwait(false),
            WideCsvSession wide => await Calibrator.CalibrateAsync(
                resolved, wide.Bind(resolved), options, recorder, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("unrecognized session shape."),
        };

        if (!result.TryGetValue(out var calibrated))
        {
            throw new InvalidOperationException(
                $"the observed calibration produced no result: {ConversionPipeline.Describe(result.Diagnostics)}");
        }

        return (calibrated, recorder.Trace());
    }

    /// <summary>The modelled floor a single accumulator entry costs — the honestly stated bound arm.</summary>
    public static long AccumulatorFloorBytes => QuantileAccumulator.FloorBytes;

    // Records the signals and changes nothing: production leaves the observer null (P-6).
    private sealed class Recorder : ICalibrationObserver
    {
        private readonly List<long> _resident = [];
        private readonly List<(string, int)> _catalog = [];
        private readonly List<(string, int, long)> _sizes = [];

        public ResourceTrace Trace() => new([.. _resident], [.. _catalog], [.. _sizes]);

        public void AccumulatorSized(string attribute, int capacity, long modeledBytes) =>
            _sizes.Add((attribute, capacity, modeledBytes));

        public void AggregateResident(long modeledBytes) => _resident.Add(modeledBytes);

        public void RunCatalog(string attribute, int liveRuns) => _catalog.Add((attribute, liveRuns));

        public void RunWritten(string path, long sizeBytes, bool isInitial)
        {
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

        public void PendingDeletions(int count)
        {
        }

        public void BufferSpilled(long residentBytes)
        {
        }
    }
}
