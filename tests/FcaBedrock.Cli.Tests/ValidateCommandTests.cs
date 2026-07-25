using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The validate vertical (D-122 part 10), end to end through argv.
/// </summary>
public sealed class ValidateCommandTests
{
    private static string Render(IEnumerable<BedrockDiagnostic> diagnostics)
    {
        var writer = new StringWriter();
        DiagnosticRenderer.Write(writer, diagnostics);
        return writer.ToString();
    }

    // The library's own answer for the same document, used as the oracle: the CLI must
    // reproduce it exactly rather than compute its own.
    private static Diagnosed<ResolvedDocument> ResolveWithLibrary(string toml, string filePath, SourceSchema? schema)
    {
        var read = SpecReader.Read(toml, Path.GetFullPath(filePath));
        Assert.True(read.TryGetValue(out var document));
        return SpecResolver.Resolve(document, schema);
    }

    // ---- the no-DATA form -----------------------------------------------------------

    [Fact]
    public async Task Validate_WhenTheSpecIsFullyDeclared_ThenExitZeroAndSilence()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenNoDataIsSupplied_ThenTheExistingNoSchemaResolutionAppliesVerbatim()
    {
        // A name-bound source cannot resolve without a schema. The CLI must report exactly
        // what SpecResolver.Resolve(document, null) reports — same codes, same order, same
        // locations, same messages.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        var oracle = ResolveWithLibrary(CliFixtures.NameBoundSpec, spec, schema: null);
        Assert.NotEmpty(oracle.Diagnostics);
        Assert.Equal(1, exit);
        Assert.Equal(Render(oracle.Diagnostics), harness.StdErr);
    }

    // ---- the DATA form --------------------------------------------------------------

