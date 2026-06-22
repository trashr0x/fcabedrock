// Copyright (c) Constantinos Orphanides. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace FcaBedrock.Golden.Tests;

// The golden harness over the real v2 fixtures. In M0 there is no converter yet,
// so "actual" is a byte-copy of the expected golden file (the documented
// placeholder); this exercises locate -> read -> byte-compare end to end. At M1
// the copy is replaced by real Convert output. The loop no-ops (passes) until the
// user adds fixtures; a [Fact] loop is used instead of a [Theory] so an empty
// fixture set does not fail with xUnit's "no data" error.
public sealed class GoldenFixtureTests
{
    [Fact]
    public void EveryGoldenOutput_ByteMatchesActual_Placeholder()
    {
        foreach (var expectedPath in FixturePaths.EnumerateExpectedOutputs())
        {
            var expected = File.ReadAllBytes(expectedPath);
            var actual = expected.ToArray(); // M0 placeholder; M1: real conversion output

            var result = ByteComparer.Compare(expected, actual);

            Assert.True(result.AreEqual, $"{expectedPath}{Environment.NewLine}{result.Message}");
        }
    }

    [Fact]
    public void MiniMushroom_HasGoldenOutputs_WhenPresent()
    {
        var exampleDir = Path.Combine(FixturePaths.V2Root, "mini-mushroom");
        if (!Directory.Exists(exampleDir))
        {
            return; // headline fixture not added yet
        }

        var marker = Path.Combine("mini-mushroom", "expected");
        var outputs = FixturePaths.EnumerateExpectedOutputs()
            .Where(p => p.Contains(marker, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(outputs);
    }
}
