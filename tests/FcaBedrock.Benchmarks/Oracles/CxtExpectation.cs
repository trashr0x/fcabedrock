using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// Streams the expected Burmeister <c>.cxt</c> for a family and returns its length and digest.
/// <para>
/// The layout is §18.1's, spelled out here independently of the writer: the <c>B</c> magic, a blank
/// line, the object and formal-attribute counts, another blank line, every object name, every
/// formal-attribute name, and then one fixed-width row of <c>X</c> and <c>.</c> per object, with a
/// trailing newline after the last row. Native settings throughout — LF endings and the trailing
/// newline the default preset emits.
/// </para>
/// <para>
/// Like the <c>.dat</c> expectation it hashes as it goes and retains nothing, so a case is limited by
/// what its <em>writer</em> can stream rather than by what its oracle can hold. The one thing it does
/// hold is the object-name and attribute-name lists, which the format itself requires the writer to
/// hold too (§18.1, P-16: bounded metadata is allowed; the matrix is not).
/// </para>
/// </summary>
internal static class CxtExpectation
{
    /// <summary>
    /// Derives the expectation for a context of <paramref name="objects"/> objects over
    /// <paramref name="attributeNames"/>, taking each object's name and ascending zero-based crossed
    /// ids from the supplied functions.
    /// </summary>
    public static ContextExpectation Stream(
        long objects,
        IReadOnlyList<string> attributeNames,
        Func<long, string> objectName,
        Func<long, IReadOnlyList<int>> crossesOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(objects);
        ArgumentNullException.ThrowIfNull(attributeNames);
        ArgumentNullException.ThrowIfNull(objectName);
        ArgumentNullException.ThrowIfNull(crossesOf);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = 0L;
        var crosses = 0L;
        var crossIdSum = 0L;

        var header = new StringBuilder();
        header.Append("B\n\n");
        header.Append(objects.ToString(CultureInfo.InvariantCulture)).Append('\n');
        header.Append(attributeNames.Count.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
        length += Append(hash, header.ToString());

        for (var index = 0L; index < objects; index++)
        {
            if ((index & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            length += Append(hash, objectName(index) + "\n");
        }

        foreach (var name in attributeNames)
        {
            length += Append(hash, name + "\n");
        }

        var row = new char[attributeNames.Count + 1];
        row[^1] = '\n';
        for (var index = 0L; index < objects; index++)
        {
            if ((index & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Array.Fill(row, '.', 0, attributeNames.Count);
            var ids = crossesOf(index);
            crosses += ids.Count;
            foreach (var id in ids)
            {
                crossIdSum += id;
                row[id] = 'X';
            }

            length += Append(hash, new string(row));
        }

        return new ContextExpectation(
            objects, crosses, crossIdSum, length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    // Every character this format emits is ASCII by construction for the families that use it —
    // digits, 'X', '.', and generated attribute names — but the length is measured in UTF-8 bytes
    // regardless, so a name that ever carried a non-ASCII character would still be counted right.
    private static int Append(IncrementalHash hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        hash.AppendData(bytes);
        return bytes.Length;
    }
}
