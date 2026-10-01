namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The work budget's schedule, exactly as <see cref="CancellationBudget"/> states it: the token is
/// checked when the remaining units reach zero or below, the count is then reset to the quantum and
/// any overshoot is discarded, and a candidate is metered when its raw length plus column count
/// reaches the quantum.
/// </summary>
public sealed class CancellationBudgetTests
{
    private const int Q = CancellationBudget.Quantum;

    [Fact]
    public void Charge_WhenTheRemainingUnitsReachZero_ThenTheTokenIsCheckedAndTheQuantumRestored()
    {
        using var cts = new CancellationTokenSource();
        var budget = new CancellationBudget(cts.Token);

        budget.Charge(Q); // reaches zero: checked (not cancelled), restored to the quantum
        cts.Cancel();
        budget.Charge(Q - 1); // one unit remains: no check

        Assert.Equal(cts.Token, Assert.ThrowsAny<OperationCanceledException>(() => budget.Charge(1)).CancellationToken);
    }

    [Fact]
    public void Charge_WhenAChargeOvershootsTheQuantum_ThenTheOvershootIsDiscarded()
    {
        using var cts = new CancellationTokenSource();
        var budget = new CancellationBudget(cts.Token);

        // 65,535 then 65,536 units: the second charge reaches the check 131,071 units in, and the
        // count restarts at a full quantum rather than at a quantum minus the overshoot.
        budget.Charge(Q - 1);
        budget.Charge(Q);
        cts.Cancel();
        budget.Charge(Q - 1);

        Assert.ThrowsAny<OperationCanceledException>(() => budget.Charge(1));
    }

    [Fact]
    public void Charge_WhenTheTokenIsCancelled_ThenTheExceptionCarriesThatToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var budget = new CancellationBudget(cts.Token);

        budget.Charge(Q - 1); // below the quantum: no check yet, although the token is cancelled

        Assert.Equal(cts.Token, Assert.ThrowsAny<OperationCanceledException>(() => budget.Charge(1)).CancellationToken);
    }

    [Fact]
    public void Charge_WhenTheBudgetIsNoCheckpoints_ThenNothingIsEverChecked()
    {
        var budget = default(NoCheckpoints);

        budget.Charge(int.MaxValue);
        budget.Charge(int.MaxValue);
    }

    [Theory]
    [InlineData(Q - 1, 1, true)]
    [InlineData(0, Q, true)]
    [InlineData(Q - 2, 1, false)]
    [InlineData(0, 1, false)]
    public void IsLong_WhenRawLengthPlusColumnsReachesTheQuantum_ThenTheCandidateIsMetered(int rawLength, int columns, bool metered) =>
        Assert.Equal(metered, CancellationBudget.IsLong(rawLength, columns));
}
