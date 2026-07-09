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
/// allocation-streaming: the incidence matrix is never materialized (P-16). All
/// ordering and naming are already decided by the planner; emit only looks values
/// up. Data diagnostics (unknown / unparseable values) are <b>aggregated per
/// attribute</b> — a count with a bounded sample, flushed in plan order after the
/// stream, never one diagnostic per row (spec §16.4, D-059) — and appended to the
/// caller-supplied collector.
/// </summary>
public static class Emitter
{
    // Per-attribute sample cap for aggregated diagnostics; bounded metadata (P-16).
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

    /// <summary>
    /// Emits the formal objects for a triple <paramref name="plan"/> over
    /// <paramref name="source"/> under <c>ordering = "subject_grouped"</c>: groups contiguous
    /// rows by cleaned subject, routes each row's predicate to the attribute(s) that bind it,
    /// and unions their crosses (§5.3.1 / §17 rule 8). Object order is first-appearance of each
    /// cleaned subject (§17 rule 4). Structural failures (invalid subject, non-contiguity) are
    /// reported to <paramref name="diagnostics"/> and stop the stream — never exceptions across
    /// the Sources/Conversion seam (D-082). Single-pass, no matrix (P-16). <c>ordering =
    /// "unordered"</c> is gated at plan (<c>TripleUnorderedNotImplementedV1</c>), so it never
    /// reaches here; this path is deliberately ordering-agnostic for Slice D reuse.
    /// </summary>
    public static async IAsyncEnumerable<EmittedObject> EmitTripleAsync(
        ConversionPlan plan,
        ITripleRowSource source,
        ICollection<BedrockDiagnostic> diagnostics,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var count = plan.Attributes.Count;
        var unknown = NewTallies(count);
        var unparseable = NewTallies(count);
        var byPredicate = IndexByPredicate(plan);

        // Subjects whose group has closed, for the §5.3 contiguity check (ordinal, P-12).
        var completed = new HashSet<string>(StringComparer.Ordinal);
        string? currentSubject = null;
        var crossed = new SortedSet<int>();
        var started = false;

        await foreach (var row in source.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            // §5.4 / §18.1 / D-085: the subject is the object name; a null (empty / missing_token
            // / short, per the source), whitespace-only, or control-char-bearing subject has no
            // usable identity — halt this conversion.
            if (!IsValidObjectName(row.Subject))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyValueInvalid, DiagnosticSeverity.Error,
                    $"The triple subject at record {row.RecordIndex} is empty, whitespace-only, a missing token, or contains a control character; it cannot name an object (§5.4).",
                    new DiagnosticLocation(RecordIndex: row.RecordIndex)));
                yield break;
            }

            var subject = row.Subject!;
            if (!started)
            {
                currentSubject = subject;
                started = true;
            }
            else if (!string.Equals(subject, currentSubject, StringComparison.Ordinal))
            {
                // Close the finished object in first-appearance order, then enforce contiguity.
                yield return new EmittedObject(currentSubject!, [.. crossed]);
                completed.Add(currentSubject!);

                if (completed.Contains(subject))
                {
                    // §5.3 / §5.3.1 / D-082: subject recurs after its group closed → not contiguous;
                    // the diagnostic identifies the subject and the offending record index.
                    diagnostics.Add(new BedrockDiagnostic(
                        DiagnosticCode.TripleSubjectNotContiguous, DiagnosticSeverity.Error,
                        $"Triple subject '{subject}' recurs at record {row.RecordIndex} after an intervening subject; ordering = \"subject_grouped\" requires contiguous subjects (§5.3).",
                        new DiagnosticLocation(RecordIndex: row.RecordIndex)));
                    yield break;
                }

                currentSubject = subject;
                crossed = new SortedSet<int>();
            }

            // Route the observation: a present predicate that binds attribute(s) classifies its
            // value into the object's crosses (null value → present-missing → missing_policy in
            // Classify). Absent/empty/unknown predicate = no observation; the subject still forms
            // its object (empty crosses are legal, §10.1).
            if (row.Predicate is { } predicate && byPredicate.TryGetValue(predicate, out var attrs))
            {
                foreach (var i in attrs)
                {
                    Classify(plan.Attributes[i], row.Value, crossed, unknown[i], unparseable[i]);
                }
            }
        }

        if (started)
        {
            yield return new EmittedObject(currentSubject!, [.. crossed]);
        }

        // Aggregated data-phase diagnostics, in plan order (P-7). Skipped on a structural halt
        // above (the conversion aborted; partial data diagnostics would be noise).
        for (var i = 0; i < count; i++)
        {
            Flush(plan.Attributes[i], unparseable[i], DiagnosticCode.SourceValueUnparseable, UnparseableSeverity, diagnostics);
            Flush(plan.Attributes[i], unknown[i], DiagnosticCode.UnknownValueObserved, UnknownSeverity, diagnostics);
        }
    }

    // Predicate selector → the plan-order indices of the attributes that bind it. A list because
    // one predicate may bind several attributes (source repeat, D-033); ordinal keys (P-12).
    private static Dictionary<string, List<int>> IndexByPredicate(ConversionPlan plan)
    {
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < plan.Attributes.Count; i++)
        {
            if (plan.Attributes[i].Source is PredicateAttributeSource predicate)
            {
                if (!map.TryGetValue(predicate.Predicate, out var list))
                {
                    list = [];
                    map[predicate.Predicate] = list;
                }

                list.Add(i);
            }
        }

        return map;
    }

    // §5.4 / §18.1 / D-085: a usable object name is non-null, not whitespace-only, and free of
    // control/newline characters (a newline would corrupt the line-structured .cxt). The source
    // has already normalized empty / missing_token / short → null.
    private static bool IsValidObjectName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var ch in name)
        {
            if (char.IsControl(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static void Accumulate(
        PlannedAttribute attribute,
        ObjectRecord record,
        SortedSet<int> crossed,
        DiagnosticTally unknown,
        DiagnosticTally unparseable)
    {
        // Wide reads its value by resolved column index; classification is shared with the
        // predicate-routed triple path (Classify).
        var raw = attribute.Source is ColumnAttributeSource column
            ? record.Field(column.Index)
            : throw new InvalidOperationException(
                $"Wide emit requires a column source on attribute '{attribute.Name}'.");
        Classify(attribute, raw, crossed, unknown, unparseable);
    }

    // Classifies one raw value (null = missing) for an attribute into the object's crosses,
    // applying missing_policy and the discretizer outcomes. Shared by the wide (column-addressed)
    // and triple (predicate-routed) emit paths so both classify values identically (P-7).
    private static void Classify(
        PlannedAttribute attribute,
        string? raw,
        SortedSet<int> crossed,
        DiagnosticTally unknown,
        DiagnosticTally unparseable)
    {
        if (raw is null)
        {
            if (attribute.MissingFormalAttributeId is { } missingId)
            {
                crossed.Add(missingId); // §10.5 missing_policy = "as_attribute" (D-068)
            }

            return; // skip (null id): no cross, no diagnostic
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
    // per-row path — P-14). Other policies accrue to the aggregate and surface at flush.
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
    // given source order). Bounded metadata, never the matrix (P-16).
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
