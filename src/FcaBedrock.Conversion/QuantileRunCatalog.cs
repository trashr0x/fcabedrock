namespace FcaBedrock.Conversion;

/// <summary>
/// One <see cref="QuantileAccumulator"/>'s spill-run catalogue, tiered by <b>generation</b>
/// (D-128). Original spills enter generation 0; when a generation holds <c>F</c> runs, exactly
/// those <c>F</c> — oldest first — become one run of the next generation. Promotion is by
/// original-leaf count, never by byte size, and a higher-generation run is never used to fill a
/// lower batch, so a generation-<c>g</c> run represents exactly <c>F^g</c> original spills.
/// <para>
/// <b>Why the tiering, and what it replaces.</b> The prior policy merged the <i>whole</i>
/// catalogue — the growing consolidated run included — every time the catalogue reached the
/// fan-in, so each value could be rewritten once per merge and total spool traffic grew
/// quadratically in the spill count at a fixed capacity. Tiering rewrites a value at most once
/// per level, which is logarithmic in the spill count. It buys that with a catalogue bounded by a
/// fixed ceiling rather than by the fan-in, and that is a real change to the letter of the
/// D-103/D-124 catalogue clause, recorded as one.
/// </para>
/// <para>
/// <b>The bound.</b> <c>L = 1 + floor(log_F(long.MaxValue))</c> is computed by repeated integer
/// division — never a floating logarithm, which rounds, and never <c>F^L</c>, which overflows.
/// <c>K = (F - 1) * L</c> in widened checked arithmetic (F = 16: L = 16, K = 240; F = 2: L = 63,
/// K = 63). At quiescence the count <b>equals</b> the sum of the base-<c>F</c> digits of the
/// number of successful original spills, so it is at most <c>K</c>; during one insertion and its
/// carry chain it is at most <c>K + 1</c>. Every successful original spill carries at least one
/// counted observation and the running total is a checked <see cref="long"/>, so the spill count
/// cannot exceed <see cref="long.MaxValue"/> and levels <c>0..L-1</c> suffice: a carry that would
/// require level <c>L</c> is a programmer error, never growth.
/// </para>
/// <para>
/// <b>Tier 2, not tier 1.</b> An entry is a <see cref="SpoolRunHandle"/> — a <c>(path, size)</c>
/// value, never an open operating-system handle. This object and its arrays are bounded by count
/// and fixed shape exactly as the list it replaces was, and the accumulator holds one reference
/// to it. Level storage is allocated per level on first use, so an accumulator that never reaches
/// a generation never pays for it, and a zero-spill accumulator never builds a catalogue at all.
/// No ancestry is retained: a consumed slot is cleared, so nothing keeps a run alive after its
/// carry.
/// </para>
/// </summary>
internal sealed class QuantileRunCatalog
{
    private readonly int _fanIn;
    private readonly SpoolRunHandle[]?[] _levels;
    private readonly int[] _occupancy;
    private List<SpoolRunHandle>? _batch;
    private int _count;

    /// <summary>Creates a catalogue for <paramref name="fanIn"/> at the computed level ceiling.</summary>
    public QuantileRunCatalog(int fanIn)
        : this(fanIn, LevelsFor(fanIn))
    {
    }

