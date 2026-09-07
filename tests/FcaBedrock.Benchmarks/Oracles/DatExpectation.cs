using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// What a correct conversion must produce: the context's shape and its exact native bytes.
/// <para>
/// <see cref="CrossIdSum"/> is the sum of every crossed formal-attribute id. It is here rather than
/// derived on demand because it costs nothing to accumulate while the expectation is already being
/// streamed, and re-deriving it would mean a second full traversal of a seventy-three-million-row
/// corpus. It is what a case with <b>no artifact to compare</b> — an emit drain — validates against:
/// a conversion that crossed the right <em>number</em> of wrong columns matches the cross count and
/// fails this.
/// </para>
/// </summary>
internal sealed record ContextExpectation(
    long Objects, long Crosses, long CrossIdSum, long ByteLength, string Sha256);

/// <summary>
/// Streams the expected native <c>.dat</c> for any family and returns its length and digest.
/// <para>
/// Every family's oracle differs in exactly one thing — which formal attributes an object crosses —
/// so that is the only thing each one supplies. The serialization is shared: native writer settings
/// (one-based ids, ascending, space-separated, LF, a trailing newline, no trailing spaces — §18.2),
/// spelled out here once rather than restated per family, where a slip would produce an expectation
/// that quietly agreed with a bug.
/// </para>
/// <para>
/// The bytes are <b>hashed while they are derived and never retained</b>, which is what lets the same
/// oracle serve a thousand-row case and a seventy-three-million-row one.
/// </para>
/// </summary>
internal static class DatExpectation
{
    /// <summary>
    /// Derives the expectation for <paramref name="objects"/> objects, taking each one's crossed
    /// formal-attribute ids from <paramref name="crossesOf"/>. The ids it returns must already be
    /// ascending and zero-based.
    /// </summary>
    public static ContextExpectation Stream(
        long objects, Func<long, IReadOnlyList<int>> crossesOf, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(objects);
        ArgumentNullException.ThrowIfNull(crossesOf);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var line = new StringBuilder(64);
        var buffer = new byte[512];
        var length = 0L;
        var crosses = 0L;
        var crossIdSum = 0L;

        for (var index = 0L; index < objects; index++)
        {
            if ((index & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var ids = crossesOf(index);
            crosses += ids.Count;

            line.Clear();
            for (var i = 0; i < ids.Count; i++)
            {
                if (i > 0)
                {
                    line.Append(' ');
                }

                crossIdSum += ids[i];
                line.Append((ids[i] + 1).ToString(CultureInfo.InvariantCulture));
            }

            line.Append('\n');
            length += Append(hash, line, ref buffer);
        }

        return new ContextExpectation(
            objects, crosses, crossIdSum, length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    /// <summary>
    /// The zero-based open-ended cut bin index for <paramref name="value"/>: the number of cuts at
    /// or below it. Spelled out here rather than borrowed from the discretizer, because borrowing
    /// the production helper would make every oracle agree with the code by construction.
    /// </summary>
    public static int BinIndex(double value, IReadOnlyList<int> cuts)
    {
        ArgumentNullException.ThrowIfNull(cuts);

        var index = 0;
        while (index < cuts.Count && cuts[index] <= value)
        {
            index++;
        }

        return index;
    }

    private static int Append(IncrementalHash hash, StringBuilder line, ref byte[] buffer)
    {
        var required = Encoding.UTF8.GetMaxByteCount(line.Length);
        if (buffer.Length < required)
        {
            buffer = new byte[required];
        }

        var written = 0;
        foreach (var chunk in line.GetChunks())
        {
            written += Encoding.UTF8.GetBytes(chunk.Span, buffer.AsSpan(written));
        }

        hash.AppendData(buffer, 0, written);
        return written;
    }
}
