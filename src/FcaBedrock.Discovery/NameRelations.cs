namespace FcaBedrock.Discovery;

/// <summary>
/// The current logical names of a probe's attributes and the facts that decide which pairs of
/// them may produce the same formal-attribute name (§7.1, D-108).
/// <para>
/// <b>Why pairs matter.</b> A probed attribute renders each category as its name, a hyphen, and
/// the value. For names <c>N</c> and <c>N-r</c>, a value <c>v</c> of the shorter one renders the
/// same text as a value <c>w</c> of the longer one exactly when <c>v = r + "-" + w</c>. So a
/// collision needs a shorter-attribute value that starts with <c>r + "-"</c>, and when no
/// observed value of the shorter attribute does, the pair cannot collide on this source,
/// whatever the longer attribute holds. The <em>flag</em> of a pair records that such a value
/// was observed, or that some of the shorter attribute's history is unknown.
/// </para>
/// <para>
/// <b>What is kept, and when the flag is decided.</b> Names live in a character trie keyed by
/// node and character. Until a domain truncates, its retained list holds every distinct value it
/// observed, so that history is checked once, at the end of the pass. After truncation each
/// discarded value is walked from its attribute's current name as it arrives, and every longer
/// name it reaches is recorded as a hit for that ordinal pair. A pair that came into being after
/// the shorter domain truncated has unknown history, and counts as flagged. Its establishment is
/// the later of the two current names' assignment events, which is why both are recorded. No
/// pair table, no discarded value and no value index is kept.
/// </para>
/// <para>
/// <b>One lifetime per ordinal pair.</b> A hit is read only for a pair whose final names are
/// still related, and it was recorded under those same names. That holds because the names that
/// can change are triple fallbacks (<c>predicate_i</c> and its <c>#k</c> ladder), which contain no
/// hyphen: a fallback can only be the shorter name of a pair, and it relates to a longer usable
/// name only by equalling that name's text before its first hyphen. Each ordinal's ladder is its
/// own set of strings, and a displaced fallback never returns to a string it left. A change to
/// the fallback ladder or to <c>AttributeNameValidity</c> reopens this argument.
/// </para>
/// </summary>
internal sealed class NameRelations
{
    private const int NoOrdinal = -1;
    private const int NoNode = -1;
    private const int Root = 0;

    // Child node by (parent node, character), one entry per character of name text: a node id
    // fits 31 bits and a UTF-16 unit 16, so the key packs both into one long.
    private readonly Dictionary<long, int> _children = [];

    // Per node: the ordinal whose current name ends there, or NoOrdinal.
    private readonly List<int> _terminalOf = [NoOrdinal];

    // Per ordinal: its current name and that name's node, when the name was assigned, when its
    // domain truncated (or -1), and the longer ordinals its discarded values reached.
    private readonly List<string> _names = [];
    private readonly List<int> _nodes = [];
    private readonly List<long> _assignedAt = [];
    private readonly List<long> _truncatedAt = [];
    private readonly List<HashSet<int>?> _hits = [];

    // Reused by the discarded-value walk, so the pass allocates nothing per value.
    private readonly List<(int Longer, int Offset)> _walk = [];

    // The probe's token. Every walk and claim below checks it at each character, because no
    // probe guard bounds the length of a name or of a discarded value (D-112).
    private readonly CancellationToken _cancellationToken;

    // A logical clock that moves only when a name is assigned or a domain first truncates, so it
    // advances at most a small multiple of the attribute count, not once per row. Checked: an
    // overflow is a defect, never a silently reordered history.
    private long _events;

    /// <summary>Starts with no names.</summary>
    /// <param name="cancellationToken">The probe's token, checked at every character a walk or a claim visits.</param>
    public NameRelations(CancellationToken cancellationToken) => _cancellationToken = cancellationToken;

    /// <summary>The number of attributes named so far; ordinals run from 0 to one less.</summary>
    public int Count => _names.Count;

    /// <summary>The current name of <paramref name="ordinal"/>.</summary>
    public string NameOf(int ordinal) => _names[ordinal];

    /// <summary>
    /// Names the next ordinal, recording the assignment as an event. A name that is already
    /// current belongs to another ordinal, which the naming matrices never produce.
    /// </summary>
    public int Add(string name)
    {
        var ordinal = _names.Count;
        _names.Add(name);
        _nodes.Add(Claim(name, ordinal));
        _assignedAt.Add(NextEvent());
        _truncatedAt.Add(-1);
        _hits.Add(null);
        return ordinal;
    }

    /// <summary>
    /// Moves <paramref name="ordinal"/> to <paramref name="name"/> during the pass, recording
    /// the assignment as an event. The ordinal keeps its domain and its hits: hits for pairs its
    /// old name formed are simply never read, because those pairs are not final (see the class
    /// remarks).
    /// </summary>
    public void Rename(int ordinal, string name)
    {
        Replace(ordinal, name);
        _assignedAt[ordinal] = NextEvent();
    }

