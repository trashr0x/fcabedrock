using System.Runtime.InteropServices;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The signal contract (D-122 part 2), exercised through the registration seam so the
/// state transition is proven without raising a real signal at the test process.
/// </summary>
public sealed class SignalSourceTests
{
    private sealed class FakeRegistration : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeRegistrar
    {
        /// <summary>The zero-based registration index that throws, or -1 for none.</summary>
        public int FailAt { get; init; } = -1;

        public List<PosixSignal> Signals { get; } = [];

        public List<Action<PosixSignalContext>> Handlers { get; } = [];

        public List<FakeRegistration> Registrations { get; } = [];

        public IDisposable Register(PosixSignal signal, Action<PosixSignalContext> handler)
        {
            if (Signals.Count == FailAt)
            {
                throw new PlatformNotSupportedException($"{signal} is unavailable on this host.");
            }

            Signals.Add(signal);
            Handlers.Add(handler);
            var registration = new FakeRegistration();
            Registrations.Add(registration);
            return registration;
        }
    }

    [Fact]
    public void OnSignal_WhenTheFirstInterruptArrives_ThenCancellationIsRequestedAndTerminationIsSuppressed()
    {
        using var coordinator = new SignalCoordinator();

        Assert.False(coordinator.Token.IsCancellationRequested);
        Assert.True(coordinator.OnSignal());
        Assert.True(coordinator.Token.IsCancellationRequested);
    }

    [Fact]
    public void OnSignal_WhenARepeatedInterruptArrives_ThenDefaultTerminationIsNoLongerSuppressed()
    {
        using var coordinator = new SignalCoordinator();

        Assert.True(coordinator.OnSignal());
        Assert.False(coordinator.OnSignal());
        Assert.False(coordinator.OnSignal());

        // The token stays cancelled: the cooperative request is not withdrawn.
        Assert.True(coordinator.Token.IsCancellationRequested);
    }

    [Fact]
    public void Registered_WhenInspected_ThenSigintAndSigtermAreTheRegisteredSignals()
    {
        Assert.Equal([PosixSignal.SIGINT, PosixSignal.SIGTERM], PosixSignalSource.Registered);
    }

    [Fact]
    public void PosixSignalSource_WhenConstructed_ThenItRegistersForSigintAndSigterm()
    {
        var registrar = new FakeRegistrar();

        using var source = new PosixSignalSource(new SignalCoordinator(), registrar.Register);

        Assert.Equal([PosixSignal.SIGINT, PosixSignal.SIGTERM], registrar.Signals);
        Assert.Equal(2, registrar.Handlers.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PosixSignalSource_WhenASignalArrives_ThenTheFirstSuppressesTerminationAndTheSecondDoesNot(int handler)
    {
        // Either registered handler drives the same coordinator, so SIGTERM after SIGINT
        // is a repeat rather than a second "first" signal.
        var registrar = new FakeRegistrar();
        using var source = new PosixSignalSource(new SignalCoordinator(), registrar.Register);

        var first = new PosixSignalContext(registrar.Signals[handler]);
        registrar.Handlers[handler](first);

        var second = new PosixSignalContext(registrar.Signals[1 - handler]);
        registrar.Handlers[1 - handler](second);

        Assert.True(first.Cancel);
        Assert.False(second.Cancel);
        Assert.True(source.Token.IsCancellationRequested);
    }

    [Fact]
    public void PosixSignalSource_WhenDisposed_ThenEveryRegistrationIsDisposed()
    {
        var registrar = new FakeRegistrar();
        var source = new PosixSignalSource(new SignalCoordinator(), registrar.Register);

        source.Dispose();

        Assert.All(registrar.Registrations, registration => Assert.True(registration.Disposed));
    }

    [Fact]
    public void PosixSignalSource_WhenTheFirstRegistrationFails_ThenNothingIsLeftBehind()
    {
        var registrar = new FakeRegistrar { FailAt = 0 };
        var coordinator = new SignalCoordinator();

        Assert.Throws<PlatformNotSupportedException>(
            () => new PosixSignalSource(coordinator, registrar.Register));

        Assert.Empty(registrar.Registrations);
        Assert.True(IsDisposed(coordinator));
    }

    [Fact]
    public void PosixSignalSource_WhenTheSecondRegistrationFails_ThenTheFirstIsDisposedAndTheCoordinatorIsReleased()
    {
        // The constructor never returns, so the host has no instance to dispose: a partially
        // built source has to release what it already acquired, or a live SIGINT handler
        // stays attached to an object nothing can reach.
        var registrar = new FakeRegistrar { FailAt = 1 };
        var coordinator = new SignalCoordinator();

        var thrown = Assert.Throws<PlatformNotSupportedException>(
            () => new PosixSignalSource(coordinator, registrar.Register));

        Assert.Contains("SIGTERM", thrown.Message, StringComparison.Ordinal);
        Assert.Single(registrar.Registrations);
        Assert.True(registrar.Registrations[0].Disposed);

        // The coordinator can no longer suppress anything: its source is gone, so a signal
        // arriving now permits the platform's default termination.
        Assert.True(IsDisposed(coordinator));
        Assert.False(coordinator.OnSignal());
    }

    // A disposed CancellationTokenSource throws when its token is read; that is the only
    // observable difference, so the test asks the question directly rather than adding a
    // production flag no production caller needs.
    private static bool IsDisposed(SignalCoordinator coordinator)
    {
        try
        {
            _ = coordinator.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    [Fact]
    public void PosixSignalSource_WhenCreatedForReal_ThenRegistrationAndDisposalSucceedOnThisHost()
    {
        // The real registration path, exercised without raising anything: this is what
        // catches a platform that refuses one of the two signals.
        using var source = PosixSignalSource.Create();

        Assert.False(source.Token.IsCancellationRequested);
    }
}
