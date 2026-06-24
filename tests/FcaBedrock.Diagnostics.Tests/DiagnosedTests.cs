namespace FcaBedrock.Diagnostics.Tests;

public sealed class DiagnosedTests
{
    private static BedrockDiagnostic Diag(DiagnosticSeverity severity) =>
        new(DiagnosticCode.UnknownValueObserved, severity, "msg");

    [Fact]
    public void HasErrors_WhenOnlyWarnings_ThenFalse()
    {
        var diagnosed = Diagnosed<int>.Ok(1, [Diag(DiagnosticSeverity.Warning), Diag(DiagnosticSeverity.Info)]);

        Assert.False(diagnosed.HasErrors);
        Assert.True(diagnosed.IsOk);
    }

    [Fact]
    public void HasErrors_WhenContainsError_ThenTrue()
    {
        var diagnosed = Diagnosed<int>.Ok(1, [Diag(DiagnosticSeverity.Warning), Diag(DiagnosticSeverity.Error)]);

        Assert.True(diagnosed.HasErrors);
        Assert.False(diagnosed.IsOk);
    }

    [Fact]
    public void HasErrors_WhenContainsFatal_ThenTrue()
    {
        var diagnosed = Diagnosed<int>.Failed([Diag(DiagnosticSeverity.Fatal)]);

        Assert.True(diagnosed.HasErrors);
    }

    [Fact]
    public void TryGetValue_WhenOk_ThenReturnsTrueAndValue()
    {
        var diagnosed = Diagnosed<string>.Ok("plan");

        Assert.True(diagnosed.TryGetValue(out var value));
        Assert.Equal("plan", value);
    }

    [Fact]
    public void TryGetValue_WhenFailed_ThenReturnsFalse()
    {
        var diagnosed = Diagnosed<string>.Failed([Diag(DiagnosticSeverity.Error)]);

        Assert.False(diagnosed.TryGetValue(out _));
    }

    [Fact]
    public void Ok_WhenNoDiagnosticsGiven_ThenDiagnosticsEmpty()
    {
        var diagnosed = Diagnosed<int>.Ok(5);

        Assert.Empty(diagnosed.Diagnostics);
    }
}
