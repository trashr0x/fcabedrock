using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec;

/// <summary>
/// Maps a parsed <see cref="BedDocument"/> plus a caller-supplied
/// <see cref="BindingSection"/> to a presence-tracked <see cref="SpecDocument"/>
/// (D-009 "load v2, save as TOML"; D-079). The v2 type codes become
/// (discretizer, scale) section pairs (D-002); a wide binding binds each attribute
/// to its positional column, a triple binding binds it to the predicate named for
/// it (§19.3/D-086) — matching v2's tabular and 3-column loads. The migrator
/// carries what the document model can represent and
/// defers semantic validation to the resolve seam (D-067) — only transcription
/// failures diagnose here. <c>include = false</c> attributes park their full
/// config (D-049); unrecoverable parked config degrades to a bare excluded
/// attribute with a <c>BedParkedConfigDropped</c> Warning, never silently.
/// The v2 <c>[Restrict To Values]</c> lines become <c>restrict_to</c> string
/// entries, carried include-independently (§10.1/D-057); a
/// <c>[Category Values]</c> entry equal to the effective
/// <c>binding.missing_token</c> becomes <c>missing_policy = "as_attribute"</c>
/// (D-068). The discrete-vs-progressive choice for <c>o</c>/<c>n</c> is supplied
/// out-of-band via <see cref="ScalingMode"/> — the <c>.bed</c> never recorded it.
/// Date type <c>d</c> is a parity deferral (D-038).
/// </summary>
public static class BedMigrator
{
    /// <summary>
    /// Builds a spec document from <paramref name="document"/> under
    /// <paramref name="binding"/>, with <paramref name="mode"/> selecting nominal
    /// (discrete) or ordinal (progressive) scaling for the cut types;
    /// <paramref name="derivedFrom"/>, when given, is recorded as
    /// <c>[provenance] derived_from</c>.
    /// </summary>
    public static Diagnosed<SpecDocument> Migrate(
        BedDocument document,
        BindingSection binding,
        ScalingMode mode = ScalingMode.Discrete,
        string? derivedFrom = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(binding);

        // The effective missing token (D-068): the resolver's default "?" unless
        // authored; an authored empty string disables token-based missing detection
        // entirely (§5.1), so no category entry can match it.
        var missingToken = binding.MissingToken is { Length: 0 } ? null : binding.MissingToken ?? "?";

        var diagnostics = new List<BedrockDiagnostic>();
        var attributes = new List<AttributeSection>(document.AttributeCount);
        for (var i = 0; i < document.AttributeCount; i++)
        {
            MigrateAttribute(document, i, binding.Shape, missingToken, mode, diagnostics, attributes);
        }

        var spec = new SpecSection(
            Version: 1, SchemaFingerprint: null, CxtOutputFingerprint: null,
            DatOutputFingerprint: null, Extends: null, Description: null);
        var provenance = derivedFrom is null
            ? null
            : new ProvenanceSection(
                Author: null, CreatedAt: null, SourceUrl: null, SourceHash: null,
                DerivedFrom: derivedFrom, Notes: null);

        var migrated = new SpecDocument(
            spec, provenance, binding, Defaults: null, Output: null,
            Templates: [], Matchers: [], attributes);
        return Finish(migrated, diagnostics);
    }

    private static void MigrateAttribute(
        BedDocument document,
        int index,
        SourceShape? shape,
        string? missingToken,
        ScalingMode mode,
        List<BedrockDiagnostic> diagnostics,
        List<AttributeSection> attributes)
    {
        var include = document.Convert[index];
        var mapped = MapConfig(document, index, shape, missingToken, mode);

        if (mapped.Section is { } section)
        {
            // Carried config may still drop pieces (a missing-token display label);
            // those Warnings report on included and excluded attributes alike.
            diagnostics.AddRange(mapped.Diagnostics);
            attributes.Add(include ? section : section with { Include = false });
            return;
        }

        if (include)
        {
            diagnostics.AddRange(mapped.Diagnostics);
            return;
        }

        // include = false is an authoring toggle (D-049): dormant config never blocks
        // migration, so an untranscribable parked config degrades to a bare excluded
        // attribute — reported, never silent. restrict_to survives the degrade: it is
        // live, include-independent config (§10.1/D-076), not parked emitted-shaping.
        var reasons = string.Join(" ", mapped.Diagnostics.Select(d => d.Message));
        diagnostics.Add(Warn(
            DiagnosticCode.BedParkedConfigDropped,
            $"Excluded attribute '{document.Names[index]}': v2 config was not migrated ({reasons}) — parked bare (include = false).",
            document.Names[index]));
        attributes.Add(Bare(document, index, shape) with { Include = false });
    }

