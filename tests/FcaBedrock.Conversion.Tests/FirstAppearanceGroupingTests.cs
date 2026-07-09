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

    private static async Task<List<Row>> CollectAsync(IEnumerable<Row> input)
    {
        var result = new List<Row>();
        await foreach (var row in FirstAppearanceGrouping.GroupByFirstAppearanceAsync(ToAsync(input), static r => r.Key))
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
}
