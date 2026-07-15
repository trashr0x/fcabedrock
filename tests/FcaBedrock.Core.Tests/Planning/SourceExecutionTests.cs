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

    // ConversionPlan is now a sealed reference-identity class (planner-owned internal
    // constructor, D-098) rather than a positional record, so the former
    // ConversionPlan_Equality_* record-equality tests no longer apply; SourceExecution's own
    // value equality is covered by the tests above.
}
