using System.Runtime.CompilerServices;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// Streams object records through a <see cref="ConversionPlan"/>, producing one
/// <see cref="EmittedObject"/> per record (spec §7 step 4). Single-pass and
/// allocation-streaming: the incidence matrix is never materialized (P-15). All
/// ordering and naming are already decided by the planner; emit only looks values
/// up. Data diagnostics (unknown / unparseable values) are <b>aggregated per
/// attribute</b> — a count with a bounded sample, flushed in plan order after the
/// stream, never one diagnostic per row (spec §16.4, D-059) — and appended to the
/// caller-supplied collector.
/// </summary>
public static class Emitter
{
    // Per-attribute sample cap for aggregated diagnostics; bounded metadata (P-15).
    private const int SampleCap = 3;

    /// <summary>Emits the formal objects for <paramref name="plan"/> over <paramref name="source"/>.</summary>
    public static async IAsyncEnumerable<EmittedObject> EmitAsync(
        ConversionPlan plan,
        IRecordSource source,
        ICollection<BedrockDiagnostic> diagnostics,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var count = plan.Attributes.Count;
        var unknown = NewTallies(count);
        var unparseable = NewTallies(count);

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var crossed = new SortedSet<int>();
            for (var i = 0; i < count; i++)
            {
                Accumulate(plan.Attributes[i], record, crossed, unknown[i], unparseable[i]);
            }

            yield return new EmittedObject(record.Name, [.. crossed]);
        }

        // Aggregated data-phase diagnostics: one per attribute, in plan order, so the
        // diagnostic sequence is deterministic (P-7) and bounded regardless of row count.
        for (var i = 0; i < count; i++)
        {
            Flush(plan.Attributes[i], unparseable[i], DiagnosticCode.SourceValueUnparseable, UnparseableSeverity, diagnostics);
            Flush(plan.Attributes[i], unknown[i], DiagnosticCode.UnknownValueObserved, UnknownSeverity, diagnostics);
        }
    }

    private static void Accumulate(
        PlannedAttribute attribute,
        ObjectRecord record,
        SortedSet<int> crossed,
        DiagnosticTally unknown,
        DiagnosticTally unparseable)
    {
        var raw = record.Field(attribute.SourceColumnIndex);
        if (raw is null)
        {
            return; // missing → skip (slice 1 default; missing_policy = as_attribute lands later)
        }

        var result = attribute.Discretizer.Discretize(raw);
        switch (result.Outcome)
        {
            case BinOutcome.NoBin:
                return; // out of range → no cross, silent (§11.2)

            case BinOutcome.Unparseable:
                unparseable.Record(raw); // §11.5 / D-050: kept, no cross, diagnosable
                return;

            case BinOutcome.Unknown:
                RecordUnknown(attribute, raw, unknown); // ordered_cuts not-in-order (§11.8)
                return;

            case BinOutcome.Bin:
                var bin = result.Value!; // a Bin always carries a non-null label
                if (!attribute.KnownBins.Contains(bin))
                {
                    // Identity domain mismatch: the bin label is the raw value (§10.6).
                    RecordUnknown(attribute, bin, unknown);
                    return;
                }

                // A recognized bin that crosses nothing (e.g. a dichotomy's false pole) has
                // no entry here and is correctly distinguished from an unknown value.
                if (attribute.CrossesByBin.TryGetValue(bin, out var ids))
                {
                    foreach (var id in ids)
                    {
                        crossed.Add(id);
                    }
                }

                return;
        }
    }

    // "include" extends the domain during the Calibrate phase, never at emit, so an
    // unknown reaching emit under that policy is an impossible state (preserved from the
    // per-row path — P-13). Other policies accrue to the aggregate and surface at flush.
    private static void RecordUnknown(PlannedAttribute attribute, string value, DiagnosticTally unknown)
    {
        if (attribute.UnknownValuePolicy == UnknownValuePolicy.Include)
        {
            throw new InvalidOperationException(
                "unknown_value_policy = \"include\" must be resolved during calibration, not emit.");
        }

        unknown.Record(value);
    }

    private static void Flush(
        PlannedAttribute attribute,
        DiagnosticTally tally,
        DiagnosticCode code,
        Func<UnknownValuePolicy, DiagnosticSeverity?> severityFor,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        if (tally.Count == 0 || severityFor(attribute.UnknownValuePolicy) is not { } severity)
        {
            return; // no occurrences, or a silent policy (skip)
        }

        var reason = code == DiagnosticCode.SourceValueUnparseable
            ? "could not be parsed as a number"
            : "are not in its declared domain";
        diagnostics.Add(new BedrockDiagnostic(
            code,
            severity,
            $"{tally.Count} value(s) on attribute '{attribute.Name}' {reason} (e.g. {tally.Sample}).",
            new DiagnosticLocation(AttributeName: attribute.Name)));
    }

    // Unknown categorical value severity (§10.6). "include" cannot reach Flush — RecordUnknown
    // throws first — so it is mapped defensively to warn.
    private static DiagnosticSeverity? UnknownSeverity(UnknownValuePolicy policy) => policy switch
    {
        UnknownValuePolicy.Skip => null,
        UnknownValuePolicy.Warn => DiagnosticSeverity.Warning,
        UnknownValuePolicy.Fail => DiagnosticSeverity.Error,
        _ => DiagnosticSeverity.Warning,
    };

    // Unparseable numeric severity (§10.6 / §11.5, D-050). "include" → warn: an unparseable
    // token cannot be added to a numeric domain, so it behaves as warn here.
    private static DiagnosticSeverity? UnparseableSeverity(UnknownValuePolicy policy) => policy switch
    {
        UnknownValuePolicy.Skip => null,
        UnknownValuePolicy.Warn => DiagnosticSeverity.Warning,
        UnknownValuePolicy.Fail => DiagnosticSeverity.Error,
        UnknownValuePolicy.Include => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Warning,
    };

    private static DiagnosticTally[] NewTallies(int count)
    {
        var tallies = new DiagnosticTally[count];
        for (var i = 0; i < count; i++)
        {
            tallies[i] = new DiagnosticTally();
        }

        return tallies;
    }

    // Per-attribute occurrence count plus a bounded first-observed sample (deterministic
    // given source order). Bounded metadata, never the matrix (P-15).
    private sealed class DiagnosticTally
    {
        private readonly List<string> _sample = [];

        public long Count { get; private set; }

        public string Sample => string.Join(", ", _sample);

        public void Record(string value)
        {
            Count++;
            if (_sample.Count < SampleCap)
            {
                _sample.Add(value);
            }
        }
    }
}