    // Maps the v2 config to a full attribute section, include-agnostically; a null
    // Section means the config cannot be transcribed and Diagnostics holds the
    // Error(s). Diagnostics alongside a non-null Section are Warnings.
    private static MappedAttribute MapConfig(
        BedDocument document, int index, SourceShape? shape, string? missingToken, ScalingMode mode)
    {
        var name = document.Names[index];
        var type = document.Types[index];
        var values = document.Values[index];
        var categories = document.Categories[index];

        return type switch
        {
            "c" => MapCategorical(document, index, shape, missingToken),
            "b" => MapDichotomic(document, index, shape, missingToken),
            "o" => MapNumericCuts(document, index, shape, mode),
            "n" => new MappedAttribute(
                Bare(document, index, shape) with
                {
                    Discretizer = new OrderedCutsDiscretizerSection(
                        categories, CutTokens(values, out var ends), ends),
                    Scale = ScaleFor(mode),
                },
                []),
            "d" => MappedAttribute.Error(Diagnostic(
                DiagnosticCode.BedDateTypeNotSupported,
                $"Attribute '{name}' uses the v2 date type 'd'; date scaling is deferred (D-038) and has no v1 carrier.",
                name)),
            _ => MappedAttribute.Error(Diagnostic(
                DiagnosticCode.BedTypeUnrecognized,
                $"Attribute '{name}' has unrecognized v2 type code '{type}' (expected c, b, o, n, or d).",
                name)),
        };
    }

    private static MappedAttribute MapCategorical(BedDocument document, int index, SourceShape? shape, string? missingToken)
    {
        var (domain, labels, missing, warnings) = SplitMissingToken(document, index, missingToken);
        return new MappedAttribute(
            Bare(document, index, shape) with
            {
                Discretizer = new IdentityDiscretizerSection(),
                Scale = new NominalScaleSection(),
                DeclaredDomain = domain,
                ValueLabels = labels,
                MissingPolicy = missing ? MissingPolicy.AsAttribute : null,
            },
            warnings);
    }

    private static MappedAttribute MapDichotomic(BedDocument document, int index, SourceShape? shape, string? missingToken)
    {
        var name = document.Names[index];
        var trueValue = document.Values[index][0];
        if (missingToken is not null && string.Equals(trueValue, missingToken, StringComparison.Ordinal))
        {
            // The token is excluded from the domain by D-068, so a dichotomic true
            // value equal to it is contradictory config no seam check would catch.
            return MappedAttribute.Error(Diagnostic(
                DiagnosticCode.BedAttributeConfigInvalid,
                $"Attribute '{name}': the dichotomic true value '{trueValue}' equals the effective missing token, which cannot be a domain value (D-068).",
                name));
        }

        var (domain, labels, missing, warnings) = SplitMissingToken(document, index, missingToken);
        return new MappedAttribute(
            Bare(document, index, shape) with
            {
                Discretizer = new IdentityDiscretizerSection(),
                Scale = new DichotomicScaleSection(trueValue),
                DeclaredDomain = domain,
                ValueLabels = labels,
                MissingPolicy = missing ? MissingPolicy.AsAttribute : null,
            },
            warnings);
    }

