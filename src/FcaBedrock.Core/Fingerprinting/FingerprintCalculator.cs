using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Fingerprinting;

/// <summary>
/// Computes the three spec fingerprints (spec §14) as SHA-256 over the pinned
/// canonical JSON generated from the resolved plan — never from TOML text
/// (D-053/D-069/D-077). Output is <c>"sha256:" + 64 lowercase hex chars</c> of
/// the UTF-8 (no BOM) canonical bytes. <c>schema_fingerprint</c> hashes only the
/// ordered planned canonical identities (D-035); the per-format output
/// fingerprints nest that schema array beside the shared row/binding inputs and
/// the format's own writer settings, never mixed (D-051/D-069). The JSON enum
/// vocabulary mirrors the spec's TOML spellings, locked by the canonical-bytes
/// goldens and the spelling test in <c>FingerprintCalculatorTests</c>.
/// </summary>
public static class FingerprintCalculator
{
    private const string HashPrefix = "sha256:";

    /// <summary>
    /// The <c>schema_fingerprint</c>: the final ordered list of planned canonical
    /// formal-attribute identities, and nothing else (§14, D-035).
    /// </summary>
    public static string ComputeSchemaFingerprint(ConversionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Hash(BuildSchemaJson(plan));
    }

    /// <summary>
    /// The <c>cxt_output_fingerprint</c>: schema + shared conversion inputs +
    /// rendered names, label style, <c>bin_label_unicode</c> and the <c>.cxt</c>
    /// writer settings (§14, D-051). The shared inputs are read from the plan's
    /// calibrated spec (D-094 effective-configuration rule).
    /// <para><b>Precondition:</b> <paramref name="inputs"/>.<see cref="CxtFingerprintInputs.LabelStyle"/>
    /// must equal <paramref name="plan"/>.<see cref="ConversionPlan.LabelStyle"/> —
    /// <see cref="FormalAttribute.RenderedName"/> already bakes the style in, so a
    /// mismatched pair would hash an inconsistent, unreproducible combination. This is
    /// validated (throws <see cref="ArgumentException"/>) rather than merely documented.</para>
    /// </summary>
    public static string ComputeCxtOutputFingerprint(ConversionPlan plan, CxtFingerprintInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.LabelStyle != plan.LabelStyle)
        {
            throw new ArgumentException(
                $"inputs.LabelStyle ({inputs.LabelStyle}) must match the plan's LabelStyle ({plan.LabelStyle}) (§14).",
                nameof(inputs));
        }

