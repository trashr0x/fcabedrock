using System.Globalization;
using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>migrate BED --out PATH|- [--force] [--shape wide|triple] [--delimiter CHAR]
/// [--header true|false] [--locale TAG|invariant] [--missing-token TOKEN]
/// [--scaling discrete|progressive]</c>, plus wide-only <c>[--object-key …]
/// [--object-key-column …]</c> and triple-only role options (D-122 part 10; D-079).
/// <para>
/// <b>Migration is transcription.</b> The handler decodes the <c>.bed</c>, maps argv to a
/// <see cref="BindingSection"/> and a <see cref="ScalingMode"/>, calls
/// <see cref="BedMigrator"/> once, and hands the result to <see cref="SingleFileOutput"/>. It
/// resolves nothing, plans nothing, calibrates nothing, converts nothing, and computes no
/// fingerprint.
/// </para>
/// <para>
/// <b>The <c>[binding]</c> it authors is exactly what the invocation supplied</b>, plus the two
/// facts a migrated spec cannot omit: the shape, and — under triple — the ordering the grammar
/// deliberately has no flag for. Authoring a default the user did not ask for would destroy the
/// omitted-versus-authored distinction the document model exists to preserve (D-049/D-066/D-075).
/// </para>
/// <para>
/// <b>Unlike probe's DATA, an unreadable BED is the CLI's own</b>: this command reads it here,
/// so a missing or wrongly encoded file is a code-less host error, exactly as an unreadable SPEC
/// is for <c>validate</c>. The asymmetry follows from where the read happens.
/// </para>
/// </summary>
internal static class MigrateCommand
{
    /// <summary>Runs the command; 0 on a delivered spec, 1 on any Error/Fatal or host failure.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var bedPath = invocation.Operand(0)!;
        var cancellation = environment.Signals.Token;
        var diagnostics = new List<BedrockDiagnostic>();

        string text;
        try
        {
            // Strict UTF-8 with no byte-order mark or exactly one leading UTF-8 mark. A rejection
            // lands here — before the reader, the migrator, and any publication — so no target is
            // ever created or replaced by an input the CLI would not accept.
            text = SpecTextDecoding.ReadAllText(environment.OpenInput, bedPath);
        }
        catch (Exception exception) when (IsBedReadFailure(exception))
        {
            // One sanitized, code-less line naming the operand and nothing else: no exception
            // type, no decoder text, no localized OS wording, no resolved path (D-122 part 2).
            return RunPipeline.HostFailure(
                environment, diagnostics, $"cannot read the .bed file '{bedPath}'.", cancellation);
        }

        var read = BedReader.Read(text, bedPath);
        diagnostics.AddRange(read.Diagnostics);
        if (!read.TryGetValue(out var bed))
        {
            return await SingleFileOutput
                .DeliverAsync(environment, invocation, null, diagnostics, cancellation).ConfigureAwait(false);
        }

        // derived_from is the invoked operand exactly as typed — never normalized, never made
        // absolute — because it is authored provenance, not a resolved path (D-122 part 10).
        var migrated = BedMigrator.Migrate(bed, Binding(invocation), Scaling(invocation), derivedFrom: bedPath);
        diagnostics.AddRange(migrated.Diagnostics);

        var document = migrated.TryGetValue(out var value) ? value : null;
        return await SingleFileOutput
            .DeliverAsync(environment, invocation, document, diagnostics, cancellation).ConfigureAwait(false);
    }

    // Presence-faithful: a field is authored only when argv supplied it. Encoding and the quote
    // character are never authored — v1 fixes both and exposes no flag for either (P-6, §5.1).
    private static BindingSection Binding(CommandInvocation invocation)
    {
        var triple = string.Equals(invocation.Value("--shape"), "triple", StringComparison.Ordinal);

        return new BindingSection(
            // Always authored: an unauthored shape is BindingShapeMissing, so the migrated spec
            // could not resolve. Omitted --shape means wide (D-122 part 10).
            Shape: triple ? SourceShape.Triple : SourceShape.Wide,
            Encoding: null,
            Delimiter: invocation.Value("--delimiter") is { } delimiter ? delimiter[0] : null,
            QuoteChar: null,
            HasHeader: invocation.Value("--header") is { } header
                ? string.Equals(header, "true", StringComparison.Ordinal)
                : null,
            Locale: invocation.Value("--locale"),
            MissingToken: invocation.Value("--missing-token"),

            // Always authored under triple, never under wide: triple output always states
            // ordering = "unordered" and the grammar carries no --ordering option
            // (D-123 point 14).
            Ordering: triple ? TripleOrdering.Unordered : null,
            Columns: Roles(invocation),
            ObjectKey: ObjectKey(invocation));
    }

    // The parser closed every direction of §5.3 — supplied together, one addressing mode, names
    // requiring --header true — so the trio is read back verbatim and nothing is re-checked.
    private static TripleColumnsSection? Roles(CommandInvocation invocation) =>
        invocation.Value("--subject") is { } subject
            ? new TripleColumnsSection(
                Column(subject), Column(invocation.Value("--predicate")!), Column(invocation.Value("--value")!))
            : null;

    // A supplied option is authored, both modes alike: row_index resolves identically to the wide
    // default, but silently dropping what the user typed would be a special case with no rule
    // behind it. --object-key-column is present exactly when --object-key column, by the parser.
    private static ObjectKeySection? ObjectKey(CommandInvocation invocation) =>
        invocation.Value("--object-key") switch
        {
            "row_index" => new ObjectKeySection(ObjectKeyMode.RowIndex, null, null, null),
            "column" => new ObjectKeySection(
                ObjectKeyMode.Column, Column(invocation.Value("--object-key-column")!), null, null),
            _ => null,
        };

    // The parser's own all-digits classification, mirrored so the supplied addressing mode is
    // preserved exactly as typed. NumberStyles.None admits no sign, whitespace, separator, or
    // exponent, so a header literally named "0" is unreachable by name — the settled grammar.
    private static ColumnRef Column(string reference) =>
        int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? new IndexColumnRef(index)
            : new NameColumnRef(reference);

    // discrete is the parser-declared default (D-045); v2 never recorded the choice.
    private static ScalingMode Scaling(CommandInvocation invocation) =>
        string.Equals(invocation.Value("--scaling"), "progressive", StringComparison.Ordinal)
            ? ScalingMode.Progressive
            : ScalingMode.Discrete;

    // The narrow named-type set for reading ONE authored file, matching the established CLI
    // boundaries. DecoderFallbackException and InvalidDataException are named specifically —
    // the first derives from ArgumentException and the second from SystemException — so a real
    // read failure stays on the code-less exit-1 path without admitting a broad base: those
    // bases are the documented call-contract channel of the reader and the migrator, and
    // absorbing one would disguise a defect as a broken file (P-14).
    private static bool IsBedReadFailure(Exception exception) =>
        exception is IOException
            or InvalidDataException
            or DecoderFallbackException
            or UnauthorizedAccessException
            or NotSupportedException;
}
