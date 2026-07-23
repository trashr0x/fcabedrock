using System.Globalization;
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
/// Template/matcher application end-to-end on the <b>native</b> path (M6 Slice B,
/// D-121): small in-memory specs and data through the complete production chain —
/// read → compose → resolve → calibrate → plan → emit → write — asserting all
/// three fingerprints <b>and</b> both <c>.cxt</c>/<c>.dat</c> byte outputs for
/// every row of the §9/D-119 neutrality-and-change matrix that templates own.
/// <para>
/// Deliberately separate from the v2 golden comparisons: those are immutable
/// evidence of what v2 produced (P-9), while templates and matchers are native
/// surface no v2 fixture uses. Asserting through the real pipeline rather than the
/// Spec-side fingerprint API is what proves the claim that actually matters —
/// §9.2's "semantically equivalent flat, materialized, template/matcher-authored,
/// and extends-composed specs resolve to … byte-identical output". A fingerprint
/// match alone would leave the writers unproven.
/// </para>
/// </summary>
public sealed class NativeTemplatePipelineTests
{
    private const string Header = "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\nhas_header = false\n\n";

    // Two boolean feature columns plus a missing cell, so `as_attribute` is visible in
    // the output and both dichotomic outcomes occur.
    private const string Csv = "1,0\n0,?\n";

    /// <summary>The materialized form: both features carry the dichotomic config explicitly.</summary>
    private static string Materialized(string extraKeys = "") =>
        Header
        + Feature("feature_1", 0, extraKeys)
        + Feature("feature_2", 1, extraKeys);

    /// <summary>The declarative twin: the same config expressed once, via one template + matcher.</summary>
    private static string Declarative(string extraTemplateKeys = "") =>
        Header
        + "[[template]]\nid = \"flag\"\ndiscretizer = { kind = \"identity\" }\n"
        + "scale = { kind = \"dichotomic\", true_value = \"1\" }\ndeclared_domain = [\"1\", \"0\"]\n"
        + extraTemplateKeys + "\n"
        + "[[matcher]]\nmatch = { name_regex = \"^feature_\\\\d+$\" }\ntemplate = \"flag\"\n\n"
        + "[[attribute]]\nname = \"feature_1\"\nsource = { kind = \"column\", index = 0 }\n\n"
        + "[[attribute]]\nname = \"feature_2\"\nsource = { kind = \"column\", index = 1 }\n";

    private static string Feature(string name, int index, string extraKeys = "") =>
        $"[[attribute]]\nname = \"{name}\"\nsource = {{ kind = \"column\", index = {index} }}\n"
        + "discretizer = { kind = \"identity\" }\nscale = { kind = \"dichotomic\", true_value = \"1\" }\n"
        + "declared_domain = [\"1\", \"0\"]\n" + extraKeys + "\n";

    // --- Representation equivalence: the §9.2 headline claim ---

    [Fact]
    public async Task Convert_WhenDeclarativeVersusMaterialized_ThenEveryStructureHashAndByteIsIdentical()
    {
        // §9.2/D-119's headline claim, asserted on all four of its terms: the same
        // effective attributes, the same plan, the same three fingerprints, and
        // byte-identical .cxt AND .dat. The two documents are written independently, so
        // this compares two specs rather than a spec with itself.
        var materialized = await ConvertAsync(Materialized());
        var declarative = await ConvertAsync(Declarative());

        AssertIdentical(materialized, declarative);

        // Non-vacuity for the structural comparisons: the plan really carries both
        // features' dichotomic columns, so "the plans are equal" is not a claim about two
        // empty projections.
        AssertPlanCovers(materialized, "feature_1", "feature_2");
        AssertPlanCovers(declarative, "feature_1", "feature_2");
        Assert.Equal(2, declarative.ResolvedAttributes.Count);
    }

