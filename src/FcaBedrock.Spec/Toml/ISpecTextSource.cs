namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Supplies base-spec TOML text for §13 composition. The source owns
/// reference→canonical-key resolution (relative paths, case rules), so path
/// and OS determinism hazards never enter Spec logic (P-11);
/// <see cref="SpecComposer"/> compares canonical keys ordinally. This package
/// does no file I/O (D-075) — the file-backed implementation is M7 host work,
/// not missing Slice F work (D-078); tests compose through an in-memory source.
/// </summary>
public interface ISpecTextSource
{
    /// <summary>
    /// Resolves <paramref name="reference"/> (the authored <c>extends</c> value)
    /// against <paramref name="referrerKey"/> (the canonical key of the spec that
    /// authored it); null when no such spec exists.
    /// </summary>
    SpecSourceText? Load(string reference, string referrerKey);
}

/// <summary>
/// One loaded base spec: its canonical identity (used for cycle detection and
/// diagnostic locations) and its TOML text.
/// </summary>
/// <param name="CanonicalKey">Stable identity of the loaded spec; two references to the same spec must yield equal (ordinal) keys.</param>
/// <param name="Toml">The spec's TOML text.</param>
public sealed record SpecSourceText(string CanonicalKey, string Toml);
