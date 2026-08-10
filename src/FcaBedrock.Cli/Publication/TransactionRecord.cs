using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Cli.Publication;

/// <summary>
/// One file the transaction owns: which target it belongs to, and in which role.
/// <para>
/// The private path is <b>not stored</b> — it is recomputed from the target, the role, and the
/// run token (<see cref="PublicationTargets.PrivateName"/>). A record therefore cannot name a
/// path at all; it can only select from the fixed set of names this code could have produced
/// for this base in this directory, which is what makes recovery provably safe.
/// </para>
/// </summary>
/// <param name="Role">
/// <see cref="PublicationTargets.StageRole"/> or <see cref="PublicationTargets.BackupRole"/>.
/// </param>
/// <param name="TargetFileName">One of this base's three artifacts targets, or, in the single-file
/// family, the base file name itself — nothing else parses, so no path can be injected.</param>
internal sealed record TransactionFileEntry(string Role, string TargetFileName);

/// <summary>
/// The private transaction record (D-123 point 7): the durable statement of what one
/// publication attempt owns, created <b>create-new before any owned residue can exist</b> and
/// removed only after the run's commit point or a complete rollback.
/// <para>
/// <b>Validity is byte-reconstruction <em>and</em> a reachable transaction shape.</b> A
/// discovered file is a record of this run's base only if (1) re-formatting the entries parsed
/// out of it reproduces its decoded text exactly, and (2) those entries describe a transaction
/// this implementation could actually have created. Canonical spelling alone is not ownership
/// evidence: a byte-perfect file can still claim a combination no run produces — an empty
/// selection, a manifest staged with no artifact, backups in the wrong order, a backup for an
/// artifact that is not being published. Such a file authorizes nothing, is left byte-identical,
/// and refuses the run.
/// </para>
/// <para>
/// <b>It is private state, not an artifact.</b> Its token, its paths, and its serialization
/// never enter output bytes, a fingerprint, or the run manifest, and no library ever reads it.
/// The <c>.toml</c> suffix is the ruled file name (D-123), not a claim on the canonical TOML
/// contract the spec writer owns — this is a fixed six-line-per-entry private format written
/// and validated only here.
/// </para>
/// </summary>
internal sealed class TransactionRecord
{
    /// <summary>The bit separating the families in a shape code — the first the artifacts family,
    /// which uses the six low bits, can never set (D-123 point 7).</summary>
    internal const int FamilyBit = 0x40;

    private const string VersionLine = "version = 1";
    private const string FileHeader = "[[file]]";

    private const int SingleStageBit = 1;
    private const int SingleBackupBit = 1 << 1;

    private TransactionRecord(string token, string baseFileName, IReadOnlyList<TransactionFileEntry> files)
    {
        Token = token;
        BaseFileName = baseFileName;
        Files = files;
    }

    /// <summary>The run token this record belongs to.</summary>
    public string Token { get; }

    /// <summary>The base file name whose namespace this record owns.</summary>
    public string BaseFileName { get; }

    /// <summary>The owned files, in the order the transaction declared them.</summary>
    public IReadOnlyList<TransactionFileEntry> Files { get; }

