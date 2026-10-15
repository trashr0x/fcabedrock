using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// A probe draft that omits categories containing CR or LF saves that fact in the file itself
/// (spec §7.1), so it survives every later step that rewrites the spec: a caller's own edits,
/// <c>calibrate</c>, and <c>fingerprint --write</c>. A spec that extends the result does not
/// inherit the notes (§13 rule 7). Through argv and real files, with the converted incidence
/// checked against the data.
/// </summary>
public sealed class ProbeOmissionPersistenceTests
{
    private static readonly string Replacement = ((char)0xFFFD).ToString();

    // Four objects. `id` truncates at --limit 3; `note` is complete and mixes safe and multiline
    // categories; every `memo` category is multiline, one with a genuine U+FFFD; `gone` is all
    // missing.
    private static readonly string Data =
        "id,note,memo,gone\n"
        + "1,a,\"m" + Replacement + "\nn\",?\n"
        + "2,\"x\ny\",\"o\rp\",\n"
        + "3,b,\"m" + Replacement + "\nn\",?\n"
        + "4,a,\"o\rp\",?\n";

    private const string Notes =
        "probe: WARNING: omitted 3 distinct categories containing CR/LF across 2 attributes (counts are per attribute). "
        + "These categories produce no crosses; objects are kept. Review the affected attribute descriptions.\n"
        + "probe: per-attribute retention limit 3; truncated attributes: 1.";

    private const string NoteDescription =
        "probe: omitted 1 distinct categories containing CR/LF; retained 2 representable categories. "
        + "Omitted examples: \"x\\ny\".";

    private static readonly string MemoDescription =
        "probe: omitted 2 distinct categories containing CR/LF; retained 0 representable categories. "
        + "Omitted examples: \"m" + Replacement + "\\nn\", \"o\\rp\". "
        + "The declared domain is fixed empty; unknown values warn.";

    private const string Marker = "probe: domain truncated after 3 distinct values; more exist.";

    private const string Enrichment = " Checked by the analyst.";

    private static SpecDocument Read(string path)
    {
        var read = SpecReader.Read(File.ReadAllText(path, new UTF8Encoding(false)));
        Assert.True(read.TryGetValue(out var document));
        return document;
    }

    private static AttributeSection Attribute(SpecDocument document, string name) =>
        document.Attributes.Single(a => a.Name == name);

    [Fact]
    public async Task Probe_WhenItOmitsCategories_ThenItsNoticesSurviveEnrichmentCalibrateAndFingerprintWrite()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", Data);
        var draftPath = temp.Resolve("draft.toml");

        var probe = new CliTestHarness();
        Assert.Equal(0, await probe.RunAsync("probe", data, "--shape", "wide", "--limit", "3", "--out", draftPath));

        // The producer's sample escapes once, and the CLI escapes the message once more.
        Assert.Equal(
            "warning ProbeDomainTruncated: The probe truncated 1 attribute domain(s) after 3 distinct values (e.g. id); "
            + "each authors its retained prefix with unknown_value_policy = \"include\" (§7.1).\n"
            + "warning ProbeValueOmitted: The probe omitted 3 distinct categories containing CR/LF across 2 attribute(s) "
            + "(counts are per attribute; e.g. \"note\": \"x\\\\ny\", \"memo\": \"m" + Replacement + "\\\\nn\", "
            + "\"memo\": \"o\\\\rp\"); they produce no crosses, every object is kept, and the draft records the omission "
            + "in its notes and the affected attribute descriptions (§7.1).\n",
            probe.StdErr);

        // The saved text: an escaped LF between the two notes lines, a doubled backslash for the
        // sample's own escape, and the replacement character escaped, never written raw.
        var draftText = File.ReadAllText(draftPath, new UTF8Encoding(false));
        Assert.Contains("descriptions.\\nprobe: per-attribute", draftText, StringComparison.Ordinal);
        Assert.Contains("\\\"m\\uFFFD\\\\nn\\\"", draftText, StringComparison.Ordinal);
        Assert.DoesNotContain(Replacement, draftText, StringComparison.Ordinal);

        var draft = Read(draftPath);
        Assert.Equal(Notes, draft.Provenance!.Notes);
        Assert.Equal(NoteDescription, Attribute(draft, "note").Description);
        Assert.Equal(MemoDescription, Attribute(draft, "memo").Description);
        Assert.Equal(Marker, Attribute(draft, "id").Description);

        // A caller enriches the document it was handed.
        var note = Attribute(draft, "note");
        var enriched = draft with
        {
            Provenance = draft.Provenance with { Author = "Analyst" },
            Attributes = [.. draft.Attributes.Select(a => a == note ? a with { Description = a.Description + Enrichment } : a)],
        };
        var enrichedPath = temp.Write("enriched.toml", SpecWriter.Write(enriched));

        var calibratedPath = temp.Resolve("calibrated.toml");
        Assert.Equal(0, await new CliTestHarness().RunAsync("calibrate", enrichedPath, data, "--out", calibratedPath));
        var fingerprintedPath = temp.Resolve("fingerprinted.toml");
        Assert.Equal(0, await new CliTestHarness().RunAsync(
            "fingerprint", calibratedPath, data, "--write", "--out", fingerprintedPath));

        foreach (var path in new[] { calibratedPath, fingerprintedPath })
        {
            var document = Read(path);
            Assert.Equal("Analyst", document.Provenance!.Author);
            Assert.Equal(Notes, document.Provenance.Notes);
            Assert.Equal(NoteDescription + Enrichment, Attribute(document, "note").Description);
            Assert.Equal(MemoDescription, Attribute(document, "memo").Description);
            Assert.Equal(Marker, Attribute(document, "id").Description);

            // The domains the freeze settled: the recovered id tail, the omitting domains as
            // authored, and the all-missing column frozen empty.
            Assert.Equal(["1", "2", "3", "4"], Attribute(document, "id").DeclaredDomain);
            Assert.Equal(["a", "b"], Attribute(document, "note").DeclaredDomain);
            Assert.Equal(UnknownValuePolicy.Warn, Attribute(document, "note").UnknownValuePolicy);
            Assert.Empty(Attribute(document, "memo").DeclaredDomain!);
            Assert.Equal(UnknownValuePolicy.Warn, Attribute(document, "memo").UnknownValuePolicy);
            Assert.Empty(Attribute(document, "gone").DeclaredDomain!);
        }

        // A derived root does not inherit the base's notes.
        var derivedPath = temp.Write("derived.toml", "[spec]\nversion = 1\nextends = \"fingerprinted.toml\"\n");
        var derivedWritten = temp.Resolve("derived-written.toml");
        Assert.Equal(0, await new CliTestHarness().RunAsync(
            "fingerprint", derivedPath, data, "--write", "--out", derivedWritten));
        var derivedText = File.ReadAllText(derivedWritten, new UTF8Encoding(false));
        Assert.DoesNotContain("[provenance]", derivedText, StringComparison.Ordinal);
        Assert.DoesNotContain("probe:", derivedText, StringComparison.Ordinal);

        // Every object converts, each with the crosses its data gives: ids 1 to 4 are columns 1
        // to 4, note-a and note-b are 5 and 6, and the multiline categories cross nothing.
        var convert = new CliTestHarness();
        Assert.Equal(0, await convert.RunAsync(
            "convert", fingerprintedPath, data, "--out", temp.Resolve("out"), "--format", "dat", "--no-manifest"));
        Assert.Equal("1 5\n2\n3 6\n4 5\n", File.ReadAllText(temp.Resolve("out.dat")));
    }
}
