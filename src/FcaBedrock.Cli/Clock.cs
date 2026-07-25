namespace FcaBedrock.Cli;

/// <summary>
/// The CLI's time seam. Core is pure and takes no ambient clock (P-13), and the run
/// manifest's <c>timestamp</c> is whole-second RFC 3339 UTC from an <b>injected</b>
/// clock (§15, D-122 part 6) — so the process boundary is the one place a real clock
/// may be read, and it is read through this interface.
/// </summary>
internal interface IClock
{
    /// <summary>The current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real clock; the only production implementation.</summary>
internal sealed class SystemClock : IClock
{
    private SystemClock()
    {
    }

    /// <summary>The shared instance.</summary>
    public static SystemClock Instance { get; } = new();

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
