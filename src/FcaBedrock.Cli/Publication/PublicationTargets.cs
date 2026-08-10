using System.Security.Cryptography;

namespace FcaBedrock.Cli.Publication;

/// <summary>Which artifact a target holds; the first three fix the canonical commit order.</summary>
internal enum PublicationTargetKind
{
    /// <summary>The Burmeister <c>.cxt</c> context (§18.1).</summary>
    Cxt,

    /// <summary>The FIMI <c>.dat</c> context (§18.2).</summary>
    Dat,

    /// <summary>The run manifest (§15) — committed last, the run's public commit marker.</summary>
    Manifest,

    /// <summary>
    /// One arbitrary file, published at the <c>--out</c> operand exactly as it was spelled
    /// (D-122 part 4, D-123 point 7): its extension is the empty string because the operand
    /// already names the file, so nothing is appended and nothing is trimmed. It is deliberately
    /// outside <see cref="PublicationTargets.CommitOrder"/> and
    /// <see cref="PublicationTargets.BackupOrder"/>, whose contents fix the artifacts family's
    /// six shape-code bit positions.
    /// </summary>
    Single,
}

/// <summary>
/// Which semantic family a target — and so a whole transaction — belongs to (D-123 point 7).
/// <para>
/// Both families share one publication authority, record format, and recovery routine, but a
/// caller may complete only its <b>own</b>: at one exact output base a convert record and a
/// single-file record are both parseable, and each names files the other command never asked to
/// write. Classification therefore takes the caller's expected family, and a valid foreign one is
/// refused and preserved byte-for-byte rather than recovered.
/// </para>
/// </summary>
internal enum PublicationFamily
{
    /// <summary>The convert family: <c>.cxt</c>, <c>.dat</c>, and the run manifest.</summary>
    Artifacts,

    /// <summary>The single-file family: exactly one arbitrary path.</summary>
    Single,
}

/// <summary>
/// How far a transaction durably got. The phase is what makes recovery unambiguous: without it,
/// "no stage and a present target" reads identically before staging (where the target is simply
/// a pre-existing file <c>--force</c> has not renamed aside yet) and after a commit rename
/// consumed the stage.
/// <para>
/// Each phase is recorded by a <b>create-new marker file</b> rather than by rewriting the record: a
/// rewritten record could be caught half-written and would brick the location, whereas a marker is
/// only ever created or not. The transition counts as having happened once that marker holds the
/// exact canonical bytes for its role (<see cref="ControlDocument"/>) — a marker whose body did not
/// land whole records no phase and is preserved, not repaired and not removed.
/// </para>
/// </summary>
internal enum TransactionPhase
{
    /// <summary>
    /// The record exists; staging is not known to be complete. A missing stage here means it was
    /// never created, so no target may be interpreted as this transaction's commit and no final
    /// may be touched.
    /// </summary>
    Preparing,

    /// <summary>
    /// Every stage was written and flushed. Only now does a missing stage prove a commit rename
    /// consumed it, which is what makes forward recovery sound.
    /// </summary>
    Staged,

    /// <summary>
    /// Rollback was entered. Recovery always finishes the rollback and never reclassifies the
    /// transaction as committed from stage/target presence.
    /// </summary>
    RollingBack,

    /// <summary>The commit point was crossed; only backup and record cleanup can remain.</summary>
    Committed,
}

/// <summary>
/// One publication target: what it holds, how the invocation spelled it, and where it is.
/// </summary>
/// <param name="Kind">Which artifact.</param>
/// <param name="Spelling">
/// The <b>verbatim</b> <c>BASE</c> operand plus the ruled extension — the spelling
/// <c>[[run.outputs]] path</c> records (§15). Never normalized, absolutized, or rewritten.
/// </param>
/// <param name="FullPath">The resolved absolute path the filesystem seam is given.</param>
internal sealed record PublicationTarget(PublicationTargetKind Kind, string Spelling, string FullPath)
{
    /// <summary>The target's file name — the unit every private name is derived from.</summary>
    public string FileName => Path.GetFileName(FullPath);
}

