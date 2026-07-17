namespace FcaBedrock.Core.Discretization;

/// <summary>
/// What a <c>value_groups</c> discretizer does with a value matching no declared
/// group (spec §11.6, D-055/D-090). The three policies differ in whether the
/// unmatched value produces a bin at all, and — when it does — whether that bin is
/// fixed by the spec or discovered from data.
/// </summary>
public enum ValueGroupsUnmatched
{
    /// <summary>
    /// No bin; the value defers to <c>unknown_value_policy</c> (§10.6). Because
    /// <c>value_groups</c> ignores <c>declared_domain</c> (D-055) an unmatched value is
    /// not a domain gap, so <c>include</c> has nothing to extend and behaves as
    /// <c>warn</c> (§11.6/D-090).
    /// </summary>
    Skip,

    /// <summary>One synthetic <c>Other</c> bin, ordered after all declared groups (§17 rule 3).</summary>
    Other,

    /// <summary>
    /// The value keeps its raw spelling as its own bin label. The bin set is
    /// <b>discovered from the data</b>, so this is the one data-dependent policy: it
    /// resolves in the Calibrate phase (§7) and emits
    /// <c>ValueGroupsPassthroughDataDependent</c>.
    /// </summary>
    Passthrough,
}
