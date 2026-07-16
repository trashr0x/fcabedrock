using System.Runtime.CompilerServices;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The <see cref="ValueCount"/> spool codec (D-095/D-103): a fixed 16-byte payload with no
/// reference fields, reusing the existing framing and integrity semantics rather than a second
/// storage system. Spilling must be byte-neutral — a value that round-trips inexactly would
/// move a cut, and with it the output bytes (P-7).
/// </summary>
public sealed class ValueCountCodecTests
{
    private static readonly ValueCountCodec Codec = ValueCountCodec.Instance;

    [Theory]
    [InlineData(0.0, 1L)]
    [InlineData(-0.0, 7L)]
    [InlineData(1.0, long.MaxValue)]
    [InlineData(-1.7976931348623157E+308, 1L)]
    [InlineData(1.7976931348623157E+308, 1L)]
    [InlineData(5e-324, 3L)]                       // the smallest subnormal
    [InlineData(0.1, 42L)]                         // no exact decimal representation
    public void RoundTrip_WhenAnyFiniteValue_ThenExactBitsAndCountSurvive(double value, long count)
    {
        var row = new ValueCount(value, count);
        Span<byte> buffer = stackalloc byte[(int)Codec.Measure(row)];

        Codec.Write(row, buffer);
        var readback = Codec.Read(buffer);

        // Bit equality, not ==: -0.0 == 0.0 would let a signed-zero corruption pass unnoticed.
        Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(readback.Value));
        Assert.Equal(count, readback.Count);
    }

    [Fact]
    public void Measure_WhenAnyRow_ThenAlwaysTheFixedPayload()
    {
        // Fixed-size framing is what lets the accumulator project a spill's byte cost without
        // inspecting values.
        Assert.Equal(16, Codec.Measure(new ValueCount(0, 0)));
        Assert.Equal(16, Codec.Measure(new ValueCount(double.MaxValue, long.MaxValue)));
        Assert.Equal(ValueCountCodec.PayloadBytes, Codec.Measure(new ValueCount(1, 1)));
    }

    [Fact]
    public void MeasureResident_WhenAnyRow_ThenZeroBecauseNothingIsReferenced()
    {
        // The value and count live inline in the array slot, which the accumulator's own model
        // charges — so there is nothing for the codec's retained-object accounting to add.
        Assert.Equal(0, Codec.MeasureResident(new ValueCount(double.MaxValue, long.MaxValue)));
        Assert.Equal(16, Unsafe.SizeOf<ValueCount>());
    }

    [Fact]
    public void Comparer_WhenBothZeroSpellings_ThenTheyCompareEqualSoIntakeMustPickOne()
    {
        // The actual .NET guarantee, pinned because it is the reason intake canonicalization
        // exists — and because it is easy to assume the opposite. double.CompareTo does NOT
        // implement IEEE totalOrder for signed zeros: it compares -0.0 and +0.0 EQUAL, and
        // Equals folds them too. So a run holding -0.0 and another holding +0.0 would aggregate
        // correctly, but WHICH spelling survives into the merged row would depend on heap order.
        // Canonicalizing at intake removes that freedom, so labels, cuts, and hashes are pinned.
        Assert.Equal(0, ValueCountComparer.Instance.Compare(new ValueCount(-0.0, 1), new ValueCount(0.0, 1)));
        Assert.Equal(0, (-0.0).CompareTo(0.0));
        Assert.True((-0.0).Equals(0.0));

        // And they are genuinely distinct bit patterns, which is what would leak without it.
        Assert.NotEqual(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(0.0));
    }

    [Fact]
    public void Read_WhenTheRecordIsShort_ThenFramingCorruptionIsSignalled()
    {
        // A payload of the wrong length is safely-identifiable corruption; the run reader maps
        // SpoolFramingException to a CorruptRun storage failure rather than mis-decoding.
        var ex = Assert.Throws<SpoolFramingException>(() => Codec.Read(new byte[15]));
        Assert.Contains("15", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenTheRecordIsLong_ThenFramingCorruptionIsSignalled() =>
        Assert.Throws<SpoolFramingException>(() => Codec.Read(new byte[17]));

    // --- The layout itself, not just its round-trip ---------------------------
    //
    // A round-trip through the same writer and reader agrees with ANY self-consistent layout,
    // including a wrong-endian or reordered one. These two pin the bytes independently: one
    // hand-authors the expected payload, the other decodes a payload this test built.

    [Fact]
    public void Write_WhenAKnownRow_ThenTheExactHandAuthoredBytes()
    {
        // 1.0 is IEEE-754 0x3FF0000000000000; 258 is 0x0000000000000102. Little-endian means each
        // is written least-significant byte first, value before count.
        var row = new ValueCount(1.0, 258);
        Span<byte> buffer = stackalloc byte[16];

        Codec.Write(row, buffer);

        Assert.Equal(
            new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F, // double 1.0, little-endian
                0x02, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // long 258, little-endian
            },
            buffer.ToArray());
    }

    [Fact]
    public void Read_WhenAnIndependentlyConstructedPayload_ThenDecodesTheIntendedRow()
    {
        // The other direction: bytes this test authored, not bytes the writer produced.
        byte[] payload =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x24, 0xC0, // double -10.0
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, // long.MaxValue
        ];

        var row = Codec.Read(payload);

        Assert.Equal(-10.0, row.Value);
        Assert.Equal(long.MaxValue, row.Count);
    }

    [Fact]
    public void Write_WhenTheSameRowTwice_ThenTheEncodingIsDeterministic()
    {
        var row = new ValueCount(0.1, 5);
        Span<byte> first = stackalloc byte[16];
        Span<byte> second = stackalloc byte[16];

        Codec.Write(row, first);
        Codec.Write(row, second);

        Assert.True(first.SequenceEqual(second));
    }
}
