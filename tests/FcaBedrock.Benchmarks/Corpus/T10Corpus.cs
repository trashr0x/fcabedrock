using System.Globalization;
using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>Which physical layout of the same T10 observations a file carries.</summary>
internal enum TripleLayout
{
    /// <summary>
    /// Every subject's ten rows are contiguous — the <c>subject_grouped</c> single-pass fast path.
    /// </summary>
    Grouped,

    /// <summary>
    /// Subjects are block-interleaved, so a subject recurs after an intervening one. This is the
    /// <c>unordered</c> path, which must group through the spool backend. First-appearance subject
    /// order is <b>identical</b> to <see cref="Grouped"/>, which is what makes the two layouts a
    /// byte-equality comparison rather than two unrelated corpora.
    /// </summary>
    Interleaved,
}

/// <summary>
/// The <b>T10</b> synthetic triple family: ten subject–predicate–value rows per subject, in two
/// physical layouts of the same observations.
/// <para>
/// It is shaped by what the triple path actually has to get right. Each subject carries a
/// <b>multi-valued</b> predicate whose values union onto one object; an <b>exact duplicate</b> of an
/// earlier row, which must be idempotent; a numeric predicate observed in two <b>equivalent raw
/// spellings</b> (<c>30</c> and <c>30.0</c>), which are one numeric value but two distinct raw
/// observations — the distinction §5.3.1's count-sensitive rule turns on; and an <b>unmatched</b>
/// predicate no attribute binds, which must keep its subject without contributing a cross. The
/// numeric predicate varies by subject over a wide range, so count-sensitive calibration meets a
/// high-cardinality population rather than a handful of repeated values.
/// </para>
/// <para>
/// Objects are subjects, so a tier's <em>input record</em> count is ten times its object count. That
/// is deliberate: the plan's tiers are input records on every family, so a triple tier and a wide
/// tier read the same number of rows and their throughput figures are comparable.
/// </para>
/// </summary>
internal static class T10Corpus
{
    /// <summary>
    /// The generator revision. <b>Bump this whenever any value definition below changes.</b>
    /// </summary>
    public const int GeneratorRevision = 1;

    /// <summary>Rows per subject. The family's name, and the tier-to-object-count divisor.</summary>
    public const int RowsPerSubject = 10;

    /// <summary>
    /// Subjects per interleaving block. Large enough that a subject genuinely recurs after many
    /// intervening ones — so the grouping backend is exercised, not bypassed — and small enough
    /// that first-appearance order stays trivially checkable.
    /// </summary>
    public const int InterleaveBlock = 64;

    /// <summary>The multi-valued categorical predicate.</summary>
    public const string TissuePredicate = "Tissue";

    /// <summary>The second multi-valued categorical predicate.</summary>
    public const string SignalPredicate = "Signal";

    /// <summary>The high-cardinality numeric predicate.</summary>
    public const string StagePredicate = "Stage";

    /// <summary>A predicate no attribute binds: its rows keep the subject and cross nothing.</summary>
    public const string UnmatchedPredicate = "Notes";

    /// <summary>The distinct values <c>Tissue</c> draws from.</summary>
    public static IReadOnlyList<string> TissueDomain { get; } =
        ["brain", "heart", "limb", "liver", "lung", "somite"];

    /// <summary>The distinct values <c>Signal</c> draws from.</summary>
    public static IReadOnlyList<string> SignalDomain { get; } = ["strong", "moderate", "weak", "none"];

    /// <summary>The number of subjects a tier of <paramref name="records"/> input rows carries.</summary>
    public static long Subjects(long records) => records / RowsPerSubject;

    /// <summary>The subject name for <paramref name="subject"/>: fixed width, so ordinal order is stable.</summary>
    public static string SubjectName(long subject) =>
        "s" + subject.ToString("D10", CultureInfo.InvariantCulture);

    /// <summary>The first <c>Tissue</c> value observed for a subject.</summary>
    public static string TissueA(long subject) => TissueDomain[(int)(Determinism.Draw(subject, 40) % 6)];

    /// <summary>The second <c>Tissue</c> value; equal to <see cref="TissueA"/> on some subjects.</summary>
    public static string TissueB(long subject) => TissueDomain[(int)(Determinism.Draw(subject, 41) % 6)];

    /// <summary>The first <c>Signal</c> value observed for a subject.</summary>
    public static string SignalA(long subject) => SignalDomain[(int)(Determinism.Draw(subject, 42) % 4)];

    /// <summary>The second <c>Signal</c> value.</summary>
    public static string SignalB(long subject) => SignalDomain[(int)(Determinism.Draw(subject, 43) % 4)];