    [Fact]
    public async Task Convert_WhenFlatVersusEquivalentExtendsSplit_ThenEveryByteAndHashIsIdentical()
    {
        // §13/§14: composition is document→document and application is resolve-time, so
        // splitting the template and matcher into a base spec changes nothing observable.
        var flat = await ConvertAsync(Declarative());

        var baseToml = Header
            + "[[template]]\nid = \"flag\"\ndiscretizer = { kind = \"identity\" }\n"
            + "scale = { kind = \"dichotomic\", true_value = \"1\" }\ndeclared_domain = [\"1\", \"0\"]\n\n"
            + "[[matcher]]\nmatch = { name_regex = \"^feature_\\\\d+$\" }\ntemplate = \"flag\"\n";
        var composed = await ConvertAsync(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n\n"
            + "[[attribute]]\nname = \"feature_1\"\nsource = { kind = \"column\", index = 0 }\n\n"
            + "[[attribute]]\nname = \"feature_2\"\nsource = { kind = \"column\", index = 1 }\n",
            baseToml);

        AssertIdentical(flat, composed);
        AssertPlanCovers(composed, "feature_1", "feature_2");
    }

    [Fact]
    public async Task Convert_WhenADerivedMatcherLayersOverABaseMatcher_ThenItEqualsTheFlatEquivalent()
    {
        // §13 rule 4 through the whole pipeline: base matchers precede derived ones, so
        // the derived template wins the field both author (missing_policy) while the
        // base's other fields survive. Compared against a flat spec written to that same
        // resolved configuration.
        var baseToml = Header
            + "[[template]]\nid = \"b\"\ndiscretizer = { kind = \"identity\" }\n"
            + "scale = { kind = \"dichotomic\", true_value = \"1\" }\ndeclared_domain = [\"1\", \"0\"]\n"
            + "missing_policy = \"as_attribute\"\n\n"
            + "[[matcher]]\nmatch = { name_regex = \"^feature_\\\\d+$\" }\ntemplate = \"b\"\n\n"
            + "[[attribute]]\nname = \"feature_1\"\nsource = { kind = \"column\", index = 0 }\n\n"
            + "[[attribute]]\nname = \"feature_2\"\nsource = { kind = \"column\", index = 1 }\n";
        var composed = await ConvertAsync(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n\n"
            + "[[template]]\nid = \"d\"\nmissing_policy = \"skip\"\n\n"
            + "[[matcher]]\nmatch = { name_regex = \"^feature_\\\\d+$\" }\ntemplate = \"d\"\n",
            baseToml);

        // The derived matcher won: missing_policy is back to skip, so no -missing column.
        var flat = await ConvertAsync(Materialized());
        AssertIdentical(flat, composed);
        AssertPlanCovers(composed, "feature_1", "feature_2");
        Assert.DoesNotContain("missing", composed.Cxt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenADerivedTemplateRetargetsAnInheritedMatcher_ThenItEqualsTheFlatEquivalent()
    {
        // §13's late binding, end to end: replacing a template by `id` in the derived spec
        // re-targets the INHERITED base matcher, so the base's own template body never
        // reaches the output. Proven on bytes, not just on the document.
        var baseToml = Header
            + "[[template]]\nid = \"t\"\ndiscretizer = { kind = \"identity\" }\n"
            + "scale = { kind = \"dichotomic\", true_value = \"9\" }\ndeclared_domain = [\"9\", \"8\"]\n\n"
            + "[[matcher]]\nmatch = { name_regex = \"^feature_\\\\d+$\" }\ntemplate = \"t\"\n\n"
            + "[[attribute]]\nname = \"feature_1\"\nsource = { kind = \"column\", index = 0 }\n\n"
            + "[[attribute]]\nname = \"feature_2\"\nsource = { kind = \"column\", index = 1 }\n";
        var composed = await ConvertAsync(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n\n"
            + "[[template]]\nid = \"t\"\ndiscretizer = { kind = \"identity\" }\n"
            + "scale = { kind = \"dichotomic\", true_value = \"1\" }\ndeclared_domain = [\"1\", \"0\"]\n",
            baseToml);

        AssertIdentical(await ConvertAsync(Materialized()), composed);
        AssertPlanCovers(composed, "feature_1", "feature_2");

        // The base template's own body never reached the effective attributes — proof the
        // inherited matcher retargeted, rather than the base simply being dropped.
        Assert.All(composed.ResolvedAttributes, line =>
        {
            Assert.Contains("domain=[1,0]", line, StringComparison.Ordinal);
            Assert.DoesNotContain("domain=[9,8]", line, StringComparison.Ordinal);
        });
    }

    // --- Neutrality: syntax that configures nothing changes nothing ---

    [Fact]
    public async Task Convert_WhenAnUnusedTemplateIsAdded_ThenEveryByteAndHashIsUnchanged()
    {
        // §9.2: an unused template is semantically dormant — it converts without error and
        // without touching a byte, even carrying config that WOULD change the output.
        var baseline = await ConvertAsync(Materialized());
        var withTemplate = await ConvertAsync(
            Materialized() + "\n[[template]]\nid = \"unused\"\nmissing_policy = \"as_attribute\"\n");

        AssertIdentical(baseline, withTemplate);
    }

    [Fact]
    public async Task Convert_WhenAMatcherMatchesNothing_ThenOnlyAWarningIsAdded()
    {
        // D-119's neutrality row: a matcher that selects nothing adds ONLY its Warning —
        // asserted alongside the successful conversion, so a warning-only result is
        // proven to still produce complete, byte-identical artifacts.
        var baseline = await ConvertAsync(Materialized());
        var withMatcher = await ConvertAsync(
            Materialized()
            + "\n[[template]]\nid = \"t\"\nmissing_policy = \"as_attribute\"\n"
            + "\n[[matcher]]\nmatch = { name_regex = \"^zz.*$\" }\ntemplate = \"t\"\n");

        AssertIdentical(baseline, withMatcher);
        Assert.Equal(DiagnosticCode.MatcherSelectsNoAttributes, Assert.Single(withMatcher.ResolveDiagnostics).Code);
    }

    [Fact]
    public async Task Convert_WhenAMatcherIsFullyShadowed_ThenOnlyAWarningIsAdded()
    {
        // D-119's uncurated-draft row in miniature: the matcher SELECTS both attributes,
        // but every field its template authors loses to their explicit ones, so the output
        // is byte-identical to the template-free spec and the Warning is the only signal
        // the author gets — which is exactly why the condition exists.
        var baseline = await ConvertAsync(Materialized());
        var shadowed = await ConvertAsync(
            Materialized()
            + "\n[[template]]\nid = \"t\"\ndiscretizer = { kind = \"identity\" }\n"
            + "scale = { kind = \"dichotomic\", true_value = \"0\" }\ndeclared_domain = [\"0\"]\n"
            + "\n[[matcher]]\nmatch = { name_regex = \"^feature_\\\\d+$\" }\ntemplate = \"t\"\n");

        AssertIdentical(baseline, shadowed);
        Assert.Equal(DiagnosticCode.MatcherFullyShadowed, Assert.Single(shadowed.ResolveDiagnostics).Code);
    }

    // --- Change: a semantic template edit moves every axis ---

    [Fact]
    public async Task Convert_WhenATemplateAddsAColumn_ThenEveryAxisMovesLikeTheFlatEdit()
    {
        // The change row, and the proof that the neutrality rows above are a property of
        // resolved semantics rather than of templates being ignored: adding
        // missing_policy = "as_attribute" to the TEMPLATE plans a new column, so all three
        // fingerprints and BOTH outputs move — and land exactly where the equivalent flat
        // edit lands.
        var before = await ConvertAsync(Declarative());
        var after = await ConvertAsync(Declarative("missing_policy = \"as_attribute\"\n"));

        // Every axis moves — including the two structural snapshots. Asserting those
        // differ is also what proves the equivalence rows above are not passing on an
        // insensitive projection: the same comparison that reports "equal" for equivalent
        // specs reports "different" for a genuine plan change.
        Assert.NotEqual(before.ResolvedAttributes, after.ResolvedAttributes);
        Assert.NotEqual(before.Plan, after.Plan);
        Assert.NotEqual(before.Fingerprints.SchemaFingerprint, after.Fingerprints.SchemaFingerprint);
        Assert.NotEqual(before.Fingerprints.CxtOutputFingerprint, after.Fingerprints.CxtOutputFingerprint);
        Assert.NotEqual(before.Fingerprints.DatOutputFingerprint, after.Fingerprints.DatOutputFingerprint);
        Assert.NotEqual(before.Cxt, after.Cxt);
        Assert.NotEqual(before.Dat, after.Dat);

        // The new columns are real, and the template edit equals the flat edit.
        Assert.Contains("feature_1-missing", after.Cxt, StringComparison.Ordinal);
        AssertPlanCovers(after, "feature_1", "feature_1-missing", "feature_2", "feature_2-missing");
        AssertIdentical(await ConvertAsync(Materialized("missing_policy = \"as_attribute\"\n")), after);
    }

    [Fact]
    public async Task Convert_WhenATemplateSuppliesNaming_ThenOnlyCxtMoves()
    {
        // §10.7/D-117 reached through a TEMPLATE rather than the attribute: naming changes
        // rendered .cxt names and the cxt fingerprint only. Template naming was carried
        // but inert at Slice A; application is what makes it live.
        var baseline = await ConvertAsync(Declarative());
        var named = await ConvertAsync(Declarative("formal_attribute_format = \"{column}::{value}\"\n"));

        Assert.NotEqual(baseline.Cxt, named.Cxt);
        Assert.NotEqual(baseline.Fingerprints.CxtOutputFingerprint, named.Fingerprints.CxtOutputFingerprint);

        Assert.Equal(baseline.Dat, named.Dat);
        Assert.Equal(baseline.Fingerprints.DatOutputFingerprint, named.Fingerprints.DatOutputFingerprint);
        Assert.Equal(baseline.Fingerprints.SchemaFingerprint, named.Fingerprints.SchemaFingerprint);

        Assert.Contains("feature_1::1", named.Cxt, StringComparison.Ordinal);

        // The plan snapshot DOES move (rendered names live in it) even though the schema
        // fingerprint does not — canonical identity is naming-independent (§14). That
        // asymmetry is the concrete reason the equivalence rows compare the plan as well
        // as the hashes: the snapshot sees changes the schema hash is designed not to.
        Assert.NotEqual(baseline.Plan, named.Plan);
        AssertPlanCovers(named, "feature_1::1", "feature_2::1");
    }

    // --- Determinism ---

    [Fact]
    public async Task Convert_WhenRunTwice_ThenTheArtifactsAreByteIdentical()
    {
        // P-7 through the whole template path, including the warning traversal.
        var spec = Declarative() + "\n[[matcher]]\nmatch = { name_regex = \"^zz$\" }\ntemplate = \"flag\"\n";

        var first = await ConvertAsync(spec);
        var second = await ConvertAsync(spec);

        AssertIdentical(first, second);
        Assert.Equal(
            first.ResolveDiagnostics.Select(d => (d.Code, d.Severity, d.Message)),
            second.ResolveDiagnostics.Select(d => (d.Code, d.Severity, d.Message)));
    }

    // ---- Harness ----

    /// <summary>
    /// The complete equivalence assertion: **structures first, then hashes, then bytes**.
    /// <para>
    /// §9.2's claim is that equivalent specs resolve to "the same effective attributes,
    /// the same plan, the same three fingerprints, and byte-identical output" — four
    /// claims, so all four are asserted. Comparing only hashes and bytes would leave the
    /// first two inferred rather than shown: a fingerprint is a lossy projection of the
    /// plan (it deliberately omits, for instance, row-shaping state), so plan or
    /// effective-attribute divergence that this dataset does not happen to expose could
    /// pass every byte comparison. The structures are compared first so a failure names
    /// the real divergence instead of an opaque hash mismatch.
    /// </para>
    /// </summary>
    private static void AssertIdentical(Converted expected, Converted actual)
    {
        Assert.Equal(expected.ResolvedAttributes, actual.ResolvedAttributes);
        Assert.Equal(expected.Plan, actual.Plan);

        Assert.Equal(expected.Fingerprints.SchemaFingerprint, actual.Fingerprints.SchemaFingerprint);
        Assert.Equal(expected.Fingerprints.CxtOutputFingerprint, actual.Fingerprints.CxtOutputFingerprint);
        Assert.Equal(expected.Fingerprints.DatOutputFingerprint, actual.Fingerprints.DatOutputFingerprint);

        Assert.Equal(expected.Cxt, actual.Cxt);
        Assert.Equal(expected.Dat, actual.Dat);
    }

    /// <summary>
    /// Non-vacuity for the structural halves of <see cref="AssertIdentical"/>: the
    /// snapshots must actually describe the expected formal attributes, so "the plans are
    /// equal" is never a statement about two empty projections.
    /// </summary>
    private static void AssertPlanCovers(Converted converted, params string[] renderedNames)
    {
        Assert.NotEmpty(converted.ResolvedAttributes);
        Assert.NotEmpty(converted.Plan);
        foreach (var name in renderedNames)
        {
            Assert.Contains(converted.Plan, line => line.Contains($"rendered={name}|", StringComparison.Ordinal));
        }
    }

    private sealed record Converted(
        string Cxt,
        string Dat,
        ComputedFingerprints Fingerprints,
        IReadOnlyList<BedrockDiagnostic> ResolveDiagnostics,
        IReadOnlyList<string> ResolvedAttributes,
        IReadOnlyList<string> Plan);

    /// <summary>
    /// A deterministic, exhaustive description of the <b>effective</b> resolved
    /// attributes — what template application actually produced, before any planning.
    /// <para>
    /// Spelled out rather than comparing <c>AttributeSpec</c> records: record equality
    /// compares each member with <c>EqualityComparer&lt;T&gt;.Default</c>, and the
    /// collection members are interface-typed, so two structurally identical domains held
    /// in different arrays compare unequal.
    /// </para>
    /// </summary>
    private static string[] DescribeAttributes(BedrockSpec spec) =>
        [.. spec.Attributes.Select(a => string.Join("|",
            $"name={a.Name}",
            $"source={a.Source}",
            $"include={a.Include}",
            $"discretizer={a.Discretizer}",
            $"scale={a.Scale}",
            $"domain=[{string.Join(",", a.DeclaredDomain ?? [])}]",
            $"restrict=[{string.Join(",", a.RestrictTo)}]",
            $"labels=[{string.Join(",", a.ValueLabels.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}]",
            $"missing={a.MissingPolicy}",
            $"unknown={a.UnknownValuePolicy}",
            $"display={a.DisplayName}",
            $"format={a.NameFormat?.Text ?? "<none>"}"))];

    /// <summary>
    /// A deterministic, exhaustive description of the planned schema: every formal
    /// attribute (id, rendered name, canonical identity, structural bin), every planned
    /// attribute (source, discretizer, known bins, cross map, missing column, policy),
    /// every restriction, plus the object key, execution shape, and label style.
    /// <para>
    /// Unordered members (<c>KnownBins</c>, <c>CrossesByBin</c>) are ordinally sorted so
    /// the projection is stable without discarding their content (P-12). This is
    /// deliberately wider than the fingerprint inputs — the point is to catch divergence
    /// the hashes are not designed to expose.
    /// </para>
    /// </summary>
    private static string[] DescribePlan(ConversionPlan plan)
    {
        var lines = new List<string>();

        foreach (var formal in plan.FormalAttributes)
        {
            lines.Add(string.Join("|",
                $"formal#{formal.Id}",
                $"rendered={formal.RenderedName}",
                $"identity={formal.Identity}",
                $"bin={formal.Bin}"));
        }

        foreach (var attribute in plan.Attributes)
        {
            var crosses = attribute.CrossesByBin
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}=>[{string.Join(",", p.Value)}]");
            lines.Add(string.Join("|",
                $"planned={attribute.Name}",
                $"source={attribute.Source}",
                $"discretizer={attribute.Discretizer}",
                $"knownBins=[{string.Join(",", attribute.KnownBins.OrderBy(b => b, StringComparer.Ordinal))}]",
                $"crosses=[{string.Join(";", crosses)}]",
                // Invariant formatting: an ambient-culture int render would make this
                // projection machine-dependent, which is exactly what it exists to rule out.
                $"missingId={attribute.MissingFormalAttributeId?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}",
                $"unknown={attribute.UnknownValuePolicy}"));
        }

        foreach (var restriction in plan.Restrictions)
        {
            lines.Add(string.Join("|",
                $"restriction={restriction.AttributeName}",
                $"source={restriction.Source}",
                $"valueType={restriction.ValueType}",
                $"entries=[{string.Join(",", restriction.Entries)}]",
                $"unknown={restriction.UnknownValuePolicy}"));
        }

        lines.Add($"objectKey={plan.ObjectKey}");
        lines.Add($"execution={plan.Execution}");
        lines.Add($"labelStyle={plan.LabelStyle}");
        return [.. lines];
    }

    // The real production chain, exactly as GoldenConversion drives it for the fixtures:
    // bootstrap read settings → session → schema-aware resolve → bind → calibrate → plan →
    // emit → write, plus the native fingerprints over that same resolved document and plan.
    private static async Task<Converted> ConvertAsync(string toml, string? baseToml = null, string csv = Csv)
    {
        var (resolvedDocument, plan, emit, diagnostics) = await PrepareAsync(toml, baseToml, csv);

        using var cxtStream = new MemoryStream();
        var cxtDiagnostics = new List<BedrockDiagnostic>();
        using (var session = EmitReplay.Begin(emit, cxtDiagnostics))
        {
            await CxtWriter.WriteAsync(plan, session.Open, WriterOptions.Native, cxtStream);
        }

        using var datStream = new MemoryStream();
        await DatWriter.WriteAsync(emit(new List<BedrockDiagnostic>()), WriterOptions.Native, datStream);

        return new Converted(
            Encoding.UTF8.GetString(cxtStream.ToArray()),
            Encoding.UTF8.GetString(datStream.ToArray()),
            SpecFingerprints.ComputeNative(resolvedDocument, plan),
            diagnostics,
            DescribeAttributes(resolvedDocument.Resolved.Spec),
            DescribePlan(plan));
    }

    private static async Task<(ResolvedDocument Document, ConversionPlan Plan,
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> Emit,
        IReadOnlyList<BedrockDiagnostic> ResolveDiagnostics)> PrepareAsync(
        string toml, string? baseToml, string csv)
    {
        var document = Compose(toml, baseToml);
        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), Describe(settings.Diagnostics));

