using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// The post-iteration correctness gate.
/// <para>
/// Every completed measured iteration is validated <b>after</b> its producer sessions have been
/// disposed and its final diagnostics are available, and <b>outside</b> the measured interval. A
/// failure throws: an iteration that produced the wrong bytes has no throughput result, and the
/// benchmark that produced it must fail rather than publish a number. The only correctness check is
/// never deferred to global cleanup, where a single late failure would silently cover every
/// iteration that preceded it.
/// </para>
/// </summary>
internal static class OutputValidation
{
    /// <summary>
    /// The emit-phase diagnostics that describe a legitimately degenerate context rather than a
    /// fault — an empty column, an empty row, an empty context. They are outcomes the spec blesses
    /// (§10.1/§16.4), and they cannot mask a wrong result here, because byte equality is the gate.
    /// </summary>
    private static readonly DiagnosticCode[] ShapeWarnings =
    [
        DiagnosticCode.AttributeHasNoCrosses,
        DiagnosticCode.ObjectHasNoCrosses,
        DiagnosticCode.NoObjectsEmitted,
    ];

    /// <summary>
    /// The three warnings a data-dependent calibration <b>always</b> emits when its mode runs.
    /// <para>
    /// Each says the same thing about a different mode — this attribute's column set came from the
    /// data, so <c>schema_fingerprint</c> depends on this input. They are statements that the mode
    /// executed, not findings about the data, and a case selecting one of these modes would emit its
    /// warning on every run over every corpus. Nothing else is filtered: an unparseable value or an
    /// insufficient population changes which cuts were produced, so a run carrying one of those has
    /// not calibrated what the benchmark claims it did.
    /// </para>
    /// </summary>
    private static readonly DiagnosticCode[] DataDependentModes =
    [
        DiagnosticCode.ObservedDomainUsed,
        DiagnosticCode.ValueGroupsPassthroughDataDependent,
        DiagnosticCode.UnknownValuePolicyInclude,
    ];

    /// <summary>
    /// Throws when a calibration pass reported anything at Error or above, or any warning beyond
    /// <see cref="DataDependentModes"/>.
    /// </summary>
    public static void RequireCleanCalibration(IReadOnlyList<BedrockDiagnostic> diagnostics, string what)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var unexpected = diagnostics
            .Where(diagnostic => !DataDependentModes.Contains(diagnostic.Code))
            .ToList();

        if (unexpected.Count > 0)
        {
            throw new InvalidOperationException($"{what}: {ConversionPipeline.Describe(unexpected)}");
        }
    }

    /// <summary>Throws unless <paramref name="path"/> has exactly the expected length and digest.</summary>
    public static void RequireFileMatches(string path, long expectedBytes, string expectedSha256, string what)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(expectedSha256);

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new InvalidOperationException($"{what}: no output was produced at '{path}'.");
        }

        if (info.Length != expectedBytes)
        {
            throw new InvalidOperationException(
                $"{what}: expected {expectedBytes} bytes at '{path}' but found {info.Length}.");
        }

        var actual = CorpusCatalog.HashFile(path);
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{what}: '{path}' has digest {actual}, expected {expectedSha256}.");
        }
    }

    /// <summary>
    /// Throws unless <paramref name="path"/> has exactly <paramref name="expectedLines"/> lines.
    /// <para>
    /// For a <c>.dat</c> artifact a line is an object, so this is a semantic assertion a digest
    /// comparison cannot make on its own: it names <em>what</em> differs when a conversion loses,
    /// duplicates, or invents an object, and it holds for a case whose exact bytes are not derivable
    /// in advance.
    /// </para>
    /// </summary>
    public static void RequireLineCount(string path, long expectedLines, string what)
    {
        ArgumentNullException.ThrowIfNull(path);

        var lines = 0L;
        using (var reader = new StreamReader(path))
        {
            while (reader.ReadLine() is not null)
            {
                lines++;
            }
        }

        if (lines != expectedLines)
        {
            throw new InvalidOperationException(
                $"{what}: '{path}' has {lines} lines, expected {expectedLines}.");
        }
    }

    /// <summary>Throws unless <paramref name="actual"/> equals <paramref name="expected"/> byte for byte.</summary>
    public static void RequireBytesMatch(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, string what)
    {
        if (actual.SequenceEqual(expected))
        {
            return;
        }

        var index = 0;
        var shared = Math.Min(actual.Length, expected.Length);
        while (index < shared && actual[index] == expected[index])
        {
            index++;
        }

        throw new InvalidOperationException(
            index < shared
                ? $"{what}: byte {index} is 0x{actual[index]:X2}, expected 0x{expected[index]:X2} "
                  + $"({actual.Length} bytes produced, {expected.Length} expected)."
                : $"{what}: {actual.Length} bytes produced, {expected.Length} expected.");
    }

    /// <summary>Throws unless <paramref name="actual"/> equals <paramref name="expected"/>.</summary>
    public static void RequireSummary(DrainSummary actual, DrainSummary expected, string what)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{what}: drained {actual}, expected {expected}.");
        }
    }

    /// <summary>
    /// Throws when the run's collected diagnostics carry anything at Error or above, or any warning
    /// beyond the blessed degenerate-shape set. A run whose diagnostics are not clean produced an
    /// artifact its own caller would have to discard (D-105's caller-discard rule), so it can carry
    /// no throughput number.
    /// </summary>
    public static void RequireCleanEmit(
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        string what,
        IReadOnlyList<DiagnosticCode>? alsoAllowed = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        // A case may name codes that are outcomes rather than faults FOR IT - the aggregated
        // DuplicateObjectKey a deduping conversion reports, say, which states exactly what that
        // case exists to do. The list is per-case and explicit, never a global relaxation: a code
        // one case expects is still a failure everywhere else.
        var unexpected = diagnostics
            .Where(diagnostic => !ShapeWarnings.Contains(diagnostic.Code))
            .Where(diagnostic => alsoAllowed is null || !alsoAllowed.Contains(diagnostic.Code))
            .ToList();

        if (unexpected.Count > 0)
        {
            throw new InvalidOperationException($"{what}: {ConversionPipeline.Describe(unexpected)}");
        }
    }
}
