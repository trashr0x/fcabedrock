using FcaBedrock.Diagnostics;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Parses authored Bedrock-spec TOML into a presence-tracked
/// <see cref="SpecDocument"/> (D-066/D-075). Strict over the v1 vocabulary:
/// unknown keys and tables fail the read (<c>SpecKeyUnrecognized</c>) so a
/// write never silently drops authored content; the one recognized-but-unmodelled
/// v1 surface left, <c>value_type = "date"</c>, fails with the transitional
/// <c>SpecSurfaceNotYetSupported</c>; wrong shapes fail with
/// <c>SpecFieldInvalid</c>. The reader enforces parse shape
/// only — possibly-invalid <em>values</em> land in the document for the
/// resolve/validate seam to judge (D-066/D-067). Diagnostics aggregate in two
/// phases (P-14): all TOML-level errors together (<c>SpecTomlInvalid</c>,
/// terminal — a broken tree would cascade garbage), then all semantic issues
/// from one whole-document walk, ordered by source position before
/// <see cref="Read"/> returns (<see cref="SortSemantic"/>, D-116/D-120).
/// </summary>
public static class SpecReader
{
    private static readonly BindingSection EmptyBinding = new(
        Shape: null, Encoding: null, Delimiter: null, QuoteChar: null, HasHeader: null,
        Locale: null, MissingToken: null, Ordering: null, Columns: null, ObjectKey: null);

    /// <summary>
    /// Reads <paramref name="toml"/>; <paramref name="filePath"/> is only a
    /// label for diagnostic locations (no I/O happens here).
    /// </summary>
    public static Diagnosed<SpecDocument> Read(string toml, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        var context = new TomlReadContext(filePath);
        var syntax = SyntaxParser.Parse(toml, filePath, validate: true);

        var hasSyntaxErrors = false;
        foreach (var message in syntax.Diagnostics)
        {
            if (message.Kind == DiagnosticMessageKind.Error)
            {
                hasSyntaxErrors = true;
                context.Fatal(DiagnosticCode.SpecTomlInvalid, $"Not valid TOML 1.0: {message.Message}", message.Span);
            }
            else
            {
                context.Warning(DiagnosticCode.SpecTomlInvalid, $"TOML parser warning: {message.Message}", message.Span);
            }
        }

        if (hasSyntaxErrors)
        {
            return Diagnosed<SpecDocument>.Failed(context.Diagnostics);
        }

        // Everything the semantic walk adds from here sorts by source position before
        // Read returns (SortSemantic); phase-1 parser warnings keep their place ahead
        // of it, so the two phases never interleave.
        var semanticFrom = context.Diagnostics.Count;

        SpecSection? spec = null;
        ProvenanceSection? provenance = null;
        BindingSection? binding = null;
        ObjectKeySection? objectKey = null;
        DefaultsSection? defaults = null;
        var outputSeen = false;
        bool? binLabelUnicode = null;
        CxtOutputSection? cxt = null;
        DatOutputSection? dat = null;
        var templates = new List<TemplateSection>();
        var matchers = new List<MatcherSection>();
        var attributes = new List<AttributeSection>();

        foreach (var pair in syntax.KeyValues)
        {
            // No root-level keys are v1 surface (§2) — everything lives in sections.
            context.Error(
                DiagnosticCode.SpecKeyUnrecognized,
                "Root-level keys are not part of a Bedrock spec; keys live in sections (§2).",
                pair.Key?.Span ?? pair.Span);
        }

        foreach (var table in syntax.Tables)
        {
            if (table.Name is not { } nameKey)
            {
                continue; // malformed header; Tomlyn reported
            }

            var name = string.Join('.', TomlSyntaxHelpers.KeyParts(nameKey));
            var isArray = table is TableArraySyntax;
            switch (name, isArray)
            {
                case ("spec", false):
                    spec = SpecSectionReaders.ReadSpec(context, table);
                    break;

                case ("provenance", false):
                    provenance = SpecSectionReaders.ReadProvenance(context, table);
                    break;

                case ("binding", false):
                    binding = SpecSectionReaders.ReadBinding(context, table);
                    break;

                case ("binding.object_key", false):
                    objectKey = SpecSectionReaders.ReadObjectKey(context, table);
                    break;

                case ("defaults", false):
                    defaults = SpecSectionReaders.ReadDefaults(context, table);
                    break;

                case ("output", false):
                    outputSeen = true;
                    binLabelUnicode = SpecSectionReaders.ReadOutput(context, table);
                    break;

                case ("output.cxt", false):
                    cxt = SpecSectionReaders.ReadOutputCxt(context, table);
                    break;

                case ("output.dat", false):
                    dat = SpecSectionReaders.ReadOutputDat(context, table);
                    break;

                case ("template", true):
                    templates.Add(AttributeReader.ReadTemplate(context, table));
                    break;

                case ("matcher", true):
                    matchers.Add(SpecSectionReaders.ReadMatcher(context, table));
                    break;

                case ("attribute", true):
                    attributes.Add(AttributeReader.Read(context, table));
                    break;

                case ("attribute", false):
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        "Attributes are written as [[attribute]] — an array of tables (§10).",
                        nameKey.Span);
                    break;

                case ("template" or "matcher", false):
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        $"[[{name}]] is an array of tables (§9).",
                        nameKey.Span);
                    break;

                case ("spec" or "provenance" or "binding" or "binding.object_key" or "defaults"
                    or "output" or "output.cxt" or "output.dat", true):
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        $"[{name}] is a single table, not an array of tables (§2).",
                        nameKey.Span);
                    break;

