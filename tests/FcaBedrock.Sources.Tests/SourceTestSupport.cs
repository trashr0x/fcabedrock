using System.Text;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// A non-seekable in-memory byte stream with a controllable read schedule, for the reading-rule
/// tests: short reads, a read failure from a byte offset on, a failing close, cancellation once
/// the final byte has been served, and one read that blocks until a test releases it. It counts
/// its reads and disposals, so a test can assert that a resource was closed exactly once.
/// </summary>
internal sealed class ScriptedStream(byte[] data) : Stream
{
    private int _position;

    /// <summary>The most bytes one read returns; smaller values force short reads.</summary>
    public int MaxChunk { get; init; } = int.MaxValue;

    /// <summary>A per-read cap, by read number (0-based); overrides <see cref="MaxChunk"/> when set.</summary>
    public Func<int, int>? ChunkAt { get; init; }

    /// <summary>Thrown by the first read that starts at or after <see cref="ReadFailureOffset"/>.</summary>
    public Exception? ReadFailure { get; init; }

    public int ReadFailureOffset { get; init; }

    /// <summary>Thrown by <see cref="Stream.Dispose()"/> (after it is counted).</summary>
    public Exception? DisposeFailure { get; init; }

    /// <summary>Cancelled by the read that serves the final byte.</summary>
    public CancellationTokenSource? CancelAtEnd { get; init; }

    /// <summary>The read number (0-based) that blocks until <see cref="Release"/> is set; -1 for none.</summary>
    public int BlockOnRead { get; init; } = -1;

    public ManualResetEventSlim Entered { get; } = new(false);

    public ManualResetEventSlim Release { get; } = new(false);

    public int DisposeCount { get; private set; }

    public int ReadCalls { get; private set; }

    public long BytesRead => _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public static ScriptedStream Utf8(string text, int maxChunk = int.MaxValue) =>
        new(new UTF8Encoding(false).GetBytes(text)) { MaxChunk = maxChunk };

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var call = ReadCalls++;
        if (call == BlockOnRead)
        {
            Entered.Set();
            Release.Wait();
        }

        if (ReadFailure is { } failure && _position >= ReadFailureOffset)
        {
            throw failure;
        }

        var cap = ChunkAt is { } chunkAt ? Math.Max(1, chunkAt(call)) : MaxChunk;
        var n = Math.Min(Math.Min(buffer.Length, cap), data.Length - _position);
        data.AsSpan(_position, n).CopyTo(buffer);
        _position += n;
        if (_position == data.Length && n > 0)
        {
            CancelAtEnd?.Cancel();
        }

        return n;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        DisposeCount++;
        if (DisposeFailure is { } failure)
        {
            throw failure;
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A test work budget around the real <see cref="CancellationBudget"/>: it counts every unit
/// charged and requests cancellation once the count reaches <c>cancelAt</c>, while the wrapped
/// budget does the checking. The units charged at the throw locate the checkpoint in the work.
/// </summary>
internal struct CancelAfterUnits(CancellationBudget inner, CancellationTokenSource source, long cancelAt) : IWorkBudget
{
    private CancellationBudget _inner = inner;

    public long Units { get; private set; }

    public void Charge(int units)
    {
        Units += units;
        if (Units >= cancelAt && !source.IsCancellationRequested)
        {
            source.Cancel();
        }

        _inner.Charge(units);
    }
}

/// <summary>Shared reading and rendering helpers for the reading-rule tests.</summary>
internal static class SourceTestSupport
{
    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    public static Func<Stream> Opener(string text, int maxChunk = int.MaxValue) =>
        () => ScriptedStream.Utf8(text, maxChunk);

    public static Binding WideBinding(char delimiter = ',', bool hasHeader = false, string missingToken = "?") =>
        new(SourceShape.Wide, "utf-8", delimiter, '"', hasHeader, "invariant", missingToken, new RowIndexObjectKey());

    public static Binding TripleBinding(
        char delimiter = ',', bool hasHeader = false, string missingToken = "?", TripleColumns? columns = null) =>
        new(SourceShape.Triple, "utf-8", delimiter, '"', hasHeader, "invariant", missingToken,
            new ColumnObjectKey(0, DuplicateObjectPolicy.Fail), columns ?? new TripleColumns(0, 1, 2), TripleOrdering.Unordered);

    public static WideCsvSession WideSession(Func<Stream> open, char delimiter = ',', bool hasHeader = false, string missingToken = "?") =>
        new(open, SourceReadSettings.CreateWide(delimiter: delimiter, hasHeader: hasHeader, missingToken: missingToken));

    public static TripleCsvSession TripleSession(Func<Stream> open, char delimiter = ',', bool hasHeader = false, string missingToken = "?") =>
        new(open, SourceReadSettings.CreateTriple(delimiter: delimiter, hasHeader: hasHeader, missingToken: missingToken));

    public static async Task<List<ObjectRecord>> DrainAsync(IAsyncEnumerable<ObjectRecord> records)
    {
        var drained = new List<ObjectRecord>();
        await foreach (var record in records)
        {
            drained.Add(record);
        }

        return drained;
    }

    public static async Task<List<TripleRow>> DrainAsync(IAsyncEnumerable<TripleRow> rows)
    {
        var drained = new List<TripleRow>();
        await foreach (var row in rows)
        {
            drained.Add(row);
        }

        return drained;
    }

    /// <summary>Drains <paramref name="source"/>, keeping what was yielded before a failure.</summary>
    public static async Task<(List<T> Items, Exception? Error)> DrainCapturingAsync<T>(IAsyncEnumerable<T> source, Action<int>? afterItem = null)
    {
        var items = new List<T>();
        try
        {
            await foreach (var item in source)
            {
                items.Add(item);
                afterItem?.Invoke(items.Count);
            }

            return (items, null);
        }
        catch (Exception ex)
        {
            return (items, ex);
        }
    }

    public static string?[] Fields(ObjectRecord record) =>
        [.. Enumerable.Range(0, record.FieldCount).Select(record.Field)];

    /// <summary>A record's fields with every character visible: CR, LF and TAB escaped, missing as null.</summary>
    public static string Render(IEnumerable<string?> fields) => "[" + string.Join(",", fields.Select(Show)) + "]";

    public static string Render(ObjectRecord record) => record.Name + ":" + Render(Fields(record));

    public static string Render(TripleRow row) => $"{row.RecordIndex}:{Render([row.Subject, row.Predicate, row.Value])}";

    public static string Show(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder("<");
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '\\' => "\\\\",
                _ when c < 0x20 || c > 0x7E => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }

        return builder.Append('>').ToString();
    }

    /// <summary>One UTF-16 unit as a string, so tests never carry raw control or non-ASCII characters.</summary>
    public static string U(int code) => ((char)code).ToString();
}
