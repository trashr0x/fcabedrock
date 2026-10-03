namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// An <b>independent</b> model of the run-write schedule a spilling <c>QuantileAccumulator</c>
/// follows. It exists to falsify the product, so it shares nothing with it: expected values come
/// from literal leaf maps, a sorted-list/RLE union and base-F digit arithmetic.
/// <para>
/// It never calls <c>QuantileRunCatalog</c>, <c>QuantileAccumulator</c>, <c>ValueCountMerger</c>,
/// <c>QuantileSelection</c>, <c>ValueCountCodec</c> or any production selection or merge policy to
/// produce an expected value. Row counts, per-write bytes, catalogue counts and the final run's
/// size are compared separately rather than folded into one number, so a defect in one cannot hide
/// behind another.
/// </para>
/// <para>
/// It models <b>two</b> schedules: the generation-tiered schedule the product must follow (D-128),
/// and the whole-catalogue schedule it superseded, kept as a negative control: wherever the two
/// differ, an observation must match the first and be rejected by the second.
/// </para>
/// </summary>
internal static class QuantileRunScheduleModel
{
    /// <summary>
    /// The framed on-disk record size for one <c>(value, count)</c> row, derived here rather than
    /// read from production: a 4-byte record-length prefix, a 4-byte rank, an 8-byte seq, and a
    /// 16-byte payload (the double's bits then the count).
    /// </summary>
    public const int RecordBytes = 4 + 4 + 8 + 16;

    /// <summary>The exact byte size of a run holding <paramref name="rows"/> rows.</summary>
    public static long RunBytes(long rows) => checked(rows * RecordBytes);