    [Fact]
    public async Task Validate_WhenDataIsSupplied_ThenTheSchemaResolvesTheNameBinding()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenTheBoundColumnIsAbsentFromTheSchema_ThenTheLibraryDiagnosticIsReported()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.MissingColumnSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, data);

        var session = new WideCsvSession(
            () => File.OpenRead(data), SourceReadSettings.CreateWide(hasHeader: true));
        var oracle = ResolveWithLibrary(CliFixtures.MissingColumnSpec, spec, await session.GetSchemaAsync());

        Assert.Equal(1, exit);
        Assert.Equal(Render(oracle.Diagnostics), harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenTheSourceIsTriple_ThenTheTripleSettingsApply()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.TripleSpec);
        var data = temp.Write("data.csv", CliFixtures.TripleData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenTheSourceIsHeaderless_ThenTheSchemaIsTheFirstRecordsWidth()
    {
        // has_header = false: the schema is a column count, so an index binding past the
        // width is the resolver's own range error and one inside it resolves.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", "red,1\ngreen,2\n");
        var harness = new CliTestHarness();

        var inRange = temp.Write("in-range.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = false

            [[attribute]]
            name = "colour"
            source = { kind = "column", index = 1 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["1", "2"]
            """);

        Assert.Equal(0, await harness.RunAsync("validate", inRange, data));
        Assert.Equal(string.Empty, harness.StdErr);

        var outOfRange = temp.Write("out-of-range.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = false

            [[attribute]]
            name = "colour"
            source = { kind = "column", index = 9 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["x"]
            """);

        var second = new CliTestHarness();
        Assert.Equal(1, await second.RunAsync("validate", outOfRange, data));
        Assert.Contains("SourceBindingInvalid", second.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_WhenTheRootFailsToParse_ThenCompositionIsNeverAttempted()
    {
        // The stages run in order and stop at the first that cannot produce a document:
        // a broken root reports its own parse diagnostics and no base is ever consulted,
        // so no cascade of composition or resolve noise follows.
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("base.toml", CliFixtures.IndexBoundSpec);
        var spec = temp.Write("spec.toml", "[spec]\nversion = 1\nextends = \"./base.toml\"\nnot_a_key = 1\n");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        var lines = harness.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1, exit);
        Assert.Single(lines);
        Assert.Contains("SpecKeyUnrecognized", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain(basePath, harness.Opened);
    }

    [Fact]
    public async Task Validate_WhenReadSettingsCannotResolve_ThenDataIsNeverOpened()
    {
        // A triple binding without `ordering` fails the schema-independent stage, so the
        // source is never opened at all.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1

            [binding]
            shape = "triple"
            """);
        var data = temp.Write("data.csv", CliFixtures.TripleData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(data, harness.Opened);
        Assert.Contains("SourceBindingInvalid", harness.StdErr, StringComparison.Ordinal);
    }

    // ---- exit codes ------------------------------------------------------------------

    [Fact]
    public async Task Validate_WhenOnlyWarningsAreProduced_ThenExitZero()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.WarningOnlySpec);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(0, exit);
        Assert.Contains("warning RestrictToValueNotInDomain:", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_WhenAnErrorIsProduced_ThenExitOne()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.ErrorSpec);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(1, exit);
        Assert.Contains("error AttributeScalingMissing:", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_WhenTheSpecIsMissing_ThenACodeLessHostErrorAndExitOne()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var missing = temp.Resolve("nope.toml");

        var exit = await harness.RunAsync("validate", missing);

        Assert.Equal(1, exit);
        Assert.Equal($"error: cannot read the spec file '{missing.Replace("\\", "\\\\")}'.\n", harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenTheDataIsMissing_ThenACodeLessHostErrorAndExitOne()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var missing = temp.Resolve("nope.csv");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, missing);

        Assert.Equal(1, exit);
        Assert.Equal($"error: cannot read the data file '{missing.Replace("\\", "\\\\")}'.\n", harness.StdErr);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'a', 0x00 })]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, (byte)'a' })]
    [InlineData(new byte[] { (byte)'#', 0xFF, (byte)'\n' })]
    public async Task Validate_WhenTheRootSpecIsNotValidUtf8_ThenACodeLessHostErrorAndExitOne(byte[] bytes)
    {
        // §2 makes UTF-8 part of the format boundary, so an invalidly encoded ROOT is an
        // ordinary unreadable-input failure — code-less, exit 1, never exit 4.
        using var temp = TempDirectory.Create();
        var spec = temp.Resolve("spec.toml");
        File.WriteAllBytes(spec, bytes);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(1, exit);
        Assert.Equal($"error: cannot read the spec file '{spec.Replace("\\", "\\\\")}'.\n", harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenTheRootSpecCarriesAUtf8ByteOrderMark_ThenItValidatesNormally()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Resolve("spec.toml");
        File.WriteAllBytes(
            spec, [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.UTF8.GetBytes(CliFixtures.IndexBoundSpec)]);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenAReferencedBaseIsNotValidUtf8_ThenTheSpecDiagnosticAndExitOne()
    {
        using var temp = TempDirectory.Create();
        File.WriteAllBytes(temp.Resolve("base.toml"), [0xFF, 0xFE, (byte)'a', 0x00]);
        var spec = temp.Write("spec.toml", "[spec]\nversion = 1\nextends = \"./base.toml\"\n");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(1, exit);
        Assert.Contains("fatal SpecExtendsNotFound:", harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("error:", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_WhenAReferencedBaseIsMissing_ThenTheSpecDiagnosticAndExitOne()
    {
        // Unlike the root operand, a missing base is a phase-owned condition and keeps its
        // registry code.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", "[spec]\nversion = 1\nextends = \"./nope.toml\"\n");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(1, exit);
        Assert.Contains("fatal SpecExtendsNotFound:", harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("error:", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_WhenAnExtendsChainResolves_ThenItValidatesAsOneComposedSpec()
    {
        using var temp = TempDirectory.Create();
        temp.Write("base/base.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["red", "green"]
            """);
        var spec = temp.Write("specs/root.toml", "[spec]\nversion = 1\nextends = \"../base/base.toml\"\n");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    // ---- diagnostic fidelity ----------------------------------------------------------

    [Fact]
    public async Task Validate_WhenSeveralDiagnosticsAreProduced_ThenLibraryOrderIsPreservedExactly()
    {
        const string toml = """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "a"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["x"]
            restrict_to = ["nope"]

            [[attribute]]
            name = "b"
            source = { kind = "column", index = 1 }
            scale = { kind = "nominal" }
            declared_domain = ["y"]

            [[attribute]]
            name = "c"
            source = { kind = "column", index = 2 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["z"]
            restrict_to = ["also-nope"]
            """;

        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", toml);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        var oracle = ResolveWithLibrary(toml, spec, schema: null);
        Assert.True(oracle.Diagnostics.Count >= 3, "the fixture must produce several diagnostics");
        Assert.Equal(1, exit);
        Assert.Equal(Render(oracle.Diagnostics), harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenDiagnosticsRender_ThenNoCanonicalIdentityKeyAppears()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.WarningOnlySpec);
        var harness = new CliTestHarness();

        await harness.RunAsync("validate", spec);

        // Attribute-scoped diagnostics carry no file, so this asserts the SHAPE of the
        // rendered line: user-facing text, never an internal identity key.
        Assert.DoesNotContain("FileIdentityKey", harness.StdErr, StringComparison.Ordinal);
        Assert.StartsWith("attribute=\"colour\": warning", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_WhenAParseDiagnosticCarriesAFile_ThenItIsTheSpecsResolvedFullPath()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", "[spec]\nversion = 1\nnot_a_key = 1\n");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(1, exit);
        Assert.Contains(
            $"file=\"{Path.GetFullPath(spec).Replace("\\", "\\\\")}\"", harness.StdErr, StringComparison.Ordinal);
    }

    // ---- the schema-only boundary -------------------------------------------------------

    [Fact]
    public async Task Validate_WhenDataIsSupplied_ThenTheSourceIsOpenedExactlyOnce()
    {
        // Every row-reading path in Sources opens the stream factory again, so one open is
        // one schema acquisition and no enumeration.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        await harness.RunAsync("validate", spec, data);

        Assert.Equal(1, harness.Opened.Count(path => string.Equals(path, data, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Validate_WhenASecondOpenWouldFail_ThenValidationStillSucceeds()
    {
        // The opener detonates on any second open of the data file: if record enumeration
        // began, this run could not exit 0.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);

        var opens = 0;
        var harness = new CliTestHarness
        {
            OpenInput = path =>
            {
                if (string.Equals(path, data, StringComparison.Ordinal) && ++opens > 1)
                {
                    throw new InvalidOperationException("record enumeration began");
                }

                return File.OpenRead(path);
            },
        };

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task Validate_WhenTheSourceIsLarge_ThenOnlyAPrefixOfItIsEverRead()
    {
        // Direct evidence that enumeration never begins: the reader stops after the first
        // record, so most of the file is never delivered. Reading the whole source — which
        // any row pass would do — is exactly what this rules out.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);

        var builder = new StringBuilder("colour,size\n");
        for (var i = 0; i < 200_000; i++)
        {
            builder.Append("red,").Append(i).Append('\n');
        }

        var data = temp.Write("data.csv", builder.ToString());
        var length = new FileInfo(data).Length;

        var delivered = 0L;
        var harness = new CliTestHarness
        {
            OpenInput = path => string.Equals(path, data, StringComparison.Ordinal)
                ? new CountingStream(File.OpenRead(path), count => Interlocked.Add(ref delivered, count))
                : File.OpenRead(path),
        };

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.True(delivered > 0, "the schema read must consume something");
        Assert.True(
            delivered < length,
            $"validate consumed {delivered} of {length} bytes; a record pass would have consumed all of them");
    }

    [Fact]
    public async Task Validate_WhenLaterRowsAreMalformed_ThenTheSchemaStillValidatesSuccessfully()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideDataWithHostileRows);

        // The rows really are hostile: values outside the declared domain, and a ragged
        // row. A conversion would have plenty to say about them.
        var records = new List<ObjectRecord>();
        await foreach (var record in new WideCsvSession(
            () => File.OpenRead(data), SourceReadSettings.CreateWide(hasHeader: true)).ReadAsync())
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
        Assert.Contains(records, record => record.Field(0) == "not-a-colour");
        Assert.Contains(records, record => record.FieldCount != 2);

        var harness = new CliTestHarness();
        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    // ---- what validate must NOT do --------------------------------------------------------

    [Fact]
    public async Task Validate_WhenStoredFingerprintsAreStale_ThenNoStaleWarningIsReported()
    {
        // Proof that validate does not verify stored fingerprints: these values are
        // nonsense and would trip every stale check if one ran.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1
            schema_fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
            cxt_output_fingerprint = "sha256:1111111111111111111111111111111111111111111111111111111111111111"
            dat_output_fingerprint = "sha256:2222222222222222222222222222222222222222222222222222222222222222"

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["red", "green"]
            """);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Validate_WhenTheSpecRequiresCalibration_ThenNoCalibrationHappens()
    {
        // An omitted declared_domain under a consuming discretizer requests observed-domain
        // calibration. Validate resolves it and stops: no ObservedDomainUsed, no rows.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "colour"
            source = { kind = "column", name = "colour" }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            """);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(1, harness.Opened.Count(path => string.Equals(path, data, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Validate_WhenRun_ThenNothingIsWrittenOrMutated()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);

        var before = Snapshot(temp.Path);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(before, Snapshot(temp.Path));
    }

    [Fact]
    public async Task Validate_WhenRun_ThenBothOperandsGoThroughTheInjectedOpener()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NameBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        await harness.RunAsync("validate", spec, data);

        Assert.Equal([spec, data], harness.Opened);
    }

    [Fact]
    public async Task Validate_WhenTheSourceIsAV2Fixture_ThenItValidatesAgainstTheRealHeader()
    {
        var data = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "v2", "mini-mushroom", "mini-mushroom.data");
        Assert.True(File.Exists(data), $"fixture not copied to the test output: {data}");

        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "gill-size"
            source = { kind = "column", name = "gill-size" }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["b", "n"]
            """);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    private static IReadOnlyList<string> Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => $"{path}|{(File.Exists(path) ? new FileInfo(path).Length : -1)}")
            .Order(StringComparer.Ordinal)];
}

/// <summary>Counts the bytes a consumer actually pulls out of an underlying stream.</summary>
internal sealed class CountingStream(Stream inner, Action<int> onRead) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        onRead(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        onRead(read);
        return read;
    }

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
