using FcaBedrock.Core.Spec;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// The preparation ↔ source pairing guard (D-098/G-1): before reading any row, a
/// calibrate/emit pass validates that the source was prepared against the same
/// resolution. Three states (<see cref="SourceProvenance"/>):
/// <list type="bullet">
/// <item>token → reference-identity with the resolution (full pairing — settings,
/// roles, missing token, and schema are all inside the token, so no schema read);</item>
/// <item>descriptor → settings value-equality, role-map equality, and ordinal
/// schema-value equality against the resolution (closes same-header/different-setting
/// and same-header/different-role mispairing for direct-constructed sources);</item>
/// <item>unvalidated → schema-value equality only (the named opt-out).</item>
/// </list>
/// Any mismatch — including an unknown provenance variant — throws
/// <see cref="InvalidOperationException"/> (the D-082 call-contract posture); it never
/// degrades to the unvalidated path.
/// </summary>
internal static class SourcePairing
{
    public static ValueTask ValidateAsync(ResolvedSpec resolution, IRecordSource source, CancellationToken cancellationToken) =>
        ValidateCoreAsync(resolution, source.Provenance, () => source.GetSchemaAsync(cancellationToken));

    public static ValueTask ValidateAsync(ResolvedSpec resolution, ITripleRowSource source, CancellationToken cancellationToken) =>
        ValidateCoreAsync(resolution, source.Provenance, () => source.GetSchemaAsync(cancellationToken));

    private static async ValueTask ValidateCoreAsync(
        ResolvedSpec resolution, SourceProvenance provenance, Func<ValueTask<SourceSchema>> readSchema)
    {
        switch (provenance)
        {
            case TokenProvenance token:
                if (!ReferenceEquals(token.Resolution, resolution))
                {
                    throw new InvalidOperationException(
                        "The source was bound against a different resolution than this preparation (D-098).");
                }

                return;

            case DescriptorProvenance descriptor:
                if (!descriptor.Settings.Equals(resolution.Settings))
                {
                    throw new InvalidOperationException(
                        "The source's read settings do not match this preparation's (D-098).");
                }

                if (descriptor.Roles != resolution.Spec.Binding.TripleColumns)
                {
                    throw new InvalidOperationException(
                        "The source's triple role map does not match this preparation's (D-098).");
                }

                await RequireSchemaMatchAsync(resolution, readSchema).ConfigureAwait(false);
                return;

            case var unvalidated when ReferenceEquals(unvalidated, SourceProvenance.Unvalidated):
                await RequireSchemaMatchAsync(resolution, readSchema).ConfigureAwait(false);
                return;

            default:
                throw new InvalidOperationException(
                    $"Unknown source provenance variant '{provenance.GetType().Name}' (D-098).");
        }
    }

    private static async ValueTask RequireSchemaMatchAsync(ResolvedSpec resolution, Func<ValueTask<SourceSchema>> readSchema)
    {
        var live = await readSchema().ConfigureAwait(false);
        if (resolution.Schema is not { } expected || !SchemasEqual(live, expected))
        {
            throw new InvalidOperationException(
                "The live source schema does not match this preparation's schema (D-098).");
        }
    }

    // Ordinal schema-value equality (P-12): same column count and the same header.
    private static bool SchemasEqual(SourceSchema a, SourceSchema b)
    {
        if (a.ColumnCount != b.ColumnCount)
        {
            return false;
        }

        if (a.Header is null || b.Header is null)
        {
            return a.Header is null && b.Header is null;
        }

        if (a.Header.Count != b.Header.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Header.Count; i++)
        {
            if (!string.Equals(a.Header[i], b.Header[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