        return Hash(BuildCxtOutputJson(plan, plan.Calibrated.Spec, inputs));
    }

    /// <summary>
    /// The <c>dat_output_fingerprint</c>: schema + shared conversion inputs + the
    /// <c>.dat</c> writer settings. Rendered names never enter (§14, D-051). The
    /// shared inputs are read from the plan's calibrated spec (D-094).
    /// </summary>
    public static string ComputeDatOutputFingerprint(ConversionPlan plan, DatFingerprintInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inputs);
        return Hash(BuildDatOutputJson(plan, plan.Calibrated.Spec, inputs));
    }

    // The canonical-bytes builders are internal so the D-069 canonical-stability
    // golden can lock the exact JSON, not just the hash.

    internal static string BuildSchemaJson(ConversionPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append("{\"attributes\":");
        AppendSchemaAttributes(builder, plan);
        builder.Append(",\"fp_format\":1,\"kind\":\"schema\"}");
        return builder.ToString();
    }

    internal static string BuildCxtOutputJson(ConversionPlan plan, BedrockSpec spec, CxtFingerprintInputs inputs)
    {
        var builder = new StringBuilder();
        builder.Append("{\"cxt\":{\"bin_label_unicode\":");
        CanonicalJson.AppendBool(builder, inputs.BinLabelUnicode);
        builder.Append(",\"label_style\":");
        CanonicalJson.AppendString(builder, Spell(inputs.LabelStyle));
        builder.Append(",\"line_endings\":");
        CanonicalJson.AppendString(builder, Spell(inputs.LineEnding));
        builder.Append(",\"rendered_names\":[");
        for (var i = 0; i < plan.FormalAttributes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            CanonicalJson.AppendString(builder, plan.FormalAttributes[i].RenderedName);
        }

        builder.Append("],\"trailing_newline\":");
        CanonicalJson.AppendBool(builder, inputs.TrailingNewline);
        builder.Append("},\"fp_format\":1,\"kind\":\"cxt_output\",\"schema\":");
        AppendSchemaAttributes(builder, plan);
        builder.Append(",\"shared\":");
        AppendShared(builder, spec);
        builder.Append('}');
        return builder.ToString();
    }

    internal static string BuildDatOutputJson(ConversionPlan plan, BedrockSpec spec, DatFingerprintInputs inputs)
    {
        var builder = new StringBuilder();
        builder.Append("{\"dat\":{\"base_index\":");
        CanonicalJson.AppendNumber(builder, inputs.BaseIndex);
        builder.Append(",\"empty_line_trailing_space\":");
        CanonicalJson.AppendBool(builder, inputs.EmptyLineTrailingSpace);
        builder.Append(",\"line_endings\":");
        CanonicalJson.AppendString(builder, Spell(inputs.LineEnding));
        builder.Append(",\"nonempty_line_trailing_space\":");
        CanonicalJson.AppendBool(builder, inputs.NonemptyLineTrailingSpace);

        // trailing_newline sorts last of the dat keys (alphabetical) and is emitted ONLY
        // when disabled. Omitting it at the historical default (true) keeps every pinned
        // .dat fingerprint byte-identical — the backward-compat contract (D-087) — while a
        // false value produces a distinct hash. This deliberately diverges from the cxt
        // twin (BuildCxtOutputJson), which has always emitted trailing_newline
        // unconditionally; the divergence is what preserves the pre-D-087 dat pins.
        if (!inputs.TrailingNewline)
        {
            builder.Append(",\"trailing_newline\":false");
        }

        builder.Append("},\"fp_format\":1,\"kind\":\"dat_output\",\"schema\":");
        AppendSchemaAttributes(builder, plan);
        builder.Append(",\"shared\":");
        AppendShared(builder, spec);
        builder.Append('}');
        return builder.ToString();
    }

    // The ordered §14 canonical-identity array: one {"bin","name","op","scale"}
    // per planned column, in plan order (D-069).
    private static void AppendSchemaAttributes(StringBuilder builder, ConversionPlan plan)
    {
        builder.Append('[');
        for (var i = 0; i < plan.FormalAttributes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var attribute = plan.FormalAttributes[i];
            builder.Append("{\"bin\":");
            AppendBin(builder, attribute.Bin);
            builder.Append(",\"name\":");
            CanonicalJson.AppendString(builder, attribute.Identity.AttributeName);
            builder.Append(",\"op\":");
            CanonicalJson.AppendString(builder, attribute.Identity.Operator);
            builder.Append(",\"scale\":");
            CanonicalJson.AppendString(builder, attribute.Identity.Scale);
            builder.Append('}');
        }

        builder.Append(']');
    }

    // D-069/D-077: a value bin / threshold / missing key is its plain string; a
    // cut bin is the fixed four-key object whose lo_open/hi_open booleans mean
    // "unbounded end" (null bound), never interval inclusivity.
    private static void AppendBin(StringBuilder builder, CanonicalBin bin)
    {
        switch (bin)
        {
            case ValueBin value:
                CanonicalJson.AppendString(builder, value.Label);
                break;

            case NumericCutBin numeric:
                builder.Append("{\"hi\":");
                if (numeric.Hi is { } hi)
                {
                    CanonicalJson.AppendNumber(builder, hi);
                }
                else
                {
                    builder.Append("null");
                }

                builder.Append(",\"hi_open\":");
                CanonicalJson.AppendBool(builder, numeric.Hi is null);
                builder.Append(",\"lo\":");
                if (numeric.Lo is { } lo)
                {
                    CanonicalJson.AppendNumber(builder, lo);
                }
                else
                {
                    builder.Append("null");
                }

                builder.Append(",\"lo_open\":");
                CanonicalJson.AppendBool(builder, numeric.Lo is null);
                builder.Append('}');
                break;

            case TextCutBin text:
                builder.Append("{\"hi\":");
                if (text.Hi is { } textHi)
                {
                    CanonicalJson.AppendString(builder, textHi);
                }
                else
                {
                    builder.Append("null");
                }

                builder.Append(",\"hi_open\":");
                CanonicalJson.AppendBool(builder, text.Hi is null);
                builder.Append(",\"lo\":");
                if (text.Lo is { } textLo)
                {
                    CanonicalJson.AppendString(builder, textLo);
                }
                else
                {
                    builder.Append("null");
                }

                builder.Append(",\"lo_open\":");
                CanonicalJson.AppendBool(builder, text.Lo is null);
                builder.Append('}');
                break;

            default:
                throw new InvalidOperationException($"Unknown canonical bin type {bin.GetType().Name}.");
        }
    }

    // The D-051 shared inputs: the conversion-affecting binding/source settings
    // and per-attribute discretizer/scale configuration. Included attributes
    // only — an excluded attribute contributes nothing to output in M2; its
    // restrict_to joins as a shared input when execution lands at M4 (§14,
    // D-077). restrict_to is absent entirely until then.
    private static void AppendShared(StringBuilder builder, BedrockSpec spec)
    {
        builder.Append("{\"attributes\":[");
        var first = true;
        foreach (var attribute in spec.Attributes)
        {
            if (!attribute.Include)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            AppendSharedAttribute(builder, attribute);
        }

        builder.Append("],\"binding\":");
        AppendBinding(builder, spec.Binding);
        builder.Append('}');
    }

    private static void AppendSharedAttribute(StringBuilder builder, AttributeSpec attribute)
    {
        // Planner invariant: an included attribute always carries both (P-10).
        var discretizer = attribute.Discretizer
            ?? throw new InvalidOperationException($"Included attribute '{attribute.Name}' has no discretizer.");
        var scale = attribute.Scale
            ?? throw new InvalidOperationException($"Included attribute '{attribute.Name}' has no scale.");

        // Effective domain only: cut discretizers ignore declared_domain (§10.3),
        // so an inert authored domain must not perturb the hash (D-077).
        builder.Append("{\"declared_domain\":");
        AppendStringArray(builder, discretizer.ConsumesDeclaredDomain ? attribute.DeclaredDomain : []);
        builder.Append(",\"discretizer\":");
        AppendDiscretizer(builder, discretizer);
        builder.Append(",\"missing_policy\":");
        CanonicalJson.AppendString(builder, Spell(attribute.MissingPolicy));
        builder.Append(",\"name\":");
        CanonicalJson.AppendString(builder, attribute.Name);
        builder.Append(",\"scale\":");
        AppendScale(builder, scale);
        builder.Append(",\"source\":");
        AppendSource(builder, attribute);
        builder.Append(",\"unknown_value_policy\":");
        CanonicalJson.AppendString(builder, Spell(attribute.UnknownValuePolicy));
        builder.Append('}');
    }

    private static void AppendDiscretizer(StringBuilder builder, Discretizer discretizer)
    {
        switch (discretizer)
        {
            case IdentityDiscretizer:
                builder.Append("{\"kind\":\"identity\"}");
                break;

            case FreePerValueDiscretizer:
                // §14/D-094: no config beyond the kind — its numeric-vs-string identity rides
                // on source.value_type (already in "source"), and its bins/domain are effective
                // (declared_domain, above). The effective numeric domain carries canonical keys.
                builder.Append("{\"kind\":\"free_per_value\"}");
                break;

            case ManualCutsDiscretizer manual:
                builder.Append("{\"cuts\":[");
                for (var i = 0; i < manual.Cuts.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    CanonicalJson.AppendNumber(builder, manual.Cuts[i]);
                }

                builder.Append("],\"ends\":");
                CanonicalJson.AppendString(builder, Spell(manual.Ends));
                builder.Append(",\"kind\":\"manual_cuts\"}");
                break;

            case OrderedCutsDiscretizer ordered:
                builder.Append("{\"cuts\":");
                AppendStringArray(builder, ordered.Cuts);
                builder.Append(",\"ends\":");
                CanonicalJson.AppendString(builder, Spell(ordered.Ends));
                builder.Append(",\"kind\":\"ordered_cuts\",\"order\":");
                AppendStringArray(builder, ordered.Order);
                builder.Append('}');
                break;

            case EqualWidthDiscretizer equalWidth:
                // §14/D-094: the AUTHORED configuration only — the resolved cuts are not
                // re-encoded here (they already ride as `bin` objects in the `schema` array,
                // so duplicating them would invite a two-source-of-truth drift). vmin/vmax
                // appear only under range = "manual", which is why an auto spec and its
                // frozen manual_cuts form share a schema_fingerprint yet may carry different
                // output fingerprints (sound: same output fingerprint ⇒ same bytes, not the
                // converse). Keys sort ordinal: bins < kind < precision < range < vmax < vmin.
                builder.Append("{\"bins\":");
                CanonicalJson.AppendNumber(builder, equalWidth.Bins);
                builder.Append(",\"kind\":\"equal_width\",\"precision\":");
                AppendPrecision(builder, equalWidth.Precision);
                builder.Append(",\"range\":");
                CanonicalJson.AppendString(builder, Spell(equalWidth.Range));
                if (equalWidth.Range == EqualWidthRange.Manual)
                {
                    // ResolvedSpec.Create pins vmin/vmax present exactly for manual (D-098).
                    builder.Append(",\"vmax\":");
                    CanonicalJson.AppendNumber(builder, equalWidth.VMax!.Value);
                    builder.Append(",\"vmin\":");
                    CanonicalJson.AppendNumber(builder, equalWidth.VMin!.Value);
                }

                builder.Append('}');
                break;

            case EqualFrequencyDiscretizer equalFrequency:
                // §14/D-094: the AUTHORED configuration only, with the resolved defaults spelled
                // ("left"/"right_value"). The calibrated cuts are not re-encoded here — they
                // already ride as `bin` objects in the `schema` array (the same effective-vs-
                // authored rule as equal_width), which is why an auto spec and its frozen
                // manual_cuts twin share a schema_fingerprint yet may carry different output
                // fingerprints. Keys sort ordinal: bins < cut_placement < kind < tie_policy.
                builder.Append("{\"bins\":");
                CanonicalJson.AppendNumber(builder, equalFrequency.Bins);
                builder.Append(",\"cut_placement\":");
                CanonicalJson.AppendString(builder, Spell(equalFrequency.CutPlacement));
                builder.Append(",\"kind\":\"equal_frequency\",\"tie_policy\":");
                CanonicalJson.AppendString(builder, Spell(equalFrequency.TiePolicy));
                builder.Append('}');
                break;

            default:
                // Deferred kinds reject at read/resolve (D-070) and never reach a
                // computable plan; hitting this is a programmer error.
                throw new InvalidOperationException($"Discretizer kind '{discretizer.Kind}' has no fingerprint encoding.");
        }
    }

    // §14/D-094: precision mirrors its two TOML forms — the string "exact" or the object
    // {"round_to":<number>}.
    private static void AppendPrecision(StringBuilder builder, CutPrecision precision)
    {
        switch (precision)
        {
            case ExactPrecision:
                builder.Append("\"exact\"");
                break;

            case RoundToPrecision roundTo:
                builder.Append("{\"round_to\":");
                CanonicalJson.AppendNumber(builder, roundTo.RoundTo);
                builder.Append('}');
                break;

            default:
                throw new InvalidOperationException($"Cut precision {precision.GetType().Name} has no fingerprint encoding.");
        }
    }

    private static void AppendScale(StringBuilder builder, Scale scale)
    {
        switch (scale)
        {
            case NominalScale:
                builder.Append("{\"kind\":\"nominal\"}");
                break;

            case DichotomicScale dichotomic:
                builder.Append("{\"kind\":\"dichotomic\",\"true_value\":");
                CanonicalJson.AppendString(builder, dichotomic.TrueValue);
                builder.Append('}');
                break;

            case OrdinalScale ordinal:
                builder.Append("{\"boundary\":");
                CanonicalJson.AppendString(builder, Spell(ordinal.Boundary));
                builder.Append(",\"direction\":");
                CanonicalJson.AppendString(builder, Spell(ordinal.Direction));
                builder.Append(",\"drop_top\":");
                CanonicalJson.AppendBool(builder, ordinal.DropTop);
                builder.Append(",\"kind\":\"ordinal\"");
                if (ordinal.Order is { } order)
                {
                    builder.Append(",\"order\":");
                    AppendStringArray(builder, order);
                }

                builder.Append('}');
                break;

            default:
                // UnimplementedScale fails planning with Fatal ScaleNotImplementedV1
                // (D-010), so no plan exists to fingerprint; programmer error here.
                throw new InvalidOperationException($"Scale kind '{scale.Kind}' has no fingerprint encoding.");
        }
    }

    private static void AppendSource(StringBuilder builder, AttributeSpec attribute)
    {
        switch (attribute.Source)
        {
            case ColumnSource column:
                builder.Append("{\"column\":");
                CanonicalJson.AppendNumber(builder, column.Index);
                builder.Append(",\"value_type\":");
                CanonicalJson.AppendString(builder, Spell(column.ValueType));
                builder.Append('}');
                break;

            case PredicateSource predicate:
                builder.Append("{\"predicate\":");
                CanonicalJson.AppendString(builder, predicate.Predicate);
                builder.Append(",\"value_type\":");
                CanonicalJson.AppendString(builder, Spell(predicate.ValueType));
                builder.Append('}');
                break;

            default:
                throw new InvalidOperationException(
                    $"Source binding {attribute.Source.GetType().Name} on attribute '{attribute.Name}' has no fingerprint encoding.");
        }
    }

    private static void AppendBinding(StringBuilder builder, Binding binding)
    {
        builder.Append('{');

        // Triple role→column-index map (§5.3/D-082): present only under triple, so
        // wide bindings keep their exact bytes (the D-077 present-only-when-applicable
        // precedent). "columns" sorts before "delimiter" ('c' < 'd'); its role keys
        // are ordinal-sorted (predicate/subject/value). A role bound by header name
        // resolves to the same indices as the equivalent index bind, so the two hash
        // identically. The triple `ordering` field is deliberately not encoded — both
        // orderings emit identical first-appearance bytes (D-082, Slice A).
        if (binding.TripleColumns is { } columns)
        {
            builder.Append("\"columns\":{\"predicate\":");
            CanonicalJson.AppendNumber(builder, columns.Predicate);
            builder.Append(",\"subject\":");
            CanonicalJson.AppendNumber(builder, columns.Subject);
            builder.Append(",\"value\":");
            CanonicalJson.AppendNumber(builder, columns.Value);
            builder.Append("},");
        }

        builder.Append("\"delimiter\":");
        CanonicalJson.AppendString(builder, binding.Delimiter.ToString());
        builder.Append(",\"encoding\":");
        CanonicalJson.AppendString(builder, binding.Encoding);
        builder.Append(",\"has_header\":");
        CanonicalJson.AppendBool(builder, binding.HasHeader);
        builder.Append(",\"locale\":");
        CanonicalJson.AppendString(builder, binding.Locale);
        builder.Append(",\"missing_token\":");
        CanonicalJson.AppendString(builder, binding.MissingToken);
        builder.Append(",\"object_key\":");
        AppendObjectKey(builder, binding.ObjectKey);
        builder.Append(",\"quote_char\":");
        CanonicalJson.AppendString(builder, binding.QuoteChar.ToString());
        builder.Append(",\"shape\":");
        CanonicalJson.AppendString(builder, Spell(binding.Shape));
        builder.Append('}');
    }

    private static void AppendObjectKey(StringBuilder builder, ObjectKey objectKey)
    {
        switch (objectKey)
        {
            case RowIndexObjectKey:
                builder.Append("{\"mode\":\"row_index\"}");
                break;

            case ColumnObjectKey column:
                builder.Append("{\"column\":");
                CanonicalJson.AppendNumber(builder, column.Index);
                builder.Append(",\"duplicate_object_policy\":");
                CanonicalJson.AppendString(builder, Spell(column.Policy));
                builder.Append(",\"mode\":\"column\"}");
                break;

            default:
                // Composite keys fail planning with Fatal (D-024); programmer error.
                throw new InvalidOperationException($"Object key {objectKey.GetType().Name} has no fingerprint encoding.");
        }
    }

    private static void AppendStringArray(StringBuilder builder, IReadOnlyList<string> values)
    {
        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            CanonicalJson.AppendString(builder, values[i]);
        }

        builder.Append(']');
    }

    // The JSON vocabulary is the spec's TOML vocabulary (D-077) — locked on this
    // side by the canonical goldens + spelling test in Core.Tests; the TOML-side
    // tables are locked by TomlSpellingsTests.

    private static string Spell(LabelStyle style) => style switch
    {
        LabelStyle.Native => "native",
        LabelStyle.V2Compat => "v2-compat",
        _ => throw new InvalidOperationException($"No fingerprint spelling for label style {style}."),
    };

    private static string Spell(LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.Lf => "lf",
        LineEnding.Crlf => "crlf",
        _ => throw new InvalidOperationException($"No fingerprint spelling for line ending {lineEnding}."),
    };

    private static string Spell(SourceShape shape) => shape switch
    {
        SourceShape.Wide => "wide",
        SourceShape.Triple => "triple",
        _ => throw new InvalidOperationException($"No fingerprint spelling for shape {shape}."),
    };

    private static string Spell(MissingPolicy policy) => policy switch
    {
        MissingPolicy.Skip => "skip",
        MissingPolicy.AsAttribute => "as_attribute",
        _ => throw new InvalidOperationException($"No fingerprint spelling for missing policy {policy}."),
    };

    private static string Spell(UnknownValuePolicy policy) => policy switch
    {
        UnknownValuePolicy.Skip => "skip",
        UnknownValuePolicy.Warn => "warn",
        UnknownValuePolicy.Fail => "fail",
        UnknownValuePolicy.Include => "include",
        _ => throw new InvalidOperationException($"No fingerprint spelling for unknown-value policy {policy}."),
    };

    private static string Spell(DuplicateObjectPolicy policy) => policy switch
    {
        DuplicateObjectPolicy.Fail => "fail",
        DuplicateObjectPolicy.Keep => "keep",
        DuplicateObjectPolicy.Dedupe => "dedupe",
        _ => throw new InvalidOperationException($"No fingerprint spelling for duplicate-object policy {policy}."),
    };

    private static string Spell(SourceValueType valueType) => valueType switch
    {
        SourceValueType.String => "string",
        SourceValueType.Number => "number",
        _ => throw new InvalidOperationException($"No fingerprint spelling for value type {valueType}."),
    };

    private static string Spell(EqualWidthRange range) => range switch
    {
        EqualWidthRange.MinMax => "min_max",
        EqualWidthRange.PercentileP1P99 => "percentile_p1_p99",
        EqualWidthRange.Manual => "manual",
        _ => throw new InvalidOperationException($"No fingerprint spelling for equal_width range {range}."),
    };

    private static string Spell(TiePolicy tiePolicy) => tiePolicy switch
    {
        TiePolicy.Left => "left",
        TiePolicy.Right => "right",
        _ => throw new InvalidOperationException($"No fingerprint spelling for equal_frequency tie_policy {tiePolicy}."),
    };

    private static string Spell(CutPlacement cutPlacement) => cutPlacement switch
    {
        CutPlacement.RightValue => "right_value",
        CutPlacement.Midpoint => "midpoint",
        _ => throw new InvalidOperationException($"No fingerprint spelling for equal_frequency cut_placement {cutPlacement}."),
    };

    private static string Spell(BinEnds ends) => ends switch
    {
        BinEnds.Open => "open",
        BinEnds.Closed => "closed",
        _ => throw new InvalidOperationException($"No fingerprint spelling for ends {ends}."),
    };

    private static string Spell(OrdinalDirection direction) => direction switch
    {
        OrdinalDirection.Ge => "ge",
        OrdinalDirection.Le => "le",
        _ => throw new InvalidOperationException($"No fingerprint spelling for direction {direction}."),
    };

    private static string Spell(OrdinalBoundary boundary) => boundary switch
    {
        OrdinalBoundary.Inclusive => "inclusive",
        OrdinalBoundary.Strict => "strict",
        _ => throw new InvalidOperationException($"No fingerprint spelling for boundary {boundary}."),
    };

    private static string Hash(string canonicalJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return HashPrefix + Convert.ToHexStringLower(hash);
    }
}
