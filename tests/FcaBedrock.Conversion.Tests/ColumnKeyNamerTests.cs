namespace FcaBedrock.Conversion.Tests;

// The keep unique-name escalation in isolation (§6.1, D-083): first vs later occurrence, the two
// independent signals (duplicate cleaned key vs name-collision disambiguation), and ordinal comparison.
public sealed class ColumnKeyNamerTests
{
    [Fact]
    public void Assign_WhenUniqueKeys_ThenKeysUsedVerbatim()
    {
        var namer = new ColumnKeyNamer();

        Assert.Equal("a", namer.Assign("a", 0, out var d0, out var e0));
        Assert.Equal("b", namer.Assign("b", 1, out var d1, out var e1));
        Assert.False(d0 || e0 || d1 || e1);
    }

    [Fact]
    public void Assign_WhenKeyRepeats_ThenSuffixedWithRecordIndexAndFlaggedDuplicate()
    {
        var namer = new ColumnKeyNamer();
        namer.Assign("a", 0, out _, out _);

        var name = namer.Assign("a", 3, out var duplicate, out var disambiguated);

        Assert.Equal("a#3", name); // <key>#<record-index>, 0-based
        Assert.True(duplicate);
        Assert.False(disambiguated);
    }

    [Fact]
    public void Assign_WhenLaterCandidateCollidesWithLiteral_ThenEscalatesAndFlagsBoth()
    {
        // [P001#2, P001, P001] → P001#2, P001, P001#2#1
        var namer = new ColumnKeyNamer();
        Assert.Equal("P001#2", namer.Assign("P001#2", 0, out _, out _));
        Assert.Equal("P001", namer.Assign("P001", 1, out _, out _));

        var name = namer.Assign("P001", 2, out var duplicate, out var disambiguated);

        Assert.Equal("P001#2#1", name);
        Assert.True(duplicate);     // P001 is a repeated cleaned key
        Assert.True(disambiguated); // its P001#2 candidate collided with the literal first row
    }

    [Fact]
    public void Assign_WhenFirstOccurrenceCollidesWithGeneratedName_ThenDisambiguatedNotDuplicate()
    {
        // [P001, P001, P001#1] → P001, P001#1, P001#1#1
        var namer = new ColumnKeyNamer();
        namer.Assign("P001", 0, out _, out _);
        namer.Assign("P001", 1, out _, out _); // → P001#1

        var name = namer.Assign("P001#1", 2, out var duplicate, out var disambiguated);

        Assert.Equal("P001#1#1", name);
        Assert.False(duplicate);    // a distinct cleaned key, not a repeat
        Assert.True(disambiguated); // but it collided with the generated P001#1
    }

    [Fact]
    public void Assign_WhenMultipleCandidatesCollide_ThenTakesFirstUnused()
    {
        var namer = new ColumnKeyNamer();
        Assert.Equal("a", namer.Assign("a", 0, out _, out _));
        Assert.Equal("a#1", namer.Assign("a", 1, out _, out _));            // duplicate → a#1
        Assert.Equal("a#1#1", namer.Assign("a#1", 2, out _, out var e));    // literal a#1 collides → a#1#1
        Assert.True(e);
    }

    [Fact]
    public void Assign_WhenKeysDifferByOrdinalCase_ThenDistinct()
    {
        // Ordinal comparison (P-12): "A" and "a" are different keys.
        var namer = new ColumnKeyNamer();

        Assert.Equal("A", namer.Assign("A", 0, out var upper, out _));
        Assert.Equal("a", namer.Assign("a", 1, out var lower, out _));
        Assert.False(upper);
        Assert.False(lower); // not a duplicate of "A"
    }
}
