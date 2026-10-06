namespace FcaBedrock.Core.Spec;

/// <summary>
/// How an attribute's raw source values are read. Spec §10.2 / D-061. The spec
/// default is per-discretizer: string-fixing kinds default to
/// <see cref="String"/>, number-fixing to <see cref="Number"/>, so no member
/// is a blanket default. <c>date</c> is reserved by the spec but has no member here:
/// v1 defines no date reading, so a source that declares <c>value_type = "date"</c>
/// resolves to <see cref="UnimplementedDateSource"/>, which carries no value type, and the
/// planner refuses it (<c>DateValueTypeNotImplementedV1</c>, §10.2/§11.7, D-038).
/// </summary>
public enum SourceValueType
{
    /// <summary>Raw values are opaque strings.</summary>
    String,

    /// <summary>Raw values parse as numbers under the binding locale.</summary>
    Number,
}