/// <summary>
/// Target derivation and the private-name grammar (D-122 part 5, D-123 point 7).
/// <para>
/// <b>Every private name is computed, never parsed out of a file.</b> A stage, a backup, a phase
/// marker, and the transaction record are each a fixed function of the base file name, the
/// target, the role, and the run's token — which is what lets a discovered record prove
/// ownership: it can only name files this function could have produced for <em>this</em> base, in
/// <em>this</em> directory.
/// </para>
/// <para><c>fcabedrock</c> is spelled in full everywhere; the <c>fb</c> abbreviation is forbidden.</para>
/// </summary>
internal static class PublicationTargets
{
    /// <summary>The private-name infix marking a staged, not-yet-committed artifact.</summary>
    internal const string StageRole = "stage";

    /// <summary>The private-name infix marking a target renamed aside before forced replacement.</summary>
    internal const string BackupRole = "backup";

    /// <summary>
    /// The identity-digest role for a control document a transaction publishes by rename. It never
    /// appears in a file name; it only keeps an evidence file's own identity digest from colliding
    /// with the stage and backup digests salted for the same target.
    /// </summary>
    internal const string EvidenceRole = "evidence";

    private const string Marker = ".fcabedrock-";
    private const string TransactionInfix = Marker + "transaction-";
    private const string PendingInfix = Marker + "pending-";
    private const string IntentInfix = Marker + "intent-";
    private const string EvidenceInfix = Marker + "e-";
    private const string EvidencePendingInfix = Marker + "ep-";
    private const string StageClaimInfix = Marker + "sc-";
    private const string RecordExtension = ".toml";

    /// <summary>The token length in characters: 16 random bytes, lowercase hex.</summary>
    internal const int TokenLength = 32;

    /// <summary>The length of a 128-bit digest in characters — the record digest and each identity value.</summary>
    internal const int DigestLength = 32;

    /// <summary>
    /// The strict upper bound on a transaction record's size, in bytes.
    /// <para>
    /// A record holds three header lines plus at most six file entries — three targets × two
    /// roles — and every value it carries is a file name bounded by the host's own name limit.
    /// The realistic worst case is a few kilobytes; 64 KiB is comfortably above it and still far
    /// below anything that could matter as an allocation. A file larger than this is, by
    /// construction, not a record this code wrote.
    /// </para>
    /// </summary>
    internal const int MaxRecordBytes = 64 * 1024;

    /// <summary>The phases that have a marker file, in ascending order of progress.</summary>
    internal static readonly TransactionPhase[] MarkedPhases =
        [TransactionPhase.Staged, TransactionPhase.RollingBack, TransactionPhase.Committed];

    /// <summary>The canonical order every stage entry and every commit rename follows.</summary>
    internal static readonly PublicationTargetKind[] CommitOrder =
        [PublicationTargetKind.Cxt, PublicationTargetKind.Dat, PublicationTargetKind.Manifest];

    /// <summary>
    /// The canonical order backups are taken in: the public marker first, so it is demoted before
    /// any artifact it could certify is touched.
    /// </summary>
    internal static readonly PublicationTargetKind[] BackupOrder =
        [PublicationTargetKind.Manifest, PublicationTargetKind.Cxt, PublicationTargetKind.Dat];

    /// <summary>
    /// Every kind, for mapping between a kind and a name. Deliberately <b>not</b> an ordering:
    /// <see cref="CommitOrder"/> and <see cref="BackupOrder"/> are the artifacts family's alone.
    /// The artifacts kinds come first, so a lookup by extension cannot be shadowed — theirs are
    /// non-empty and distinct, and only <see cref="PublicationTargetKind.Single"/>'s is empty.
    /// </summary>
    internal static readonly PublicationTargetKind[] AllKinds =
        [PublicationTargetKind.Cxt, PublicationTargetKind.Dat, PublicationTargetKind.Manifest,
            PublicationTargetKind.Single];

