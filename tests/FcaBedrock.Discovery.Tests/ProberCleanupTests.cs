using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// How a probe closes the session's records (D-041). A result the pass has selected (a read
/// failure, a breached guard, a structural halt, or an exception already in flight, cancellation
/// included) is never replaced by a failure to close the records. After the pass reads every
/// record, a close failure is a read failure of that pass in the existing narrow set: it reports
/// <c>ProbeSourceReadFailed</c> and no draft. The records are closed exactly once.
/// </summary>
public sealed class ProberCleanupTests
{
    private static readonly ObjectRecord[] Records = [new("0", ["x", "y"]), new("1", ["y", "x"])];

    private static Func<Stream> Open(string text) => () => new MemoryStream(new UTF8Encoding(false).GetBytes(text));

    private static async Task<(Diagnosed<SpecDocument>? Result, Exception? Error)> ProbeWideAsync(
        CloseFault fault, IReadOnlyList<ObjectRecord>? listed, ProbeOptions? options = null)
    {
        var settings = SourceReadSettings.CreateWide(hasHeader: false);
        var session = new FaultyWideSession(new WideCsvSession(Open("k1,x\nk2,y\nk3,x\n"), settings), fault, listed);
        try
        {
            return (await Prober.ProbeAsync(session, settings, options), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static async Task<(Diagnosed<SpecDocument>? Result, Exception? Error)> ProbeTripleAsync(
        CloseFault fault, IReadOnlyList<TripleRow>? listed, string data)
    {
        var settings = SourceReadSettings.CreateTriple();
        var session = new FaultyTripleSession(new TripleCsvSession(Open(data), settings), fault, listed);
        try
        {
            return (await Prober.ProbeTripleAsync(session, settings), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static void AssertFailedWith(Diagnosed<SpecDocument>? result, DiagnosticCode code)
    {
        Assert.NotNull(result);
        Assert.False(result.Value.TryGetValue(out _));
        Assert.Contains(result.Value.Diagnostics, d => d.Code == code);
    }

    [Fact]
    public async Task ProbeWide_WhenAGenericSessionReadsEveryRecordThenFailsOnlyWhenClosed_ThenProbeSourceReadFailedAndNoDraft()
    {
        var fault = new CloseFault { DisposeFailure = new IOException("injected: close failed") };

        var (result, error) = await ProbeWideAsync(fault, Records);

        Assert.Null(error);
        AssertFailedWith(result, DiagnosticCode.ProbeSourceReadFailed);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task ProbeWide_WhenAGuardIsBreachedAndTheCloseFails_ThenProbeLimitExceededIsKept()
    {
        var fault = new CloseFault { DisposeFailure = new IOException("injected: close failed") };

        var (result, error) = await ProbeWideAsync(fault, listed: null, ProbeOptions.Create(maxTotalRetainedValues: 2));

        Assert.Null(error);
        AssertFailedWith(result, DiagnosticCode.ProbeLimitExceeded);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task ProbeWide_WhenAReadFailsAndTheCloseFails_ThenTheReadFailureIsKept()
    {
        // Different exception types, so the diagnostic shows which failure was kept.
        var fault = new CloseFault { ThrowOnMove = 1, MoveFailure = new InvalidDataException("injected: read failed"), DisposeFailure = new IOException("injected: close failed") };

        var (result, error) = await ProbeWideAsync(fault, Records);

        Assert.Null(error);
        AssertFailedWith(result, DiagnosticCode.ProbeSourceReadFailed);
        Assert.Contains("(InvalidDataException)", Assert.Single(result!.Value.Diagnostics).Message, StringComparison.Ordinal);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task ProbeWide_WhenCancellationIsInFlightAndTheCloseFails_ThenTheSameCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = new OperationCanceledException(cts.Token);
        var fault = new CloseFault { ThrowOnMove = 1, MoveFailure = canceled, DisposeFailure = new IOException("injected: close failed") };

        var (_, error) = await ProbeWideAsync(fault, Records);

        Assert.Same(canceled, error);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task ProbeWide_WhenAPassCompletesWithACleanClose_ThenADraftIsReturned()
    {
        var fault = new CloseFault();

        var (result, error) = await ProbeWideAsync(fault, Records);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result.Value.TryGetValue(out _));
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProbeTriple_WhenAnInvalidSubjectHaltsThePass_ThenObjectKeyValueInvalidIsKept(bool closeFails)
    {
        var fault = new CloseFault { DisposeFailure = closeFails ? new IOException("injected: close failed") : null };

        var (result, error) = await ProbeTripleAsync(fault, listed: null, "s1,p,v\n,p,w\ns3,p,v\n");

        Assert.Null(error);
        AssertFailedWith(result, DiagnosticCode.ObjectKeyValueInvalid);
        Assert.Equal(1, fault.DisposeCalls);
    }

    [Fact]
    public async Task ProbeTriple_WhenAGenericSessionCompletesThenFailsOnlyWhenClosed_ThenProbeSourceReadFailedAndNoDraft()
    {
        var fault = new CloseFault { DisposeFailure = new IOException("injected: close failed") };

        var (result, error) = await ProbeTripleAsync(fault, [new TripleRow(0, "s1", "p", "v")], "s1,p,v\n");

        Assert.Null(error);
        AssertFailedWith(result, DiagnosticCode.ProbeSourceReadFailed);
        Assert.Equal(1, fault.DisposeCalls);
    }

    /// <summary>What one injected session enumeration does, and how often it was closed.</summary>
    private sealed class CloseFault
    {
        public int ThrowOnMove { get; init; } = -1;

        public Exception? MoveFailure { get; init; }

        public Exception? DisposeFailure { get; init; }

        public int DisposeCalls { get; set; }
    }

    /// <summary>
    /// Wraps a real enumeration, or replaces it with a listed one that returns false at its end and
    /// fails only when closed (a generic, non-CSV session).
    /// </summary>
    private sealed class FaultyEnumerable<T>(Func<CancellationToken, IAsyncEnumerable<T>>? inner, IReadOnlyList<T>? listed, CloseFault fault)
        : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(inner?.Invoke(cancellationToken).GetAsyncEnumerator(cancellationToken), listed, fault);

        private sealed class Enumerator(IAsyncEnumerator<T>? source, IReadOnlyList<T>? listed, CloseFault fault) : IAsyncEnumerator<T>
        {
            private int _index;
            private T? _current;

            public T Current => source is not null ? source.Current : _current!;

            public async ValueTask<bool> MoveNextAsync()
            {
                if (_index == fault.ThrowOnMove)
                {
                    throw fault.MoveFailure!;
                }

                if (source is not null)
                {
                    _index++;
                    return await source.MoveNextAsync();
                }

                if (_index >= listed!.Count)
                {
                    return false;
                }

                _current = listed[_index++];
                return true;
            }

            public async ValueTask DisposeAsync()
            {
                fault.DisposeCalls++;
                if (source is not null)
                {
                    await source.DisposeAsync();
                }

                if (fault.DisposeFailure is { } failure)
                {
                    throw failure;
                }
            }
        }
    }

    private sealed class FaultyWideSession(IWideSourceSession inner, CloseFault fault, IReadOnlyList<ObjectRecord>? listed) : IWideSourceSession
    {
        public SourceShape Shape => SourceShape.Wide;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) => inner.GetSchemaAsync(cancellationToken);

        public IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default) =>
            new FaultyEnumerable<ObjectRecord>(listed is null ? inner.ReadAsync : null, listed, fault);
    }

    private sealed class FaultyTripleSession(ITripleSourceSession inner, CloseFault fault, IReadOnlyList<TripleRow>? listed) : ITripleSourceSession
    {
        public SourceShape Shape => SourceShape.Triple;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) => inner.GetSchemaAsync(cancellationToken);

        public IAsyncEnumerable<TripleRow> ReadRowsAsync(TripleColumns columns, CancellationToken cancellationToken = default) =>
            new FaultyEnumerable<TripleRow>(listed is null ? ct => inner.ReadRowsAsync(columns, ct) : null, listed, fault);
    }
}
