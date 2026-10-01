using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

/// <summary>What one injected upstream enumeration does, and how often it was advanced and closed.</summary>
internal sealed class UpstreamFault
{
    /// <summary>The 0-based <c>MoveNextAsync</c> call that throws <see cref="MoveFailure"/>; -1 for none.</summary>
    public int ThrowOnMove { get; init; } = -1;

    public Exception? MoveFailure { get; init; }

    /// <summary>Thrown by <c>DisposeAsync</c> after the real enumerator underneath was disposed.</summary>
    public Exception? DisposeFailure { get; init; }

    public int MoveCalls { get; set; }

    public int DisposeCalls { get; set; }

    public static IOException CloseFailure() => new("injected: close failed");

    public static IOException ReadFailure() => new("injected: read failed");
}

/// <summary>
/// An upstream row stream that either wraps a real enumeration (the CSV pipeline underneath) or is a
/// hand-written list enumeration: a generic, non-CSV upstream that returns false at its end without
/// throwing and fails only in <c>DisposeAsync</c> when told to. Counts every call.
/// </summary>
internal sealed class FaultyEnumerable<T>(Func<CancellationToken, IAsyncEnumerable<T>>? inner, IReadOnlyList<T>? items, UpstreamFault fault)
    : IAsyncEnumerable<T>
{
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        inner is not null
            ? new Wrapping(inner(cancellationToken).GetAsyncEnumerator(cancellationToken), fault)
            : new Listed(items!, fault);

    private sealed class Wrapping(IAsyncEnumerator<T> source, UpstreamFault fault) : IAsyncEnumerator<T>
    {
        private int _index;

        public T Current => source.Current;

        public ValueTask<bool> MoveNextAsync()
        {
            fault.MoveCalls++;
            if (_index++ == fault.ThrowOnMove)
            {
                throw fault.MoveFailure!;
            }

            return source.MoveNextAsync();
        }

        public async ValueTask DisposeAsync()
        {
            fault.DisposeCalls++;
            await source.DisposeAsync().ConfigureAwait(false);
            if (fault.DisposeFailure is { } failure)
            {
                throw failure;
            }
        }
    }

    private sealed class Listed(IReadOnlyList<T> items, UpstreamFault fault) : IAsyncEnumerator<T>
    {
        private int _index;
        private T? _current;

        public T Current => _current!;

        public ValueTask<bool> MoveNextAsync()
        {
            fault.MoveCalls++;
            if (_index == fault.ThrowOnMove)
            {
                throw fault.MoveFailure!;
            }

            if (_index >= items.Count)
            {
                return ValueTask.FromResult(false);
            }

            _current = items[_index++];
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            fault.DisposeCalls++;
            return fault.DisposeFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        }
    }
}

/// <summary>A wide source whose record enumeration carries an <see cref="UpstreamFault"/>; provenance and schema are the inner source's.</summary>
internal sealed class FaultyRecordSource(IRecordSource inner, UpstreamFault fault, IReadOnlyList<ObjectRecord>? listed = null) : IRecordSource
{
    public SourceProvenance Provenance => inner.Provenance;

    public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) => inner.GetSchemaAsync(cancellationToken);

    public IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default) =>
        new FaultyEnumerable<ObjectRecord>(listed is null ? inner.ReadAsync : null, listed, fault);
}

/// <summary>A triple source whose row enumeration carries an <see cref="UpstreamFault"/>; provenance and schema are the inner source's.</summary>
internal sealed class FaultyTripleSource(ITripleRowSource inner, UpstreamFault fault, IReadOnlyList<TripleRow>? listed = null) : ITripleRowSource
{
    public SourceProvenance Provenance => inner.Provenance;

    public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) => inner.GetSchemaAsync(cancellationToken);

    public IAsyncEnumerable<TripleRow> ReadRowsAsync(CancellationToken cancellationToken = default) =>
        new FaultyEnumerable<TripleRow>(listed is null ? inner.ReadRowsAsync : null, listed, fault);
}

/// <summary>Drains an emit stream, optionally abandoning it while the emitter is suspended at a yield.</summary>
internal static class EmitDrain
{
    public static async Task<(int Objects, List<BedrockDiagnostic> Diagnostics, Exception? Error)> RunAsync(
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit, int abandonAfter = -1)
    {
        var diagnostics = new List<BedrockDiagnostic>();
        var objects = 0;
        try
        {
            var enumerator = emit(diagnostics).GetAsyncEnumerator();
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    objects++;
                    if (objects == abandonAfter)
                    {
                        break; // the emitter is suspended at its yield and is disposed there
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            return (objects, diagnostics, null);
        }
        catch (Exception ex)
        {
            return (objects, diagnostics, ex);
        }
    }

    /// <summary>The distinct Error-or-worse codes, in first-occurrence order.</summary>
    public static string[] ErrorCodes(IEnumerable<BedrockDiagnostic> diagnostics) =>
        [.. diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Error).Select(d => d.Code.ToString()).Distinct()];
}
