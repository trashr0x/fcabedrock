using System.Collections.Immutable;
using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Manifest;

/// <summary>
/// Renders a <see cref="RunManifest"/> as the canonical §15 run-manifest text
/// (D-122 part 6, D-123 point 8): <c>[run]</c>, then the ordered
/// <c>[[run.outputs]]</c>, then the conditional <c>[[run.spec_files]]</c>, then
/// the ordered <c>[[run.calibrations]]</c>, in the fixed per-section field order,
/// with <c>key = value</c> single spacing, one blank line between tables, LF
/// endings, a final LF, no comments, and no BOM.
/// <para>
/// It shares the spec writer's canonical literal and array machinery
/// (<see cref="TomlLiteral"/>, <see cref="TomlArrays"/>) rather than minting a
/// second set of escaping, number, date-time, array, or wrapping rules (D-075/
/// D-113, P-5) — which is why the model and this writer live in
/// <c>FcaBedrock.Spec</c> and the CLI never formats TOML itself.
/// </para>
/// <para>
/// Pure and synchronous: it formats the supplied immutable facts and does no I/O,
/// source enumeration, calibration, planning, hashing, fingerprinting, clock
/// access, environment access, path resolution, or mutation. Retained
/// <see cref="AttributeCalibration"/> outcomes are read directly — never
/// re-derived (D-093) — and every caller-supplied path, hash, argv element, and
/// version string is preserved verbatim modulo ordinary TOML string escaping,
/// which changes representation only.
/// </para>
/// </summary>
public static class RunManifestWriter
{
    /// <summary>Renders <paramref name="manifest"/> as canonical §15 manifest text (LF, no BOM).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is null.</exception>
    public static string Write(RunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var text = new StringBuilder();
        WriteRun(text, manifest.Run);
        WriteOutputs(text, manifest.Outputs);
        WriteSpecFiles(text, manifest.SpecFiles);
        WriteCalibrations(text, manifest.Calibrations);
        return text.ToString();
    }

    private static void WriteRun(StringBuilder text, RunSection run)
    {
        BeginTable(text, "[run]");
        Key(text, "tool_version", TomlLiteral.FormatString(run.ToolVersion));

        // A bare TOML offset date-time. RunSection admits only a whole-second UTC
        // instant, so this renders `…Z` with no fractional part — the §15 form —
        // without the writer normalizing an audit value.
        Key(text, "timestamp", TomlLiteral.FormatDateTime(run.Timestamp));

        // One inline array whatever its length (§15): the complete audit argv,
        // actual argv[0] included, element for element, in order.
        Key(text, "command_line", TomlArrays.Inline(FormatStrings(run.CommandLine)));

        Key(text, "spec_path", TomlLiteral.FormatString(run.SpecPath));
        Key(text, "spec_file_hash", TomlLiteral.FormatString(run.SpecFileHash));
        Key(text, "schema_fingerprint", TomlLiteral.FormatString(run.SchemaFingerprint));

        // Present iff that format was written — the model enforces the pairing
        // against the output set, so presence here is never inferred.
        if (run.CxtOutputFingerprint is { } cxt)
        {
            Key(text, "cxt_output_fingerprint", TomlLiteral.FormatString(cxt));
        }

        if (run.DatOutputFingerprint is { } dat)
        {
            Key(text, "dat_output_fingerprint", TomlLiteral.FormatString(dat));
        }

        Key(text, "input_path", TomlLiteral.FormatString(run.InputPath));
        Key(text, "input_hash", TomlLiteral.FormatString(run.InputHash));
    }

    // Canonical format order, CXT before DAT (D-122 part 6), walked explicitly
    // rather than read off enum declaration order or a sort comparer — so the
    // emitted order is a stated property of this writer and is independent of the
    // order the caller happened to assemble the outputs in.
    private static void WriteOutputs(StringBuilder text, IReadOnlyList<RunOutput> outputs)
    {
        foreach (var format in CanonicalFormatOrder)
        {
            foreach (var output in outputs)
            {
                if (output.Format != format)
                {
                    continue;
                }

                BeginTable(text, "[[run.outputs]]");
                Key(text, "format", TomlLiteral.FormatString(Spelling(format)));
                Key(text, "path", TomlLiteral.FormatString(output.Path));
                Key(text, "hash", TomlLiteral.FormatString(output.Hash));
            }
        }
    }

