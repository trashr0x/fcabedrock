using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Golden.Tests;

/// <summary>
/// Spec conformance for <c>value_groups</c> (§11.6/§12.3/§17, M4 Slice E / D-090/D-104).
/// <para>
/// Unlike the rest of this suite these are not driven off a v2 golden — no v2 fixture uses
/// <c>value_groups</c>, and the discretizer has no v2 counterpart to be byte-compared against
/// (§11.6 is native surface). Instead each test runs the spec's <b>own normative example</b>
/// verbatim, through the complete authored-TOML → read → resolve → calibrate → plan → emit →
/// write pipeline, and asserts the native bytes match what §11.6 says they should be. Agreement
/// with the spec, not merely with the implementation (P-8).
/// </para>
/// </summary>
public sealed class ValueGroupsConformanceTests
{
    private const string Header = "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\nhas_header = false\n\n";

    // Drives the real production chain end to end, exactly as GoldenConversion does for the
    // fixtures: bootstrap read settings → session → schema-aware resolve → bind → calibrate →
    // plan → emit → write.
    private static async Task<string> ConvertAsync(string toml, string csv)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document),
            string.Join("; ", read.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var settingsResult = SpecResolver.ResolveReadSettings(document!);
        Assert.True(settingsResult.TryGetValue(out var settings));

        Stream OpenStream() => new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var session = new WideCsvSession(OpenStream, settings!);
        var schema = await session.GetSchemaAsync();

        var resolved = SpecResolver.Resolve(document!, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var token = resolvedDocument!.Resolved;
        var calibrated = CalibratedSpec.RequiresData(token.Spec)
            ? await CalibrateAsync(token, session)
            : CalibratedSpec.FromFullyDeclared(token);

        Assert.True(ConversionPlanner.Plan(calibrated).TryGetValue(out var plan),
            "the plan must succeed for a conformance example");

        using var output = new MemoryStream();
        await CxtWriter.WriteAsync(
            plan!,
            () => Emitter.EmitAsync(plan!, session.Bind(token), new List<BedrockDiagnostic>()),
            WriterOptions.Native,
            output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static async Task<CalibratedSpec> CalibrateAsync(ResolvedSpec token, WideCsvSession session)
    {
        var result = await Calibrator.CalibrateAsync(token, session.Bind(token));
        Assert.True(result.TryGetValue(out var calibrated),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return calibrated!;
    }

    [Fact]
    public async Task ValueGroups_WhenTheSpecsNominalExample_ThenOneColumnPerGroupInDeclarationOrder()
    {
        // §11.6's opening example verbatim (Uni-Degree before School — deliberately NOT
        // alphabetical), with the default unmatched = "skip".
        var toml = Header
            + "[[attribute]]\nname = \"education\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"value_groups\", groups = ["
            + "{ label = \"Uni-Degree\", values = [\"Bachelors\", \"Masters\", \"PhD\"] }, "
            + "{ label = \"School\", values = [\"11th\", \"HS-grad\"] }], unmatched = \"skip\" }\n"
            + "scale = { kind = \"nominal\" }\n"
            + "unknown_value_policy = \"skip\"\n";

        var cxt = await ConvertAsync(toml, "Bachelors\n11th\nPreschool");

        // §17 rule 3: declaration order, so Uni-Degree is column 0. §11.6: "Preschool" matches no
        // group and unmatched = "skip" gives it no bin — an empty row, not an error.
        Assert.Equal(
            "B\n\n3\n2\n\n0\n1\n2\neducation-Uni-Degree\neducation-School\nX.\n.X\n..\n",
            cxt);
    }

    [Fact]
    public async Task ValueGroups_WhenTheSpecsOrdinalOverGroupsExample_ThenCumulativeThresholdsIncludingOther()
    {
        // §11.6's "Ordinal over groups with an Other bin" example, verbatim — including its
        // full-permutation order ["School", "Undergrad", "Postgrad", "Other"].
        var toml = Header
            + "[[attribute]]\nname = \"education\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"value_groups\", unmatched = \"other\", groups = ["
            + "{ label = \"School\", values = [\"11th\", \"HS-grad\"] }, "
            + "{ label = \"Undergrad\", values = [\"Bachelors\"] }, "
            + "{ label = \"Postgrad\", values = [\"Masters\", \"PhD\"] }] }\n"
            + "scale = { kind = \"ordinal\", direction = \"ge\", "
            + "order = [\"School\", \"Undergrad\", \"Postgrad\", \"Other\"] }\n";

        var cxt = await ConvertAsync(toml, "HS-grad\nBachelors\nPhD\nPreschool");

        // ge+inclusive over the authored order: each object crosses its own threshold and every
        // one below it. "Preschool" falls into the synthetic Other, which the author placed last,
        // so it crosses all four.
        Assert.Equal(
            "B\n\n4\n4\n\n0\n1\n2\n3\n"
                + "education->=School\neducation->=Undergrad\neducation->=Postgrad\neducation->=Other\n"
                + "X...\nXX..\nXXX.\nXXXX\n",
            cxt);
    }

    [Fact]
    public async Task ValueGroups_WhenTheSpecsRegexExample_ThenPartialUnanchoredCaseSensitiveMatching()
    {
        // §11.6's regex example verbatim: pattern = "^I[0-9]{2}" for ICD-10 cardiac codes, plus
        // the section's stated semantics — partial (unanchored) and case-sensitive by default.
        var toml = Header
            + "[[attribute]]\nname = \"dx\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"value_groups\", groups = ["
            + "{ label = \"ICD-10-Cardiac\", pattern = \"^I[0-9]{2}\" }], unmatched = \"other\" }\n"
            + "scale = { kind = \"nominal\" }\n";

        var cxt = await ConvertAsync(toml, "I21\nI219\ni21\nE11");

        // "I21" and the longer "I219" both match (the pattern is unanchored at the right, so it
        // matches a prefix); "i21" does NOT (case-sensitive by default); "E11" does not.
        Assert.Equal(
            "B\n\n4\n2\n\n0\n1\n2\n3\ndx-ICD-10-Cardiac\ndx-Other\nX.\nX.\n.X\n.X\n",
            cxt);
    }

    [Fact]
    public async Task ValueGroups_WhenPassthrough_ThenTheDataDiscoversOneColumnPerUngroupedValue()
    {
        // §11.6: "values not matching any group keep their raw value as the bin label (mixed
        // grouped and ungrouped attributes)" — the data-dependent schema, resolved in Calibrate
        // (§7) and warned about. Run through the real calibrate step, not a hand-built plan.
        var toml = Header
            + "[[attribute]]\nname = \"education\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"value_groups\", groups = ["
            + "{ label = \"School\", values = [\"11th\", \"HS-grad\"] }], unmatched = \"passthrough\" }\n"
            + "scale = { kind = \"nominal\" }\n";

        var cxt = await ConvertAsync(toml, "11th\nBachelors\nHS-grad\nPhD");

        // §17 rule 3: the declared group first, then the discovered bins in first-observation
        // order (Bachelors before PhD — their input order, not alphabetical).
        Assert.Equal(
            "B\n\n4\n3\n\n0\n1\n2\n3\neducation-School\neducation-Bachelors\neducation-PhD\n"
                + "X..\n.X.\nX..\n..X\n",
            cxt);
    }

    [Fact]
    public async Task ValueGroups_WhenIncludeUnderSkip_ThenItBehavesAsWarnWithNoSchemaExtension()
    {
        // §11.6's closing rule: value_groups does not consult declared_domain (D-055), so
        // `include` has nothing to extend and behaves as `warn` — no bin, one Warning, and no
        // column added for the unmatched value.
        var toml = Header
            + "[[attribute]]\nname = \"education\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"value_groups\", groups = ["
            + "{ label = \"School\", values = [\"11th\"] }] }\n"
            + "scale = { kind = \"nominal\" }\n"
            + "unknown_value_policy = \"include\"\n";

        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document));
        var resolved = SpecResolver.Resolve(document!, new SourceSchema(1));
        Assert.True(resolved.TryGetValue(out var resolvedDocument));

        // The spec stays fully declared: `include` must not make it data-dependent (§7).
        Assert.False(CalibratedSpec.RequiresData(resolvedDocument!.Resolved.Spec));

        var cxt = await ConvertAsync(toml, "11th\nPhD");

        Assert.Equal("B\n\n2\n1\n\n0\n1\neducation-School\nX\n.\n", cxt);
    }
}
