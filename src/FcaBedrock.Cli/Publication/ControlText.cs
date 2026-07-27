using System.Text;

namespace FcaBedrock.Cli.Publication;

/// <summary>
/// The canonical body every control whose <em>role</em> is its whole meaning carries: the intent
/// descriptor, each phase marker, and each stage claim.
/// <para>
/// These used to be zero-byte files whose existence was the entire signal. That could not survive
/// the empty-occupant race: an empty file put at a control's name refuses this transaction's
/// create-new, and afterwards nothing distinguishes it from the control this run would have made
/// there — so a resumed run removed it on a length. Nor could it survive an empty object simply
/// <em>replacing</em> an acknowledged claim, which would then be believed as authority over the
/// stage beside it (CX-M7H-046). Binding the run token, the base, the authoritative record's
/// digest, the exact role, and — for a claim — the identity it acknowledges into fixed bytes makes
/// the question answerable: discovery and removal require <b>these exact bytes</b>, and an occupant
/// that is empty, partial, or bound to anything else is preserved.
/// </para>
/// <para>
/// This supersedes CX-M7H-029's zero-byte mechanism. It is a statement about the transaction, not
/// about the object, so it is no stronger than the authoritative record's own byte proof — an actor
/// who can copy the record can copy this too. It is exactly strong enough for what it must decide,
/// and no weaker than the proof the record already relies on.
/// </para>
/// </summary>
internal static class ControlDocument
{
    /// <summary>The role a phase marker carries — the phase's own name.</summary>
    public static string RoleOf(TransactionPhase phase) => PublicationTargets.PhaseName(phase);

    /// <summary>
    /// The role a stage claim carries. The target kind is part of it, so a claim written for one
    /// artifact can never be read as the claim for another.
    /// </summary>
    public static string ClaimRole(PublicationTargetKind kind) => "claim-" + PublicationTargets.KindCode(kind);

    /// <summary>The role the intent descriptor carries.</summary>
    public const string IntentRole = "intent";

    /// <summary>
    /// The exact bytes a control in <c>role</c> holds for this transaction: the run token, the
    /// base, the role itself, the authoritative record's digest, and — where the control
    /// acknowledges an object, as a stage claim does — that object's identity digest. Null
    /// <c>acknowledged</c> is a control that acknowledges nothing but the transaction itself.
    /// </summary>
    public static byte[] Bytes(
        string token, string baseFileName, string role, string recordDigest, string? acknowledged = null)
    {
        var text = new StringBuilder();
        text.Append("version = 1").Append('\n');
        ControlText.AppendKey(text, "token", token);
        ControlText.AppendKey(text, "base", baseFileName);
        ControlText.AppendKey(text, "control", role);
        ControlText.AppendKey(text, "record", recordDigest);

        if (acknowledged is not null)
        {
            ControlText.AppendKey(text, "stage", acknowledged);
        }

        return ControlText.Utf8NoBom.GetBytes(text.ToString());
    }

    /// <summary>Whether <paramref name="bytes"/> is exactly that control, and nothing else.</summary>
    public static bool Matches(
        byte[]? bytes,
        string token,
        string baseFileName,
        string role,
        string recordDigest,
        string? acknowledged = null) =>
        bytes is not null
        && bytes.AsSpan().SequenceEqual(Bytes(token, baseFileName, role, recordDigest, acknowledged));
}

/// <summary>
/// The text conventions the private control documents share — the transaction record and the
/// per-target identity evidence.
/// <para>
/// <b>One reader, one writer, one strictness.</b> Both documents are validated by the same
/// decisive rule: re-formatting the fields parsed out of a file must reproduce its decoded text
/// exactly. That only proves anything if both use the same escaping, the same line discipline,
/// and the same closed-set matching — so those live here rather than being spelled twice
/// (P-5).
/// </para>
/// <para>
/// The format is deliberately trivial: LF-terminated <c>key = "value"</c> lines, UTF-8 without a
/// byte-order mark, nothing conditional. It is private state, never an artifact, and no library
/// ever reads it.
/// </para>
/// </summary>
internal static class ControlText
{
    /// <summary>The one encoding every control document is written in.</summary>
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The exact text of a <c>key = "value"</c> line, without its terminator.</summary>
    public static string KeyLine(string key, string value)
    {
        var line = new StringBuilder(key).Append(" = ");
        JsonStringEscaping.AppendLiteral(line, value);
        return line.ToString();
    }

    /// <summary>Appends a complete <c>key = "value"</c> line, terminator included.</summary>
    public static void AppendKey(StringBuilder text, string key, string value) =>
        text.Append(KeyLine(key, value)).Append('\n');

    /// <summary>
    /// Decodes <paramref name="bytes"/> as the strict UTF-8 a control document is written in, or
    /// returns false. An ill-formed sequence, a byte-order mark, or a carriage return is not
    /// something this writer produces, so it is not one of its documents.
    /// </summary>
    public static bool TryDecode(byte[] bytes, out string text)
    {
        text = string.Empty;
        if (bytes.AsSpan().StartsWith([(byte)0xEF, (byte)0xBB, (byte)0xBF]))
        {
            return false;
        }

        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return !text.Contains('\r', StringComparison.Ordinal);
    }

    /// <summary>Consumes the line at <paramref name="index"/> when it is exactly <paramref name="expected"/>.</summary>
    public static bool Take(string[] lines, ref int index, string expected)
    {
        if (index >= lines.Length || !string.Equals(lines[index], expected, StringComparison.Ordinal))
        {
            return false;
        }

        index++;
        return true;
    }

    /// <summary>
    /// Consumes the line at <paramref name="index"/> when it equals <c>key = "candidate"</c> for
    /// exactly one candidate, and returns that candidate — so a value is <b>selected from a closed
    /// set</b> rather than unescaped out of the file.
    /// </summary>
    public static string? Match(string[] lines, ref int index, string key, IReadOnlyList<string> candidates)
    {
        if (index >= lines.Length)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (string.Equals(lines[index], KeyLine(key, candidate), StringComparison.Ordinal))
            {
                index++;
                return candidate;
            }
        }

        return null;
    }
}
