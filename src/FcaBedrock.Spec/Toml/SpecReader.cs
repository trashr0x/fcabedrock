using FcaBedrock.Diagnostics;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Parses authored Bedrock-spec TOML into a presence-tracked
/// <see cref="SpecDocument"/> (D-066/D-075). Strict over the v1 vocabulary:
/// unknown keys and tables fail the read (<c>SpecKeyUnrecognized</c>) so a
/// write never silently drops authored content; recognized-but-unmodelled v1
/// surface fails with the transitional <c>SpecSurfaceNotYetSupported</c>; wrong
/// shapes fail with <c>SpecFieldInvalid</c>. The reader enforces parse shape
/// only — possibly-invalid <em>values</em> land in the document for the
/// resolve/validate seam to judge (D-066/D-067). Diagnostics aggregate in two
/// phases (P-13): all TOML-level errors together (<c>SpecTomlInvalid</c>,
/// terminal — a broken tree would cascade garbage), then all semantic issues
/// from one whole-document walk.
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

        SpecSection? spec = null;
        ProvenanceSection? provenance = null;
        BindingSection? binding = null;
        ObjectKeySection? objectKey = null;
        DefaultsSection? defaults = null;
        var outputSeen = false;
        bool? binLabelUnicode = null;
        CxtOutputSection? cxt = null;
        DatOutputSection? dat = null;
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

                case ("attribute", true):
                    attributes.Add(AttributeReader.Read(context, table));
                    break;

                case ("attribute", false):
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        "Attributes are written as [[attribute]] — an array of tables (§10).",
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
                    if (TomlSpellings.IsIn(TomlSpellings.DeferredTables, name))
                    {
                        // D-075 closed set: [[template]]/[[matcher]] land with the
                        // extends slice (Slice F); read fails so nothing is dropped.
                        context.Error(
                            DiagnosticCode.SpecSurfaceNotYetSupported,
                            $"[[{name}]] is recognized v1 surface not yet supported by this build (D-075).",
                            nameKey.Span);
                    }
                    else
                    {
                        context.Error(
                            DiagnosticCode.SpecKeyUnrecognized,
                            $"Table '{name}' is not recognized.",
                            nameKey.Span);
                    }

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

        var document = new SpecDocument(spec, provenance, binding, defaults, output, attributes);
        return Finish(document, context.Diagnostics);
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
