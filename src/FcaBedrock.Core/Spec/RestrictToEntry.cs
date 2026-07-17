namespace FcaBedrock.Core.Spec;

/// <summary>
/// One <c>restrict_to</c> entry: an object-level filter on raw values, applied
/// before discretization (spec §10.4). Executable from M4 Slice F (D-091): the
/// planner carries one <see cref="Planning.PlannedRestriction"/> per attribute
/// with entries — included or filter-only — and emit filters whole objects
/// existentially (OR within an attribute, AND across attributes).
/// <para>
/// The union has exactly three recognized variants — <see cref="RestrictToValue"/>,
/// <see cref="RestrictToNumber"/>, and <see cref="RestrictToRange"/>. It is
/// deliberately <em>not</em> mechanically closed: the Spec document model reuses it
/// as its authored carrier (D-057), so the authored and resolved shapes coincide.
/// Core's trust boundary (<see cref="ResolvedSpec.Create"/>) therefore rejects an
/// unrecognized variant explicitly rather than relying on the type system here.
/// </para>
/// </summary>
public abstract record RestrictToEntry;

/// <summary>Keeps objects whose raw value equals <paramref name="Value"/> (categorical form, §10.4).</summary>
/// <param name="Value">The raw value to match, compared ordinally (P-12) — never trimmed or case-folded.</param>
public sealed record RestrictToValue(string Value) : RestrictToEntry;

/// <summary>
/// Keeps objects whose numeric raw value equals <paramref name="Value"/> by
/// <b>parsed numeric identity</b> (§10.4/D-091): <c>30</c>, <c>30.0</c>, and
/// <c>3e1</c> are one entry, and both signed zeros collapse to positive zero. It is
/// neither string-spelling equality nor a single-point range, and matching carries
/// no tolerance or epsilon — a raw observation matches exactly when its parsed,
/// zero-canonicalized value is <c>==</c> this one.
/// <para>
/// Deliberately a positional record able to hold a non-finite value, exactly as
/// <see cref="RestrictToRange"/>'s bounds can: the document model reuses this union
/// (D-057), so the carrier must represent an authored <c>{ value = nan }</c> long
/// enough for the <b>resolve seam</b> to diagnose it as
/// <c>RestrictToRangeInvalid</c> on the user-facing channel (P-14) — a throwing
/// factory would turn an authoring error into a parse-time exception. The boundary
/// is layered instead: the seam validates and zero-canonicalizes what it resolves,
/// and <see cref="ResolvedSpec.Create"/> (plus the calibrated-state factories)
/// re-validate and throw, so nothing non-finite reaches plan, emit, or a
/// fingerprint even from a hand-built graph.
/// </para>
/// </summary>
/// <param name="Value">The exact numeric value to match; finite and zero-canonicalized once resolved.</param>
public sealed record RestrictToNumber(double Value) : RestrictToEntry;

/// <summary>
/// Keeps objects whose numeric raw value lies in [<paramref name="From"/>,
/// <paramref name="To"/>) — low-inclusive, high-exclusive, either end open when
/// null (§10.4). Both bounds null (<c>{}</c>) matches any usable numeric value.
/// </summary>
/// <param name="From">Inclusive lower bound; open when null.</param>
/// <param name="To">Exclusive upper bound; open when null.</param>
public sealed record RestrictToRange(double? From, double? To) : RestrictToEntry;
