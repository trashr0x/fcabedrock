using System.Globalization;
using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// The <c>plan</c> stdout byte contract (D-122 part 10). Compact, plain text, line-oriented,
/// LF only, one final LF, no byte-order mark, and deterministic for a given plan.
/// <code>
/// formal_attributes = &lt;N&gt;
/// formal_attribute &lt;i&gt;: attribute=&lt;str&gt; scale=&lt;str&gt; bin_key=&lt;str&gt; operator=&lt;str&gt; rendered=&lt;str&gt; source=&lt;src&gt; bin=&lt;bin&gt;
/// calibrations = &lt;M&gt;
/// calibration &lt;j&gt;: attribute=&lt;str&gt; kind=&lt;kind&gt; &lt;values&gt;
/// restrictions = &lt;K&gt;
/// restriction &lt;k&gt;: attribute=&lt;str&gt; source=&lt;src&gt; value_type=&lt;vt&gt; unknown_value_policy=&lt;uvp&gt; entries=[&lt;e&gt;, …]
/// schema_fingerprint = &lt;fp&gt;
/// cxt_output_fingerprint = &lt;fp&gt;
/// dat_output_fingerprint = &lt;fp&gt;
/// </code>
/// <para>
/// <b>Vocabulary.</b> <c>&lt;src&gt;</c> is <c>column index=&lt;int&gt;</c> or
/// <c>predicate name=&lt;str&gt;</c>. <c>&lt;bin&gt;</c> is <c>value label=&lt;str&gt;</c>,
/// <c>numeric_cut lo=&lt;num|none&gt; hi=&lt;num|none&gt;</c>, or
/// <c>text_cut lo=&lt;str|none&gt; hi=&lt;str|none&gt;</c> — an unbounded end is the explicit bare
/// token <c>none</c>, never a blank or an infinity spelling. <c>&lt;kind&gt;</c> reuses §15's
/// settled calibration vocabulary (<c>cuts</c>, <c>observed_domain</c>,
/// <c>include_additions</c>, <c>passthrough_bins</c>). <c>&lt;e&gt;</c> is
/// <c>string(&lt;str&gt;)</c>, <c>number(&lt;num&gt;)</c>, or
/// <c>range(from=&lt;num|none&gt;, to=&lt;num|none&gt;)</c>.
/// </para>
/// <para>
/// <b>Why every string is a JSON literal.</b> Attribute names, rendered names, bin labels,
/// predicate selectors, and domain values are author- and data-controlled: a quote, a
/// backslash, a tab, a control character, or a newline in any of them would otherwise break
/// the line structure this format depends on. Escaping them through the CLI's own JSON string
/// rules keeps one line one record, and printable Unicode survives verbatim.
/// </para>
/// <para>
/// <b>What is deliberately absent.</b> No CLR type name, no <c>enum.ToString()</c> — every
/// fixed token is spelled here, where the contract is (the <c>DiagnosticRenderer</c>
/// precedent). No set or dictionary is enumerated, so no hash-order artifact can reach the
/// bytes: the recognized-bin set and the bin→ids map are not reported. No culture-sensitive
/// text, no object identity, no filesystem identity key, no audit data, no clock.
/// </para>
/// </summary>
internal static class PlanReport
{
    /// <summary>Renders the complete report for <paramref name="plan"/> and its native <paramref name="fingerprints"/>.</summary>
    public static string Render(ConversionPlan plan, ComputedFingerprints fingerprints)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fingerprints);

        // Source lookup by logical attribute name: names are unique by the time a plan exists
        // (AttributeNameDuplicate is an Error at resolve), and the planner registers a
        // PlannedAttribute for every attribute that contributes a formal column.
        var sources = new Dictionary<string, AttributeSource>(StringComparer.Ordinal);
        foreach (var attribute in plan.Attributes)
        {
            sources[attribute.Name] = attribute.Source;
        }

        var builder = new StringBuilder();

        AppendCount(builder, "formal_attributes", plan.FormalAttributes.Count);
        foreach (var formal in plan.FormalAttributes)
        {
            AppendFormalAttribute(builder, formal, sources);
        }

        AppendCount(builder, "calibrations", plan.Calibrated.Calibrations.Count);
        for (var i = 0; i < plan.Calibrated.Calibrations.Count; i++)
        {
            AppendCalibration(builder, i, plan.Calibrated.Calibrations[i]);
        }

        AppendCount(builder, "restrictions", plan.Restrictions.Count);
        for (var i = 0; i < plan.Restrictions.Count; i++)
        {
            AppendRestriction(builder, i, plan.Restrictions[i]);
        }

        AppendValue(builder, "schema_fingerprint", fingerprints.SchemaFingerprint);
        AppendValue(builder, "cxt_output_fingerprint", fingerprints.CxtOutputFingerprint);
        AppendValue(builder, "dat_output_fingerprint", fingerprints.DatOutputFingerprint);

        return builder.ToString();
    }

    private static void AppendCount(StringBuilder builder, string label, int count)
    {
        builder.Append(label);
        builder.Append(" = ");
        builder.Append(Integer(count));
        builder.Append('\n');
    }

    private static void AppendValue(StringBuilder builder, string label, string value)
    {
        builder.Append(label);
        builder.Append(" = ");
        builder.Append(value);
        builder.Append('\n');
    }

    // The zero-based plan position is FormalAttribute.Id — the same number the .dat column
    // order and the emitter's crossed-id lists use, not a re-derived counter.
    private static void AppendFormalAttribute(
        StringBuilder builder, FormalAttribute formal, Dictionary<string, AttributeSource> sources)
    {
        builder.Append("formal_attribute ");
        builder.Append(Integer(formal.Id));
        builder.Append(": ");

        // The canonical identity first, complete and in fixed order, then the rendered name:
        // the two are different things (§14), and the report must never let them be confused.
        AppendString(builder, "attribute", formal.Identity.AttributeName);
        builder.Append(' ');
        AppendString(builder, "scale", formal.Identity.Scale);
        builder.Append(' ');
        AppendString(builder, "bin_key", formal.Identity.BinKey);
        builder.Append(' ');
        AppendString(builder, "operator", formal.Identity.Operator);
        builder.Append(' ');
        AppendString(builder, "rendered", formal.RenderedName);
        builder.Append(' ');
        AppendSource(builder, sources[formal.Identity.AttributeName]);
        builder.Append(' ');
        AppendBin(builder, formal.Bin);
        builder.Append('\n');
    }

    private static void AppendSource(StringBuilder builder, AttributeSource source)
    {
        switch (source)
        {
            case ColumnAttributeSource column:
                builder.Append("source=column index=");
                builder.Append(Integer(column.Index));
                break;

            case PredicateAttributeSource predicate:
                builder.Append("source=predicate ");
                AppendString(builder, "name", predicate.Predicate);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(source), source, "Unknown planned attribute source.");
        }
    }

    private static void AppendBin(StringBuilder builder, CanonicalBin bin)
    {
        switch (bin)
        {
            case ValueBin value:
                builder.Append("bin=value ");
                AppendString(builder, "label", value.Label);
                break;

            case NumericCutBin numeric:
                builder.Append("bin=numeric_cut lo=");
                builder.Append(numeric.Lo is { } lo ? Number(lo) : Unbounded);
                builder.Append(" hi=");
                builder.Append(numeric.Hi is { } hi ? Number(hi) : Unbounded);
                break;

            case TextCutBin text:
                builder.Append("bin=text_cut lo=");
                AppendOptionalString(builder, text.Lo);
                builder.Append(" hi=");
                AppendOptionalString(builder, text.Hi);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(bin), bin, "Unknown canonical bin.");
        }
    }

    // §15's kinds, verbatim, so one vocabulary describes a retained outcome wherever it is
    // reported. A legitimately empty outcome renders `values=[]` explicitly — the zero-discovery
    // marker is data, not an omission (D-104/REG-PRES-007).
    private static void AppendCalibration(StringBuilder builder, int index, AttributeCalibration calibration)
    {
        builder.Append("calibration ");
        builder.Append(Integer(index));
        builder.Append(": ");
        AppendString(builder, "attribute", calibration.AttributeName);
        builder.Append(' ');

        switch (calibration)
        {
            case CalibratedCuts cuts:
                builder.Append("kind=cuts cuts=");
                AppendNumbers(builder, cuts.Cuts);
                break;

            case ObservedDomain observed:
                builder.Append("kind=observed_domain values=");
                AppendStrings(builder, observed.Values);
                break;

            case IncludeAdditions additions:
                builder.Append("kind=include_additions values=");
                AppendStrings(builder, additions.Values);
                break;

            case PassthroughBins bins:
                builder.Append("kind=passthrough_bins values=");
                AppendStrings(builder, bins.Values);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(calibration), calibration, "Unknown calibration outcome.");
        }

        builder.Append('\n');
    }

    private static void AppendRestriction(StringBuilder builder, int index, PlannedRestriction restriction)
    {
        builder.Append("restriction ");
        builder.Append(Integer(index));
        builder.Append(": ");
        AppendString(builder, "attribute", restriction.AttributeName);
        builder.Append(' ');
        AppendSource(builder, restriction.Source);
        builder.Append(" value_type=");
        builder.Append(Spell(restriction.ValueType));
        builder.Append(" unknown_value_policy=");
        builder.Append(Spell(restriction.UnknownValuePolicy));
        builder.Append(" entries=[");

        for (var i = 0; i < restriction.Entries.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            AppendEntry(builder, restriction.Entries[i]);
        }

        builder.Append("]\n");
    }

    private static void AppendEntry(StringBuilder builder, RestrictToEntry entry)
    {
        switch (entry)
        {
            case RestrictToValue value:
                builder.Append("string(");
                AppendLiteral(builder, value.Value);
                builder.Append(')');
                break;

            case RestrictToNumber number:
                builder.Append("number(");
                builder.Append(Number(number.Value));
                builder.Append(')');
                break;

            case RestrictToRange range:
                builder.Append("range(from=");
                builder.Append(range.From is { } from ? Number(from) : Unbounded);
                builder.Append(", to=");
                builder.Append(range.To is { } to ? Number(to) : Unbounded);
                builder.Append(')');
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(entry), entry, "Unknown restriction entry.");
        }
    }

    private static void AppendStrings(StringBuilder builder, IReadOnlyList<string> values)
    {
        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            AppendLiteral(builder, values[i]);
        }

        builder.Append(']');
    }

    private static void AppendNumbers(StringBuilder builder, IReadOnlyList<double> values)
    {
        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Number(values[i]));
        }

        builder.Append(']');
    }

    private static void AppendString(StringBuilder builder, string key, string value)
    {
        builder.Append(key);
        builder.Append('=');
        AppendLiteral(builder, value);
    }

    private static void AppendOptionalString(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append(Unbounded);
            return;
        }

        AppendLiteral(builder, value);
    }

    private static void AppendLiteral(StringBuilder builder, string value) =>
        JsonStringEscaping.AppendLiteral(builder, value);

    /// <summary>The bare token an unbounded cut end or range bound renders as.</summary>
    private const string Unbounded = "none";

    // The one §14 number rule, reused rather than restated: 30, 30.0 and 3e1 all render 30,
    // invariantly and identically on every machine (D-096/D-101).
    private static string Number(double value) => CanonicalNumber.Format(value);

    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);

    // Spelled out where the byte contract lives, so renaming an enum member cannot move these
    // bytes and no CLR identifier reaches stdout.
    private static string Spell(SourceValueType valueType) => valueType switch
    {
        SourceValueType.String => "string",
        SourceValueType.Number => "number",
        _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "Unknown source value type."),
    };

    private static string Spell(UnknownValuePolicy policy) => policy switch
    {
        UnknownValuePolicy.Skip => "skip",
        UnknownValuePolicy.Warn => "warn",
        UnknownValuePolicy.Fail => "fail",
        UnknownValuePolicy.Include => "include",
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown unknown-value policy."),
    };
}
