using System.Globalization;
using FcaBedrock.Core.Spec;
using FcaBedrock.Discovery;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>probe DATA --shape wide|triple --out PATH|- [--force] [--delimiter CHAR]
/// [--header true|false] [--missing-token TOKEN] [--locale TAG|invariant] [--limit N]</c>,
/// plus triple-only <c>[--ordering …] [--subject …] [--predicate …] [--value …]</c>
/// (D-122 part 10; spec §7.1).
/// <para>
/// <b>M7 adds no discovery semantics — only a command surface over M5's</b> (roadmap M7). So
/// this handler maps argv to typed settings, calls one <see cref="Prober"/> entry exactly once,
/// and hands the result to <see cref="SingleFileOutput"/>. It infers nothing structural,
/// re-validates no §5.3 role map, restates no default, implements no boundedness guard, and
/// renders no diagnostic of its own.
/// </para>
/// <para>
/// <b>Every DATA read failure past the argv boundary is Discovery's</b>, a missing file
/// included: the CLI opens nothing itself, so the first open happens inside the library's own
/// schema acquisition and its failure is that library's <c>ProbeSourceReadFailed</c> — one
/// condition, one owner, no second classification and no double report (D-067).
/// </para>
/// </summary>
internal static class ProbeCommand
{
    /// <summary>Runs the command; 0 on a delivered draft, 1 on any Error/Fatal or host failure.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var dataPath = invocation.Operand(0)!;
        var cancellation = environment.Signals.Token;

        // The one input seam (D-123 part 5), handed over unopened: the library's own read
        // owns every failure from here on, which is what keeps a missing DATA a
        // ProbeSourceReadFailed diagnostic rather than a code-less host line.
        Stream Open() => environment.OpenInput(dataPath);

        var triple = string.Equals(invocation.Value("--shape"), "triple", StringComparison.Ordinal);
        var settings = Settings(invocation, triple);
        var options = Options(invocation);

        var probed = triple
            ? await Prober.ProbeTripleAsync(
                new TripleCsvSession(Open, settings), settings, Roles(invocation), options, cancellation)
                .ConfigureAwait(false)
            : await Prober.ProbeAsync(
                new WideCsvSession(Open, settings), settings, options, cancellation).ConfigureAwait(false);

        var draft = probed.TryGetValue(out var document) ? document : null;
        return await SingleFileOutput
            .DeliverAsync(
                environment, invocation, draft, probed.Diagnostics,
                additionalInputs: [], committedReport: null, cancellation)
            .ConfigureAwait(false);
    }

    // Overrides only. Every omitted setting keeps the factory's own §5.1/§7.1 value — including
    // the shape-specific has_header and the encoding and quote character the CLI exposes no flag
    // for — so no default is restated here and the draft still authors every effective setting.
    private static SourceReadSettings Settings(CommandInvocation invocation, bool triple)
    {
        var library = triple ? SourceReadSettings.CreateTriple() : SourceReadSettings.CreateWide();
        var delimiter = invocation.Value("--delimiter") is { } spelled ? spelled[0] : library.Delimiter;
        var hasHeader = invocation.Value("--header") is { } header
            ? string.Equals(header, "true", StringComparison.Ordinal)
            : library.HasHeader;
        var missingToken = invocation.Value("--missing-token") ?? library.MissingToken;

        return triple
            ? SourceReadSettings.CreateTriple(
                delimiter: delimiter,
                hasHeader: hasHeader,
                missingToken: missingToken,

                // subject_grouped is explicit-only and never inferred (§7.1); omitted keeps the
                // factory's unordered, and the library then enforces contiguity for the explicit
                // choice alone.
                ordering: Ordering(invocation) ?? library.Ordering!.Value)
            : SourceReadSettings.CreateWide(
                delimiter: delimiter, hasHeader: hasHeader, missingToken: missingToken);
    }

    private static TripleOrdering? Ordering(CommandInvocation invocation) =>
        invocation.Value("--ordering") switch
        {
            "subject_grouped" => TripleOrdering.SubjectGrouped,
            "unordered" => TripleOrdering.Unordered,
            _ => null,
        };

    // Overrides only again: the retention limit's default and the three aggregate guards stay
    // pinned library defaults, and the guards have no CLI flag at all (§7.1, D-108/D-110).
    private static ProbeOptions Options(CommandInvocation invocation)
    {
        var library = ProbeOptions.Default;
        var limit = invocation.Value("--limit") is { } spelled
            ? int.Parse(spelled, NumberStyles.None, CultureInfo.InvariantCulture)
            : library.ValueRetentionLimit;

        return ProbeOptions.Create(
            valueRetentionLimit: limit, locale: invocation.Value("--locale") ?? library.Locale);
    }

    // The parser has already closed every direction of §5.3: the trio is supplied together, in
    // ONE addressing mode, and header-name addressing requires --header true. So the trio is read
    // back verbatim and nothing is re-checked. Omitted, it stays null: the library owns the
    // { subject = 0, predicate = 1, value = 2 } default and the draft authors it explicitly.
    private static TripleColumnsSection? Roles(CommandInvocation invocation) =>
        invocation.Value("--subject") is { } subject
            ? new TripleColumnsSection(
                Column(subject), Column(invocation.Value("--predicate")!), Column(invocation.Value("--value")!))
            : null;

    // The parser's own all-digits classification, mirrored so the supplied addressing mode
    // survives into the draft exactly as typed: an all-digits role is an index, anything else is
    // a header name. NumberStyles.None admits no sign, whitespace, separator, or exponent.
    private static ColumnRef Column(string role) =>
        int.TryParse(role, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? new IndexColumnRef(index)
            : new NameColumnRef(role);
}
