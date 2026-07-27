using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Cli.Publication;

/// <summary>
/// The identity digest of one filesystem object, as the transaction records it (CX-M7H-019/024).
/// <para>
/// <b>Why a digest and not the identity itself.</b> A raw volume/inode triple is an internal key
/// that must never surface (§13, D-122 part 11); a salted digest proves the same equality without
/// carrying the numbers anywhere. Salting with the run token, the role, and the target file name
/// binds each value to exactly one entry of exactly one transaction, so evidence can never be
/// transplanted between entries or runs.
/// </para>
/// <para>
/// 128 bits, matching the transaction token and the intent digest: this value authorizes deleting
/// or replacing a file, so its collision resistance is a safety property, not a formatting choice.
/// </para>
/// </summary>
internal static class IdentityEvidence
{
    /// <summary>The value meaning "this entry does not exist for this target".</summary>
    public const string NotApplicable = "none";

    /// <summary>The value meaning "the host reported no filesystem identity for this object".</summary>
    public const string Unavailable = "unknown";

    /// <summary>The digest length in characters — 128 bits as lowercase hex.</summary>
    public const int Length = 32;

    /// <summary>
    /// The digest of <paramref name="key"/> for <paramref name="role"/> on
    /// <paramref name="targetFileName"/>, or <see cref="Unavailable"/> when the platform reported
    /// no OS identity — never a path fallback, which identifies a <em>name</em> and would prove
    /// nothing about the object at it.
    /// </summary>
    public static string Of(string token, string role, string targetFileName, FileIdentityKey? key)
    {
        if (key is not { } identity || !identity.TryGetOperatingSystemIdentity(out var volume, out var low, out var high))
        {
            return Unavailable;
        }

        var salt = ControlText.Utf8NoBom.GetBytes(token + "\n" + role + "\n" + targetFileName + "\n");
        var material = new byte[salt.Length + 24];
        salt.CopyTo(material, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(material.AsSpan(salt.Length, 8), volume);
        BinaryPrimitives.WriteUInt64LittleEndian(material.AsSpan(salt.Length + 8, 8), low);
        BinaryPrimitives.WriteUInt64LittleEndian(material.AsSpan(salt.Length + 16, 8), high);

        return Convert.ToHexStringLower(SHA256.HashData(material).AsSpan(0, Length / 2));
    }

    /// <summary>True when <paramref name="value"/> identifies an object rather than naming its absence.</summary>
    public static bool IsIdentity(string value) =>
        value.Length == Length && PublicationTargets.IsHex(value);
}

/// <summary>
/// One target's durable identity evidence: which object this transaction renamed aside as a
/// backup, and which object it staged (CX-M7H-019/023/024).
/// <para>
/// <b>Why it exists.</b> A derived file name identifies a <em>path</em>. Rollback and recovery
/// must delete a published final only when it is the very object this transaction staged, and
/// restore a backup only when it is the very object this transaction renamed aside — a
/// substituted, restored, or newly appeared file at the same path is not that object. Since a
/// rename preserves a file's OS identity, the identity captured from the stage's own open handle
/// is exactly the proof that survives the commit rename.
/// </para>
/// <para>
/// <b>Fixed path, atomic publication.</b> Its name is a function of the base, the role, and the
/// run token alone — never of a runtime identity value — so the complete set of control paths is
/// resolvable and collision-checkable <em>before</em> the transaction begins. The bytes are
/// written under a pending name and published with one non-overwriting rename, so the
/// authoritative name never holds a partial encoding; an interrupted pending file is removed only
/// because the already-validated record authorizes that exact derived path.
/// </para>
/// </summary>
internal sealed class StageEvidence
{
    private const string VersionLine = "version = 1";

    private StageEvidence(string token, string baseFileName, string targetFileName, string backup, string stage)
    {
        Token = token;
        BaseFileName = baseFileName;
        TargetFileName = targetFileName;
        Backup = backup;
        Stage = stage;
    }

    /// <summary>The run token this evidence belongs to.</summary>
    public string Token { get; }

    /// <summary>The base file name whose namespace this evidence lives in.</summary>
    public string BaseFileName { get; }

    /// <summary>The target this evidence describes.</summary>
    public string TargetFileName { get; }

    /// <summary>The pre-existing target renamed aside, <c>none</c>, or <c>unknown</c>.</summary>
    public string Backup { get; }

    /// <summary>The staged object, <c>none</c>, or <c>unknown</c>.</summary>
    public string Stage { get; }