    /// <summary>
    /// Creates a catalogue with an explicit level ceiling. Production always passes
    /// <see cref="LevelsFor"/>; a lower ceiling exists only so the level-<c>L</c> fail-closed rule
    /// can be exercised without <c>F^L</c> spills (EP-6 — a construction seam, not a behaviour
    /// branch: nothing below reads which constructor was used).
    /// </summary>
    public QuantileRunCatalog(int fanIn, int levels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fanIn, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(levels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(levels, LevelsFor(fanIn));

        _fanIn = fanIn;
        _levels = new SpoolRunHandle[]?[levels];
        _occupancy = new int[levels];
    }

    /// <summary>The merge fan-in <c>F</c> this catalogue promotes at.</summary>
    public int FanIn => _fanIn;

    /// <summary>The level ceiling <c>L</c>: valid generations are <c>0 .. L-1</c>.</summary>
    public int Levels => _levels.Length;

    /// <summary>The quiescent ceiling <c>K = (F - 1) * L</c>.</summary>
    public long Ceiling => checked((long)(_fanIn - 1) * _levels.Length);

    /// <summary>The live run handles the catalogue holds, across every generation.</summary>
    public int Count => _count;

    /// <summary>
    /// <c>L = 1 + floor(log_F(long.MaxValue))</c> by repeated integer division. No floating
    /// logarithm (it rounds, and a rounded ceiling is either unsound or wasteful) and no
    /// <c>F^L</c> (it overflows by construction).
    /// </summary>
    public static int LevelsFor(int fanIn)
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

    /// <summary>The quiescent ceiling <c>K = (F - 1) * L</c>, in widened checked arithmetic.</summary>
    public static long CeilingFor(int fanIn) => checked((long)(fanIn - 1) * LevelsFor(fanIn));

    /// <summary>Whether generation <paramref name="generation"/> holds a full batch of <c>F</c> runs.</summary>
    public bool IsFull(int generation) =>
        generation >= 0 && generation < _occupancy.Length && _occupancy[generation] == _fanIn;

    /// <summary>Adds a freshly written original spill at generation 0.</summary>
    public void Insert(SpoolRunHandle run) => Add(generation: 0, run);

    /// <summary>
    /// The <c>F</c> runs of a full generation, oldest first — the exact batch a carry merges. The
    /// list is the catalogue's own bounded scratch (capacity <c>F</c>), refilled on each call, so
    /// no per-carry allocation accumulates. The catalogue is <b>not</b> modified: a batch is only
    /// consumed when <see cref="Carry"/> commits its successful output.
    /// </summary>
    public List<SpoolRunHandle> Batch(int generation)
    {
        if (!IsFull(generation))
        {
            throw new InvalidOperationException(
                $"generation {generation} holds {(generation >= 0 && generation < _occupancy.Length ? _occupancy[generation] : 0)} runs, not a full batch of {_fanIn}.");
        }

        _batch ??= new List<SpoolRunHandle>(_fanIn);
        _batch.Clear();
        var slots = _levels[generation]!;
        for (var i = 0; i < _fanIn; i++)
        {
            _batch.Add(slots[i]);
        }

        return _batch;
    }

    /// <summary>
    /// Commits a completed carry: the <c>F</c> runs of <paramref name="generation"/> are replaced
    /// by <paramref name="output"/> at the next generation. Called <b>only after</b> the merge has
    /// returned successfully, so a failed or cancelled carry leaves the catalogue exactly as it
    /// was and the workspace — which tracks every undeleted path — owns the cleanup. A partial
    /// carry is never resumed.
    /// </summary>
    public void Carry(int generation, SpoolRunHandle output)
    {
        if (!IsFull(generation))
        {
            throw new InvalidOperationException($"generation {generation} is not a full batch of {_fanIn}.");
        }

        if (generation + 1 >= _levels.Length)
        {
            // Fail closed rather than grow. Reaching here would mean more than long.MaxValue
            // successful original spills, which the checked observation total already forbids.
            throw new InvalidOperationException(
                $"a carry from generation {generation} would require generation {generation + 1}, above the level ceiling {_levels.Length}.");
        }

        var slots = _levels[generation]!;
        Array.Clear(slots); // release the consumed slots: no ancestry is retained
        _occupancy[generation] = 0;
        _count -= _fanIn;
        Add(generation + 1, output);
    }

    /// <summary>
    /// The deterministic finalization snapshot: generation <b>ascending</b>, creation order within
    /// a generation. Its length is the live count, which is at most <c>K</c>.
    /// </summary>
    public List<SpoolRunHandle> Snapshot()
    {
        var snapshot = new List<SpoolRunHandle>(_count);
        for (var generation = 0; generation < _levels.Length; generation++)
        {
            var slots = _levels[generation];
            for (var i = 0; i < _occupancy[generation]; i++)
            {
                snapshot.Add(slots![i]);
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Replaces everything with the one consolidated run. The generation tiering has done its work
    /// by this point — the run covers every leaf — so it is simply the catalogue's single entry.
    /// </summary>
    public void ReplaceWithFinal(SpoolRunHandle run)
    {
        Clear();
        Add(generation: 0, run);
    }

    /// <summary>Drops every entry, releasing the handles the slots referenced.</summary>
    public void Clear()
    {
        for (var generation = 0; generation < _levels.Length; generation++)
        {
            if (_levels[generation] is { } slots)
            {
                Array.Clear(slots);
            }

            _occupancy[generation] = 0;
        }

        _batch?.Clear();
        _count = 0;
    }

    private void Add(int generation, SpoolRunHandle run)
    {
        var slots = _levels[generation] ??= new SpoolRunHandle[_fanIn];
        var at = _occupancy[generation];
        if (at >= _fanIn)
        {
            throw new InvalidOperationException(
                $"generation {generation} already holds {_fanIn} runs; a carry must consume it before another run is added.");
        }

        slots[at] = run;
        _occupancy[generation] = at + 1;
        _count++;
    }
}
