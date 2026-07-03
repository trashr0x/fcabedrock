namespace FcaBedrock.Core.Spec;

/// <summary>
/// How an attribute's raw source values are read. Spec §10.2 / D-061. The spec
/// default is per-discretizer — string-fixing kinds default to
/// <see cref="String"/>, number-fixing to <see cref="Number"/> — so no member
/// is a blanket default. <c>date</c> is reserved by the spec but deliberately
/// absent here until its parse+reject lands (D-038).
/// </summary>
public enum SourceValueType
{
    /// <summary>Raw values are opaque strings.</summary>
    String,

    /// <summary>Raw values parse as numbers under the binding locale.</summary>
    Number,
}
