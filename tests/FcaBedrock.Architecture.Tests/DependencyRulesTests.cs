// Copyright (c) Constantinos Orphanides. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Xunit;

namespace FcaBedrock.Architecture.Tests;

// Executable encoding of the CLAUDE.md dependency rule: Diagnostics is the only
// internal package referenced by everything, Core references only Diagnostics,
// and there are no cycles. The rules are read off the compiled FcaBedrock.*.dll
// metadata, so the moment a later milestone introduces a forbidden cross-package
// reference, the offending edge fails here.
public sealed class DependencyRulesTests
{
    private const string Prefix = "FcaBedrock.";

    [Fact]
    public void Diagnostics_IsALeaf()
    {
        var diagnostics = Production().Single(a => Name(a) == "FcaBedrock.Diagnostics");

        Assert.Empty(InternalReferences(diagnostics));
    }

    [Fact]
    public void Core_ReferencesOnlyDiagnostics()
    {
        var core = Production().Single(a => Name(a) == "FcaBedrock.Core");

        Assert.All(InternalReferences(core), r => Assert.Equal("FcaBedrock.Diagnostics", r));
    }

    [Fact]
    public void InternalReferenceGraph_IsAcyclic()
    {
        var graph = Production().ToDictionary(
            Name,
            a => InternalReferences(a).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        var cycle = FindCycle(graph);

        Assert.True(cycle is null, cycle is null ? string.Empty : "reference cycle: " + string.Join(" -> ", cycle));
    }

    private static string Name(Assembly assembly) => assembly.GetName().Name ?? string.Empty;

    private static IReadOnlyList<Assembly> Production()
    {
        var result = new List<Assembly>();
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "FcaBedrock.*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(Assembly.LoadFrom(path));
        }

        return result;
    }

    private static IEnumerable<string> InternalReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal));

    private static IReadOnlyList<string>? FindCycle(IReadOnlyDictionary<string, HashSet<string>> graph)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var node in graph.Keys)
        {
            var cycle = Visit(node, graph, visiting, visited, stack);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        return null;
    }

    private static IReadOnlyList<string>? Visit(
        string node,
        IReadOnlyDictionary<string, HashSet<string>> graph,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<string> stack)
    {
        if (visited.Contains(node))
        {
            return null;
        }

        if (!visiting.Add(node))
        {
            var from = stack.IndexOf(node);
            return stack.Skip(from < 0 ? 0 : from).Append(node).ToList();
        }

        stack.Add(node);
        if (graph.TryGetValue(node, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                var cycle = Visit(dependency, graph, visiting, visited, stack);
                if (cycle is not null)
                {
                    return cycle;
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        visiting.Remove(node);
        visited.Add(node);
        return null;
    }
}