    /// <summary>
    /// The level ceiling <c>L = 1 + floor(log_F(long.MaxValue))</c>, by repeated integer division.
    /// No floating logarithm (which rounds) and no <c>F^L</c> power (which overflows).
    /// </summary>
    public static int Levels(int fanIn)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fanIn, 2);

        var levels = 1;
        var remaining = long.MaxValue;
        while (remaining >= fanIn)
        {
            remaining /= fanIn;
            levels++;
        }

        return levels;
    }

    /// <summary>The quiescent catalogue ceiling <c>K = (F - 1) * L</c>, in widened checked arithmetic.</summary>
    public static long Ceiling(int fanIn) => checked((long)(fanIn - 1) * Levels(fanIn));

    /// <summary>
    /// The sum of the base-<paramref name="radix"/> digits of <paramref name="value"/>: the exact
    /// quiescent catalogue count after that many successful original spills, not merely a bound.
    /// </summary>
    public static long DigitSum(long value, int radix)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        long sum = 0;
        while (value > 0)
        {
            sum += value % radix;
            value /= radix;
        }

        return sum;
    }

    /// <summary>
    /// The exact sorted union of the named leaves: ascending distinct values with checked count
    /// addition. Implemented as an ordered insert over a list (an RLE fold), never a dictionary and
    /// never the production merger.
    /// </summary>
    public static List<QuantileModelRow> Union(IReadOnlyList<QuantileSpillLeaf> leaves, IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(indices);

        var folded = new List<QuantileModelRow>();
        foreach (var index in indices)
        {
            foreach (var row in leaves[index].Rows)
            {
                var at = 0;
                while (at < folded.Count && folded[at].Value < row.Value)
                {
                    at++;
                }

                if (at < folded.Count && folded[at].Value.Equals(row.Value))
                {
                    folded[at] = folded[at] with { Count = checked(folded[at].Count + row.Count) };
                }
                else
                {
                    folded.Insert(at, row);
                }
            }
        }

        return folded;
    }

    /// <summary>
    /// The superseded <b>whole-catalogue</b> schedule, kept as the negative control: before writing
    /// original <c>j</c>, if the catalogue already holds <c>F</c> runs the whole catalogue (the
    /// growing consolidated run included) is merged into one; then <c>j</c> is written and
    /// appended. Finalization consolidates whatever remains.
    /// </summary>
    public static QuantileRunSchedule WholeCatalogue(int fanIn, IQuantileSpillLeaves leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);

        var writes = new List<QuantileRunWrite>();
        var catalogue = new List<QuantileModelRun>();
        var counts = new List<int>();

        for (var j = 0; j < leaves.Count; j++)
        {
            if (catalogue.Count >= fanIn)
            {
                var merged = MergeAll(leaves, catalogue, writes, QuantileRunWriteKind.Carry, generation: 0);
                catalogue.Clear();
                catalogue.Add(merged);
                counts.Add(catalogue.Count);
            }

            var original = new QuantileModelRun(0, [j], leaves.Rows(j));
            writes.Add(new QuantileRunWrite(QuantileRunWriteKind.Original, 0, original.Rows, original.Leaves));
            catalogue.Add(original);
            counts.Add(catalogue.Count);
        }

        return Finalize(fanIn, leaves, catalogue, writes, counts);
    }

    /// <summary>
    /// The <b>generation-tiered</b> schedule the product follows: original <c>j</c> is written first
    /// and enters generation 0; whenever a generation holds <c>F</c> runs those <c>F</c>, oldest
    /// first, become one run of the next generation, and the cascade continues upward. A
    /// generation-<c>g</c> run therefore covers exactly <c>F^g</c> original leaves.
    /// </summary>
    public static QuantileRunSchedule GenerationTiered(int fanIn, IQuantileSpillLeaves leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);

        var writes = new List<QuantileRunWrite>();
        var levels = new List<List<QuantileModelRun>>();
        var counts = new List<int>();

        for (var j = 0; j < leaves.Count; j++)
        {
            var original = new QuantileModelRun(0, [j], leaves.Rows(j));
            writes.Add(new QuantileRunWrite(QuantileRunWriteKind.Original, 0, original.Rows, original.Leaves));
            Level(levels, 0).Add(original);
            counts.Add(Occupancy(levels));

            var generation = 0;
            while (generation < levels.Count && levels[generation].Count == fanIn)
            {
                var batch = levels[generation];
                var carried = MergeAll(leaves, batch, writes, QuantileRunWriteKind.Carry, generation + 1);
                batch.Clear();
                Level(levels, generation + 1).Add(carried);
                counts.Add(Occupancy(levels));
                generation++;
            }
        }

        // Generation ascending, creation order within a generation.
        var snapshot = new List<QuantileModelRun>();
        foreach (var level in levels)
        {
            snapshot.AddRange(level);
        }

        return Finalize(fanIn, leaves, snapshot, writes, counts);
    }

    /// <summary>
    /// How many runs each generation holds, lowest generation first, after <paramref name="spills"/>
    /// original spills: the base-F digits of the spill count.
    /// </summary>
    public static IReadOnlyList<int> GenerationOccupancy(int fanIn, long spills)
    {
        var occupancy = new List<int>();
        var remaining = spills;
        while (remaining > 0)
        {
            occupancy.Add((int)(remaining % fanIn));
            remaining /= fanIn;
        }

        return occupancy;
    }

    /// <summary>Leaves of <paramref name="rows"/> ascending values each, every leaf disjoint from every other.</summary>
    public static List<QuantileSpillLeaf> DisjointLiteralLeaves(int leaves, int rows, long countPerRow = 1)
    {
        var built = new List<QuantileSpillLeaf>(leaves);
        var next = 1.0;
        for (var j = 0; j < leaves; j++)
        {
            var map = new List<QuantileModelRow>(rows);
            for (var r = 0; r < rows; r++)
            {
                map.Add(new QuantileModelRow(next++, countPerRow));
            }

            built.Add(new QuantileSpillLeaf(map));
        }

        return built;
    }

    private static QuantileRunSchedule Finalize(
        int fanIn,
        IQuantileSpillLeaves leaves,
        List<QuantileModelRun> surviving,
        List<QuantileRunWrite> writes,
        List<int> counts)
    {
        var quiescent = surviving.Count;

        // The bounded multi-pass merger: chunk by fan-in, carry a lone tail unchanged, repeat.
        var runs = new List<QuantileModelRun>(surviving);
        while (runs.Count > 1)
        {
            var next = new List<QuantileModelRun>();
            for (var at = 0; at < runs.Count; at += fanIn)
            {
                var batch = runs.GetRange(at, Math.Min(fanIn, runs.Count - at));
                next.Add(batch.Count == 1
                    ? batch[0]
                    : MergeAll(leaves, batch, writes, QuantileRunWriteKind.Final, generation: -1));
            }

            runs = next;
        }

        if (surviving.Count > 0)
        {
            // The product reports its catalogue once more after consolidation, whether or not the
            // merger had to write anything.
            counts.Add(1);
        }

        var final = runs.Count == 1 ? runs[0] : new QuantileModelRun(0, [], 0);
        long written = 0;
        foreach (var write in writes)
        {
            written = checked(written + RunBytes(write.Rows));
        }

        var finalBytes = RunBytes(final.Rows);
        return new QuantileRunSchedule(
            writes,
            counts,
            counts.Count == 0 ? 0 : counts.Max(),
            quiescent,
            written,
            finalBytes,
            checked((2 * written) + finalBytes),
            final.Rows);
    }

    private static QuantileModelRun MergeAll(
        IQuantileSpillLeaves leaves,
        List<QuantileModelRun> batch,
        List<QuantileRunWrite> writes,
        QuantileRunWriteKind kind,
        int generation)
    {
        var covered = new List<int>();
        foreach (var run in batch)
        {
            covered.AddRange(run.Leaves);
        }

        var rows = leaves.UnionRows(covered);
        writes.Add(new QuantileRunWrite(kind, generation, rows, covered));
        return new QuantileModelRun(generation < 0 ? 0 : generation, covered, rows);
    }

    private static List<QuantileModelRun> Level(List<List<QuantileModelRun>> levels, int generation)
    {
        while (levels.Count <= generation)
        {
            levels.Add([]);
        }

        return levels[generation];
    }

    private static int Occupancy(List<List<QuantileModelRun>> levels)
    {
        var total = 0;
        foreach (var level in levels)
        {
            total += level.Count;
        }

        return total;
    }

    private sealed record QuantileModelRun(int Generation, IReadOnlyList<int> Leaves, int Rows);
}