    /// <summary>Creates the evidence a transaction will publish for one target.</summary>
    public static StageEvidence Create(
        string token, string baseFileName, string targetFileName, string backup, string stage) =>
        new(token, baseFileName, targetFileName, backup, stage);

    /// <summary>The evidence's exact text: LF-terminated lines, no BOM, nothing conditional.</summary>
    public string Format()
    {
        var text = new StringBuilder();
        text.Append(VersionLine).Append('\n');
        ControlText.AppendKey(text, "token", Token);
        ControlText.AppendKey(text, "base", BaseFileName);
        ControlText.AppendKey(text, "target", TargetFileName);
        ControlText.AppendKey(text, "backup", Backup);
        ControlText.AppendKey(text, "stage", Stage);
        return text.ToString();
    }

    /// <summary>The evidence's bytes: UTF-8, no byte-order mark.</summary>
    public byte[] ToBytes() => ControlText.Utf8NoBom.GetBytes(Format());

    /// <summary>
    /// Reads <paramref name="bytes"/> as the evidence of <paramref name="expectedToken"/> for
    /// <paramref name="expectedTargetFileName"/>, or returns null when it is anything else. The
    /// expectations come from the <b>file name</b> and the validated record, so a file whose
    /// content claims another token, base, or target is refused rather than believed.
    /// </summary>
    public static StageEvidence? TryParse(
        byte[] bytes, string expectedToken, string expectedBaseFileName, string expectedTargetFileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (!ControlText.TryDecode(bytes, out var text))
        {
            return null;
        }

        var lines = text.Split('\n');
        var index = 0;

        if (!ControlText.Take(lines, ref index, VersionLine)
            || !ControlText.Take(lines, ref index, ControlText.KeyLine("token", expectedToken))
            || !ControlText.Take(lines, ref index, ControlText.KeyLine("base", expectedBaseFileName))
            || !ControlText.Take(lines, ref index, ControlText.KeyLine("target", expectedTargetFileName)))
        {
            return null;
        }

        if (ReadValue(lines, ref index, "backup") is not { } backup
            || ReadValue(lines, ref index, "stage") is not { } stage)
        {
            return null;
        }

        // The trailing element of the split is the text after the final LF; anything there means
        // the file did not end where the writer ends one.
        if (index != lines.Length - 1 || lines[^1].Length != 0)
        {
            return null;
        }

        var evidence = new StageEvidence(expectedToken, expectedBaseFileName, expectedTargetFileName, backup, stage);

        // Decisive: the bytes must be exactly what this writer would produce for the fields read.
        return string.Equals(evidence.Format(), text, StringComparison.Ordinal) ? evidence : null;
    }

    /// <summary>
    /// Whether this evidence agrees with what <paramref name="record"/> says the transaction owns:
    /// an entry has a real value or <c>unknown</c>, and an entry the record does not hold is
    /// <c>none</c>. A file that claims a backup the record never reserved authorizes nothing.
    /// </summary>
    public bool Matches(TransactionRecord record, string directory)
    {
        ArgumentNullException.ThrowIfNull(record);

        var hasBackup = record.BackupOf(directory, TargetFileName) is not null;
        var hasStage = false;
        foreach (var entry in record.Files)
        {
            hasStage |= string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal)
                && string.Equals(entry.TargetFileName, TargetFileName, StringComparison.Ordinal);
        }

        return hasBackup != string.Equals(Backup, IdentityEvidence.NotApplicable, StringComparison.Ordinal)
            && hasStage != string.Equals(Stage, IdentityEvidence.NotApplicable, StringComparison.Ordinal);
    }

    // `none`, `unknown`, or a 128-bit lowercase-hex digest — a closed set, so no value is ever
    // unescaped out of the file.
    private static string? ReadValue(string[] lines, ref int index, string key)
    {
        if (index >= lines.Length)
        {
            return null;
        }

        var line = lines[index];
        foreach (var fixedValue in new[] { IdentityEvidence.NotApplicable, IdentityEvidence.Unavailable })
        {
            if (string.Equals(line, ControlText.KeyLine(key, fixedValue), StringComparison.Ordinal))
            {
                index++;
                return fixedValue;
            }
        }

        var prefix = ControlText.KeyLine(key, string.Empty);
        var opening = prefix[..^1];
        if (line.Length != opening.Length + IdentityEvidence.Length + 1
            || !line.StartsWith(opening, StringComparison.Ordinal)
            || line[^1] != '"')
        {
            return null;
        }

        var value = line[opening.Length..^1];
        if (!IdentityEvidence.IsIdentity(value))
        {
            return null;
        }

        index++;
        return value;
    }
}
