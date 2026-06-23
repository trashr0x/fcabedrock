using System.Globalization;
using System.Text;
using static System.FormattableString;

namespace FcaBedrock.Golden.Tests;

// The project-standard byte-equality primitive for golden comparison (P-5).
// Exact comparison with a human-readable diff so a golden failure points at the
// first differing offset instead of dumping two opaque blobs.
internal static class ByteComparer
{
    public static ByteComparisonResult Compare(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        var min = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < min; i++)
        {
            if (expected[i] != actual[i])
            {
                return ByteComparisonResult.Differs(i, expected, actual);
            }
        }

        if (expected.Length != actual.Length)
        {
            return ByteComparisonResult.Differs(min, expected, actual);
        }

        return ByteComparisonResult.Equal;
    }
}

internal sealed class ByteComparisonResult
{
    private ByteComparisonResult(bool areEqual, int firstDifferenceOffset, string message)
    {
        AreEqual = areEqual;
        FirstDifferenceOffset = firstDifferenceOffset;
        Message = message;
    }

    public bool AreEqual { get; }

    // -1 when the sequences are equal.
    public int FirstDifferenceOffset { get; }

    public string Message { get; }

    public static ByteComparisonResult Equal { get; } =
        new(true, -1, "byte sequences are identical");

    public static ByteComparisonResult Differs(int offset, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Invariant(
            $"byte sequences differ at offset {offset} (expected length {expected.Length}, actual length {actual.Length})"));
        sb.Append("  expected: ").AppendLine(HexWindow(expected, offset));
        sb.Append("  actual:   ").Append(HexWindow(actual, offset));
        return new ByteComparisonResult(false, offset, sb.ToString());
    }

    private static string HexWindow(ReadOnlySpan<byte> data, int center)
    {
        const int Radius = 8;
        var start = Math.Max(0, center - Radius);
        var end = Math.Min(data.Length, center + Radius + 1);
        if (start >= end)
        {
            return "(empty)";
        }

        var sb = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            if (i > start)
            {
                sb.Append(' ');
            }

            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
