using System.Runtime.CompilerServices;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// Independent validation of the conservative resident accounting (D-082 / F2). Each test asserts the
// modeled bytes are ≥ an actual heap size derived from raw .NET-10-CoreCLR-x64 layout literals here (NOT
// from ResidentModel's padded constants), so the bound is proved against reality, not recomputed from the
// same formula. Overflow-safety is exercised directly on the saturating helpers, not via unallocatable
// inputs.
public sealed class RowCodecResidentTests
{
    // Raw x64 object-layout facts, independent of ResidentModel's (padded) constants.
    private const long RealObjectHeader = 16; // sync-block index + method-table pointer
    private const long RealArrayHeader = 24;  // object header (16) + length/bounds (8)
    private const long RealReference = 8;

    private static long RoundUp8(long n) => (n + 7) & ~7L;

    // Actual x64 string heap size: header + length int (4) + UTF-16 payload + null terminator (2), 8-aligned.
    private static long RealStringBytes(int length) => RoundUp8(RealObjectHeader + 4 + (2L * length) + 2);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(7)]
    public void StringCost_IsConservativeOverRealLayout_WithTerminatorAndAlignment(int length)
    {
        var modeled = ResidentModel.StringCost(length);

        Assert.True(modeled >= RealStringBytes(length), $"StringCost({length}) = {modeled} < real {RealStringBytes(length)}");
        Assert.Equal(0, modeled % 8); // 8-byte aligned
    }

    [Fact]
    public void TripleMeasureResident_WhenAllNull_ThenZero_ExcludingTheSlot()
    {
        // The RankedRow slot (rank/seq/index + inline refs) is accounted by the buffer's backing array,
        // never by MeasureResident — so an all-null triple row retains no referenced objects.
        Assert.Equal(0, TripleRowCodec.Instance.MeasureResident(new TripleRow(0, null, null, null)));
    }

    [Fact]
    public void TripleMeasureResident_ChargesEachNonNullStringConservatively()
    {
        var row = new TripleRow(0, "sub", null, "value"); // null predicate charges nothing

        var actual = RealStringBytes(3) + RealStringBytes(5);
        Assert.True(TripleRowCodec.Instance.MeasureResident(row) >= actual);
    }

    [Fact]
    public void DedupeMeasureResident_ChargesRecordFieldsNameArrayAndStrings_AtLeastActual()
    {
        string?[] fields = ["ab", null, "wxyz"]; // 2 non-null + 1 null across 3 slots
        var row = DedupeRow.Live(new ObjectRecord("12345", fields), 0);

        // Independent conservative actual for the retained referenced objects (raw x64 layout):
        const long objectRecord = RealObjectHeader + (2 * RealReference);         // header + _fields + Name refs
        var name = RealStringBytes(5);                                            // "12345"
        var fieldArray = RealArrayHeader + (3 * RealReference);                    // string?[3]: header + a ref per field
        var strings = RealStringBytes(2) + RealStringBytes(4);                     // "ab" + "wxyz"; the null field adds none
        var actual = objectRecord + name + fieldArray + strings;

        var modeled = DedupeRowCodec.Instance.MeasureResident(row);
        Assert.True(modeled >= actual, $"MeasureResident {modeled} < independent actual {actual}");
    }

    [Fact]
    public void DedupeMeasureResident_ChargesAReferencePerField_IncludingNulls()
    {
        // All-null fields: Measure is tiny (4 B/field) but MeasureResident charges a field-array reference
        // for every field regardless of nullity, plus the array header and the record/name.
        var row = DedupeRow.Live(new ObjectRecord("x", new string?[100]), 0);

        var resident = DedupeRowCodec.Instance.MeasureResident(row);
        Assert.True(resident >= 100 * RealReference, "each field (null or not) must charge an array reference");
        Assert.True(resident > DedupeRowCodec.Instance.Measure(row), "resident exceeds the tiny serialized size");
    }

    [Fact]
    public void DedupeMeasureResident_LargeFieldCount_StaysPositiveAndChargesReferences()
    {
        // A large field count stays in long arithmetic (positive, not wrapped) and charges each reference.
        var row = DedupeRow.Live(new ObjectRecord("0", new string?[200_000]), 0);

        var resident = DedupeRowCodec.Instance.MeasureResident(row);
        Assert.True(resident >= 200_000L * ResidentModel.Reference);
        Assert.True(resident > 0);
    }

    [Fact]
    public void BackingArrayBytes_IsExactConservativeAndMonotonic()
    {
        var stride = (long)Unsafe.SizeOf<RankedRow<TripleRow>>();

        Assert.Equal(ResidentModel.ArrayHeader + (4 * stride), ResidentModel.BackingArrayBytes(4, stride));
        Assert.True(ResidentModel.BackingArrayBytes(4, stride) >= RealArrayHeader + (4 * stride)); // ≥ real header + slots
        Assert.True(ResidentModel.BackingArrayBytes(8, stride) > ResidentModel.BackingArrayBytes(4, stride)); // grows with capacity
    }

    [Fact]
    public void BufferBytes_ChargesTheListObjectOnTopOfTheBackingArray()
    {
        var stride = (long)Unsafe.SizeOf<RankedRow<DedupeRow>>();

        // The List<T> object itself (real x64: header 16 + _items ref 8 + _size/_version 8 = 32) is charged
        // beyond the backing array — proving the buffer object is accounted, not only its backing array (F2).
        Assert.True(ResidentModel.BufferBytes(0, stride) >= 32, "the List<T> object itself must be charged");
        Assert.True(ResidentModel.BufferBytes(4, stride) > ResidentModel.BackingArrayBytes(4, stride), "List object adds on top of the backing array");
        Assert.True(ResidentModel.BufferBytes(8, stride) > ResidentModel.BufferBytes(4, stride), "grows with capacity");
    }

    [Fact]
    public void SaturatingAdd_ClampsAtLongMaxValue()
    {
        Assert.Equal(7, ResidentModel.SaturatingAdd(3, 4));
        Assert.Equal(long.MaxValue, ResidentModel.SaturatingAdd(long.MaxValue - 1, 5));
        Assert.Equal(long.MaxValue, ResidentModel.SaturatingAdd(long.MaxValue, long.MaxValue));
    }

    [Fact]
    public void SaturatingMul_ClampsAtLongMaxValueAndHandlesZero()
    {
        Assert.Equal(2_000_000_000_000L, ResidentModel.SaturatingMul(1_000_000, 2_000_000)); // no overflow
        Assert.Equal(0, ResidentModel.SaturatingMul(long.MaxValue, 0));
        Assert.Equal(long.MaxValue, ResidentModel.SaturatingMul(long.MaxValue / 2, 8)); // overflow → clamp
        Assert.Equal(long.MaxValue, ResidentModel.SaturatingMul(int.MaxValue, long.MaxValue));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 8)]
    [InlineData(8, 8)]
    [InlineData(9, 16)]
    [InlineData(-4, 0)]
    public void RoundUpTo8_RoundsUp(long n, long expected) => Assert.Equal(expected, ResidentModel.RoundUpTo8(n));

    [Fact]
    public void RoundUpTo8_SaturatesNearLongMax() => Assert.Equal(long.MaxValue, ResidentModel.RoundUpTo8(long.MaxValue - 2));
}
