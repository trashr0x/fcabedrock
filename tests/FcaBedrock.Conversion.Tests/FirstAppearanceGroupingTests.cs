using System.Globalization;

namespace FcaBedrock.Conversion.Tests;

public sealed class FirstAppearanceGroupingTests
{
    // Tag uniquely identifies each row, so asserting the Tag sequence fully pins the output order.
    private readonly record struct Row(string? Key, int Tag);

    [Fact]
    public async Task GroupByFirstAppearanceAsync_WhenInterleaved_ThenContiguousInFirstAppearanceOrder()
    {
        // Keys first appear a, b, c; rows are interleaved. Output groups them contiguously in
        // first-appearance order, preserving input (Tag) order within each group.
        Row[] input = [new("a", 0), new("b", 1), new("a", 2), new("c", 3), new("b", 4), new("a", 5)];

        var result = await CollectAsync(input);

        Assert.Equal([0, 2, 5, 1, 4, 3], result.Select(r => r.Tag)); // a-group, b-group, c-group
    }

    [Fact]
    public async Task GroupByFirstAppearanceAsync_WhenAlreadyGrouped_ThenOrderUnchanged()
    {
        Row[] input = [new("a", 0), new("a", 1), new("b", 2), new("c", 3), new("c", 4)];

        var result = await CollectAsync(input);

        Assert.Equal([0, 1, 2, 3, 4], result.Select(r => r.Tag));
    }

    [Fact]
    public async Task GroupByFirstAppearanceAsync_WhenNullKeys_ThenPassThroughRankedByFirstAppearance()
    {
        // A null key is ranked like any other by its first appearance (index 1 here) and never
        // throws — the caller, not the grouper, decides whether a null key is a structural error.
        Row[] input = [new("a", 0), new(null, 1), new("b", 2), new("a", 3), new(null, 4)];

        var result = await CollectAsync(input);

        Assert.Equal([0, 3, 1, 4, 2], result.Select(r => r.Tag)); // a-group, null-group, b-group
    }

    [Fact]
    public async Task GroupByFirstAppearanceAsync_WhenOrdinalKeys_ThenNoCultureFolding()
    {
        // P-12: keys compare ordinally, never culture-folded. Distinct code-unit keys stay distinct
        // groups even when a culture-aware compare might treat them as equal.
        Row[] input = [new("SS", 0), new("ß", 1), new("SS", 2)];

        var result = await CollectAsync(input);

        Assert.Equal([0, 2, 1], result.Select(r => r.Tag)); // "SS" group, then the distinct "ß" group
    }

    [Fact]
    public async Task GroupByFirstAppearanceAsync_WhenSpillForced_ThenMatchesInMemory()
    {
        // A one-byte budget spills every row (each over budget → its own run), forcing the full
        // spill/merge path. The codec round-trips keys (incl. null) exactly, so the order is identical
        // to the in-memory path — spilling never changes results (P-7).
        Row[] input =
        [
            new("a", 0), new("b", 1), new("a", 2), new("c", 3), new("b", 4),
            new("a", 5), new(null, 6), new("b", 7), new(null, 8),
        ];

        var inMemory = await CollectAsync(input);
        var spilled = await CollectAsync(input, SpillEveryRow());

        Assert.Equal(inMemory.Select(r => r.Tag), spilled.Select(r => r.Tag));
    }

    [Fact]
    public async Task GroupByFirstAppearanceAsync_WhenSpillForcedMultiStage_ThenFirstAppearanceOrder()
    {
        // Fan-in 2 with a per-row spill forces multi-stage merges; keys still group in first-appearance
        // order (0, 1, 2 first appear at rows 0, 1, 2), source order preserved within each group.
        Row[] input = [.. Enumerable.Range(0, 12).Select(i => new Row((i % 3).ToString(CultureInfo.InvariantCulture), i))];

        var spilled = await CollectAsync(input, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2));

        Assert.Equal([0, 3, 6, 9, 1, 4, 7, 10, 2, 5, 8, 11], spilled.Select(r => r.Tag));
    }

    private static GroupingOptions SpillEveryRow() => new(maxBufferedBytes: 1);

    private static async Task<List<Row>> CollectAsync(IEnumerable<Row> input, GroupingOptions? options = null)
    {
        var result = new List<Row>();
        var reports = new GroupingReports();
        await foreach (var row in FirstAppearanceGrouping.GroupByFirstAppearanceAsync(
            ToAsync(input), static r => r.Key, RowCodec.Instance, options ?? GroupingOptions.Default, reports))
        {
            result.Add(row);
        }

        return result;
    }

    private static async IAsyncEnumerable<Row> ToAsync(IEnumerable<Row> rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    private sealed class RowCodec : IRowCodec<Row>
    {
        public static readonly RowCodec Instance = new();

        public long Measure(Row row) => RowFraming.MeasureString(row.Key) + sizeof(int);

        // Retained referenced objects only (the RankedRow slot + backing array are counted by the loop).
        public long MeasureResident(Row row) =>
            row.Key is null ? 0 : ResidentModel.StringCost(row.Key.Length);

        public void Write(Row row, Span<byte> destination)
        {
            var offset = 0;
            RowFraming.WriteString(destination, ref offset, row.Key);
            RowFraming.WriteInt32(destination, ref offset, row.Tag);
        }

        public Row Read(ReadOnlySpan<byte> source)
        {
            var offset = 0;
            var key = RowFraming.ReadString(source, ref offset);
            var tag = RowFraming.ReadInt32(source, ref offset);
            return new Row(key, tag);
        }
    }
}
