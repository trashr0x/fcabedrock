namespace FcaBedrock.Core.Spec;

/// <summary>How an attribute treats a raw value outside its declared domain. Spec §10.6.</summary>
public enum UnknownValuePolicy
{
    /// <summary>No cross, object kept, no diagnostic.</summary>
    Skip,

    /// <summary>No cross, object kept, emit <c>UnknownValueObserved</c> (Warning) — the default.</summary>
    Warn,

    /// <summary>Abort: <c>UnknownValueObserved</c> (Error).</summary>
    Fail,

    /// <summary>Extend the declared domain on the fly with the observed value.</summary>
    Include,
}
