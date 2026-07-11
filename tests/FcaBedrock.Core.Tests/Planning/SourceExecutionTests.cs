using System.Reflection;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Planning;

public sealed class SourceExecutionTests
{
    [Fact]
    public void SourceExecution_Constructors_AreNeitherPublicNorProtected()
    {
        // D-082: the hierarchy is mechanically closed — no accessible base constructor outside this
        // assembly, so no out-of-assembly type can derive. private protected reflects as FamANDAssem;
        // assert nothing is public / protected / protected internal.
        var ctors = typeof(SourceExecution).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(ctors);
        Assert.All(ctors, c =>
        {
            Assert.False(c.IsPublic);
            Assert.False(c.IsFamily);           // protected
            Assert.False(c.IsFamilyOrAssembly); // protected internal
        });
    }

    [Fact]
    public void WideExecution_Instance_IsASingleton()
    {
        Assert.Same(WideExecution.Instance, WideExecution.Instance);
        Assert.Empty(typeof(WideExecution).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void TripleExecution_ExposesResolvedOrdering()
    {
        Assert.Equal(TripleOrdering.Unordered, new TripleExecution(TripleOrdering.Unordered).Ordering);
        Assert.Equal(TripleOrdering.SubjectGrouped, new TripleExecution(TripleOrdering.SubjectGrouped).Ordering);
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public void TripleExecution_IndependentlyConstructedEquivalent_AreEqual(TripleOrdering ordering)
    {
        Assert.Equal(new TripleExecution(ordering), new TripleExecution(ordering));
        Assert.Equal(new TripleExecution(ordering).GetHashCode(), new TripleExecution(ordering).GetHashCode());
    }

    [Fact]
    public void TripleExecution_DifferentOrderings_AreNotEqual()
    {
        Assert.NotEqual(
            new TripleExecution(TripleOrdering.SubjectGrouped),
            new TripleExecution(TripleOrdering.Unordered));
    }

    [Fact]
    public void ConversionPlan_Equality_HoldsAcrossIndependentlyObtainedWideExecutions()
    {
        // The Execution member must not break ConversionPlan record equality: hold the other members
        // fixed (same instances) and vary only the (equal) executions.
        var (formals, attrs) = EmptyMembers();
        var key = new RowIndexObjectKey();

        var a = new ConversionPlan(formals, attrs, key, WideExecution.Instance);
        var b = new ConversionPlan(formals, attrs, key, WideExecution.Instance);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ConversionPlan_Equality_HoldsAcrossIndependentlyConstructedTripleExecutions()
    {
        var (formals, attrs) = EmptyMembers();
        var key = new ColumnObjectKey(0, DuplicateObjectPolicy.Fail);

        var a = new ConversionPlan(formals, attrs, key, new TripleExecution(TripleOrdering.Unordered));
        var b = new ConversionPlan(formals, attrs, key, new TripleExecution(TripleOrdering.Unordered));

        Assert.Equal(a, b);
    }

    [Fact]
    public void ConversionPlan_Equality_FailsAcrossDifferentExecutionVariants()
    {
        var (formals, attrs) = EmptyMembers();
        var key = new RowIndexObjectKey();

        var wide = new ConversionPlan(formals, attrs, key, WideExecution.Instance);
        var triple = new ConversionPlan(formals, attrs, key, new TripleExecution(TripleOrdering.Unordered));

        Assert.NotEqual(wide, triple);
    }

    private static (IReadOnlyList<FormalAttribute> Formals, IReadOnlyList<PlannedAttribute> Attrs) EmptyMembers()
    {
        IReadOnlyList<FormalAttribute> formals = new List<FormalAttribute>();
        IReadOnlyList<PlannedAttribute> attrs = new List<PlannedAttribute>();
        return (formals, attrs);
    }
}
