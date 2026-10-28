using System.Globalization;
using System.Text;
using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>stats SPEC DATA [--temp-dir DIR]</c> (D-122 part 10): the full shared pipeline plus
/// <b>exactly one</b> bounded emit-counting enumeration, reporting six fields and writing no
/// file.
/// <code>
/// objects = N
/// formal_attributes = N
/// crosses = N
/// density = 0.000000
/// crossless_objects = N
/// empty_attributes = N
/// </code>
/// <para>
/// <b>Bounded by the column count, not the context.</b> Counting keeps four integers and one
/// <c>bool</c> per planned column; no emitted object, no row's cross list, and no incidence
/// cell is retained (EP-16). There is no second pass: D-122 part 10 allows exactly one bounded
/// emit-counting pass, so <c>EmitReplay</c> is deliberately not used.
/// </para>
/// <para>
/// Emit diagnostics become authoritative when the single enumeration completes, the
/// single-pass boundary, as for <c>.dat</c> (§16.2/D-105), and keep their order. Warnings and
/// Info leave the report and exit 0 intact; an Error or Fatal suppresses the report and exits
/// 1.
/// </para>
/// </summary>
internal static class StatsCommand
{
    /// <summary>The fourth line when the context has no cells at all.</summary>
    internal const string ZeroCellDensity = "n/a (0 cells)";

    /// <summary>Runs the command; 0 when no Error/Fatal diagnostic was produced, otherwise 1.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var outcome = await RunRendering.PrepareAsync(invocation, environment).ConfigureAwait(false);
        if (outcome.IsError || outcome.Value.Value is not { } run)
        {
            return RunRendering.Fail(environment, outcome, environment.Signals.Token);
        }

        var cancellation = environment.Signals.Token;
        var diagnostics = new List<BedrockDiagnostic>(outcome.Value.Diagnostics);

        // Once, as for plan, and before the counting pass, so the stale warnings keep their
        // place ahead of the emit diagnostics in library order.
        diagnostics.AddRange(SpecFingerprints.VerifyStored(run.RootDocument, run.Fingerprints, run.RootKey));

        // The one counting pass, then the stability re-check, before any diagnostic or report byte
        // is written.
        var counted = await ContextStatistics.CountAsync(run, diagnostics, cancellation).ConfigureAwait(false);
        if (counted.IsError || counted.Value.Value is not { } counts)
        {
            return RunRendering.Fail(environment, counted, cancellation);
        }

        return RunRendering.Complete(environment, counted.Value.Diagnostics, Render(counts), cancellation);
    }

    private static string Render(ContextStatistics counts)
    {
        var builder = new StringBuilder();
        Append(builder, "objects", Integer(counts.Objects));
        Append(builder, "formal_attributes", Integer(counts.FormalAttributes));
        Append(builder, "crosses", Integer(counts.Crosses));
        Append(builder, "density", FormatDensity(counts.Crosses, counts.Objects, counts.FormalAttributes));
        Append(builder, "crossless_objects", Integer(counts.CrosslessObjects));
        Append(builder, "empty_attributes", Integer(counts.EmptyAttributes));
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string label, string value)
    {
        builder.Append(label);
        builder.Append(" = ");
        builder.Append(value);
        builder.Append('\n');
    }

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The exact ratio <c>crosses / (objects × formal_attributes)</c> to six fractional digits.
    /// <para>
    /// <b>Integer-exact fixed point, not floating point.</b> The scaled numerator and the cell
    /// count are held in <see cref="Int128"/>: <c>objects × formal_attributes</c> alone can pass
    /// 2^63 at the v1 target scale (D-007), and a <c>double</c> would round the ratio before the
    /// sixth digit was decided. The seventh digit is resolved by comparing twice the remainder
    /// with the divisor, so a value strictly above half rounds up, one strictly below truncates,
    /// and an <b>exact</b> half goes to even: no binary approximation participates anywhere.
    /// </para>
    /// <para>
    /// A zero cell count has no ratio, so it renders <see cref="ZeroCellDensity"/> and the other
    /// five fields stay present.
    /// </para>
    /// </summary>
    internal static string FormatDensity(long crosses, long objects, long formalAttributes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(crosses);
        ArgumentOutOfRangeException.ThrowIfNegative(objects);
        ArgumentOutOfRangeException.ThrowIfNegative(formalAttributes);

        var cells = (Int128)objects * formalAttributes;
        if (cells == Int128.Zero)
        {
            return ZeroCellDensity;
        }

        var scaled = (Int128)crosses * 1_000_000;
        var quotient = scaled / cells;
        var remainder = scaled - (quotient * cells);
        var twiceRemainder = remainder * 2;

        if (twiceRemainder > cells || (twiceRemainder == cells && quotient % 2 != Int128.Zero))
        {
            quotient++;
        }

        var whole = quotient / 1_000_000;
        var fraction = (long)(quotient - (whole * 1_000_000));
        return whole.ToString(CultureInfo.InvariantCulture)
            + "."
            + fraction.ToString("D6", CultureInfo.InvariantCulture);
    }
}