    /// <summary>
    /// Moves <paramref name="ordinal"/> to <paramref name="name"/> after the pass, when every
    /// flag has been decided, so no event is recorded.
    /// </summary>
    public void Replace(int ordinal, string name)
    {
        _terminalOf[_nodes[ordinal]] = NoOrdinal;
        _names[ordinal] = name;
        _nodes[ordinal] = Claim(name, ordinal);
    }

    /// <summary>
    /// Records a value <paramref name="ordinal"/>'s domain discarded, stamping the domain's
    /// truncation on its first discard, and walks it from the ordinal's current name.
    /// <para>
    /// Every discarded value is walked, a repeat of an earlier discarded one and one containing
    /// CR or LF included, because no discarded value is remembered.
    /// </para>
    /// </summary>
    public void RecordDiscard(int ordinal, string value)
    {
        if (_truncatedAt[ordinal] < 0)
        {
            _truncatedAt[ordinal] = NextEvent();
        }

        var start = DescendantStart(ordinal);
        if (start == NoNode)
        {
            return;
        }

        _walk.Clear();
        Walk(start, value, _walk);
        foreach (var (longer, _) in _walk)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            (_hits[ordinal] ??= []).Add(longer);
        }
    }

    /// <summary>
    /// The node for <paramref name="ordinal"/>'s current name plus a hyphen, where every walk
    /// of its values starts, or -1 when no current name begins with that prefix.
    /// </summary>
    public int DescendantStart(int ordinal) => Child(_nodes[ordinal], '-');

    /// <summary>
    /// Appends every longer current name that <paramref name="value"/> reaches from
    /// <paramref name="start"/> (a node from <see cref="DescendantStart"/>) together with the
    /// offset of the hyphen that follows that name's remainder in the value. A reached name
    /// <c>N + "-" + r</c> is appended only when the value starts with <c>r + "-"</c>, the empty
    /// remainder included, which is the flag's whole test.
    /// </summary>
    public void Walk(int start, string value, List<(int Longer, int Offset)> into)
    {
        var node = start;
        for (var offset = 0; ; offset++)
        {
            if (offset == value.Length)
            {
                return;
            }

            _cancellationToken.ThrowIfCancellationRequested();
            var character = value[offset];
            if (character == '-' && _terminalOf[node] is var longer and not NoOrdinal)
            {
                into.Add((longer, offset));
            }

            node = Child(node, character);
            if (node == NoNode)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Appends every ordinal whose current name is <paramref name="ordinal"/>'s current name
    /// cut before one of its hyphens: each shorter name of a current pair it is the longer of.
    /// </summary>
    public void Shorter(int ordinal, List<(int Shorter, int Offset)> into)
    {
        var name = _names[ordinal];
        var node = Root;
        for (var offset = 0; offset < name.Length; offset++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (name[offset] == '-' && _terminalOf[node] is var shorter and not NoOrdinal)
            {
                into.Add((shorter, offset));
            }

            node = Child(node, name[offset]);
        }
    }

    /// <summary>
    /// Whether the pair's flag is forced by unknown history: the shorter domain truncated before
    /// the pair existed, that is before the later of the two names' assignments (§7.1).
    /// </summary>
    public bool Forced(int shorter, int longer) =>
        _truncatedAt[shorter] >= 0 && Math.Max(_assignedAt[shorter], _assignedAt[longer]) > _truncatedAt[shorter];

    /// <summary>Whether a discarded value of <paramref name="shorter"/> reached <paramref name="longer"/>.</summary>
    public bool Hit(int shorter, int longer) => _hits[shorter]?.Contains(longer) == true;

    private long NextEvent() => _events = checked(_events + 1);

    // Ends `name` at a node owned by `ordinal`, creating the path as needed. Two current
    // attributes never share a name, so an owned node is an invariant failure, not a case.
    private int Claim(string name, int ordinal)
    {
        var node = Root;
        foreach (var character in name)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var key = Key(node, character);
            if (!_children.TryGetValue(key, out var child))
            {
                child = _terminalOf.Count;
                _terminalOf.Add(NoOrdinal);
                _children.Add(key, child);
            }

            node = child;
        }

        if (_terminalOf[node] != NoOrdinal)
        {
            throw new InvalidOperationException(
                $"Two current probe attributes share a name (ordinals {_terminalOf[node]} and {ordinal}).");
        }

        _terminalOf[node] = ordinal;
        return node;
    }

    private int Child(int node, char character) =>
        _children.TryGetValue(Key(node, character), out var child) ? child : NoNode;

    private static long Key(int node, char character) => ((long)node << 16) | character;
}
