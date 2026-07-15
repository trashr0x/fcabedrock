namespace FcaBedrock.Core.Spec;

/// <summary>
/// One name-bound reference and the resolved 0-based column index it produced,
/// together with the binding <em>site</em> it proves (D-098/G-1). A bare
/// <c>(name, index)</c> pair would verify the header but not the member that uses
/// it, so <see cref="ResolvedSpec.Create"/> re-checks both: the header carries
/// exactly one ordinal occurrence of <see cref="Name"/> at <see cref="Index"/>,
/// and the resolved member at the identified site actually carries that index.
/// <para>
/// Mechanically closed — the <see langword="private protected"/> base constructor
/// leaves no accessible base constructor to out-of-assembly types, so the trust
/// boundary's site switch is exhaustive by construction (P-10).
/// </para>
/// </summary>
public abstract record ResolvedNameBinding
{
    private protected ResolvedNameBinding(string name, int index)
    {
        Name = name;
        Index = index;
    }

    /// <summary>The authored header name that was bound.</summary>
    public string Name { get; }

    /// <summary>The resolved 0-based column index.</summary>
    public int Index { get; }
}

/// <summary>
/// The site is a named attribute's <c>source</c> — it must exist, be unique, and
/// be a <see cref="ColumnSource"/> whose index matches (§10.2).
/// </summary>
public sealed record AttributeSourceNameBinding : ResolvedNameBinding
{
    /// <summary>Records that attribute <paramref name="attributeName"/> bound source name <paramref name="name"/> to <paramref name="index"/>.</summary>
    public AttributeSourceNameBinding(string attributeName, string name, int index)
        : base(name, index) => AttributeName = attributeName;

    /// <summary>The attribute whose source was name-bound.</summary>
    public string AttributeName { get; }
}

/// <summary>
/// The site is the binding's object key — it must be a <see cref="ColumnObjectKey"/>
/// whose index matches (§5.4).
/// </summary>
public sealed record ObjectKeyNameBinding : ResolvedNameBinding
{
    /// <summary>Records that the object-key column name <paramref name="name"/> bound to <paramref name="index"/>.</summary>
    public ObjectKeyNameBinding(string name, int index) : base(name, index)
    {
    }
}

/// <summary>
/// The site is one triple role — the <see cref="TripleColumns"/> field for
/// <see cref="Role"/> must carry the recorded index (§5.3).
/// </summary>
public sealed record TripleRoleNameBinding : ResolvedNameBinding
{
    /// <summary>Records that triple role <paramref name="role"/> bound name <paramref name="name"/> to <paramref name="index"/>.</summary>
    public TripleRoleNameBinding(TripleRole role, string name, int index) : base(name, index) => Role = role;

    /// <summary>The triple role that was name-bound.</summary>
    public TripleRole Role { get; }
}

/// <summary>One of the three triple roles (§5.3).</summary>
public enum TripleRole
{
    /// <summary>The subject role (also the object-key column).</summary>
    Subject,

    /// <summary>The predicate role.</summary>
    Predicate,

    /// <summary>The value role.</summary>
    Value,
}
