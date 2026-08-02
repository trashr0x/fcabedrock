using System.Text;

namespace FcaBedrock.Cli;

/// <summary>
/// The one decoder for authored document text the CLI reads: a TOML spec (§2 — "a Bedrock
/// spec is a UTF-8 TOML 1.0 document") and a v2 <c>.bed</c> alike.
/// <para>
/// <b>Strict UTF-8, with no byte-order mark or exactly one leading UTF-8 mark.</b> A UTF-16
/// or UTF-32 mark is rejected rather than silently transcoded, and a malformed sequence is
/// rejected rather than repaired into <c>U+FFFD</c>: a repair would change authored content
/// with no diagnostic, and that content becomes attribute names, domain values, and labels.
/// Exactly one leading mark is consumed — a second consecutive mark is ordinary content.
/// </para>
/// <para>
/// Spelled out here because the convenient overloads are all permissive:
/// <see cref="StreamReader"/>'s mark detection also recognizes UTF-16 and UTF-32, and the
/// one-argument <see cref="UTF8Encoding"/> replaces invalid bytes instead of rejecting them.
/// One concern, one owner (P-5): no other decoding policy exists in the CLI.
/// </para>
/// </summary>
internal static class SpecTextDecoding
{
    // throwOnInvalidBytes: an ill-formed sequence must fail the read, not become U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Opens <paramref name="path"/> through <paramref name="open"/>, reads it whole, and
    /// decodes it. Opening stays the caller's seam (D-123 part 5); nothing here reaches the
    /// filesystem on its own.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes carry a non-UTF-8 byte-order mark.</exception>
    /// <exception cref="DecoderFallbackException">The bytes are not valid UTF-8.</exception>
    public static string ReadAllText(Func<string, Stream> open, string path)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(path);

        using var stream = open(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Decode(buffer.ToArray());
    }

    /// <summary>Decodes <paramref name="bytes"/> under the policy above.</summary>
    /// <exception cref="InvalidDataException">The bytes carry a non-UTF-8 byte-order mark.</exception>
    /// <exception cref="DecoderFallbackException">The bytes are not valid UTF-8.</exception>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (StartsWith(bytes, [0x00, 0x00, 0xFE, 0xFF])
            || StartsWith(bytes, [0xFF, 0xFE, 0x00, 0x00])
            || StartsWith(bytes, [0xFE, 0xFF])
            || StartsWith(bytes, [0xFF, 0xFE]))
        {
            throw new InvalidDataException("The spec is not UTF-8: it carries a UTF-16 or UTF-32 byte-order mark.");
        }

        var offset = StartsWith(bytes, [0xEF, 0xBB, 0xBF]) ? 3 : 0;
        return StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
    }

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> prefix) =>
        bytes.AsSpan().StartsWith(prefix);
}
