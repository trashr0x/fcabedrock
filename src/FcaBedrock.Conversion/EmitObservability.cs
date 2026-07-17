using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion;

/// <summary>
/// Tracks the three whole-stream emit observations (§16.4/D-058) over one emit attempt:
/// <c>NoObjectsEmitted</c> (zero rows), <c>ObjectHasNoCrosses</c> (empty rows), and
/// <c>AttributeHasNoCrosses</c> (empty columns). Shared by every emit path so the four cannot
/// drift.
/// <para>
/// All three are expected outcomes rather than faults — most sharply after <c>restrict_to</c>
/// filtering, because calibration and the column vocabulary are computed over the <b>input
/// universe</b> before objects are filtered (§7), so the surviving population need not span every
/// bin. They are Warnings and the (degenerate but structurally valid) output is still written.
/// </para>
/// <para>
/// Bounded (P-16): a <see cref="bool"/> per planned column plus a count and a three-item sample —
/// never the incidence matrix, and never one diagnostic per row or column.
/// </para>
/// </summary>
internal sealed class EmitObservability
{
    private readonly bool[] _crossed;
    private readonly DiagnosticTally _emptyObjects = new();
    private long _emitted;

    public EmitObservability(int formalAttributeCount) => _crossed = new bool[formalAttributeCount];

    /// <summary>
    /// Records one <b>emitted</b> object. Only emitted objects count: an object <c>restrict_to</c>
    /// excluded never had a row, so it can neither be an empty row nor cross a column.
    /// </summary>
    public void Record(EmittedObject emitted)
    {
        _emitted++;
        if (emitted.CrossedFormalAttributeIds.Count == 0)
        {
            _emptyObjects.Record(emitted.Name); // sample is emission order — the first three
            return;
        }

        foreach (var id in emitted.CrossedFormalAttributeIds)
        {
            _crossed[id] = true;
        }
    }

    /// <summary>
    /// Flushes the aggregates, in the pinned order <c>NoObjectsEmitted</c> →
    /// <c>ObjectHasNoCrosses</c> → <c>AttributeHasNoCrosses</c> (whole context, then rows, then
    /// columns).
    /// <para>
    /// <b>Only for a normally-completed, valid run.</b> Three things suppress all three warnings:
    /// </para>
    /// <list type="bullet">
    /// <item>a <b>structural halt</b> and a <b>grouping-storage failure</b> — every caller reaches
    /// this past its halt guard, so the stream never got here; after a halt it is truncated, and
    /// "no objects" would describe the halt rather than the data (the established §16.4 rule,
    /// applied unchanged);</item>
    /// <item>a <b>policy abort</b> — <paramref name="aborted"/>, set when a data aggregate flushed
    /// at Error under <c>unknown_value_policy = "fail"</c>, including a filter-only restriction's
    /// (§10.4/§10.6/D-097). The stream did complete, but the run is <b>invalid</b>: G-12's rule is
    /// that any Error/Fatal means the caller must discard the artifact, so describing the shape of
    /// a context that is about to be thrown away is noise.</item>
    /// </list>
    /// </summary>
    /// <param name="plan">The plan whose formal attributes name the empty columns.</param>
    /// <param name="aborted">Whether a <c>fail</c>-policy aggregate reported an Error (§10.4/§10.6).</param>
    /// <param name="diagnostics">The emit diagnostic sink.</param>
    public void Flush(ConversionPlan plan, bool aborted, ICollection<BedrockDiagnostic> diagnostics)
    {
        if (aborted)
        {
            return;
        }

        if (_emitted == 0)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Warning,
                "The conversion emitted no objects; the context has no rows (§16.4). The input was empty, " +
                "or restrict_to excluded every object (§10.4)."));
        }

        if (_emptyObjects.Count > 0)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.ObjectHasNoCrosses, DiagnosticSeverity.Warning,
                $"{_emptyObjects.Count} emitted object(s) cross no formal attribute (e.g. {_emptyObjects.Sample}); " +
                "their rows are empty (§16.4)."));
        }

        // Plan order, so the count and the bounded sample are deterministic (P-7).
        var count = 0;
        var sample = new List<string>(3);
        for (var id = 0; id < _crossed.Length; id++)
        {
            if (_crossed[id])
            {
                continue;
            }

            count++;
            if (sample.Count < 3)
            {
                sample.Add(plan.FormalAttributes[id].RenderedName);
            }
        }

        if (count > 0)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.AttributeHasNoCrosses, DiagnosticSeverity.Warning,
                $"{count} formal attribute(s) were never crossed by an emitted object (e.g. {string.Join(", ", sample)}); " +
                "their columns are empty (§7/§16.4)."));
        }
    }
}
