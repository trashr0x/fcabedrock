using FcaBedrock.Diagnostics;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Applies §13 composition: walks the <c>extends</c> chain via an
/// <see cref="ISpecTextSource"/> and folds the merge base-most first
/// (§13/D-052), producing a flat document with <c>extends</c> consumed — the
/// input <see cref="SpecResolver.Resolve"/> requires. Composition is a
/// document→document step preceding the seam and belongs to the spec-resolve
/// phase for §16.4 ownership (D-078). The merge operates on authored surface
/// only: <c>null</c> is "not authored", and <c>derived ?? base</c> is the only
/// override operator, so an unauthored derived field never overrides an
/// authored base field.
/// </summary>
public static class SpecComposer
{
    /// <summary>
    /// Composes <paramref name="document"/>, whose canonical key is
    /// <paramref name="documentKey"/>. A document with no authored
    /// <c>extends</c> passes through unchanged (after the version gate). Fatal
    /// on a missing or unsupported <c>[spec].version</c> anywhere in the chain
    /// — the root is gated before any base loads (D-078) — on a base that
    /// cannot be found (<c>SpecExtendsNotFound</c>), on a cycle
    /// (<c>SpecExtendsCycle</c>, including self-extends), and on a base that
    /// fails to parse (its diagnostics aggregate with base-file locations).
    /// </summary>
    public static Diagnosed<SpecDocument> Compose(SpecDocument document, string documentKey, ISpecTextSource source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(documentKey);
        ArgumentNullException.ThrowIfNull(source);

        var diagnostics = new List<BedrockDiagnostic>();

        // Root version gate (D-078): an unversioned/unsupported root must not
        // drive v1 extends semantics, and a missing-base/cycle diagnostic must
        // never fire first — so this precedes any source consultation.
        if (!CheckVersion(document, documentKey, diagnostics))
        {
            return Diagnosed<SpecDocument>.Failed(diagnostics);
        }

        if (document.Spec?.Extends is null)
        {
            return Diagnosed<SpecDocument>.Ok(document, diagnostics);
        }

        // chainKeys is most-derived first, for cycle messages; visited is the
        // ordinal membership set (§13: canonical-key identity).
        var chainKeys = new List<string> { documentKey };
        var visited = new HashSet<string>(StringComparer.Ordinal) { documentKey };
        var chain = new List<SpecDocument> { document };
        var currentKey = documentKey;

        while (chain[^1].Spec?.Extends is { } reference)
        {
            if (source.Load(reference, currentKey) is not { } loaded)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.SpecExtendsNotFound, DiagnosticSeverity.Fatal,
                    $"extends = \"{reference}\" (referenced from '{currentKey}') was not found (§13).",
                    new DiagnosticLocation(File: currentKey)));
                return Diagnosed<SpecDocument>.Failed(diagnostics);
            }

            if (!visited.Add(loaded.CanonicalKey))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.SpecExtendsCycle, DiagnosticSeverity.Fatal,
                    $"extends forms a cycle: {string.Join(" -> ", chainKeys)} -> {loaded.CanonicalKey} (§13).",
                    new DiagnosticLocation(File: currentKey)));
                return Diagnosed<SpecDocument>.Failed(diagnostics);
            }

            var read = SpecReader.Read(loaded.Toml, loaded.CanonicalKey);
            diagnostics.AddRange(read.Diagnostics);
            if (!read.TryGetValue(out var baseDocument))
            {
                return Diagnosed<SpecDocument>.Failed(diagnostics); // cannot merge over a broken base
            }

            if (!CheckVersion(baseDocument, loaded.CanonicalKey, diagnostics))
            {
                return Diagnosed<SpecDocument>.Failed(diagnostics);
            }

            chainKeys.Add(loaded.CanonicalKey);
            chain.Add(baseDocument);
            currentKey = loaded.CanonicalKey;
        }

        // Fold base-most first (§13/D-052): the merge is applied at each chain
        // step, each time layering the next more-derived document on top.
        var composed = chain[^1];
        for (var i = chain.Count - 2; i >= 0; i--)
        {
            composed = MergeStep(composed, chain[i]);
        }

        return Diagnosed<SpecDocument>.Ok(composed, diagnostics);
    }

    /// <summary>
    /// §2/§13: every spec in an extends chain must itself declare
    /// <c>version = 1</c> — a base's content must not enter a composed v1 spec
    /// under unknown semantics. The composed document's own version (the
    /// derived file's) is re-checked only by the resolve seam, so per flow the
    /// condition fires exactly once (D-078).
    /// </summary>
    private static bool CheckVersion(SpecDocument document, string key, List<BedrockDiagnostic> diagnostics)
    {
        switch (document.Spec?.Version)
        {
            case 1:
                return true;

            case { } version:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.SpecVersionUnsupported, DiagnosticSeverity.Fatal,
                    $"Spec '{key}' declares version {version}; this implementation supports version 1 (§2/§13).",
                    new DiagnosticLocation(File: key)));
                return false;

            default:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.SpecVersionUnsupported, DiagnosticSeverity.Fatal,
                    $"Spec '{key}' declares no [spec] version; every spec in an extends chain must declare version = 1 (§2/§13).",
                    new DiagnosticLocation(File: key)));
                return false;
        }
    }

    /// <summary>One §13 merge step: <paramref name="derived"/> layered over <paramref name="baseDocument"/>.</summary>
    private static SpecDocument MergeStep(SpecDocument baseDocument, SpecDocument derived) =>
        new(
            // [spec] is per-spec (§13/D-078): version, description, and stored
            // fingerprints all come from the derived file — base-stored
            // fingerprints are ignored (§13) — and extends is consumed here.
            derived.Spec is { } spec ? spec with { Extends = null } : null,
            derived.Provenance, // never inherited (§13 rule 7)
            MergeBinding(baseDocument.Binding, derived.Binding),
            MergeDefaults(baseDocument.Defaults, derived.Defaults),
            MergeOutput(baseDocument.Output, derived.Output),
            MergeTemplates(baseDocument.Templates, derived.Templates),
            [.. baseDocument.Matchers, .. derived.Matchers], // §13 rule 4: base then derived (last-match-wins precedence at M6)
            MergeAttributes(baseDocument.Attributes, derived.Attributes));

    private static BindingSection? MergeBinding(BindingSection? baseSection, BindingSection? derived)
    {
        if (baseSection is null || derived is null)
        {
            return derived ?? baseSection;
        }

        return new BindingSection(
            derived.Shape ?? baseSection.Shape,
            derived.Encoding ?? baseSection.Encoding,
            derived.Delimiter ?? baseSection.Delimiter,
            derived.QuoteChar ?? baseSection.QuoteChar,
            derived.HasHeader ?? baseSection.HasHeader,
            derived.Locale ?? baseSection.Locale,
            derived.MissingToken ?? baseSection.MissingToken,
            derived.Ordering ?? baseSection.Ordering,
            // The nested tables override as whole values (D-078 refinement of
            // §13 rule 1): a per-leaf merge could compose an incoherent
            // object-key mode hybrid, or a partial triple remap with silently
            // duplicated role indices.
            derived.Columns ?? baseSection.Columns,
            derived.ObjectKey ?? baseSection.ObjectKey);
    }

    private static DefaultsSection? MergeDefaults(DefaultsSection? baseSection, DefaultsSection? derived)
    {
        if (baseSection is null || derived is null)
        {
            return derived ?? baseSection;
        }

        return new DefaultsSection(
            derived.Include ?? baseSection.Include,
            derived.MissingPolicy ?? baseSection.MissingPolicy,
            derived.UnknownValuePolicy ?? baseSection.UnknownValuePolicy,
            derived.DuplicateObjectPolicy ?? baseSection.DuplicateObjectPolicy,
            derived.OrdinalDirection ?? baseSection.OrdinalDirection,
            derived.OrdinalBoundary ?? baseSection.OrdinalBoundary);
    }

    private static OutputSection? MergeOutput(OutputSection? baseSection, OutputSection? derived)
    {
        if (baseSection is null || derived is null)
        {
            return derived ?? baseSection;
        }

        // §13 rule 6: [output] merges per leaf field — a base [output.cxt]
        // line-ending and a derived [output.cxt] trailing-newline both survive.
        return new OutputSection(
            derived.BinLabelUnicode ?? baseSection.BinLabelUnicode,
            MergeCxt(baseSection.Cxt, derived.Cxt),
            MergeDat(baseSection.Dat, derived.Dat));
    }

    private static CxtOutputSection? MergeCxt(CxtOutputSection? baseSection, CxtOutputSection? derived)
    {
        if (baseSection is null || derived is null)
        {
            return derived ?? baseSection;
        }

        return new CxtOutputSection(
            derived.LineEndings ?? baseSection.LineEndings,
            derived.TrailingNewline ?? baseSection.TrailingNewline,
            derived.SizeAdvisoryBytes ?? baseSection.SizeAdvisoryBytes);
    }

    private static DatOutputSection? MergeDat(DatOutputSection? baseSection, DatOutputSection? derived)
    {
        if (baseSection is null || derived is null)
        {
            return derived ?? baseSection;
        }

        return new DatOutputSection(
            derived.LineEndings ?? baseSection.LineEndings,
            derived.BaseIndex ?? baseSection.BaseIndex,
            derived.NonemptyLineTrailingSpace ?? baseSection.NonemptyLineTrailingSpace,
            derived.EmptyLineTrailingSpace ?? baseSection.EmptyLineTrailingSpace);
    }

    /// <summary>
    /// §13 rule 3, as carrier composition only (template application/resolution
    /// precedence is M6, D-078): a derived template whose <c>id</c> matches a
    /// base entry replaces it in place (base position kept); new, id-less, and
    /// duplicate entries append after all inherited templates.
    /// </summary>
    private static IReadOnlyList<TemplateSection> MergeTemplates(
        IReadOnlyList<TemplateSection> baseTemplates,
        IReadOnlyList<TemplateSection> derived)
    {
        var result = new List<TemplateSection>(baseTemplates);
        var baseRegion = baseTemplates.Count;
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in derived)
        {
            // Only the base region is searched, and each id replaces at most
            // once — authoring duplicates are preserved into the composed
            // document rather than silently collapsed (D-078).
            var index = template.Id is { } id && !replaced.Contains(id)
                ? IndexOfTemplate(result, baseRegion, id)
                : -1;
            if (index >= 0)
            {
                result[index] = template;
                replaced.Add(template.Id!);
            }
            else
            {
                result.Add(template);
            }
        }

        return result;
    }

    /// <summary>
    /// §13 rule 5 / D-052: position-preserving whole-attribute override by
    /// <c>name</c> — a derived same-name attribute replaces the base's in place
    /// (the derived section verbatim; inherited fields are dropped unless
    /// repeated); new names append after all inherited attributes, in derived
    /// order. Unnamed attributes never match; duplicates are preserved for
    /// <c>AttributeNameDuplicate</c> to reject at the resolve seam (D-080),
    /// exactly as in a flat file (D-078).
    /// </summary>
    private static IReadOnlyList<AttributeSection> MergeAttributes(
        IReadOnlyList<AttributeSection> baseAttributes,
        IReadOnlyList<AttributeSection> derived)
    {
        var result = new List<AttributeSection>(baseAttributes);
        var baseRegion = baseAttributes.Count;
        var overridden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in derived)
        {
            var index = attribute.Name is { } name && !overridden.Contains(name)
                ? IndexOfAttribute(result, baseRegion, name)
                : -1;
            if (index >= 0)
            {
                result[index] = attribute;
                overridden.Add(attribute.Name!);
            }
            else
            {
                result.Add(attribute);
            }
        }

        return result;
    }

    private static int IndexOfTemplate(List<TemplateSection> templates, int baseRegion, string id)
    {
        for (var i = 0; i < baseRegion; i++)
        {
            if (string.Equals(templates[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfAttribute(List<AttributeSection> attributes, int baseRegion, string name)
    {
        for (var i = 0; i < baseRegion; i++)
        {
            if (string.Equals(attributes[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
