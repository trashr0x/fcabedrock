using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// How calibration closes its upstream (D-041). A result the pass has selected (a spill storage
/// failure, a structural halt, or an exception already in flight) is never replaced by a failure
/// to close the upstream; after a complete pass a sole close failure fails the pass. The upstream is
/// closed exactly once.
/// </summary>
public sealed class CalibratorCleanupTests
{
    private static readonly string Numbers = string.Concat(Enumerable.Range(0, 5000).Select(i => i.ToString(CultureInfo.InvariantCulture) + "\n"));

    private static AttributeSpec EqualFrequency() =>
        new("n", new ColumnSource(0, SourceValueType.Number), Include: true,
            new CalibrationPending(new PendingEqualFrequency(2, TiePolicy.Left, CutPlacement.RightValue), CultureInfo.InvariantCulture),
            new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static GroupingOptions FailingSpill() =>
        new(maxBufferedBytes: 1, fileSystem: new FakeSpoolFileSystem { OnCreateRun = _ => new IOException("injected: the spool device is full") });

    private static async Task<(Diagnosed<CalibratedSpec>? Result, Exception? Error)> CalibrateWideAsync(
        UpstreamFault fault, GroupingOptions options, IReadOnlyList<ObjectRecord>? listed = null)
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [EqualFrequency()]);
        var source = ConversionFixtures.SourceOver(Numbers, spec.Binding);
        var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());
        try
        {
            return (await Calibrator.CalibrateAsync(resolved, new FaultyRecordSource(source, fault, listed), options, observer: null, CancellationToken.None), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static async Task<(Diagnosed<CalibratedSpec>? Result, Exception? Error)> CalibrateTripleAsync(
        UpstreamFault fault, IReadOnlyList<TripleRow>? listed = null)
    {
        // An omitted declared domain is calibrated from the data, so the raw triple pass runs.
        var spec = new BedrockSpec(
            ConversionFixtures.Triple(TripleOrdering.SubjectGrouped),
            [ConversionFixtures.PredicateNominal("a", "p", domain: null)]);
        var source = ConversionFixtures.TripleSourceOver(UnorderedTripleRowSourceTests.TripleBadSubject, spec.Binding);
        var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());
        try
        {
            return (await Calibrator.CalibrateTripleAsync(resolved, new FaultyTripleSource(source, fault, listed)), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalibrateWide_WhenASpillFailsWhileTheRecordsAreOpen_ThenTheStorageFailureIsKept(bool closeFails)
    {
        var fault = new UpstreamFault { DisposeFailure = closeFails ? UpstreamFault.CloseFailure() : null };

        var (result, error) = await CalibrateWideAsync(fault, FailingSpill());

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.False(result.Value.IsOk);
        Assert.Contains(result.Value.Diagnostics, d => d.Code == DiagnosticCode.GroupingStorageFailed);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task CalibrateWide_WhenAReadFailsAndTheCloseFails_ThenTheReadFailureIsKept()
    {
        var read = UpstreamFault.ReadFailure();
        var fault = new UpstreamFault { ThrowOnMove = 1, MoveFailure = read, DisposeFailure = UpstreamFault.CloseFailure() };

        var (_, error) = await CalibrateWideAsync(fault, GroupingOptions.Default, [new ObjectRecord("0", ["1"]), new ObjectRecord("1", ["2"])]);

        Assert.Same(read, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task CalibrateWide_WhenAGenericUpstreamEndsAndOnlyItsCloseFails_ThenTheCloseFailureFailsThePass()
    {
        var close = UpstreamFault.CloseFailure();
        var fault = new UpstreamFault { DisposeFailure = close };

        var (_, error) = await CalibrateWideAsync(fault, GroupingOptions.Default, [new ObjectRecord("0", ["1"]), new ObjectRecord("1", ["2"])]);

        Assert.Same(close, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalibrateTriple_WhenAnInvalidSubjectHaltsTheRawPass_ThenTheHaltIsKept(bool closeFails)
    {
        var fault = new UpstreamFault { DisposeFailure = closeFails ? UpstreamFault.CloseFailure() : null };

        var (result, error) = await CalibrateTripleAsync(fault);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.False(result.Value.IsOk);
        Assert.Contains(result.Value.Diagnostics, d => d.Code == DiagnosticCode.ObjectKeyValueInvalid);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task CalibrateTriple_WhenCancellationIsInFlightAndTheCloseFails_ThenTheSameCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = new OperationCanceledException(cts.Token);
        var fault = new UpstreamFault { ThrowOnMove = 0, MoveFailure = canceled, DisposeFailure = UpstreamFault.CloseFailure() };

        var (_, error) = await CalibrateTripleAsync(fault, [new TripleRow(0, "s1", "p", "v")]);

        Assert.Same(canceled, error);
        Assert.Equal(1, fault.DisposeCalls);
    }
}