                default:
                    context.Error(
                        DiagnosticCode.SpecKeyUnrecognized,
                        $"Table '{name}' is not recognized.",
                        nameKey.Span);
                    break;
            }
        }

        if (objectKey is not null)
        {
            // [binding.object_key] may be authored without a [binding] header;
            // presence of either yields a binding section (mirrored by the writer).
            binding = (binding ?? EmptyBinding) with { ObjectKey = objectKey };
        }

        var output = outputSeen || cxt is not null || dat is not null
            ? new OutputSection(binLabelUnicode, cxt, dat)
            : null;

        var document = new SpecDocument(spec, provenance, binding, defaults, output, templates, matchers, attributes);
        SortSemantic(context.Diagnostics, semanticFrom);
        return Finish(document, context.Diagnostics);
    }

    /// <summary>
    /// The one source-position ordering policy for phase-2 diagnostics (D-116), applied
    /// once at the boundary rather than scattered among readers: individual readers emit
    /// in whatever order traversal produces, and the collected result is ordered here by
    /// <c>(Line, Column, emission ordinal)</c>. Future readers inherit it automatically.
    /// <para>
    /// The emission ordinal is part of the comparison, not merely a tie-break convention,
    /// which makes the order <b>total</b> — so equal-position diagnostics keep their
    /// relative order regardless of the underlying sort's stability, and two reads of one
    /// document produce identical ordered lists (P-7). Diagnostics with no span (document
    /// level) compare as line 0, column 0 and therefore come first, in emission order.
    /// </para>
    /// <para>
    /// Scoped to <paramref name="from"/> onward so the phase separation is untouched:
    /// TOML syntax errors are terminal and never reach here, and phase-1 parser warnings
    /// stay ahead of every semantic diagnostic.
    /// </para>
    /// <para>
    /// <b>Internal rather than private as a deliberate test seam</b> (P-6): the
    /// span-less branch is defensive — <see cref="TomlReadContext"/> always attaches a
    /// span, so no authored document can reach it through <see cref="Read"/> — and the
    /// equal-position tie-break is invisible from the outside when the sort happens to be
    /// stable anyway. Both are load-bearing ordering guarantees, so they are exercised
    /// directly with constructed diagnostics rather than left to a test that cannot fail.
    /// Production behaviour is unchanged: <see cref="Read"/> remains the only caller.
    /// </para>
    /// </summary>
    internal static void SortSemantic(List<BedrockDiagnostic> diagnostics, int from)
    {
        var count = diagnostics.Count - from;
        if (count < 2)
        {
            return;
        }

        var keyed = new (int Line, int Column, int Ordinal, BedrockDiagnostic Diagnostic)[count];
        for (var i = 0; i < count; i++)
        {
            var diagnostic = diagnostics[from + i];
            keyed[i] = (diagnostic.Location?.Line ?? 0, diagnostic.Location?.Column ?? 0, i, diagnostic);
        }

        Array.Sort(keyed, static (left, right) =>
        {
            var byLine = left.Line.CompareTo(right.Line);
            if (byLine != 0)
            {
                return byLine;
            }

            var byColumn = left.Column.CompareTo(right.Column);
            return byColumn != 0 ? byColumn : left.Ordinal.CompareTo(right.Ordinal);
        });

        for (var i = 0; i < count; i++)
        {
            diagnostics[from + i] = keyed[i].Diagnostic;
        }
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
}
