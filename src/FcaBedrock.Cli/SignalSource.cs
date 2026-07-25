using System.Runtime.InteropServices;

namespace FcaBedrock.Cli;

/// <summary>
/// The cancellation seam the host and every command observe (D-122 part 2).
/// </summary>
internal interface ISignalSource : IDisposable
{
    /// <summary>Cancelled by the first interrupt; the cooperative-cancellation token.</summary>
    CancellationToken Token { get; }
}

/// <summary>
/// The signal state machine, separated from the registration mechanism so the
/// transition is testable without raising a real signal at the test process.
/// <para>
/// First interrupt: request cooperative cancellation and <b>suppress</b> the platform's
/// default termination, so cleanup runs, no run is committed, and the process exits 3.
/// A repeated interrupt no longer suppresses it — the platform terminates immediately,
/// and by the publication ordering only uncommitted residue can survive (D-122 part 2).
/// </para>
/// </summary>
internal sealed class SignalCoordinator : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private int _signalled;

    /// <summary>The cooperative-cancellation token.</summary>
    public CancellationToken Token => _source.Token;

    /// <summary>
    /// Records one interrupt and returns whether the platform's default termination
    /// should be suppressed. Signal handlers may run on any thread, so the first-versus-
    /// repeat decision is an interlocked transition rather than a plain flag.
    /// </summary>
    public bool OnSignal()
    {
        if (Interlocked.Exchange(ref _signalled, 1) != 0)
        {
            return false;
        }

        try
        {
            _source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The host has already torn down; there is nothing left to cancel, and
            // suppressing termination at that point would only hang the process.
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public void Dispose() => _source.Dispose();
}

/// <summary>Creates one platform signal registration; the seam tests substitute.</summary>
/// <param name="signal">The signal to register for.</param>
/// <param name="handler">The handler invoked when it arrives.</param>
/// <returns>A disposable registration.</returns>
internal delegate IDisposable SignalRegistrar(PosixSignal signal, Action<PosixSignalContext> handler);

/// <summary>
/// The real signal source: SIGINT and SIGTERM through
/// <see cref="PosixSignalRegistration"/>, both driving one <see cref="SignalCoordinator"/>.
/// </summary>
internal sealed class PosixSignalSource : ISignalSource
{
    /// <summary>The signals the CLI registers for, in registration order.</summary>
    internal static readonly PosixSignal[] Registered = [PosixSignal.SIGINT, PosixSignal.SIGTERM];

    private readonly SignalCoordinator _coordinator;
    private readonly List<IDisposable> _registrations = [];

    internal PosixSignalSource(SignalCoordinator coordinator, SignalRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(registrar);
        _coordinator = coordinator;

        // Construction is failure-atomic. If the platform refuses the second signal, the
        // first registration is already live and the caller would never receive an instance
        // to dispose — so a partially built source releases everything it acquired, and the
        // coordinator with it, before the original failure propagates.
        try
        {
            foreach (var signal in Registered)
            {
                _registrations.Add(registrar(signal, OnSignal));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Creates the production source over real POSIX signal registrations.</summary>
    public static PosixSignalSource Create() =>
        new(new SignalCoordinator(), static (signal, handler) => PosixSignalRegistration.Create(signal, handler));

    /// <inheritdoc/>
    public CancellationToken Token => _coordinator.Token;

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
        _coordinator.Dispose();
    }

    private void OnSignal(PosixSignalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Cancel = _coordinator.OnSignal();
    }
}
