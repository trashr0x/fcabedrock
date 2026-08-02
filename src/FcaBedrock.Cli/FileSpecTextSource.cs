using System.Text;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli;

/// <summary>
/// One file of the composed spec, as the run manifest needs it (§15 <c>[[run.spec_files]]</c>).
/// </summary>
/// <param name="FullPath">The resolved path — used for filesystem identity, never serialized.</param>
/// <param name="Spelling">
/// The authored spelling: the verbatim SPEC operand for the root, or the authored
/// referrer-relative <c>extends</c> reference for a base. Canonical identity keys and normalized
/// paths never enter the manifest (§13/§15).
/// </param>
/// <param name="Hash">The prefixed hash of the file's exact raw bytes.</param>
internal sealed record SpecChainFile(string FullPath, string Spelling, string Hash);

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
/// <para>
/// <b>Every file it loads is retained as a §15 chain fact</b> — the authored spelling, the
/// raw-bytes hash, and the resolved path — in load order, which is root-first-then-bases. The
/// hash covers the bytes <em>as read</em>, byte-order mark and original line endings included,
/// and is taken from the very read that produced the text: the run manifest never costs a
/// second open, and a chain file is never re-read merely to hash it (D-122 part 5).
/// </para>
/// </summary>
internal sealed class FileSpecTextSource : ISpecTextSource
{
    private readonly Func<string, Stream> _open;
    private readonly FileIdentity _identity;
    private readonly Dictionary<FileIdentityKey, string> _canonicalKeys = [];
    private readonly Dictionary<string, string> _resolutionPaths = new(StringComparer.Ordinal);
    private readonly List<SpecChainFile> _chain = [];
    private readonly HashSet<string> _recorded = new(StringComparer.Ordinal);

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
        return ReadAs(path, path);
    }

    /// <summary>
    /// Every file loaded through this host, in load order — the root first, then each base as
    /// the chain was walked, which is exactly §15's <c>[[run.spec_files]]</c> order. A single
    /// file leaves one entry, and the caller decides that no chain means no section.
    /// </summary>
    public IReadOnlyList<SpecChainFile> Chain => _chain;

    // One read serves both the text and the hash; `spelling` is what the manifest records, so
    // it is the caller's authored form — the SPEC operand for the root, the authored
    // referrer-relative reference for a base — never the resolved path.
    private string ReadAs(string path, string spelling)
    {
        byte[] bytes;
        using (var stream = _open(path))
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        var text = SpecTextDecoding.Decode(bytes);

        // Recorded only after a successful decode: a file that is not a readable spec is not a
        // chain fact. The hash is over the raw bytes, before the byte-order mark is consumed.
        var fullPath = Path.GetFullPath(path);
        if (_recorded.Add(fullPath))
        {
            _chain.Add(new SpecChainFile(fullPath, spelling, ContentHash.Of(bytes)));
        }

        return text;
    }

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
            // The authored reference is what §15 records, so the chain entry carries `reference`
            // verbatim while the read itself uses the resolved path.
            toml = ReadAs(resolved, reference);
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