    // The whole family is absent when there is no extends chain; when present the
    // caller-supplied root-first-then-bases order and the verbatim authored
    // spellings are preserved, each table ordered `path` then `hash` (§15).
    private static void WriteSpecFiles(StringBuilder text, IReadOnlyList<SpecFileEntry> specFiles)
    {
        foreach (var entry in specFiles)
        {
            BeginTable(text, "[[run.spec_files]]");
            Key(text, "path", TomlLiteral.FormatString(entry.Path));
            Key(text, "hash", TomlLiteral.FormatString(entry.Hash));
        }
    }

    // The whole family is absent when no outcome was retained; otherwise one entry
    // per retained outcome in the supplied CalibratedSpec.Calibrations order — no
    // sorting, grouping, deduplication, re-resolution, or re-derivation.
    private static void WriteCalibrations(StringBuilder text, IReadOnlyList<RunCalibration> calibrations)
    {
        foreach (var calibration in calibrations)
        {
            BeginTable(text, "[[run.calibrations]]");
            Key(text, "attribute", TomlLiteral.FormatString(calibration.Outcome.AttributeName));

            switch (calibration.Outcome)
            {
                case CalibratedCuts cuts:
                    Key(text, "kind", TomlLiteral.FormatString("cuts"));

                    // Non-null for a cuts outcome by the RunCalibration invariant:
                    // the authored kind spelling is a supplied fact, never guessed.
                    Key(text, "discretizer", TomlLiteral.FormatString(calibration.Discretizer!));

                    // Inline whatever its length (§15).
                    Key(text, "cuts", TomlArrays.Inline(FormatDoubles(cuts.Cuts)));
                    break;

                case ObservedDomain observed:
                    WriteValues(text, "observed_domain", observed.Values);
                    break;

                case IncludeAdditions include:
                    WriteValues(text, "include_additions", include.Values);
                    break;

                case PassthroughBins passthrough:
                    WriteValues(text, "passthrough_bins", passthrough.Values);
                    break;

                default:
                    // Unreachable: AttributeCalibration is mechanically closed.
                    throw new ArgumentOutOfRangeException(
                        nameof(calibrations), calibration.Outcome, "Unknown calibration outcome.");
            }
        }
    }

    // The three non-cut kinds share one payload shape: `kind` then `values`, the
    // one manifest field the D-113 wrapping applies to. A legitimate
    // zero-discovery outcome therefore serializes as an explicit `values = []`
    // (D-122 part 15 / REG-PRES-007), which the shared rule renders inline.
    private static void WriteValues(StringBuilder text, string kind, IReadOnlyList<string> values)
    {
        Key(text, "kind", TomlLiteral.FormatString(kind));
        Key(text, ValuesKey, TomlArrays.Wrappable(ValuesKey, FormatStrings(values)));
    }

    private static string Spelling(RunOutputFormat format) => format switch
    {
        RunOutputFormat.Cxt => "cxt",
        RunOutputFormat.Dat => "dat",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown run output format."),
    };

    private static List<string> FormatStrings(IReadOnlyList<string> values)
    {
        var items = new List<string>(values.Count);
        foreach (var value in values)
        {
            items.Add(TomlLiteral.FormatString(value));
        }

        return items;
    }

    private static List<string> FormatDoubles(IReadOnlyList<double> values)
    {
        var items = new List<string>(values.Count);
        foreach (var value in values)
        {
            items.Add(TomlLiteral.FormatDouble(value));
        }

        return items;
    }

    private static void BeginTable(StringBuilder text, string header)
    {
        if (text.Length > 0)
        {
            text.Append('\n');
        }

        text.Append(header).Append('\n');
    }

    private static void Key(StringBuilder text, string key, string value) =>
        text.Append(key).Append(" = ").Append(value).Append('\n');

    private static readonly ImmutableArray<RunOutputFormat> CanonicalFormatOrder =
        [RunOutputFormat.Cxt, RunOutputFormat.Dat];

    private const string ValuesKey = "values";
}