    /// <summary>The family <paramref name="kind"/> belongs to — the one authority on the question.</summary>
    public static PublicationFamily FamilyOf(PublicationTargetKind kind) =>
        kind == PublicationTargetKind.Single ? PublicationFamily.Single : PublicationFamily.Artifacts;

    /// <summary>A fresh, unpredictable run token: 16 cryptographically random bytes as lowercase hex.</summary>
    public static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>The ruled extension for <paramref name="kind"/> (D-122 part 5 / §15).</summary>
    public static string Extension(PublicationTargetKind kind) => kind switch
    {
        PublicationTargetKind.Cxt => ".cxt",
        PublicationTargetKind.Dat => ".dat",
        PublicationTargetKind.Manifest => ".manifest.toml",
        PublicationTargetKind.Single => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown publication target kind."),
    };

    /// <summary>The marker infix for <paramref name="phase"/>.</summary>
    public static string PhaseName(TransactionPhase phase) => phase switch
    {
        TransactionPhase.Staged => "staged",
        TransactionPhase.RollingBack => "rollback",
        TransactionPhase.Committed => "committed",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "That phase has no marker."),
    };

    /// <summary>
    /// The target for <paramref name="kind"/> under the verbatim <paramref name="baseOperand"/>.
    /// The extension is <b>appended</b> to the operand exactly as given — an operand that already
    /// carries an extension keeps it and gains another (D-122 part 5: no inference from an
    /// extension, so none is ever stripped either).
    /// </summary>
    public static PublicationTarget Target(string baseOperand, PublicationTargetKind kind)
    {
        ArgumentNullException.ThrowIfNull(baseOperand);
        var spelling = baseOperand + Extension(kind);
        return new PublicationTarget(kind, spelling, Path.GetFullPath(spelling));
    }

    /// <summary>The base operand's own resolved file name — the record's namespace.</summary>
    public static string BaseFileName(string baseOperand)
    {
        ArgumentNullException.ThrowIfNull(baseOperand);
        return Path.GetFileName(Path.GetFullPath(baseOperand));
    }

    /// <summary>The directory every target, stage, backup, marker, and record of this run lives in.</summary>
    public static string Directory(string baseOperand)
    {
        ArgumentNullException.ThrowIfNull(baseOperand);

        // GetFullPath always yields a rooted path, so the directory is never null here; the
        // fallback keeps the compiler honest without inventing a behaviour.
        return Path.GetDirectoryName(Path.GetFullPath(baseOperand)) ?? Path.GetFullPath(".");
    }

    /// <summary>The private file name a <paramref name="role"/> sibling of <paramref name="targetFileName"/> takes.</summary>
    public static string PrivateName(string targetFileName, string role, string token) =>
        targetFileName + Marker + role + "-" + token;

    /// <summary>The transaction record's file name for <paramref name="baseFileName"/> and <paramref name="token"/>.</summary>
    public static string RecordName(string baseFileName, string token) =>
        baseFileName + TransactionInfix + token + RecordExtension;

    /// <summary>
    /// The name the record is written under before it is published.
    /// <para>
    /// The record's discoverable name must never hold empty or partial bytes: a crash between
    /// creating that file and finishing it would leave something a later run correctly refuses to
    /// parse and correctly refuses to delete, blocking the output base until a human intervenes.
    /// So the bytes are written, flushed, and closed under <em>this</em> name and the completed
    /// record is then published with a single rename.
    /// </para>
    /// <para>
    /// A well-formed pending file is authoritative for nothing — it precedes every stage — so it
    /// is safe for a later run to remove, which is what makes record creation recoverable.
    /// </para>
    /// </summary>
    public static string PendingRecordName(string baseFileName, string token) =>
        baseFileName + PendingInfix + token;

    /// <summary>
    /// The token of a well-formed pending record name, or null when <paramref name="fileName"/> is
    /// not one for <paramref name="baseFileName"/>. A malformed pending-shaped name is an unknown
    /// lookalike and is never removed.
    /// </summary>
    public static string? TokenOfPending(string fileName, string baseFileName)
    {
        var prefix = baseFileName + PendingInfix;
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var token = fileName[prefix.Length..];
        return IsToken(token) ? token : null;
    }

    /// <summary>
    /// The intent descriptor's file name: the control file created immediately
    /// <em>after</em> the pending record, whose name confines it to this base and classifies it as
    /// this run's descriptor — carrying the run token, the whole one-byte record shape (the two
    /// families take disjoint ranges of it, so the family bit rides in this name like any other),
    /// the 128-bit digest of the exact record bytes that shape produces, and the identity digest of
    /// the object the pending record's create-new actually produced.
    /// <para>
    /// <b>The name classifies; it does not prove.</b> Ownership rests on the descriptor's exact
    /// canonical bytes (<see cref="ControlDocument"/>), which bind the token, the base, the
    /// authoritative record's digest, and this role. Discovery and removal both require those exact
    /// bytes: an empty, partial, refused, invalid, or substituted object at this name is preserved
    /// and authorizes no mutation — least of all removal of the pending record it would otherwise
    /// have named.
    /// </para>
    /// <para>
    /// Written after the acquisition, the descriptor states the one thing that authorizes removing
    /// a pending record: <b>this transaction created that object there</b>. A refused acquisition
    /// writes no descriptor at all.
    /// </para>
    /// </summary>
    public static string IntentName(
        string baseFileName, string token, int shape, string digest, string pendingIdentity) =>
        baseFileName + IntentInfix + token + "-" + Hex2(shape) + "-" + digest + "-" + pendingIdentity;

    /// <summary>
    /// The token, shape, record digest, and pending-object identity digest of a well-formed intent
    /// name, or null when <paramref name="fileName"/> is not one for
    /// <paramref name="baseFileName"/>. The claim it carries is checked against the record it
    /// describes by the caller; this only reads the name.
    /// </summary>
    public static (string Token, int Shape, string Digest, string PendingIdentity)? IntentOf(
        string fileName, string baseFileName)
    {
        var prefix = baseFileName + IntentInfix;
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = fileName[prefix.Length..];
        if (rest.Length != TokenLength + 1 + 2 + 1 + DigestLength + 1 + DigestLength
            || rest[TokenLength] != '-'
            || rest[TokenLength + 3] != '-'
            || rest[TokenLength + 4 + DigestLength] != '-')
        {
            return null;
        }

        var token = rest[..TokenLength];
        var shape = rest.Substring(TokenLength + 1, 2);
        var digest = rest.Substring(TokenLength + 4, DigestLength);
        var identity = rest[(TokenLength + 5 + DigestLength)..];
        return IsToken(token) && IsHex(shape) && IsHex(digest) && IsHex(identity)
            ? (token, Convert.ToInt32(shape, 16), digest, identity)
            : null;
    }

    /// <summary>
    /// One target's durable identity evidence. The name is a function of the
    /// base, the artifact kind, and the run token <b>only</b> — never of a runtime identity value
    /// — so every control path a transaction will ever own is resolvable before it begins.
    /// </summary>
    public static string EvidenceName(string baseFileName, PublicationTargetKind kind, string token) =>
        baseFileName + EvidenceInfix + KindCode(kind) + "-" + token;

    /// <summary>The name evidence is written under before its one non-overwriting publish rename.</summary>
    public static string EvidencePendingName(string baseFileName, PublicationTargetKind kind, string token) =>
        baseFileName + EvidencePendingInfix + KindCode(kind) + "-" + token;

    /// <summary>
    /// The <b>stage claim</b> for one artifact: the control file created
    /// immediately <em>after</em> the stage object exists. Its name confines it to this base and
    /// classifies it as this run's claim for this artifact kind, and repeats the identity digest of
    /// the object that creation actually produced.
    /// <para>
    /// <b>The name classifies; it does not prove.</b> Ownership rests on the claim's exact
    /// canonical bytes (<see cref="ControlDocument"/>), which bind the token, the base, the
    /// authoritative record's digest, this role <em>with its target kind</em>, and that same stage
    /// identity — so name and body must agree. Discovery and removal both require those exact
    /// bytes: an empty, partial, refused, invalid, or substituted object at this name is preserved,
    /// authorizes no mutation of the stage beside it, and is not removed as this run's residue.
    /// </para>
    /// <para>
    /// <b>The order is the other half.</b> A record entry only <em>predicts</em> a private path,
    /// and the published evidence arrives later, after the artifact is written. A claim written
    /// <em>before</em> the attempt would survive a refused create-new and go on to authorize
    /// deleting the occupant that refused it. Written after, it says exactly one thing: this
    /// transaction created <b>that object</b> there. A refused acquisition writes no claim at all.
    /// </para>
    /// </summary>
    public static string StageClaimName(
        string baseFileName, PublicationTargetKind kind, string token, string identityDigest) =>
        baseFileName + StageClaimInfix + KindCode(kind) + "-" + token + "-" + identityDigest;

    /// <summary>The kind, token, and claimed identity digest of a well-formed stage-claim name, or null.</summary>
    public static (PublicationTargetKind Kind, string Token, string Digest)? StageClaimOf(
        string fileName, string baseFileName)
    {
        var prefix = baseFileName + StageClaimInfix;
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = fileName[prefix.Length..];
        if (rest.Length != 1 + 1 + TokenLength + 1 + DigestLength || rest[1] != '-' || rest[TokenLength + 2] != '-')
        {
            return null;
        }

        var token = rest.Substring(2, TokenLength);
        var digest = rest[(TokenLength + 3)..];
        if (!IsToken(token) || !IsHex(digest))
        {
            return null;
        }

        foreach (var kind in AllKinds)
        {
            if (KindCode(kind) == rest[0])
            {
                return (kind, token, digest);
            }
        }

        return null;
    }

    /// <summary>
    /// The identity-digest role for the pending transaction record — the object the intent
    /// descriptor's name binds itself to.
    /// </summary>
    internal const string RecordRole = "record";

    /// <summary>The kind and token of a well-formed evidence name, or null.</summary>
    public static (PublicationTargetKind Kind, string Token)? EvidenceOf(string fileName, string baseFileName) =>
        ControlOf(fileName, baseFileName + EvidenceInfix);

    /// <summary>The kind and token of a well-formed pending-evidence name, or null.</summary>
    public static (PublicationTargetKind Kind, string Token)? EvidencePendingOf(string fileName, string baseFileName) =>
        ControlOf(fileName, baseFileName + EvidencePendingInfix);

    /// <summary>The single-character code naming <paramref name="kind"/> inside a control file name.</summary>
    public static char KindCode(PublicationTargetKind kind) => kind switch
    {
        PublicationTargetKind.Cxt => 'c',
        PublicationTargetKind.Dat => 'd',
        PublicationTargetKind.Manifest => 'm',
        PublicationTargetKind.Single => 's',
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown publication target kind."),
    };

    /// <summary>
    /// The phase marker's file name. It confines the marker to this base and classifies which phase
    /// it records; the phase is durable only when the object there holds the exact canonical bytes
    /// for that role (<see cref="ControlDocument"/>). An empty, partial, invalid, or substituted
    /// object at this name records no phase, authorizes no cleanup, and is preserved.
    /// </summary>
    public static string MarkerName(string baseFileName, TransactionPhase phase, string token) =>
        baseFileName + Marker + PhaseName(phase) + "-" + token;

    /// <summary>
    /// The token embedded in <paramref name="recordFileName"/>, or null when the name is not a
    /// well-formed record name for <paramref name="baseFileName"/>. A name that claims the
    /// namespace but carries no valid token is deliberately <em>not</em> readable as a record:
    /// it is an unknown lookalike, and callers must leave it alone.
    /// </summary>
    public static string? TokenOfRecord(string recordFileName, string baseFileName)
    {
        var prefix = baseFileName + TransactionInfix;
        if (!recordFileName.StartsWith(prefix, StringComparison.Ordinal)
            || !recordFileName.EndsWith(RecordExtension, StringComparison.Ordinal))
        {
            return null;
        }

        var token = recordFileName[prefix.Length..^RecordExtension.Length];
        return IsToken(token) ? token : null;
    }

    /// <summary>
    /// The phase and token <paramref name="fileName"/> marks, or null when it is not a well-formed
    /// phase marker for <paramref name="baseFileName"/>.
    /// </summary>
    public static (TransactionPhase Phase, string Token)? MarkerOf(string fileName, string baseFileName)
    {
        foreach (var phase in MarkedPhases)
        {
            var prefix = baseFileName + Marker + PhaseName(phase) + "-";
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var token = fileName[prefix.Length..];
            return IsToken(token) ? (phase, token) : null;
        }

        return null;
    }

    /// <summary>True when <paramref name="value"/> is exactly a 32-character lowercase-hex token.</summary>
    public static bool IsToken(string value) => value.Length == TokenLength && IsHex(value);

    /// <summary>True when every character of <paramref name="value"/> is lowercase hex.</summary>
    public static bool IsHex(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    // The shared tail of the two per-kind control names: `<code>-<token>`.
    private static (PublicationTargetKind Kind, string Token)? ControlOf(string fileName, string prefix)
    {
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = fileName[prefix.Length..];
        if (rest.Length != 1 + 1 + TokenLength || rest[1] != '-')
        {
            return null;
        }

        var token = rest[2..];
        if (!IsToken(token))
        {
            return null;
        }

        foreach (var kind in AllKinds)
        {
            if (KindCode(kind) == rest[0])
            {
                return (kind, token);
            }
        }

        return null;
    }

    private static string Hex2(int value) =>
        value is >= 0 and < 256 ? Convert.ToHexStringLower([(byte)value]) : throw new ArgumentOutOfRangeException(nameof(value));

    /// <summary>
    /// True when <paramref name="fileName"/> claims this run's private namespace — the record or a
    /// phase marker for <paramref name="baseFileName"/>, or a stage/backup sibling of a canonical
    /// target admitted by either family — <b>whatever follows the marker</b>. A single-file target
    /// <em>is</em> the base file name, so its siblings arrive by the marker test, not the loop.
    /// <para>
    /// Deliberately broader than the well-formed grammar: a file called
    /// <c>adult.cxt.fcabedrock-stage-nonsense</c> is claiming the namespace even though no run
    /// could have produced it. Recognizing it is what turns it into a reported collision that is
    /// left byte-identical, rather than something a later run silently writes over.
    /// </para>
    /// </summary>
    public static bool IsPrivateName(string fileName, string baseFileName) =>
        IsPrivateName(fileName, baseFileName, StringComparison.Ordinal);

    /// <summary>
    /// As <see cref="IsPrivateName(string, string)"/>, under <paramref name="comparison"/>.
    /// <para>
    /// Discovery passes <see cref="StringComparison.OrdinalIgnoreCase"/> to gather a superset that
    /// can include a case variant of this base, because on a case-insensitive directory those name
    /// the same physical files. Whether such an entry really is this run's namespace is then
    /// decided by filesystem identity, never by the comparison.
    /// </para>
    /// </summary>
    public static bool IsPrivateName(string fileName, string baseFileName, StringComparison comparison)
    {
        if (fileName.StartsWith(baseFileName + Marker, comparison))
        {
            return true;
        }

        foreach (var kind in CommitOrder)
        {
            var target = baseFileName + Extension(kind);
            if (fileName.StartsWith(target + Marker + StageRole + "-", comparison)
                || fileName.StartsWith(target + Marker + BackupRole + "-", comparison))
            {
                return true;
            }
        }

        return false;
    }
}
