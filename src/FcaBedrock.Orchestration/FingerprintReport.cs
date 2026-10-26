namespace FcaBedrock.Orchestration;

/// <summary>
/// The three computed <b>native</b> fingerprints of a prepared run, each beside the state of the
/// corresponding stored field on the <b>root</b> document (D-122 part 10).
/// <para>
/// <b>The states are computed here, not warned about.</b> This report is the owner of the
/// stored-versus-computed comparison, so a host that presents it does not also verify the stored
/// fields: doing both would report the same fact twice, once as a field and once as a stale
/// Warning. Comparison is ordinal over the complete stored string, so a malformed or
/// differently-cased value reads <see cref="StoredFingerprintState.Stale"/> (D-077).
/// </para>
/// </summary>
public sealed record FingerprintReport
{
    private FingerprintReport(
        FingerprintComparison schema, FingerprintComparison cxtOutput, FingerprintComparison datOutput)
    {
        Schema = schema;
        CxtOutput = cxtOutput;
        DatOutput = datOutput;
    }

    /// <summary>The schema fingerprint.</summary>
    public FingerprintComparison Schema { get; }

    /// <summary>The native <c>.cxt</c> output fingerprint.</summary>
    public FingerprintComparison CxtOutput { get; }

    /// <summary>The native <c>.dat</c> output fingerprint.</summary>
    public FingerprintComparison DatOutput { get; }

    /// <summary>The report of <paramref name="run"/>, from the root's stored values as the run read them.</summary>
    public static FingerprintReport Of(PreparedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var stored = run.RootDocument.Spec;
        return new FingerprintReport(
            new FingerprintComparison(run.Fingerprints.SchemaFingerprint, stored?.SchemaFingerprint),
            new FingerprintComparison(run.Fingerprints.CxtOutputFingerprint, stored?.CxtOutputFingerprint),
            new FingerprintComparison(run.Fingerprints.DatOutputFingerprint, stored?.DatOutputFingerprint));
    }
}

/// <summary>One computed fingerprint and the state of the stored field beside it.</summary>
public sealed record FingerprintComparison
{
    internal FingerprintComparison(string computed, string? stored)
    {
        ArgumentNullException.ThrowIfNull(computed);
        Computed = computed;

        // Absent = the optional field was not authored (§3); match = ordinal equality with the
        // COMPLETE computed string; stale = anything else, malformed values included.
        Stored = stored is null
            ? StoredFingerprintState.Absent
            : string.Equals(stored, computed, StringComparison.Ordinal)
                ? StoredFingerprintState.Match
                : StoredFingerprintState.Stale;
    }

    /// <summary>The computed fingerprint, <c>sha256:</c> and its digest.</summary>
    public string Computed { get; }

    /// <summary>The state of the stored field.</summary>
    public StoredFingerprintState Stored { get; }
}

/// <summary>The state of a stored fingerprint field against its computed value.</summary>
public enum StoredFingerprintState
{
    /// <summary>The field was not authored.</summary>
    Absent,

    /// <summary>The field equals the computed value, ordinally and completely.</summary>
    Match,

    /// <summary>The field holds anything else, a malformed value included.</summary>
    Stale,
}