    /// <summary>
    /// Every target this transaction touches, first mention first — the union of its staged finals
    /// and any target it merely demotes, such as the old manifest of a <c>--no-manifest</c> run.
    /// </summary>
    public IEnumerable<string> Targets
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Files)
            {
                if (seen.Add(entry.TargetFileName))
                {
                    yield return entry.TargetFileName;
                }
            }
        }
    }

    /// <summary>
    /// The backup path this transaction reserved for <paramref name="targetFileName"/>, or
    /// <see langword="null"/> when it reserved none.
    /// <para>
    /// A reserved backup is the record's durable statement that the target <b>pre-existed</b> at
    /// preflight, which is what lets rollback decide between restoring old bytes and removing a
    /// file this run introduced — without inspecting anything it cannot trust.
    /// </para>
    /// </summary>
    public string? BackupOf(string directory, string targetFileName)
    {
        foreach (var entry in Files)
        {
            if (string.Equals(entry.Role, PublicationTargets.BackupRole, StringComparison.Ordinal)
                && string.Equals(entry.TargetFileName, targetFileName, StringComparison.Ordinal))
            {
                return PathOf(directory, entry);
            }
        }

        return null;
    }

    /// <summary>
    /// The private path of <paramref name="entry"/> inside <paramref name="directory"/> —
    /// derived, never read from the record.
    /// </summary>
    public string PathOf(string directory, TransactionFileEntry entry) =>
        Path.Combine(directory, PublicationTargets.PrivateName(entry.TargetFileName, entry.Role, Token));

    /// <summary>The absolute path of <paramref name="entry"/>'s target inside <paramref name="directory"/>.</summary>
    public static string TargetPathOf(string directory, TransactionFileEntry entry) =>
        Path.Combine(directory, entry.TargetFileName);

    /// <summary>Creates the record a fresh transaction will write.</summary>
    public static TransactionRecord Create(
        string token, string baseFileName, IReadOnlyList<TransactionFileEntry> files) =>
        new(token, baseFileName, files);

    /// <summary>
    /// Which family this record belongs to, derived from its own entries — never stored, so no
    /// record's emitted text changes and no version moves (D-123 point 7). It is total: a
    /// single-file target's file name <em>is</em> the base file name and an artifact target's
    /// never can be, and <see cref="IsReachable"/> rejects a record that mixes them or names
    /// neither — so every record that parses has the family of its first entry.
    /// </summary>
    public PublicationFamily Family =>
        Files.Count > 0 && KindOf(Files[0].TargetFileName, BaseFileName) is { } kind
            ? PublicationTargets.FamilyOf(kind)
            : PublicationFamily.Artifacts;

    /// <summary>
    /// The code naming this record's shape: which targets it stages, and which it backs up. It is
    /// what the pre-record intent descriptor carries — the entry <em>order</em> needs
    /// no encoding, because each family only ever writes one arrangement: an artifacts transaction
    /// writes backups in <see cref="PublicationTargets.BackupOrder"/>, then stages in
    /// <see cref="PublicationTargets.CommitOrder"/>; a single-file one writes its one optional
    /// backup, then its required stage.
    /// The families occupy <b>disjoint</b> ranges: artifacts shapes are the six legacy bits
    /// <c>0x00</c>–<c>0x3F</c>, unmoved, and a single-file shape sets <see cref="FamilyBit"/> plus
    /// its own two, so only <c>0x41</c> and <c>0x43</c> are reachable there.
    /// </summary>
    public int ShapeCode
    {
        get
        {
            if (Family == PublicationFamily.Single)
            {
                var single = FamilyBit;
                foreach (var entry in Files)
                {
                    var stage = string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal);
                    single |= stage ? SingleStageBit : SingleBackupBit;
                }

                return single;
            }

            var code = 0;
            foreach (var entry in Files)
            {
                if (KindOf(entry.TargetFileName, BaseFileName) is not { } kind)
                {
                    continue;
                }

                var bit = 1 << Array.IndexOf(PublicationTargets.CommitOrder, kind);
                code |= string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal)
                    ? bit
                    : bit << PublicationTargets.CommitOrder.Length;
            }

            return code;
        }
    }

    /// <summary>
    /// The 128-bit digest of this record's exact bytes, as lowercase hex — the second half of the
    /// intent descriptor, which is what makes that descriptor self-validating: a name whose digest
    /// disagrees with its own shape, token, and base is not one this code wrote.
    /// </summary>
    public string Digest => Convert.ToHexStringLower(SHA256.HashData(ToBytes()).AsSpan(0, 16));

    /// <summary>The record's exact text: LF-terminated lines, no BOM, nothing conditional.</summary>
    public string Format()
    {
        var text = new StringBuilder();
        text.Append(VersionLine).Append('\n');
        ControlText.AppendKey(text, "token", Token);
        ControlText.AppendKey(text, "base", BaseFileName);

        foreach (var entry in Files)
        {
            text.Append('\n').Append(FileHeader).Append('\n');
            ControlText.AppendKey(text, "role", entry.Role);
            ControlText.AppendKey(text, "target", entry.TargetFileName);
        }

        return text.ToString();
    }

    /// <summary>The record's bytes: UTF-8, no byte-order mark.</summary>
    public byte[] ToBytes() => ControlText.Utf8NoBom.GetBytes(Format());

    /// <summary>
    /// The record <paramref name="shape"/> describes for <paramref name="baseFileName"/>, or null
    /// when that code names no transaction this implementation could have created.
    /// </summary>
    public static TransactionRecord? FromShape(string token, string baseFileName, int shape)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(baseFileName);

        // Disjoint ranges, so the family bit selects the decoding; IsReachable gates both.
        if (shape >= 0 && (shape & FamilyBit) != 0)
        {
            var single = SingleShape(baseFileName, shape);
            return single is not null && IsReachable(single, baseFileName)
                ? new TransactionRecord(token, baseFileName, single)
                : null;
        }

        var order = PublicationTargets.CommitOrder;
        if (shape < 0 || shape >= 1 << (order.Length * 2))
        {
            return null;
        }

        var files = new List<TransactionFileEntry>();
        foreach (var kind in PublicationTargets.BackupOrder)
        {
            if ((shape & (1 << (Array.IndexOf(order, kind) + order.Length))) != 0)
            {
                files.Add(new TransactionFileEntry(
                    PublicationTargets.BackupRole, baseFileName + PublicationTargets.Extension(kind)));
            }
        }

        foreach (var kind in order)
        {
            if ((shape & (1 << Array.IndexOf(order, kind))) != 0)
            {
                files.Add(new TransactionFileEntry(
                    PublicationTargets.StageRole, baseFileName + PublicationTargets.Extension(kind)));
            }
        }

        return IsReachable(files, baseFileName) ? new TransactionRecord(token, baseFileName, files) : null;
    }

    // Any bit outside this family's own three describes nothing this code writes.
    private static List<TransactionFileEntry>? SingleShape(string baseFileName, int shape)
    {
        if ((shape & ~(FamilyBit | SingleStageBit | SingleBackupBit)) != 0)
        {
            return null;
        }

        var target = baseFileName + PublicationTargets.Extension(PublicationTargetKind.Single);
        var files = new List<TransactionFileEntry>();
        if ((shape & SingleBackupBit) != 0)
        {
            files.Add(new TransactionFileEntry(PublicationTargets.BackupRole, target));
        }

        if ((shape & SingleStageBit) != 0)
        {
            files.Add(new TransactionFileEntry(PublicationTargets.StageRole, target));
        }

        return files;
    }

    /// <summary>
    /// Reads <paramref name="bytes"/> as the record of <paramref name="expectedToken"/> for
    /// <paramref name="expectedBaseFileName"/>, or returns null when it is anything else.
    /// <paramref name="expectedToken"/> comes from the discovered <b>file name</b>, so a record
    /// whose content claims a different token is refused rather than believed.
    /// </summary>
    public static TransactionRecord? TryParse(byte[] bytes, string expectedToken, string expectedBaseFileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (!ControlText.TryDecode(bytes, out var text))
        {
            return null;
        }

        var lines = text.Split('\n');
        var index = 0;

        // The three header lines, matched against exactly what Format would have written.
        if (!ControlText.Take(lines, ref index, VersionLine)
            || !ControlText.Take(lines, ref index, ControlText.KeyLine("token", expectedToken))
            || !ControlText.Take(lines, ref index, ControlText.KeyLine("base", expectedBaseFileName)))
        {
            return null;
        }

        var targets = CanonicalTargetNames(expectedBaseFileName);
        var roles = new[] { PublicationTargets.StageRole, PublicationTargets.BackupRole };
        var files = new List<TransactionFileEntry>();

        // Each entry is exactly "", "[[file]]", role, target — so anything else after the header
        // block fails the blank-separator step below rather than needing its own rejection.
        while (index < lines.Length - 1)
        {
            if (!ControlText.Take(lines, ref index, string.Empty) || !ControlText.Take(lines, ref index, FileHeader))
            {
                return null;
            }

            if (ControlText.Match(lines, ref index, "role", roles) is not { } role
                || ControlText.Match(lines, ref index, "target", targets) is not { } target)
            {
                return null;
            }

            files.Add(new TransactionFileEntry(role, target));
        }

        // The trailing element of the split is the text after the final LF; anything there means
        // the file did not end where the writer ends one.
        if (index != lines.Length - 1 || lines[^1].Length != 0)
        {
            return null;
        }

        // At most one entry per (target, role): two stages for one target could not both be
        // committed, and two backups could not both be restored.
        var seen = new HashSet<(string Role, string Target)>();
        foreach (var entry in files)
        {
            if (!seen.Add((entry.Role, entry.TargetFileName)))
            {
                return null;
            }
        }

        var record = new TransactionRecord(expectedToken, expectedBaseFileName, files);

        // Two decisive checks. The bytes must be exactly what this writer would produce for the
        // entries just read, AND those entries must describe a transaction that could exist.
        return string.Equals(record.Format(), text, StringComparison.Ordinal) && IsReachable(files, expectedBaseFileName)
            ? record
            : null;
    }

    /// <summary>
    /// Whether <paramref name="files"/> is a shape production could have written for
    /// <paramref name="baseFileName"/>.
    /// <para>
    /// Preflight builds exactly one arrangement per family: backups first — for the artifacts
    /// family the manifest ahead of what it certifies — then stages in canonical order. Anything
    /// else is unreachable, so it is not authority to delete or move a file, however well spelled
    /// it is. <b>A record never mixes families</b>, and each has its own narrower clause: neither
    /// relaxes the other, and <see cref="PublicationTargetKind.Single"/> is deliberately outside
    /// <see cref="PublicationTargets.CommitOrder"/> and <see cref="PublicationTargets.BackupOrder"/>,
    /// so the artifacts ordering rules cannot express its shapes and are not asked to.
    /// </para>
    /// </summary>
    private static bool IsReachable(IReadOnlyList<TransactionFileEntry> files, string baseFileName)
    {
        var stages = new List<PublicationTargetKind>();
        var backups = new List<PublicationTargetKind>();
        var seenStage = false;
        PublicationFamily? family = null;

        foreach (var entry in files)
        {
            if (KindOf(entry.TargetFileName, baseFileName) is not { } kind)
            {
                return false;
            }

            // One transaction publishes one family; rejecting a mixed record makes Family total.
            var entryFamily = PublicationTargets.FamilyOf(kind);
            if (family is { } claimed && claimed != entryFamily)
            {
                return false;
            }

            family = entryFamily;

            if (string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal))
            {
                seenStage = true;
                stages.Add(kind);
                continue;
            }

            // Every backup precedes every stage: the record reads in the order the transaction acts.
            if (seenStage)
            {
                return false;
            }

            backups.Add(kind);
        }

        if (family == PublicationFamily.Single)
        {
            // One stage for the one target, optionally preceded by its one backup. A backup with
            // no stage, two stages, or two backups is unreachable — encoded, only 0x41 and 0x43.
            return stages.Count == 1 && backups.Count <= 1;
        }

        // A transaction publishes something, and never the manifest alone: --format selects at
        // least one of .cxt/.dat, and the manifest is a sidecar of that selection.
        if (stages.Count == 0 || !stages.Exists(static kind => kind != PublicationTargetKind.Manifest))
        {
            return false;
        }

        if (!IsSubsequenceOf(stages, PublicationTargets.CommitOrder)
            || !IsSubsequenceOf(backups, PublicationTargets.BackupOrder))
        {
            return false;
        }

        // A backup exists only for a target this run replaces, or for the old public marker a
        // --no-manifest run demotes. A backup for an artifact it never stages is unreachable.
        foreach (var backup in backups)
        {
            if (backup != PublicationTargetKind.Manifest && !stages.Contains(backup))
            {
                return false;
            }
        }

        return true;
    }

    private static PublicationTargetKind? KindOf(string targetFileName, string baseFileName)
    {
        foreach (var kind in PublicationTargets.AllKinds)
        {
            if (string.Equals(targetFileName, baseFileName + PublicationTargets.Extension(kind), StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return null;
    }

    private static bool IsSubsequenceOf(List<PublicationTargetKind> values, PublicationTargetKind[] order)
    {
        var next = 0;
        foreach (var value in values)
        {
            while (next < order.Length && order[next] != value)
            {
                next++;
            }

            if (next == order.Length)
            {
                return false;
            }

            next++;
        }

        return true;
    }

    private static string[] CanonicalTargetNames(string baseFileName)
    {
        var kinds = Enum.GetValues<PublicationTargetKind>();
        var names = new string[kinds.Length];
        for (var i = 0; i < kinds.Length; i++)
        {
            names[i] = baseFileName + PublicationTargets.Extension(kinds[i]);
        }

        return names;
    }
}
