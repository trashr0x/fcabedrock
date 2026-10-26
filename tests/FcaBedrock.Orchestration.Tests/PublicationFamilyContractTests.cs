using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The artifacts family as it is (D-139 part 7, D-140): the kind set, the commit and backup
/// orders, the per-kind codes and extensions, which kinds belong to which family, the single-file
/// shape bytes and the private-name grammar's edge. A later export builds on both families without
/// changing either, so the values are pinned literals here, never regenerated from the code under
/// test.
/// </summary>
public sealed class PublicationFamilyContractTests
{
    private const string Token = "aaaaaaaabbbbbbbbccccccccdddddddd";

    [Fact]
    public void CommitOrder_WhenRead_ThenItIsCxtDatManifest()
    {
        Assert.Equal(
            [PublicationTargetKind.Cxt, PublicationTargetKind.Dat, PublicationTargetKind.Manifest],
            PublicationTargets.CommitOrder);
    }

    [Fact]
    public void BackupOrder_WhenRead_ThenItIsManifestCxtDat()
    {
        Assert.Equal(
            [PublicationTargetKind.Manifest, PublicationTargetKind.Cxt, PublicationTargetKind.Dat],
            PublicationTargets.BackupOrder);
    }

    [Fact]
    public void AllKinds_WhenRead_ThenItIsTheThreeArtifactKindsThenTheSingleFile()
    {
        Assert.Equal(
            [PublicationTargetKind.Cxt, PublicationTargetKind.Dat, PublicationTargetKind.Manifest, PublicationTargetKind.Single],
            PublicationTargets.AllKinds);
    }

    [Theory]
    [InlineData(nameof(PublicationTargetKind.Cxt), 'c')]
    [InlineData(nameof(PublicationTargetKind.Dat), 'd')]
    [InlineData(nameof(PublicationTargetKind.Manifest), 'm')]
    [InlineData(nameof(PublicationTargetKind.Single), 's')]
    public void KindCode_WhenEachKindIsEncoded_ThenItIsItsPinnedLetter(string kind, char code)
    {
        Assert.Equal(code, PublicationTargets.KindCode(Kind(kind)));
    }

    [Theory]
    [InlineData(nameof(PublicationTargetKind.Cxt), ".cxt")]
    [InlineData(nameof(PublicationTargetKind.Dat), ".dat")]
    [InlineData(nameof(PublicationTargetKind.Manifest), ".manifest.toml")]
    [InlineData(nameof(PublicationTargetKind.Single), "")]
    public void Extension_WhenEachKindIsNamed_ThenItIsItsRuledExtension(string kind, string extension)
    {
        Assert.Equal(extension, PublicationTargets.Extension(Kind(kind)));
    }

    [Theory]
    [InlineData(nameof(PublicationTargetKind.Cxt), false)]
    [InlineData(nameof(PublicationTargetKind.Dat), false)]
    [InlineData(nameof(PublicationTargetKind.Manifest), false)]
    [InlineData(nameof(PublicationTargetKind.Single), true)]
    public void FamilyOf_WhenEachKindIsClassified_ThenOnlyTheSingleKindIsTheSingleFamily(string kind, bool single)
    {
        Assert.Equal(
            single ? PublicationFamily.Single : PublicationFamily.Artifacts, PublicationTargets.FamilyOf(Kind(kind)));
    }

    [Fact]
    public void FromShape_WhenEverySingleFamilyByteIsTried_ThenOnly0x41And0x43AreReachable()
    {
        // Every byte that carries the single-file family bit (0x40): a stage alone, and a backup
        // with its stage, are the only arrangements a single-file transaction ever writes.
        var reachable = new List<int>();
        for (var shape = 0; shape < 0x100; shape++)
        {
            if ((shape & 0x40) == 0)
            {
                continue;
            }

            if (TransactionRecord.FromShape(Token, "draft.toml", shape) is { } record)
            {
                Assert.Equal(PublicationFamily.Single, record.Family);
                reachable.Add(shape);
            }
        }

        Assert.Equal([0x41, 0x43], reachable);
    }

    [Theory]
    [InlineData("adult.spec-0123456789ab.toml")]
    [InlineData("adult.spec-0123456789ab.toml.fcabedrock-stage-aaaaaaaabbbbbbbbccccccccdddddddd")]
    [InlineData("adult.spec-0123456789ab.toml.fcabedrock-backup-aaaaaaaabbbbbbbbccccccccdddddddd")]
    [InlineData("adult.cxt")]
    [InlineData("adult.manifest.toml")]
    [InlineData("adult.toml")]
    [InlineData("adultfcabedrock-stage")]
    [InlineData("other.cxt.fcabedrock-stage-aaaaaaaabbbbbbbbccccccccdddddddd")]
    public void IsPrivateName_WhenANameLiesOutsideTheGrammar_ThenItClaimsNothing(string fileName)
    {
        // A recipe named from a content hash, and that name's own single-file stage and backup
        // siblings, sit beside base `adult` without entering the artifacts family's namespace.
        Assert.False(PublicationTargets.IsPrivateName(fileName, "adult"));
    }

    // Theory rows name the internal kind, because a public test method cannot take an internal type.
    private static PublicationTargetKind Kind(string name) => Enum.Parse<PublicationTargetKind>(name);
}
