using System.Text;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli;

/// <summary>
/// The file-backed <see cref="ISpecTextSource"/> — the host work D-078 reserved for M7,
/// realizing §13 / D-122 part 11.
/// <para>
/// <b>Two different things, kept apart.</b> A file's <em>identity</em> is a
/// <see cref="FileIdentityKey"/>: opaque, OS-derived where available, and the only thing
/// that decides whether two spellings are the same file. A file's <em>canonical key</em>
/// is what <see cref="SpecComposer"/> receives, and it is a readable full path, because
/// the composer hands it straight to <see cref="SpecReader"/> as the diagnostic
/// <c>file</c> location and embeds it in the <c>SpecExtendsNotFound</c> /
/// <c>SpecExtendsCycle</c> messages — an identity key there would surface as a
/// user-facing "path" and would have to be rewritten out of library diagnostics, which
/// the CLI must never do. The first spelling seen for an identity becomes that identity's
/// canonical key, so every later alias resolves to it and the composer's ordinal
/// comparison detects the revisit as a cycle.
/// </para>
/// <para>
/// <b>Resolution paths are retained separately</b> from canonical keys, so a relative
/// <c>extends</c> is always resolved against the directory of the file that authored it —
/// multilevel chains stay referrer-relative — and no key is ever implicitly reinterpreted
/// as a path.
/// </para>
/// <para>
/// Authored references stay relative-only: an absolute reference is not resolved and
/// takes the established not-found outcome (D-122 part 11), as does a base that cannot be
/// read. Authored path text is never normalized or rewritten here.
/// </para>
/// </summary>
internal sealed class FileSpecTextSource : ISpecTextSource
{
    // throwOnInvalidBytes: an ill-formed sequence must fail the read, not become U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Func<string, Stream> _open;
    private readonly FileIdentity _identity;
    private readonly Dictionary<FileIdentityKey, string> _canonicalKeys = [];
    private readonly Dictionary<string, string> _resolutionPaths = new(StringComparer.Ordinal);

    /// <summary>Creates a host reading through <paramref name="open"/> and identifying through <paramref name="identity"/>.</summary>
    public FileSpecTextSource(Func<string, Stream> open, FileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(identity);
        _open = open;
        _identity = identity;
    }

    /// <summary>
    /// Registers the root spec operand and returns its canonical key — the key
    /// <see cref="SpecReader.Read(string, string?)"/> and
    /// <see cref="SpecComposer.Compose"/> must both be given, so the root participates in
    /// the same identity space as every base and cannot be re-entered under an alias.
    /// </summary>
    public string RegisterRoot(string operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return Canonicalize(Path.GetFullPath(operand));
    }

    /// <summary>
    /// Reads the text of <paramref name="path"/> as <b>strict UTF-8</b>. §2 is explicit —
    /// "a Bedrock spec is a UTF-8 TOML 1.0 document" — so encoding is part of the format
    /// boundary, not a convenience: a UTF-16/UTF-32 byte-order mark is rejected rather than
    /// silently transcoded, and a malformed byte sequence is rejected rather than repaired
    /// into U+FFFD, which would change authored content with no diagnostic. An optional
    /// UTF-8 BOM is consumed.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes carry a non-UTF-8 byte-order mark.</exception>
    /// <exception cref="DecoderFallbackException">The bytes are not valid UTF-8.</exception>
    public string ReadText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes;
        using (var stream = _open(path))
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        return Decode(bytes);
    }

    // Strict decoding, spelled out here because the convenient overloads are all permissive:
    // StreamReader's BOM detection also recognizes UTF-16 and UTF-32, and the one-argument
    // UTF8Encoding replaces invalid bytes instead of rejecting them.
    private static string Decode(byte[] bytes)
    {
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

    /// <inheritdoc/>
    public SpecSourceText? Load(string reference, string referrerKey)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(referrerKey);

        if (reference.Length == 0 || Path.IsPathRooted(reference))
        {
            return null;
        }

        if (!_resolutionPaths.TryGetValue(referrerKey, out var referrerPath))
        {
            return null;
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(referrerPath) ?? ".", reference));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        string toml;
        try
        {
            toml = ReadText(resolved);
        }
        catch (PathTooLongException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            // A non-UTF-8 byte-order mark. InvalidDataException derives from SystemException,
            // not IOException, so it needs its own clause.
            return null;
        }
        catch (DecoderFallbackException)
        {
            // Malformed UTF-8: unreadable as a spec, so it takes the established
            // unreadable-base outcome (SpecExtendsNotFound) rather than escaping as a fault.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        return new SpecSourceText(Canonicalize(resolved), toml);
    }

    private string Canonicalize(string fullPath)
    {
        var identity = _identity.KeyFor(fullPath);
        if (_canonicalKeys.TryGetValue(identity, out var existing))
        {
            // A second spelling of a file already in this chain: return the FIRST
            // spelling's key so the composer sees a revisited key and reports the cycle.
            // The original resolution path is kept, which matters because that file's own
            // relative references were already resolved against it.
            return existing;
        }

        _canonicalKeys[identity] = fullPath;
        _resolutionPaths[fullPath] = fullPath;
        return fullPath;
    }
}
