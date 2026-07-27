using System.Security.Cryptography;

namespace FcaBedrock.Cli;

/// <summary>
/// The one spelling of a raw-bytes hash in the CLI (§15): the literal <c>sha256:</c> followed by
/// 64 <b>lowercase</b> hexadecimal digits, over the exact bytes as they were read or written —
/// byte-order mark, original line endings, and non-ASCII sequences all included, with nothing
/// decoded, normalized, or canonicalized first.
/// <para>
/// It is a raw-content digest and deliberately unrelated to the plan-derived fingerprints
/// (§14/D-053): those hash a canonical JSON projection, this hashes a file.
/// </para>
/// </summary>
internal static class ContentHash
{
    /// <summary>The algorithm prefix every manifest hash field carries.</summary>
    public const string Prefix = "sha256:";

    /// <summary>The prefixed digest of <paramref name="bytes"/>.</summary>
    public static string Of(ReadOnlySpan<byte> bytes) => Format(Convert.ToHexStringLower(SHA256.HashData(bytes)));

    /// <summary>Prefixes an already-computed 64-character lowercase hex digest.</summary>
    public static string Format(string lowercaseHex)
    {
        ArgumentNullException.ThrowIfNull(lowercaseHex);
        return Prefix + lowercaseHex;
    }
}
