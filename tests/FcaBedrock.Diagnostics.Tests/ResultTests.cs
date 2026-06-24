namespace FcaBedrock.Diagnostics.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Ok_WhenConstructed_ThenIsOkAndExposesValue()
    {
        var result = Result<int, string>.Ok(42);

        Assert.True(result.IsOk);
        Assert.False(result.IsError);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Err_WhenConstructed_ThenIsErrorAndExposesError()
    {
        var result = Result<int, string>.Err("boom");

        Assert.True(result.IsError);
        Assert.False(result.IsOk);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void Value_WhenError_ThenThrows()
    {
        var result = Result<int, string>.Err("boom");

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Error_WhenOk_ThenThrows()
    {
        var result = Result<int, string>.Ok(1);

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void TryGetValue_WhenOk_ThenReturnsTrueAndValue()
    {
        var result = Result<int, string>.Ok(7);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(7, value);
    }

    [Fact]
    public void Map_WhenOk_ThenTransformsValue()
    {
        var result = Result<int, string>.Ok(21).Map(v => v * 2);

        Assert.True(result.IsOk);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Map_WhenError_ThenPreservesError()
    {
        var result = Result<int, string>.Err("boom").Map(v => v * 2);

        Assert.True(result.IsError);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void Bind_WhenError_ThenShortCircuits()
    {
        var result = Result<int, string>.Err("boom").Bind(v => Result<int, string>.Ok(v + 1));

        Assert.True(result.IsError);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void Match_WhenOk_ThenInvokesOkBranch()
    {
        var label = Result<int, string>.Ok(1).Match(_ => "ok", _ => "err");

        Assert.Equal("ok", label);
    }
}
