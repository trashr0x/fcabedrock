using System.Globalization;
using System.Text;
using FcaBedrock.Diagnostics;
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
/// cell is retained (P-16). There is no second pass: the plan's stop condition forbids one, so
/// <c>EmitReplay</c> is deliberately not used.
/// </para>
/// <para>
/// Emit diagnostics become authoritative when the single enumeration completes — the
/// single-pass boundary, as for <c>.dat</c> (§16.2/D-105) — and keep their order. Warnings and
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

        var outcome = await RunPipeline.PrepareAsync(invocation, environment).ConfigureAwait(false);
        if (outcome is not PipelinePrepared prepared)
        {
            return RunPipeline.Fail(environment, outcome, environment.Signals.Token);
        }

        var run = prepared.Run;
        var cancellation = environment.Signals.Token;
        var diagnostics = new List<BedrockDiagnostic>(prepared.Diagnostics);

        // Once, as for plan — and before the counting pass, so the stale warnings keep their
        // place ahead of the emit diagnostics in library order.
        diagnostics.AddRange(SpecFingerprints.VerifyStored(run.RootDocument, run.Fingerprints, run.RootKey));

        Counts counts;
        try
        {
            counts = await CountAsync(run, diagnostics, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (RunPipeline.IsDataReadFailure(exception))
        {
            return RunPipeline.HostFailure(
                environment, diagnostics, RunPipeline.DataReadMessage(run.DataPath), cancellation);
        }

        // The counting pass is the run's second (or later) complete pass, so stability is
        // re-checked here — before any diagnostic or report byte is written.
        if (run.Input.HasMismatch)
        {
            return RunPipeline.HostFailure(
                environment, diagnostics, RunPipeline.InputChangedMessage(run.DataPath), cancellation);
        }

        return RunPipeline.Complete(environment, diagnostics, Render(counts), cancellation);
    }

    /// <summary>The six counted fields.</summary>
    private readonly record struct Counts(
        long Objects, long FormalAttributes, long Crosses, long CrosslessObjects, long EmptyAttributes);

    // One enumeration, bounded state: the per-column "was ever crossed" flags are the only
    // collection, and their size is the planned column count.
    private static async Task<Counts> CountAsync(
        PreparedRun run, List<BedrockDiagnostic> diagnostics, CancellationToken cancellation)
    {
        var formalAttributes = run.Plan.FormalAttributes.Count;
        var crossed = new bool[formalAttributes];
        long objects = 0;
        long crosses = 0;
        long crosslessObjects = 0;

        await foreach (var emitted in run.Emit(diagnostics).WithCancellation(cancellation).ConfigureAwait(false))
        {
            objects++;

            var ids = emitted.CrossedFormalAttributeIds;
            if (ids.Count == 0)
            {
                crosslessObjects++;
            }

            crosses += ids.Count;
            for (var i = 0; i < ids.Count; i++)
            {
                crossed[ids[i]] = true;
            }
        }

        long empty = 0;
        foreach (var seen in crossed)
        {
            if (!seen)
            {
                empty++;
            }
        }

        return new Counts(objects, formalAttributes, crosses, crosslessObjects, empty);
    }

    private static string Render(Counts counts)
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
    /// count are held in <see cref="Int128"/> — <c>objects × formal_attributes</c> alone can pass
    /// 2^63 at the v1 target scale (D-007), and a <c>double</c> would round the ratio before the
    /// sixth digit was decided. The seventh digit is resolved by comparing twice the remainder
    /// with the divisor, so a value strictly above half rounds up, one strictly below truncates,
    /// and an <b>exact</b> half goes to even — no binary approximation participates anywhere.
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
