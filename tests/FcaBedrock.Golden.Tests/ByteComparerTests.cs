// Copyright (c) Constantinos Orphanides. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace FcaBedrock.Golden.Tests;

// M0's real evidence that the byte-compare mechanism works: these run regardless
// of whether the v2 fixtures have been added. They prove the comparator reports
// equality on identical bytes and pinpoints the first difference otherwise.
public sealed class ByteComparerTests
{
    [Fact]
    public void Compare_WhenSequencesIdentical_ThenReportsEqual()
    {
        var a = new byte[] { 0x42, 0x00, 0x01, 0x58 };
        var b = new byte[] { 0x42, 0x00, 0x01, 0x58 };

        var result = ByteComparer.Compare(a, b);

        Assert.True(result.AreEqual, result.Message);
        Assert.Equal(-1, result.FirstDifferenceOffset);
    }

    [Fact]
    public void Compare_WhenBytesDifferAtOffset_ThenReportsThatOffset()
    {
        var a = new byte[] { 0x42, 0x00, 0x01, 0x58 };
        var b = new byte[] { 0x42, 0x00, 0x99, 0x58 };

        var result = ByteComparer.Compare(a, b);

        Assert.False(result.AreEqual);
        Assert.Equal(2, result.FirstDifferenceOffset);
    }

    [Fact]
    public void Compare_WhenLengthsDiffer_ThenReportsFirstMissingByteOffset()
    {
        var shorter = new byte[] { 0x01, 0x02, 0x03 };
        var longer = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var result = ByteComparer.Compare(shorter, longer);

        Assert.False(result.AreEqual);
        Assert.Equal(3, result.FirstDifferenceOffset);
    }

    [Fact]
    public void Compare_WhenBothEmpty_ThenReportsEqual()
    {
        var result = ByteComparer.Compare(Array.Empty<byte>(), Array.Empty<byte>());

        Assert.True(result.AreEqual, result.Message);
    }
}