    /// <summary>
    /// The subject's numeric <c>Stage</c>, spread widely enough that a count-sensitive calibration
    /// over a scale tier meets a genuinely high-cardinality population.
    /// </summary>
    public static int Stage(long subject) => (int)(Determinism.Draw(subject, 44) % 250_000);

    /// <summary>
    /// The subject's ten rows, in their within-subject order. Row 2 repeats row 0's value, row 6
    /// spells row 5's number differently, row 7 repeats row 5 exactly, row 8 is unmatched, and
    /// row 9 repeats row 3 exactly.
    /// </summary>
    public static (string Predicate, string Value) Row(long subject, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowsPerSubject);

        var stage = Stage(subject).ToString(CultureInfo.InvariantCulture);
        return row switch
        {
            0 => (TissuePredicate, TissueA(subject)),
            1 => (TissuePredicate, TissueB(subject)),
            2 => (TissuePredicate, TissueA(subject)),          // exact duplicate of row 0
            3 => (SignalPredicate, SignalA(subject)),
            4 => (SignalPredicate, SignalB(subject)),
            5 => (StagePredicate, stage),
            6 => (StagePredicate, stage + ".0"),               // one numeric value, two raw spellings
            7 => (StagePredicate, stage),                      // exact duplicate of row 5
            8 => (UnmatchedPredicate, "observation " + row),   // bound by nothing
            _ => (SignalPredicate, SignalA(subject)),          // exact duplicate of row 3
        };
    }

    /// <summary>The distinct <c>Tissue</c> values a subject contributes, in first-observation order.</summary>
    public static IReadOnlyList<string> DistinctTissues(long subject)
    {
        var a = TissueA(subject);
        var b = TissueB(subject);
        return string.Equals(a, b, StringComparison.Ordinal) ? [a] : [a, b];
    }

    /// <summary>The distinct <c>Signal</c> values a subject contributes, in first-observation order.</summary>
    public static IReadOnlyList<string> DistinctSignals(long subject)
    {
        var a = SignalA(subject);
        var b = SignalB(subject);
        return string.Equals(a, b, StringComparison.Ordinal) ? [a] : [a, b];
    }

    /// <summary>
    /// The physical order in which a layout emits its rows.
    /// <para>
    /// One definition, used by the writer and by every oracle, so a file's order and an expectation
    /// about that order cannot drift apart. It matters beyond bookkeeping: a discovered domain is
    /// recorded in <b>first-observation</b> order (§17 rule 3), so the two layouts legitimately
    /// discover the same values in different orders, and an expectation that assumed otherwise
    /// would be wrong rather than the code.
    /// </para>
    /// </summary>
    public static IEnumerable<(long Subject, int Row)> PhysicalOrder(long records, TripleLayout layout)
    {
        var subjects = Subjects(records);
        if (layout == TripleLayout.Grouped)
        {
            for (var subject = 0L; subject < subjects; subject++)
            {
                for (var row = 0; row < RowsPerSubject; row++)
                {
                    yield return (subject, row);
                }
            }

            yield break;
        }

        // Round-robin within a block: every subject in the block appears (in subject order) before
        // any of them appears a second time, so first-appearance SUBJECT order is unchanged while no
        // subject's rows are contiguous.
        for (var start = 0L; start < subjects; start += InterleaveBlock)
        {
            var end = Math.Min(start + InterleaveBlock, subjects);
            for (var row = 0; row < RowsPerSubject; row++)
            {
                for (var subject = start; subject < end; subject++)
                {
                    yield return (subject, row);
                }
            }
        }
    }

    /// <summary>
    /// Streams <paramref name="records"/> triple rows in <paramref name="layout"/>, as UTF-8
    /// without BOM and LF line endings. Headerless, matching the triple shape default (§5.1).
    /// </summary>
    public static void Write(
        Stream destination, long records, TripleLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(records);

        if (records % RowsPerSubject != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(records), records, $"a T10 tier must be a whole number of {RowsPerSubject}-row subjects.");
        }

        using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1 << 16,
            leaveOpen: true);

        var line = new StringBuilder(96);
        var emitted = 0L;
        foreach (var (subject, row) in PhysicalOrder(records, layout))
        {
            Cancel(emitted++, cancellationToken);
            WriteRow(writer, line, subject, row);
        }

        writer.Flush();
    }

    private static void Cancel(long counter, CancellationToken cancellationToken)
    {
        if ((counter & 0x3FFF) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // No field here can contain the delimiter, a quote, or a line break by construction, so no
    // escaping is reachable; the wide family owns the quoting case.
    private static void WriteRow(TextWriter writer, StringBuilder line, long subject, int row)
    {
        var (predicate, value) = Row(subject, row);
        line.Clear();
        line.Append(SubjectName(subject)).Append(',').Append(predicate).Append(',').Append(value).Append('\n');
        writer.Write(line);
    }
}
