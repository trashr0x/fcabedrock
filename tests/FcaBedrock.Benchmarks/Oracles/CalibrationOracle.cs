using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Core.Calibration;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// Independent expectations for what a calibration pass over a W16 tier must resolve.
/// <para>
/// A calibration benchmark that only proved "a pass completed" would measure nothing worth
/// reporting: the whole point of the phase is which cuts and which domain it produces, and both are
/// data-dependent, so both are exactly the kind of result that can drift silently. These
/// expectations are derived from the corpus definition and the documented calibration semantics,
/// never from the calibrator — and they are exact, not approximate, because the population is a
/// known arithmetic sequence rather than something that has to be sorted to be known.
/// </para>
/// </summary>
internal static class CalibrationOracle
{
    /// <summary>
    /// The observed domain <c>c2</c> must resolve to: its distinct cleaned values in
    /// <b>first-observation</b> order over the input universe (§17 rule 3).
    /// </summary>
    public static IReadOnlyList<string> ExpectedC2Domain(long records)
    {
        var seen = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var column = W16Corpus.ColCategoricalFirst + 2;

        for (var row = 0L; row < records; row++)
        {
            if (W16Corpus.CleanedValue(row, column) is { } value && known.Add(value))
            {
                seen.Add(value);
            }
        }

        return seen;
    }

    /// <summary>The exact minimum and maximum of <c>n_wide</c> over the input universe.</summary>
    public static (double Min, double Max) ExpectedWideRange(long records)
    {
        var min = long.MaxValue;
        var max = long.MinValue;
        for (var row = 0L; row < records; row++)
        {
            var value = W16Corpus.WideHundredths(row);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        return (Determinism.HundredthsValue(min), Determinism.HundredthsValue(max));
    }

    /// <summary>
    /// The exact equal-frequency cuts for <c>n_seq</c>.
    /// <para>
    /// <c>n_seq</c> is the row index, so the population is <c>0 .. records-1</c> with every value
    /// distinct and already ascending — which makes the k-th boundary's order statistic knowable by
    /// arithmetic. Equal-frequency places <c>bins - 1</c> boundaries at ranks <c>N*k/bins</c>; with
    /// all values distinct there are no tied groups to resolve and no feasibility clamp to apply, so
    /// the cut is simply the value at that rank. Ranks are computed in <see cref="System.Int128"/>
    /// exactly as the production selection does, because at the target population a
    /// <c>double</c> rank target rounds onto the wrong order statistic.
    /// </para>
    /// </summary>
    public static IReadOnlyList<double> ExpectedSeqCuts(long records, int bins)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);

        var cuts = new List<double>(bins - 1);
        for (var k = 1; k < bins; k++)
        {
            // The first value whose cumulative count reaches N*k/bins. Cumulative count at value v
            // is v+1, so the boundary value is ceil(N*k/bins) - 1 and the cut is the next value up:
            // the half-open [lo, hi) geometry puts the boundary at the first value of the new bin.
            var target = ((Int128)records * k) + bins - 1;
            var index = (long)(target / bins);
            cuts.Add(index);
        }

