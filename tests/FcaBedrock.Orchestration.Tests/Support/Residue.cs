using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Orchestration;
using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.TestSupport;

/// <summary>
/// Hand-built transaction residue. The record format is written out literally here rather than
/// through the production writer, so a change to it fails these tests instead of silently keeping
/// them green.
/// </summary>
internal sealed class Residue
{
    private Residue(string directory, string baseName, string token)
    {
        Directory = directory;
        BaseName = baseName;
        Token = token;
    }

    public string Directory { get; }

    public string BaseName { get; }

    public string Token { get; }

    public string RecordPath =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-transaction-{Token}.toml");

    public static Residue Create(string directory, string baseName, string token) =>
        new(directory, baseName, token);

    public string PrivatePath(string role, string target) =>
        Path.Combine(Directory, $"{target}.fcabedrock-{role}-{Token}");

    public string MarkerPath(string phase) =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-{phase}-{Token}");

    /// <summary>The evidence path for a target, by its single-character kind code.</summary>
    public string EvidencePath(string kind) =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-e-{kind}-{Token}");

    /// <summary>The pending-evidence path for a target, by its single-character kind code.</summary>
    public string EvidencePendingPath(string kind) =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-ep-{kind}-{Token}");

    /// <summary>
    /// Records that the interrupted transaction durably reached <paramref name="phase"/>. The body
    /// is the canonical control document (the run token, this base, the phase's own role, and the
    /// digest of the record this residue carries), spelled out here rather than produced by the
    /// writer under test, so a change to it fails these tests instead of silently keeping them
    /// green.
    /// </summary>
    public void WriteMarker(string phase) => File.WriteAllBytes(MarkerPath(phase), ControlBody(phase));

    /// <summary>
    /// The exact bytes a control of <paramref name="role"/> carries for this residue, with the
    /// identity it acknowledges where it acknowledges one.
    /// </summary>
    public byte[] ControlBody(string role, string? acknowledged = null)
    {
        var text = new StringBuilder()
            .Append("version = 1\n")
            .Append("token = \"").Append(Token).Append("\"\n")
            .Append("base = \"").Append(BaseName).Append("\"\n")
            .Append("control = \"").Append(role).Append("\"\n")
            .Append("record = \"").Append(RecordDigest).Append("\"\n");

        if (acknowledged is not null)
        {
            text.Append("stage = \"").Append(acknowledged).Append("\"\n");
        }

        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    /// <summary>The digest of the record this residue wrote: what every control of it binds to.</summary>
    public string RecordDigest { get; private set; } = string.Empty;

    /// <summary>
    /// The stage-claim path for a target, by its single-character kind code and the identity digest
    /// the claim asserts about the object its create-new produced.
    /// </summary>
    public string StageClaimPath(string kind, string digest) =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-sc-{kind}-{Token}-{digest}");

    /// <summary>
    /// Records that the interrupted transaction created the stage object for <paramref name="target"/>:
    /// the durable fact that separates its own stage from a file that merely occupies the path.
    /// The identity it acknowledges appears in both the name and the body, and the body is spelled
    /// out here rather than produced by the writer under test.
    /// </summary>
    public string WriteStageClaim(string kind, string target)
    {
        var digest = Identity("stage", target);
        var path = StageClaimPath(kind, digest);
        File.WriteAllBytes(path, ControlBody("claim-" + kind, digest));
        return path;
    }

    /// <summary>
    /// Writes one target's identity evidence, spelled out here rather than produced by the writer
    /// under test, so a change to the format fails these tests instead of silently keeping them
    /// green. The two identity <em>values</em> necessarily come from the real filesystem: they name
    /// objects, and only the host can say what an object is.
    /// </summary>
    public void WriteEvidence(string kind, string target, string backup, string stage)
    {
        var text = new StringBuilder()
            .Append("version = 1\n")
            .Append("token = \"").Append(Token).Append("\"\n")
            .Append("base = \"").Append(BaseName).Append("\"\n")
            .Append("target = \"").Append(target).Append("\"\n")
            .Append("backup = \"").Append(backup).Append("\"\n")
            .Append("stage = \"").Append(stage).Append("\"\n");

        File.WriteAllText(EvidencePath(kind), text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The identity digest of the file at <paramref name="path"/>, as this transaction would record it.</summary>
    public string IdentityOf(string path, string role, string target) =>
        IdentityEvidence.Of(Token, role, target, FileIdentity.CreateDefault().KeyFor(path));

    /// <summary>The identity digest of a private file this residue already holds.</summary>
    public string Identity(string role, string target) => IdentityOf(PrivatePath(role, target), role, target);

    /// <summary>
    /// The identity digest of an object that <b>no longer exists</b>: what evidence carries for a
    /// stage a commit rename consumed, or a backup a restore renamed home.
    /// </summary>
    public string ConsumedIdentity(string role, string target)
    {
        var path = PrivatePath(role, target);
        File.WriteAllText(path, "consumed");
        var digest = Identity(role, target);
        File.Delete(path);
        return digest;
    }

    public void WriteRecord(IReadOnlyList<(string Role, string Target)> files)
    {
        var text = new StringBuilder()
            .Append("version = 1\n")
            .Append("token = \"").Append(Token).Append("\"\n")
            .Append("base = \"").Append(BaseName).Append("\"\n");

        foreach (var (role, target) in files)
        {
            text.Append("\n[[file]]\nrole = \"").Append(role).Append("\"\ntarget = \"").Append(target).Append("\"\n");
        }

        var bytes = new UTF8Encoding(false).GetBytes(text.ToString());
        File.WriteAllBytes(RecordPath, bytes);

        // Every control of this transaction binds to this record; the digest is the first 128 bits
        // of its SHA-256, exactly as the writer under test computes it.
        RecordDigest = Convert.ToHexStringLower(SHA256.HashData(bytes).AsSpan(0, 16));
    }

    public void WritePrivate(string role, string target, string content) =>
        File.WriteAllText(PrivatePath(role, target), content, new UTF8Encoding(false));
}
