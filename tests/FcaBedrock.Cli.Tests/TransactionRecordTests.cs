using System.Text;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The private transaction record's format and — the part that matters — its validation.
/// <para>
/// The record is the only thing that authorizes recovery to delete, move, or replace a file, so
/// these cases push at exactly that: a record is believed only when re-formatting what was parsed
/// out of it reproduces the file byte for byte, and even a believed one can name nothing but the
/// fixed set of file names its own base computes.
/// </para>
/// </summary>
public sealed class TransactionRecordTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";
    private const string Base = "out";

    // The legacy artifacts record for one staged `.cxt`, recorded from the protected baseline
    // before the single-file family existed: the text is authored here rather than produced by
    // Format(), and the digest was computed independently over these exact bytes — SHA-256, first
    // 16 bytes, lowercase hex — rather than read back from Digest.
    private const string LegacyStageRecordText =
        "version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n\n"
        + "[[file]]\nrole = \"stage\"\ntarget = \"out.cxt\"\n";

    private const string LegacyStageRecordDigest = "d07769a8e5a667f81d09b944110b58b2";

    private const string LegacyIdentity = "aaaaaaaabbbbbbbbccccccccdddddddd";

    [Fact]
    public void Format_WhenTheRecordHasEntries_ThenItIsTheDocumentedText()
    {
        var record = TransactionRecord.Create(
            Token,
            Base,
            [
                new TransactionFileEntry("backup", "out.manifest.toml"),
                new TransactionFileEntry("stage", "out.cxt"),
            ]);

        Assert.Equal(
            """
            version = 1
            token = "0123456789abcdef0123456789abcdef"
            base = "out"

            [[file]]
            role = "backup"
            target = "out.manifest.toml"

            [[file]]
            role = "stage"
            target = "out.cxt"

            """.ReplaceLineEndings("\n"),
            record.Format());
    }

    [Fact]
    public void ToBytes_WhenTheRecordIsWritten_ThenItIsUtf8WithoutABom()
    {
        var bytes = TransactionRecord.Create(Token, Base, [new TransactionFileEntry("stage", "out.cxt")]).ToBytes();

        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void TryParse_WhenTheRecordIsItsOwnOutput_ThenItRoundTrips()
    {
        var entries = new[]
        {
            new TransactionFileEntry("backup", "out.cxt"),
            new TransactionFileEntry("stage", "out.cxt"),
            new TransactionFileEntry("stage", "out.dat"),
            new TransactionFileEntry("stage", "out.manifest.toml"),
        };

        var parsed = TransactionRecord.TryParse(
            TransactionRecord.Create(Token, Base, entries).ToBytes(), Token, Base);

        Assert.NotNull(parsed);
        Assert.Equal(Token, parsed.Token);
        Assert.Equal(Base, parsed.BaseFileName);
        Assert.Equal(entries, parsed.Files);
    }

    [Fact]
    public void TryParse_WhenTheRecordSelectsNothing_ThenItIsNotAReachableTransaction()
    {
        // Canonically spelled, and still impossible: a transaction always publishes at least one
        // artifact, so an empty selection is authority no run could have written.
        Assert.Null(TransactionRecord.TryParse(
            TransactionRecord.Create(Token, Base, []).ToBytes(), Token, Base));
    }

    [Theory]

    // The manifest is a sidecar of a selection; it is never the whole selection.
    [InlineData("stage:out.manifest.toml")]

    // Stages out of canonical commit order.
    [InlineData("stage:out.dat|stage:out.cxt")]
    [InlineData("stage:out.manifest.toml|stage:out.cxt")]

    // Backups out of canonical order — the marker is always demoted first.
    [InlineData("backup:out.cxt|backup:out.manifest.toml|stage:out.cxt")]
    [InlineData("backup:out.dat|backup:out.cxt|stage:out.cxt|stage:out.dat")]

    // A backup after a stage: the record reads in the order the transaction acts.
    [InlineData("stage:out.cxt|backup:out.cxt")]

    // An orphan artifact backup — nothing this run publishes could have needed it.
    [InlineData("backup:out.dat|stage:out.cxt")]
    public void TryParse_WhenTheEntriesCouldNotHaveBeenWritten_ThenItIsNotARecord(string shape)
    {
        // Every one of these is byte-perfect canonical text. Reconstruction proves the spelling;
        // it cannot prove the transaction is one this implementation produces, and only a
        // reachable transaction may authorize deleting or moving a file.
        var entries = new List<TransactionFileEntry>();
        foreach (var part in shape.Split('|'))
        {
            var fields = part.Split(':');
            entries.Add(new TransactionFileEntry(fields[0], fields[1]));
        }

        var bytes = TransactionRecord.Create(Token, Base, entries).ToBytes();

        Assert.Null(TransactionRecord.TryParse(bytes, Token, Base));
    }

    [Theory]
    [InlineData("stage:out.cxt")]
    [InlineData("stage:out.dat")]
    [InlineData("stage:out.cxt|stage:out.dat")]
    [InlineData("stage:out.cxt|stage:out.dat|stage:out.manifest.toml")]
    [InlineData("backup:out.cxt|stage:out.cxt")]
    [InlineData("backup:out.manifest.toml|stage:out.cxt")]
    [InlineData("backup:out.manifest.toml|backup:out.cxt|backup:out.dat|"
        + "stage:out.cxt|stage:out.dat|stage:out.manifest.toml")]
    public void TryParse_WhenTheEntriesAreAShapeProductionWrites_ThenItIsAccepted(string shape)
    {
        var bytes = TransactionRecord.Create(Token, Base, Entries(shape)).ToBytes();

        Assert.NotNull(TransactionRecord.TryParse(bytes, Token, Base));
    }

    private static List<TransactionFileEntry> Entries(string shape)
    {
        var entries = new List<TransactionFileEntry>();
        foreach (var part in shape.Split('|'))
        {
            var fields = part.Split(':');
            entries.Add(new TransactionFileEntry(fields[0], fields[1]));
        }

        return entries;
    }

    [Theory]

    // A different token than the file name carries: the name is the authority, not the content.
    [InlineData("version = 1\ntoken = \"ffffffffffffffffffffffffffffffff\"\nbase = \"out\"\n")]

    // A different base: the record belongs to another namespace.
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"other\"\n")]

    // A version this code never wrote.
    [InlineData("version = 2\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n")]

    // Reordered header keys.
    [InlineData("token = \"0123456789abcdef0123456789abcdef\"\nversion = 1\nbase = \"out\"\n")]

    // An extra key this writer does not emit.
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\nextra = 1\n")]

    // No final newline.
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"")]

    // Carriage returns: rewritten by something that is not this writer.
    [InlineData("version = 1\r\ntoken = \"0123456789abcdef0123456789abcdef\"\r\nbase = \"out\"\r\n")]

    // A role that authorizes nothing.
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n\n"
        + "[[file]]\nrole = \"commit\"\ntarget = \"out.cxt\"\n")]

    // A target outside the three this base computes — including a traversal attempt.
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n\n"
        + "[[file]]\nrole = \"stage\"\ntarget = \"../../elsewhere.txt\"\n")]
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n\n"
        + "[[file]]\nrole = \"stage\"\ntarget = \"C:\\\\Windows\\\\system32\\\\drivers\\\\etc\\\\hosts\"\n")]
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n\n"
        + "[[file]]\nrole = \"stage\"\ntarget = \"other.cxt\"\n")]

    // Two entries for one (role, target): neither could both be committed nor both restored.
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n\n"
        + "[[file]]\nrole = \"stage\"\ntarget = \"out.cxt\"\n\n[[file]]\nrole = \"stage\"\ntarget = \"out.cxt\"\n")]

    // A malformed entry block.
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n\n"
        + "[[file]]\ntarget = \"out.cxt\"\nrole = \"stage\"\n")]
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n"
        + "[[file]]\nrole = \"stage\"\ntarget = \"out.cxt\"\n")]

    // Trailing content after the last entry.
    [InlineData("version = 1\ntoken = \"0123456789abcdef0123456789abcdef\"\nbase = \"out\"\n\n"
        + "[[file]]\nrole = \"stage\"\ntarget = \"out.cxt\"\nnotes = 1\n")]
    public void TryParse_WhenTheTextIsNotSomethingThisWriterProduces_ThenItIsNotARecord(string text)
    {
        Assert.Null(TransactionRecord.TryParse(Encoding.UTF8.GetBytes(text), Token, Base));
    }

    [Fact]
    public void TryParse_WhenTheBytesCarryAByteOrderMark_ThenItIsNotARecord()
    {
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(
            TransactionRecord.Create(Token, Base, [new TransactionFileEntry("stage", "out.cxt")]).ToBytes());

        Assert.Null(TransactionRecord.TryParse(bytes.ToArray(), Token, Base));
    }

    [Fact]
    public void MarkerOf_WhenTheNameIsAWellFormedMarker_ThenItsPhaseAndTokenAreRead()
    {
        // The phase is the durable fact recovery reads first, so its spelling is a contract.
        (TransactionPhase Phase, string Spelling)[] phases =
        [
            (TransactionPhase.Staged, "staged"),
            (TransactionPhase.RollingBack, "rollback"),
            (TransactionPhase.Committed, "committed"),
        ];

        foreach (var (phase, spelling) in phases)
        {
            var name = PublicationTargets.MarkerName(Base, phase, Token);

            Assert.Equal($"out.fcabedrock-{spelling}-{Token}", name);
            Assert.Equal((phase, Token), PublicationTargets.MarkerOf(name, Base));
            Assert.True(PublicationTargets.IsPrivateName(name, Base));
        }
    }

    [Theory]
    [InlineData("out.fcabedrock-staged-nothex")]
    [InlineData("out.fcabedrock-elsewhere-0123456789abcdef0123456789abcdef")]
    [InlineData("other.fcabedrock-staged-0123456789abcdef0123456789abcdef")]
    public void MarkerOf_WhenTheNameIsNotAWellFormedMarker_ThenThereIsNoPhase(string name)
    {
        Assert.Null(PublicationTargets.MarkerOf(name, Base));
    }

    [Fact]
    public void TryParse_WhenTheBytesAreNotUtf8_ThenItIsNotARecord()
    {
        Assert.Null(TransactionRecord.TryParse([0xC3, 0x28, 0x0A], Token, Base));
    }

    [Fact]
    public void TryParse_WhenTheBytesAreEmpty_ThenItIsNotARecord()
    {
        Assert.Null(TransactionRecord.TryParse([], Token, Base));
    }

    [Fact]
    public void PathOf_WhenAnEntryIsRead_ThenItsPrivatePathIsDerivedNotStored()
    {
        // The record carries no path at all: it selects a role and a target, and the file name
        // follows from those plus the token. That is what makes an escape unrepresentable.
        var record = TransactionRecord.Create(Token, Base, [new TransactionFileEntry("stage", "out.cxt")]);

        Assert.DoesNotContain("path", record.Format(), StringComparison.Ordinal);
        Assert.Equal(
            Path.Combine("C:", "work", $"out.cxt.fcabedrock-stage-{Token}"),
            record.PathOf(Path.Combine("C:", "work"), record.Files[0]));
    }

    [Fact]
    public void TokenOfRecord_WhenTheNameIsWellFormed_ThenTheTokenIsRead()
    {
        Assert.Equal(Token, PublicationTargets.TokenOfRecord($"out.fcabedrock-transaction-{Token}.toml", Base));
    }

    [Theory]
    [InlineData("out.fcabedrock-transaction-.toml")]
    [InlineData("out.fcabedrock-transaction-NOTHEX0123456789abcdef01234567.toml")]
    [InlineData("out.fcabedrock-transaction-0123456789abcdef0123456789abcdef")]
    [InlineData("other.fcabedrock-transaction-0123456789abcdef0123456789abcdef.toml")]
    public void TokenOfRecord_WhenTheNameIsNotWellFormed_ThenThereIsNoToken(string name)
    {
        Assert.Null(PublicationTargets.TokenOfRecord(name, Base));
    }

    [Theory]
    [InlineData("stage:out.cxt")]
    [InlineData("stage:out.dat")]
    [InlineData("stage:out.cxt|stage:out.dat|stage:out.manifest.toml")]
    [InlineData("backup:out.manifest.toml|backup:out.cxt|stage:out.cxt")]
    [InlineData("backup:out.manifest.toml|backup:out.cxt|backup:out.dat|"
        + "stage:out.cxt|stage:out.dat|stage:out.manifest.toml")]
    public void FromShape_WhenAShapeIsReachable_ThenItReproducesTheRecordItDescribes(string shape)
    {
        // These rows are the legacy artifacts family, whose shapes are the six low bits of the
        // intent descriptor (the single-file 0x41/0x43 shapes have their own methods below), and
        // this is why six bits are enough: the entry ORDER is not a degree of freedom — a
        // transaction writes backups in canonical order, then stages in canonical order, and
        // nothing else.
        var record = TransactionRecord.Create(Token, Base, Entries(shape));

        var decoded = TransactionRecord.FromShape(Token, Base, record.ShapeCode);

        Assert.NotNull(decoded);
        Assert.Equal(record.Files, decoded.Files);
        Assert.Equal(record.Format(), decoded.Format());
    }

    [Theory]

    // Nothing selected at all.
    [InlineData(0x00)]

    // The manifest staged alone — a sidecar of a selection, never the selection.
    [InlineData(0x04)]

    // A backup for an artifact this transaction never stages.
    [InlineData(0x10)]
    public void FromShape_WhenAShapeIsUnreachable_ThenThereIsNoRecord(int shape) =>
        Assert.Null(TransactionRecord.FromShape(Token, Base, shape));

    // ---- the single-file family (D-123 point 7) --------------------------------------------------

    [Theory]
    [InlineData("stage:out")]
    [InlineData("backup:out|stage:out")]
    public void TryParse_WhenTheEntriesAreASingleFileShapeProductionWrites_ThenItIsAccepted(string shape) =>
        Assert.NotNull(Parse(shape));

    // A backup with no stage, a backup after its stage, and two of either role: one target admits
    // one stage and one backup, because one create-new produces one object.
    [Theory]
    [InlineData("backup:out")]
    [InlineData("stage:out|backup:out")]
    [InlineData("stage:out|stage:out")]
    [InlineData("backup:out|backup:out")]
    public void TryParse_WhenASingleFileRecordCouldNotHaveBeenWritten_ThenItIsNotARecord(string shape) =>
        Assert.Null(Parse(shape));

    // One transaction publishes one family, so a mixed record describes no run at all — and
    // rejecting it is what makes Family total for every record that does parse.
    [Theory]
    [InlineData("stage:out|stage:out.cxt")]
    [InlineData("backup:out.manifest.toml|stage:out")]
    [InlineData("backup:out|stage:out.cxt")]
    public void TryParse_WhenARecordMixesTheSingleFileAndArtifactFamilies_ThenItIsNotARecord(string shape) =>
        Assert.Null(Parse(shape));

    [Theory]
    [InlineData("stage:out.cxt", false)]
    [InlineData("backup:out.manifest.toml|backup:out.cxt|stage:out.cxt|stage:out.manifest.toml", false)]
    [InlineData("stage:out", true)]
    [InlineData("backup:out|stage:out", true)]
    public void Family_WhenARecordIsParsed_ThenItIsTheFamilyItsEntriesDescribe(string shape, bool single) =>
        Assert.Equal(
            single ? PublicationFamily.Single : PublicationFamily.Artifacts,
            Assert.IsType<TransactionRecord>(Parse(shape)).Family);

    [Theory]
    [InlineData(0x41, "stage:out")]
    [InlineData(0x43, "backup:out|stage:out")]
    public void FromShape_WhenASingleFileShapeIsReachable_ThenItReproducesTheRecordItDescribes(
        int shape, string expected)
    {
        var decoded = Assert.IsType<TransactionRecord>(TransactionRecord.FromShape(Token, Base, shape));

        Assert.Equal(TransactionRecord.Create(Token, Base, Entries(expected)).Files, decoded.Files);
        Assert.Equal(shape, decoded.ShapeCode);
    }

    // Nothing selected; a backup with no stage; an artifacts bit borrowed into a single-file
    // shape; and an unused high bit, in either family.
    [Theory]
    [InlineData(0x40)]
    [InlineData(0x42)]
    [InlineData(0x44)]
    [InlineData(0x81)]
    [InlineData(0xC1)]
    public void FromShape_WhenASingleFileShapeIsUnreachable_ThenThereIsNoRecord(int shape) =>
        Assert.Null(TransactionRecord.FromShape(Token, Base, shape));

    [Fact]
    public void ShapeCode_WhenAnArtifactRecordIsEncoded_ThenEveryBitPositionIsUnchanged()
    {
        // Each legacy bit against a literal. The single-file family is a disjoint range above
        // these, so adding it may not move one of them.
        Assert.Equal(0x01, Shape("stage:out.cxt"));
        Assert.Equal(0x02, Shape("stage:out.dat"));
        Assert.Equal(0x04, Shape("stage:out.manifest.toml"));
        Assert.Equal(0x08, Shape("backup:out.cxt"));
        Assert.Equal(0x10, Shape("backup:out.dat"));
        Assert.Equal(0x20, Shape("backup:out.manifest.toml"));
        Assert.Equal(0x3F, Shape("backup:out.manifest.toml|backup:out.cxt|backup:out.dat|"
            + "stage:out.cxt|stage:out.dat|stage:out.manifest.toml"));
    }

    [Fact]
    public void EvidenceNames_WhenTheKindIsSingleFile_ThenItIsReadBackAsItsOwnKind()
    {
        Assert.Equal(
            $"out.fcabedrock-e-s-{Token}",
            PublicationTargets.EvidenceName(Base, PublicationTargetKind.Single, Token));
        Assert.Equal(
            (PublicationTargetKind.Single, Token),
            PublicationTargets.EvidenceOf($"out.fcabedrock-e-s-{Token}", Base));
        Assert.Equal(
            (PublicationTargetKind.Single, Token, LegacyIdentity),
            PublicationTargets.StageClaimOf($"out.fcabedrock-sc-s-{Token}-{LegacyIdentity}", Base));

        // An unknown code still refuses, and a single-file target's spelling IS the operand.
        Assert.Null(PublicationTargets.EvidenceOf($"out.fcabedrock-e-x-{Token}", Base));
        Assert.Null(PublicationTargets.StageClaimOf($"out.fcabedrock-sc-x-{Token}-{LegacyIdentity}", Base));
        Assert.Equal(string.Empty, PublicationTargets.Extension(PublicationTargetKind.Single));
    }

    [Fact]
    public void Record_WhenALegacySingleArtifactStageIsFormatted_ThenItsTextIsExactlyTheLiteralLegacyBytes()
    {
        var record = TransactionRecord.Create(Token, Base, [new TransactionFileEntry("stage", "out.cxt")]);

        Assert.Equal(LegacyStageRecordText, record.Format());
        Assert.Equal(LegacyStageRecordDigest, record.Digest);
    }

    // Built from the literal shape byte and the literal digest, so a changed ShapeCode or Format
    // cannot follow it.
    [Fact]
    public void IntentName_WhenALegacySingleArtifactIsStaged_ThenTheShapeByteIsTheLiteralZeroOne() =>
        Assert.Equal(
            $"out.fcabedrock-intent-{Token}-01-{LegacyStageRecordDigest}-{LegacyIdentity}",
            PublicationTargets.IntentName(Base, Token, 0x01, LegacyStageRecordDigest, LegacyIdentity));

    private static TransactionRecord? Parse(string shape) =>
        TransactionRecord.TryParse(TransactionRecord.Create(Token, Base, Entries(shape)).ToBytes(), Token, Base);

    private static int Shape(string shape) => TransactionRecord.Create(Token, Base, Entries(shape)).ShapeCode;

    [Fact]
    public void Digest_WhenTheRecordChanges_ThenSoDoesItsDigest()
    {
        // The digest binds a descriptor to the exact bytes it claims, so it must move with them.
        var one = TransactionRecord.Create(Token, Base, [new TransactionFileEntry("stage", "out.cxt")]).Digest;
        var two = TransactionRecord.Create(
            Token,
            Base,
            [new TransactionFileEntry("stage", "out.cxt"), new TransactionFileEntry("stage", "out.dat")]).Digest;

        Assert.Equal(32, one.Length);
        Assert.True(PublicationTargets.IsHex(one));
        Assert.NotEqual(one, two);
    }

    [Fact]
    public void IntentName_WhenTheNameIsWellFormed_ThenItsClaimIsRead()
    {
        // The descriptor names four things, and the fourth is what makes it an acknowledgement
        // rather than a prediction: the identity of the object the pending record's create-new
        // actually produced.
        const string Identity = "aaaaaaaabbbbbbbbccccccccdddddddd";
        var record = TransactionRecord.Create(Token, Base, [new TransactionFileEntry("stage", "out.cxt")]);
        var name = PublicationTargets.IntentName(Base, Token, record.ShapeCode, record.Digest, Identity);

        Assert.Equal($"out.fcabedrock-intent-{Token}-01-{record.Digest}-{Identity}", name);
        Assert.Equal(
            (Token, record.ShapeCode, record.Digest, Identity), PublicationTargets.IntentOf(name, Base));
        Assert.True(PublicationTargets.IsPrivateName(name, Base));
    }

    [Theory]
    [InlineData("out.fcabedrock-intent-nothex-01-0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef")]
    [InlineData("out.fcabedrock-intent-0123456789abcdef0123456789abcdef-zz-0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef")]
    [InlineData("out.fcabedrock-intent-0123456789abcdef0123456789abcdef-01-short-0123456789abcdef0123456789abcdef")]

    // No acknowledged identity at all: the pre-correction shape, which named a path and not an object.
    [InlineData("out.fcabedrock-intent-0123456789abcdef0123456789abcdef-01-0123456789abcdef0123456789abcdef")]
    [InlineData("out.fcabedrock-intent-0123456789abcdef0123456789abcdef-01-0123456789abcdef0123456789abcdef-nothexnothexnothexnothexnothexnn")]
    [InlineData("other.fcabedrock-intent-0123456789abcdef0123456789abcdef-01-0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef")]
    public void IntentOf_WhenTheNameIsNotWellFormed_ThenThereIsNoClaim(string name) =>
        Assert.Null(PublicationTargets.IntentOf(name, Base));

    [Fact]
    public void EvidenceNames_WhenTheyAreFormed_ThenEachIsReadBackAsItsOwnKind()
    {
        // Fixed, short, and a function of the base, the role, and the token alone — so every one of
        // them is resolvable before the transaction begins, and none is confusable with the other.
        Assert.Equal($"out.fcabedrock-e-c-{Token}", PublicationTargets.EvidenceName(Base, PublicationTargetKind.Cxt, Token));
        Assert.Equal($"out.fcabedrock-ep-m-{Token}", PublicationTargets.EvidencePendingName(Base, PublicationTargetKind.Manifest, Token));

        Assert.Equal(
            (PublicationTargetKind.Dat, Token),
            PublicationTargets.EvidenceOf($"out.fcabedrock-e-d-{Token}", Base));
        Assert.Equal(
            (PublicationTargetKind.Cxt, Token),
            PublicationTargets.EvidencePendingOf($"out.fcabedrock-ep-c-{Token}", Base));

        // A pending name is never read as an authoritative one, and neither is a foreign base.
        Assert.Null(PublicationTargets.EvidenceOf($"out.fcabedrock-ep-c-{Token}", Base));
        Assert.Null(PublicationTargets.EvidencePendingOf($"out.fcabedrock-e-c-{Token}", Base));
        Assert.Null(PublicationTargets.EvidenceOf($"out.fcabedrock-e-x-{Token}", Base));
        Assert.Null(PublicationTargets.EvidenceOf($"other.fcabedrock-e-c-{Token}", Base));
        Assert.True(PublicationTargets.IsPrivateName($"out.fcabedrock-e-c-{Token}", Base));
        Assert.True(PublicationTargets.IsPrivateName($"out.fcabedrock-ep-c-{Token}", Base));
    }

    [Fact]
    public void StageEvidence_WhenItIsFormatted_ThenItIsTheDocumentedText()
    {
        var evidence = StageEvidence.Create(Token, Base, "out.cxt", "none", "0123456789abcdef0123456789abcdef");

        Assert.Equal(
            """
            version = 1
            token = "0123456789abcdef0123456789abcdef"
            base = "out"
            target = "out.cxt"
            backup = "none"
            stage = "0123456789abcdef0123456789abcdef"

            """.ReplaceLineEndings("\n"),
            evidence.Format());
    }

    [Theory]
    [InlineData("none", "unknown")]
    [InlineData("unknown", "none")]
    [InlineData("0123456789abcdef0123456789abcdef", "fedcba9876543210fedcba9876543210")]
    public void StageEvidence_WhenItIsItsOwnOutput_ThenItRoundTrips(string backup, string stage)
    {
        var bytes = StageEvidence.Create(Token, Base, "out.cxt", backup, stage).ToBytes();

        var parsed = StageEvidence.TryParse(bytes, Token, Base, "out.cxt");

        Assert.NotNull(parsed);
        Assert.Equal(backup, parsed.Backup);
        Assert.Equal(stage, parsed.Stage);
    }

    [Theory]

    // A value outside the closed set: not `none`, not `unknown`, not a 128-bit lowercase-hex digest.
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.cxt\"\nbackup = \"maybe\"\nstage = \"none\"\n")]
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.cxt\"\nbackup = \"none\"\nstage = \"0123\"\n")]
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.cxt\"\nbackup = \"none\"\n"
        + "stage = \"0123456789ABCDEF0123456789abcdef\"\n")]

    // A different token, base, or target than the caller expects.
    [InlineData("version = 1\ntoken = \"ffffffffffffffffffffffffffffffff\"\nbase = \"out\"\ntarget = \"out.cxt\"\n"
        + "backup = \"none\"\nstage = \"none\"\n")]
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"other\"\ntarget = \"out.cxt\"\nbackup = \"none\"\nstage = \"none\"\n")]
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.dat\"\nbackup = \"none\"\nstage = \"none\"\n")]

    // A version this code never wrote, a missing line, a reordered pair, and trailing content.
    [InlineData("version = 2\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.cxt\"\nbackup = \"none\"\nstage = \"none\"\n")]
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.cxt\"\nstage = \"none\"\n")]
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.cxt\"\nstage = \"none\"\nbackup = \"none\"\n")]
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.cxt\"\nbackup = \"none\"\nstage = \"none\"\nx = 1\n")]

    // No final newline, and carriage returns.
    [InlineData("version = 1\ntoken = \"T\"\nbase = \"out\"\ntarget = \"out.cxt\"\nbackup = \"none\"\nstage = \"none\"")]
    [InlineData("version = 1\r\ntoken = \"T\"\r\nbase = \"out\"\r\ntarget = \"out.cxt\"\r\nbackup = \"none\"\r\n"
        + "stage = \"none\"\r\n")]
    public void StageEvidence_WhenTheTextIsNotSomethingThisWriterProduces_ThenItIsNotEvidence(string text) =>
        Assert.Null(StageEvidence.TryParse(
            Encoding.UTF8.GetBytes(text.Replace("\"T\"", $"\"{Token}\"", StringComparison.Ordinal)),
            Token,
            Base,
            "out.cxt"));

    [Fact]
    public void StageEvidence_WhenTheBytesCarryAByteOrderMark_ThenItIsNotEvidence()
    {
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(StageEvidence.Create(Token, Base, "out.cxt", "none", "none").ToBytes());

        Assert.Null(StageEvidence.TryParse(bytes.ToArray(), Token, Base, "out.cxt"));
    }

    [Theory]
    [InlineData("out.fcabedrock-transaction-anything.toml", true)]
    [InlineData("out.cxt.fcabedrock-stage-anything", true)]
    [InlineData("out.dat.fcabedrock-backup-anything", true)]
    [InlineData("out.manifest.toml.fcabedrock-stage-anything", true)]
    [InlineData("out.cxt", false)]
    [InlineData("out-notes.txt", false)]
    [InlineData("other.cxt.fcabedrock-stage-anything", false)]
    public void IsPrivateName_WhenANameClaimsTheNamespace_ThenItIsRecognizedWhateverFollowsTheMarker(
        string name, bool expected)
    {
        // Broader than the well-formed grammar on purpose: recognizing a malformed claim is what
        // turns it into a reported collision instead of something a later run overwrites.
        Assert.Equal(expected, PublicationTargets.IsPrivateName(name, Base));
    }
}