    private static MappedAttribute MapNumericCuts(BedDocument document, int index, SourceShape? shape, ScalingMode mode)
    {
        var name = document.Names[index];
        var tokens = CutTokens(document.Values[index], out var ends);
        var cuts = new List<double>(tokens.Count);
        foreach (var token in tokens)
        {
            // Cut values in the .bed are invariant schema strings (§14); only the
            // data column is read with the binding locale. The document carrier
            // stores numbers, so an unparseable token is a transcription failure.
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var cut))
            {
                cuts.Add(cut);
            }
            else
            {
                return MappedAttribute.Error(Diagnostic(
                    DiagnosticCode.BedAttributeConfigInvalid,
                    $"Attribute '{name}': numeric cut token '{token}' is not an invariant number.",
                    name));
            }
        }

        return new MappedAttribute(
            Bare(document, index, shape) with
            {
                Discretizer = new ManualCutsDiscretizerSection(cuts, ends),
                Scale = ScaleFor(mode),
            },
            []);
    }

    // The D-068 structural rule for the domain-list types (c, b): a [Category
    // Values] entry equal to the effective missing token selects
    // missing_policy = "as_attribute" and leaves declared_domain/value_labels; a
    // display label on that entry has no v1 carrier ({column}-missing is canonical,
    // §10.5/D-074) and drops with a Warning. Display labels (§10.8) keep only
    // non-identity mappings, keyed by raw value — v2 order, last spelling wins.
    private static (IReadOnlyList<string> Domain, IReadOnlyDictionary<string, string>? Labels, bool Missing, List<BedrockDiagnostic> Warnings)
        SplitMissingToken(BedDocument document, int index, string? missingToken)
    {
        var name = document.Names[index];
        var values = document.Values[index];
        var categories = document.Categories[index];

        var domain = new List<string>(values.Count);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = false;
        var warnings = new List<BedrockDiagnostic>();
        for (var j = 0; j < values.Count; j++)
        {
            var label = j < categories.Count && !string.Equals(values[j], categories[j], StringComparison.Ordinal)
                ? categories[j]
                : null;
            if (missingToken is not null && string.Equals(values[j], missingToken, StringComparison.Ordinal))
            {
                missing = true;
                if (label is not null)
                {
                    warnings.Add(Warn(
                        DiagnosticCode.BedMissingTokenLabelDropped,
                        $"Attribute '{name}': the missing-token category '{values[j]}' carried display label '{label}', which has no v1 carrier (the missing column is '{name}-missing', §10.5) — label dropped.",
                        name));
                }

                continue;
            }

            domain.Add(values[j]);
            if (label is not null)
            {
                labels[values[j]] = label;
            }
        }

        return (domain, labels.Count > 0 ? labels : null, missing, warnings);
    }

    // Name + shape-appropriate source + the include-independent restrict_to
    // (§10.1/D-057); everything else unauthored. The full maps build on this via
    // `with`. A wide (or shape-absent) binding binds by positional column; a triple
    // binding binds by predicate name — the v2 attribute name (§19.3/D-086).
    private static AttributeSection Bare(BedDocument document, int index, SourceShape? shape) =>
        new(
            Name: document.Names[index],
            Source: shape == SourceShape.Triple
                ? new PredicateSourceSection(Name: document.Names[index], ValueType: null)
                : new ColumnSourceSection(Index: index, Name: null, ValueType: null),
            Description: null,
            Include: null,
            Template: null,
            Discretizer: null,
            Scale: null,
            DeclaredDomain: null,
            RestrictTo: RestrictEntries(document.RestrictTo[index]),
            ValueLabels: null,
            MissingPolicy: null,
            UnknownValuePolicy: null);

    // The v2 restrict line: raw values, comma-separated, OR'd within the attribute
    // (lineage.md). Tokens carry verbatim (no trim — restrict matches raw values);
    // a blank line means no filter, so restrict_to stays unauthored. Numeric
    // attributes keep string entries too: v2 restricted by raw-value equality, and
    // the seam's RestrictToOnNumericRequiresRange owns the shape mismatch (D-063).
    private static IReadOnlyList<RestrictToEntry>? RestrictEntries(string line) =>
        line.Trim().Length == 0
            ? null
            : line.Split(',').Select(RestrictToEntry (token) => new RestrictToValue(token)).ToList();

    private static ScaleSection ScaleFor(ScalingMode mode) =>
        mode == ScalingMode.Progressive
            ? new OrdinalScaleSection(
                // direction is the migration's choice, so it is authored; boundary/
                // order/drop_top stay unauthored — over cut bins an authored boundary
                // or order is a D-060 validation error, and the resolver's defaults
                // already reproduce v2's le-threshold rendering.
                Direction: OrdinalDirection.Le, Boundary: null, Order: null, DropTop: null)
            : new NominalScaleSection();

    // The v2 cut spec is the [Category Values] tokens with sentinel ends: a leading
    // "<" and/or trailing ">" mark open ends; the interior tokens are the cuts.
    // ends is always authored: the resolver defaults an absent ends to open, but a
    // sentinel-less v2 cut spec means closed — omission would silently flip it.
    private static IReadOnlyList<string> CutTokens(IReadOnlyList<string> tokens, out BinEnds ends)
    {
        var open = tokens.Count > 0 && (tokens[0] == "<" || tokens[^1] == ">");
        ends = open ? BinEnds.Open : BinEnds.Closed;
        return tokens.Where(t => t is not ("<" or ">")).ToList();
    }

    private static Diagnosed<SpecDocument> Finish(SpecDocument document, List<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            {
                return Diagnosed<SpecDocument>.Failed(diagnostics);
            }
        }

        return Diagnosed<SpecDocument>.Ok(document, diagnostics);
    }

    private static BedrockDiagnostic Diagnostic(DiagnosticCode code, string message, string attributeName) =>
        new(code, DiagnosticSeverity.Error, message,
            new DiagnosticLocation(File: null, Line: null, Column: null, AttributeName: attributeName, RecordIndex: null));

    private static BedrockDiagnostic Warn(DiagnosticCode code, string message, string attributeName) =>
        new(code, DiagnosticSeverity.Warning, message,
            new DiagnosticLocation(File: null, Line: null, Column: null, AttributeName: attributeName, RecordIndex: null));

    private sealed record MappedAttribute(AttributeSection? Section, List<BedrockDiagnostic> Diagnostics)
    {
        public static MappedAttribute Error(BedrockDiagnostic diagnostic) => new(null, [diagnostic]);
    }
}