        var session = new WideCsvSession(() => new MemoryStream(Encoding.UTF8.GetBytes(csv)), readSettings);
        var schema = await session.GetSchemaAsync();

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument), Describe(resolved.Diagnostics));

        var token = resolvedDocument.Resolved;
        var source = session.Bind(token);
        var calibrated = CalibratedSpec.RequiresData(token.Spec)
            ? await CalibrateAsync(token, source)
            : CalibratedSpec.FromFullyDeclared(token);

        var planned = ConversionPlanner.Plan(calibrated);
        Assert.True(planned.TryGetValue(out var plan), Describe(planned.Diagnostics));

        return (resolvedDocument, plan, sink => Emitter.EmitAsync(plan, source, sink), resolved.Diagnostics);
    }

    private static async Task<CalibratedSpec> CalibrateAsync(ResolvedSpec token, WideCsvSource source)
    {
        var result = await Calibrator.CalibrateAsync(token, source);
        Assert.True(result.TryGetValue(out var calibrated), Describe(result.Diagnostics));
        return calibrated;
    }

    private static SpecDocument Compose(string toml, string? baseToml)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));
        if (baseToml is null)
        {
            return document;
        }

        var composed = SpecComposer.Compose(document, "derived.toml", new SingleBaseSource(baseToml));
        Assert.True(composed.TryGetValue(out var result), Describe(composed.Diagnostics));
        return result;
    }

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private sealed class SingleBaseSource(string toml) : ISpecTextSource
    {
        public SpecSourceText? Load(string reference, string referrerKey) =>
            reference == "base.toml" ? new SpecSourceText(reference, toml) : null;
    }
}
