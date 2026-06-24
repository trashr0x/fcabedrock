using System.Runtime.CompilerServices;
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
/// up. Diagnostics raised while reading data (e.g. unknown values) are appended to
/// the caller-supplied collector.
/// </summary>
public static class Emitter
{
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

        long recordIndex = 0;
        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var crossed = new SortedSet<int>();
            foreach (var attribute in plan.Attributes)
            {
                Accumulate(attribute, record, recordIndex, crossed, diagnostics);
            }

            yield return new EmittedObject(record.Name, [.. crossed]);
            recordIndex++;
        }
    }

    private static void Accumulate(
        PlannedAttribute attribute,
        ObjectRecord record,
        long recordIndex,
        SortedSet<int> crossed,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        var raw = record.Field(attribute.SourceColumnIndex);
        if (raw is null)
        {
            return; // missing → skip (slice 1 default; missing_policy = as_attribute lands later)
        }

        var bin = attribute.Discretizer.Discretize(raw);
        if (bin is null)
        {
            return; // out of range → no cross
        }

        if (!attribute.KnownBins.Contains(bin))
        {
            ReportUnknown(attribute, bin, recordIndex, diagnostics);
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
    }

    private static void ReportUnknown(
        PlannedAttribute attribute,
        string value,
        long recordIndex,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        switch (attribute.UnknownValuePolicy)
        {
            case UnknownValuePolicy.Skip:
                break;
            case UnknownValuePolicy.Warn:
                diagnostics.Add(Unknown(attribute, value, recordIndex, DiagnosticSeverity.Warning));
                break;
            case UnknownValuePolicy.Fail:
                diagnostics.Add(Unknown(attribute, value, recordIndex, DiagnosticSeverity.Error));
                break;
            case UnknownValuePolicy.Include:
                // "include" is resolved during the Calibrate phase (spec §10.6), never at emit.
                throw new InvalidOperationException(
                    "unknown_value_policy = \"include\" must be resolved during calibration, not emit.");
            default:
                throw new InvalidOperationException($"Unhandled unknown-value policy {attribute.UnknownValuePolicy}.");
        }
    }

    private static BedrockDiagnostic Unknown(
        PlannedAttribute attribute,
        string value,
        long recordIndex,
        DiagnosticSeverity severity) =>
        new(
            DiagnosticCode.UnknownValueObserved,
            severity,
            $"Value '{value}' on attribute '{attribute.Name}' is not in its declared domain.",
            new DiagnosticLocation(AttributeName: attribute.Name, RecordIndex: recordIndex));
}
