using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Byte-pins the canonical writer's <c>declared_domain</c> wrapping (D-113): the
/// cutoff is a complete-line measurement of 100 UTF-16 code units over the
/// *final escaped* text, wrapping is one escaped value per line, and no other
/// array is affected. The boundary cases build their expected line in test code
/// and assert its length independently, so an off-by-one in the writer cannot
/// hide behind a fixture the writer itself produced.
/// </summary>
public sealed class DeclaredDomainWrappingTests
{
    // `declared_domain = ` — the key, spaces, and equals sign that every
    // measurement includes. Spelled out here rather than imported so the tests
    // do not inherit the writer's own arithmetic.
    private const string KeyPrefix = "declared_domain = ";

    private const int Cutoff = 100;

    [Fact]
    public void Write_WhenDeclaredDomainLineIsShort_ThenInline()
    {
        var toml = WriteAttributeWithDomain(["red", "blue"]);

        Assert.Contains("declared_domain = [\"red\", \"blue\"]\n", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenDeclaredDomainLineIsExactlyAtCutoff_ThenInline()
    {
        // Two 37-char values: 18 (key) + 1 ([) + 39 + 2 (, ) + 39 + 1 (]) = 100.
        var values = new[] { new string('a', 37), new string('b', 37) };
        var expected = KeyPrefix + "[\"" + values[0] + "\", \"" + values[1] + "\"]";
        Assert.Equal(Cutoff, expected.Length);

        Assert.Contains(expected + "\n", WriteAttributeWithDomain(values), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenDeclaredDomainLineIsOneOverCutoff_ThenWrapped()
    {
        // One code unit longer than the at-cutoff case above.
        var values = new[] { new string('a', 38), new string('b', 37) };
        var wouldBeInline = KeyPrefix + "[\"" + values[0] + "\", \"" + values[1] + "\"]";
        Assert.Equal(Cutoff + 1, wouldBeInline.Length);

        var toml = WriteAttributeWithDomain(values);

        Assert.DoesNotContain(wouldBeInline, toml, StringComparison.Ordinal);
        Assert.Contains(
            "declared_domain = [\n  \"" + values[0] + "\",\n  \"" + values[1] + "\",\n]\n",
            toml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenEscapingPushesLineOverCutoff_ThenWrapped()
    {
        // Both value sets carry the same *raw* lengths (37 and 37); only the
        // escaped rendering differs, so this pins that the measurement is taken
        // over the final escaped text and not over the raw values.
        var plain = new[] { new string('a', 37), new string('b', 37) };
        var quoted = new[] { new string('a', 36) + "\"", new string('b', 37) };
        Assert.Equal(plain[0].Length, quoted[0].Length);

        // The quote escapes to \" — one code unit more than the plain twin.
        var quotedInline = KeyPrefix + "[\"" + new string('a', 36) + "\\\"\", \"" + quoted[1] + "\"]";
        Assert.Equal(Cutoff + 1, quotedInline.Length);

        Assert.Contains(KeyPrefix + "[", WriteAttributeWithDomain(plain), StringComparison.Ordinal);
        Assert.Contains(
            "declared_domain = [\n  \"" + new string('a', 36) + "\\\"\",\n  \"" + quoted[1] + "\",\n]\n",
            WriteAttributeWithDomain(quoted),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenDeclaredDomainAuthoredEmpty_ThenInlineEmptyArray()
    {
        // D-071's authored-[] provenance survives unchanged; wrapping never
        // applies to it (the complete line is 20 code units).
        Assert.Contains("declared_domain = []\n", WriteAttributeWithDomain([]), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenSingleValueExceedsCutoff_ThenWrappedButNotSplit()
    {
        var value = new string('x', 120);
        var toml = WriteAttributeWithDomain([value]);

        Assert.Contains("declared_domain = [\n  \"" + value + "\",\n]\n", toml, StringComparison.Ordinal);
        Assert.Contains("\"" + value + "\"", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenAttributeDomainWraps_ThenExactCanonicalBytes()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("education", 0,
                ["Bachelors", "Masters", "Doctorate", "Prof-school", "Some-college", "HS-grad", "11th"])]);

        Assert.Equal(
            Lines(
                "[spec]",
                "version = 1",
                "",
                "[binding]",
                "shape = \"wide\"",
                "",
                "[[attribute]]",
                "name = \"education\"",
                "source = { kind = \"column\", index = 0 }",
                "declared_domain = [",
                "  \"Bachelors\",",
                "  \"Masters\",",
                "  \"Doctorate\",",
                "  \"Prof-school\",",
                "  \"Some-college\",",
                "  \"HS-grad\",",
                "  \"11th\",",
                "]",
                "discretizer = { kind = \"identity\" }",
                "scale = { kind = \"nominal\" }"),
            SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenTemplateDomainWraps_ThenSameRenderingAsAttribute()
    {
        // D-113 applies to declared_domain authored by [[template]] identically.
        string[] domain = ["Bachelors", "Masters", "Doctorate", "Prof-school", "Some-college", "HS-grad", "11th"];
        var template = new TemplateSection("education_levels", Include: null, Discretizer: null, Scale: null,
            domain, RestrictTo: null, ValueLabels: null, MissingPolicy: null, UnknownValuePolicy: null);

        var toml = SpecWriter.Write(DocumentFixtures.Document([DocumentFixtures.Attribute("a")], templates: [template]));

        Assert.Contains(
            Lines(
                "[[template]]",
                "id = \"education_levels\"",
                "declared_domain = [",
                "  \"Bachelors\",",
                "  \"Masters\",",
                "  \"Doctorate\",",
                "  \"Prof-school\",",
                "  \"Some-college\",",
                "  \"HS-grad\",",
                "  \"11th\",",
                "]"),
            toml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenDomainIsLarge_ThenDeterministicAndFullyWrapped()
    {
        var values = Enumerable.Range(0, 1000).Select(i => $"value-{i:D4}").ToArray();

        var toml = WriteAttributeWithDomain(values);

        Assert.Equal(toml, WriteAttributeWithDomain(values));
        Assert.Contains("declared_domain = [\n", toml, StringComparison.Ordinal);
        Assert.Equal(1000, toml.Split('\n').Count(line => line.StartsWith("  \"value-", StringComparison.Ordinal)));
        Assert.Contains("  \"value-0000\",\n", toml, StringComparison.Ordinal);
        Assert.Contains("  \"value-0999\",\n]\n", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteReadWrite_WhenDomainWraps_ThenCanonicalTextIsIdempotent()
    {
        // The wrapped form must re-read (Tomlyn parses multiline arrays and the
        // trailing comma) and re-write to the identical bytes — the D-075
        // idempotence oracle, now over wrapped output.
        var first = WriteAttributeWithDomain(Enumerable.Range(0, 40).Select(i => $"value-{i:D2}").ToArray());

        Assert.True(SpecReader.Read(first).TryGetValue(out var reread));
        Assert.Equal(first, SpecWriter.Write(reread));
    }

    [Fact]
    public void Write_WhenOtherArraysExceedCutoff_ThenStillInline()
    {
        // D-113 wraps top-level declared_domain and nothing else: restrict_to is
        // the sharpest control (a top-level array sharing the same renderer), and
        // scale.order covers the nested/inline-table case.
        string[] longValues = [new('p', 20), new('q', 20), new('r', 20), new('s', 20)];
        var restrictTo = longValues.Select(v => (RestrictToEntry)new RestrictToValue(v)).ToArray();

        var toml = SpecWriter.Write(DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", restrictTo: restrictTo,
                scale: new OrdinalScaleSection(OrdinalDirection.Le, OrdinalBoundary.Strict, longValues, DropTop: null))]));

        var restrictLine = "restrict_to = [\"" + string.Join("\", \"", longValues) + "\"]";
        Assert.True(restrictLine.Length > Cutoff, "the control line must exceed the declared_domain cutoff");
        Assert.Contains(restrictLine + "\n", toml, StringComparison.Ordinal);
        Assert.Contains("order = [\"" + string.Join("\", \"", longValues) + "\"]", toml, StringComparison.Ordinal);
    }

    private static string WriteAttributeWithDomain(IReadOnlyList<string> domain) =>
        SpecWriter.Write(DocumentFixtures.Document([DocumentFixtures.Nominal("a", 0, domain)]));

    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";
}