/// <summary>One distinct value and its count, as a literal the model owns.</summary>
internal readonly record struct QuantileModelRow(double Value, long Count);

/// <summary>One original spill's literal population, ascending and distinct.</summary>
internal sealed record QuantileSpillLeaf(IReadOnlyList<QuantileModelRow> Rows);

/// <summary>The leaf population a schedule is modelled over, independent of the product.</summary>
internal interface IQuantileSpillLeaves
{
    /// <summary>How many original leaves there are.</summary>
    int Count { get; }

    /// <summary>The row count of leaf <paramref name="index"/>.</summary>
    int Rows(int index);

    /// <summary>The exact row count of the union of the named leaves.</summary>
    int UnionRows(IReadOnlyList<int> indices);
}

/// <summary>Literal leaf maps, folded by the model's own sorted-list union.</summary>
internal sealed class LiteralQuantileSpillLeaves(IReadOnlyList<QuantileSpillLeaf> leaves) : IQuantileSpillLeaves
{
    /// <summary>The literal maps.</summary>
    public IReadOnlyList<QuantileSpillLeaf> Leaves { get; } = leaves;

    public int Count => Leaves.Count;

    public int Rows(int index) => Leaves[index].Rows.Count;

    public int UnionRows(IReadOnlyList<int> indices) => QuantileRunScheduleModel.Union(Leaves, indices).Count;
}

/// <summary>Which kind of run a modelled write produced.</summary>
internal enum QuantileRunWriteKind
{
    /// <summary>An original intake spill.</summary>
    Original,

    /// <summary>
    /// A during-intake merge: a generation carry, or the whole-catalogue schedule's consolidation.
    /// </summary>
    Carry,

    /// <summary>A finalization merge, at or below the merger's fan-in chunking.</summary>
    Final,
}

/// <summary>One modelled run write: its kind, output generation, exact row count and covered leaves.</summary>
internal readonly record struct QuantileRunWrite(QuantileRunWriteKind Kind, int Generation, int Rows, IReadOnlyList<int> Leaves);

/// <summary>A complete modelled schedule and its exact byte accounting.</summary>
internal sealed record QuantileRunSchedule(
    IReadOnlyList<QuantileRunWrite> Writes,
    IReadOnlyList<int> CatalogueAfterEachTransition,
    int PeakCatalogue,
    int QuiescentCatalogue,
    long WrittenBytes,
    long FinalBytes,
    long TotalIoBytes,
    int FinalRows)
{
    /// <summary>The ordered write kinds.</summary>
    public IReadOnlyList<QuantileRunWriteKind> Kinds => [.. Writes.Select(write => write.Kind)];

    /// <summary>The ordered row counts of every write.</summary>
    public IReadOnlyList<int> Rows => [.. Writes.Select(write => write.Rows)];

    /// <summary>The ordered row counts of the during-intake merges alone.</summary>
    public IReadOnlyList<int> IntakeMergeRows =>
        [.. Writes.Where(write => write.Kind == QuantileRunWriteKind.Carry).Select(write => write.Rows)];

    /// <summary>The ordered row counts of the finalization merges alone.</summary>
    public IReadOnlyList<int> FinalMergeRows =>
        [.. Writes.Where(write => write.Kind == QuantileRunWriteKind.Final).Select(write => write.Rows)];

    /// <summary>How many original spills the schedule wrote.</summary>
    public int OriginalWrites => Writes.Count(write => write.Kind == QuantileRunWriteKind.Original);

    /// <summary>How many merges ran during intake.</summary>
    public int IntakeMerges => Writes.Count(write => write.Kind == QuantileRunWriteKind.Carry);

    /// <summary>How many merges ran at finalization.</summary>
    public int FinalMerges => Writes.Count(write => write.Kind == QuantileRunWriteKind.Final);
}