        return cuts;
    }

    /// <summary>The calibration outcome recorded for <paramref name="attribute"/>, or null.</summary>
    public static AttributeCalibration? Outcome(CalibratedSpec calibrated, string attribute)
    {
        ArgumentNullException.ThrowIfNull(calibrated);
        return calibrated.Calibrations.FirstOrDefault(
            outcome => string.Equals(outcome.AttributeName, attribute, StringComparison.Ordinal));
    }

    /// <summary>
    /// Validates a completed W16 calibration against the expectations above, and against the shape
    /// contract the calibrated state itself must satisfy — one retained outcome per data-dependent
    /// attribute, of the kind that attribute's configuration requires.
    /// </summary>
    public static void RequireW16(CalibratedSpec calibrated, long records, string what)
    {
        ArgumentNullException.ThrowIfNull(calibrated);

        var seq = Require<CalibratedCuts>(calibrated, "n_seq", what);
        var expectedSeq = ExpectedSeqCuts(records, W16CalibrationSpec.SeqBins);
        if (!seq.Cuts.SequenceEqual(expectedSeq))
        {
            throw new InvalidOperationException(
                $"{what}: n_seq calibrated to [{string.Join(", ", seq.Cuts)}], expected [{string.Join(", ", expectedSeq)}].");
        }

        // The min/max and percentile attributes resolve to `bins - 1` finite, strictly ascending
        // cuts inside the range the data actually spans. The exact interpolated values are the
        // production formula's business (D-102 pins it); what an independent oracle can and should
        // assert is that they are well formed and bracketed by the real extremes.
        var wide = Require<CalibratedCuts>(calibrated, "n_wide", what);
        var (min, max) = ExpectedWideRange(records);
        RequireCutShape(wide.Cuts, W16CalibrationSpec.WideBins, min, max, "n_wide", what);

        var skew = Require<CalibratedCuts>(calibrated, "n_skew", what);
        RequireCutShape(skew.Cuts, W16CalibrationSpec.SkewBins, 7, int.MaxValue, "n_skew", what);

        var domain = Require<ObservedDomain>(calibrated, "c2", what);
        var expectedDomain = ExpectedC2Domain(records);
        if (!domain.Values.SequenceEqual(expectedDomain, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{what}: c2 observed [{string.Join(", ", domain.Values)}], expected [{string.Join(", ", expectedDomain)}].");
        }
    }

    /// <summary>
    /// The observed domain <c>Tissue</c> must resolve to, for a given physical layout: its distinct
    /// cleaned values in <b>first-observation</b> order over the raw stream (§17 rule 3).
    /// <para>
    /// This is layout-dependent on purpose, and it is the one place the two T10 layouts legitimately
    /// differ. They carry the same observations, so they discover the same <em>set</em>; but the
    /// interleaved file reaches every subject's first row before any subject's second, so the
    /// <em>order</em> in which those values are first seen is genuinely different. A declared-domain
    /// conversion is therefore byte-identical across the layouts, while a discovered one need not
    /// be — and an expectation that assumed otherwise would be wrong about the spec, not about the
    /// code.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ExpectedTissueDomain(long records, TripleLayout layout)
    {
        var seen = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (subject, row) in T10Corpus.PhysicalOrder(records, layout))
        {
            var (predicate, value) = T10Corpus.Row(subject, row);
            if (string.Equals(predicate, T10Corpus.TissuePredicate, StringComparison.Ordinal) && known.Add(value))
            {
                seen.Add(value);
            }
        }

        return seen;
    }

    /// <summary>The exact minimum and maximum <c>Stage</c> over a tier's subjects.</summary>
    public static (double Min, double Max) ExpectedStageRange(long records)
    {
        var subjects = T10Corpus.Subjects(records);
        var min = int.MaxValue;
        var max = int.MinValue;
        for (var subject = 0L; subject < subjects; subject++)
        {
            var stage = T10Corpus.Stage(subject);
            min = Math.Min(min, stage);
            max = Math.Max(max, stage);
        }

        return (min, max);
    }

    /// <summary>
    /// Validates a completed T10 calibration.
    /// <para>
    /// The observed domain is checked <b>exactly</b>, including its layout-specific order. The
    /// equal-frequency cuts are checked for shape and bracketing rather than value: this population
    /// is deliberately tie-heavy — every subject contributes its stage twice, once per raw spelling —
    /// so an exact expectation would mean re-implementing the boundary-feasibility algorithm D-103
    /// pins, and an oracle that reimplements the thing it checks is not evidence. The exact-value
    /// check lives on the wide case instead, where the population is an arithmetic sequence and the
    /// order statistics are knowable without sorting; the property that <em>both triple layouts
    /// produce the same cuts</em> is proved separately, where an equality is the honest assertion.
    /// </para>
    /// </summary>
    public static void RequireT10(CalibratedSpec calibrated, long records, string what)
    {
        ArgumentNullException.ThrowIfNull(calibrated);

        var layout = what.Contains("grouped", StringComparison.Ordinal)
            ? TripleLayout.Grouped
            : TripleLayout.Interleaved;

        var domain = Require<ObservedDomain>(calibrated, "Tissue", what);
        var expected = ExpectedTissueDomain(records, layout);
        if (!domain.Values.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{what}: Tissue observed [{string.Join(", ", domain.Values)}], expected [{string.Join(", ", expected)}].");
        }

        var stage = Require<CalibratedCuts>(calibrated, "Stage", what);
        var (min, max) = ExpectedStageRange(records);
        RequireCutShape(stage.Cuts, bins: 8, min, max, "Stage", what);
    }

    /// <summary>
    /// The exact minimum and maximum of a W16 numeric column over the input universe, derived by
    /// enumerating the generator. Missing cells are skipped, exactly as a calibration population
    /// skips them.
    /// </summary>
    public static (double Min, double Max) ExpectedNumericRange(long records, int column)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var row = 0L; row < records; row++)
        {
            if (W16Corpus.CleanedValue(row, column) is not { } text)
            {
                continue;
            }

            var value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        return (min, max);
    }

    /// <summary>
    /// Validates the many-quantile calibration: sixteen exact equal-frequency attributes, four bin
    /// counts over each of the four numeric columns, all resolved in one pass.
    /// <para>
    /// <c>n_seq</c> is checked <b>exactly</b> at every bin count — its population is the row index,
    /// so each boundary is an order statistic over a known arithmetic sequence and no sorting is
    /// needed to know it. The other three columns are deliberately tie-heavy or skewed, so an exact
    /// expectation would mean re-implementing the D-103 feasibility algorithm under test; there the
    /// check is cut shape and bracketing by the real extremes. Which is which is stated rather than
    /// implied, because the two are not equally strong.
    /// </para>
    /// </summary>
    public static void RequireManyQuantiles(CalibratedSpec calibrated, long records, string what)
    {
        ArgumentNullException.ThrowIfNull(calibrated);

        foreach (var (name, column) in W16PressureSpecs.NumericColumns)
        {
            var (min, max) = ExpectedNumericRange(records, column);
            foreach (var bins in W16PressureSpecs.QuantileBinCounts)
            {
                var attribute = W16PressureSpecs.QuantileAttributeName(name, bins);
                var outcome = Require<CalibratedCuts>(calibrated, attribute, what);

                if (column == W16Corpus.ColSeq)
                {
                    var expected = ExpectedSeqCuts(records, bins);
                    if (!outcome.Cuts.SequenceEqual(expected))
                    {
                        throw new InvalidOperationException(
                            $"{what}: {attribute} calibrated to [{string.Join(", ", outcome.Cuts)}], "
                            + $"expected [{string.Join(", ", expected)}].");
                    }

                    continue;
                }

                RequireCutShape(outcome.Cuts, bins, min, max, attribute, what);
            }
        }

        if (calibrated.Calibrations.Count != W16PressureSpecs.ManyQuantileAttributeCount)
        {
            throw new InvalidOperationException(
                $"{what}: {calibrated.Calibrations.Count} retained outcomes, "
                + $"expected {W16PressureSpecs.ManyQuantileAttributeCount}.");
        }
    }

    /// <summary>
    /// The distinct values column <paramref name="column"/> takes over the input universe, in
    /// <b>first-observation</b> order (§17 rule 3), skipping missing cells.
    /// </summary>
    public static IReadOnlyList<string> ExpectedColumnDomain(long records, int column)
    {
        var seen = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        for (var row = 0L; row < records; row++)
        {
            if (W16Corpus.CleanedValue(row, column) is { } value && known.Add(value))
            {
                seen.Add(value);
            }
        }

        return seen;
    }

    /// <summary>
    /// Validates <c>include</c> recovery: the additions are exactly the column's values that the
    /// spec did not declare, in first-observation order, with the declared value absent from them.
    /// <para>
    /// Order is the assertion that earns its place. A recovery producing the right <em>set</em> in
    /// the wrong order would give a different column layout and different output bytes, and a
    /// set-equality check would not notice.
    /// </para>
    /// </summary>
    public static void RequireIncludeRecovery(CalibratedSpec calibrated, long records, string what)
    {
        ArgumentNullException.ThrowIfNull(calibrated);

        var outcome = Require<IncludeAdditions>(calibrated, "c0", what);
        var expected = ExpectedColumnDomain(records, W16Corpus.ColCategoricalFirst)
            .Where(value => !string.Equals(value, W16RecoverySpecs.FirstDeclared, StringComparison.Ordinal))
            .ToList();

        if (!outcome.Values.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{what}: c0 recovered [{string.Join(", ", outcome.Values)}], expected [{string.Join(", ", expected)}].");
        }
    }

    /// <summary>
    /// Validates <c>value_groups</c> pass-through: the discovered bins are exactly the column's
    /// values that the authored group does not cover, in first-observation order.
    /// </summary>
    public static void RequirePassthrough(CalibratedSpec calibrated, long records, string what)
    {
        ArgumentNullException.ThrowIfNull(calibrated);

        var outcome = Require<PassthroughBins>(calibrated, "c0", what);
        var grouped = new[] { W16RecoverySpecs.FirstDeclared, W16RecoverySpecs.SecondDeclared };
        var expected = ExpectedColumnDomain(records, W16Corpus.ColCategoricalFirst)
            .Where(value => !grouped.Contains(value, StringComparer.Ordinal))
            .ToList();

        if (!outcome.Values.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{what}: c0 passed through [{string.Join(", ", outcome.Values)}], "
                + $"expected [{string.Join(", ", expected)}].");
        }
    }

    private static void RequireCutShape(
        IReadOnlyList<double> cuts, int bins, double lowerBound, double upperBound, string attribute, string what)
    {
        if (cuts.Count != bins - 1)
        {
            throw new InvalidOperationException(
                $"{what}: {attribute} produced {cuts.Count} cuts, expected {bins - 1}.");
        }

        for (var i = 0; i < cuts.Count; i++)
        {
            if (!double.IsFinite(cuts[i]))
            {
                throw new InvalidOperationException($"{what}: {attribute} cut {i} is not finite ({cuts[i]}).");
            }

            if (i > 0 && cuts[i] <= cuts[i - 1])
            {
                throw new InvalidOperationException($"{what}: {attribute} cuts are not strictly ascending.");
            }
        }

        if (cuts[0] < lowerBound || cuts[^1] > upperBound)
        {
            throw new InvalidOperationException(
                $"{what}: {attribute} cuts [{cuts[0]}, {cuts[^1]}] fall outside the observed range "
                + $"[{lowerBound}, {upperBound}].");
        }
    }

    private static T Require<T>(CalibratedSpec calibrated, string attribute, string what)
        where T : AttributeCalibration
    {
        var outcome = Outcome(calibrated, attribute);
        return outcome as T
            ?? throw new InvalidOperationException(
                $"{what}: {attribute} retained {outcome?.GetType().Name ?? "no outcome"}, expected {typeof(T).Name}.");
    }
}
