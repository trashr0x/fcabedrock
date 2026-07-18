using FcaBedrock.Core.Spec;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The programmer-error half of P-14: what <see cref="Prober.ProbeAsync"/> throws rather than
/// diagnoses. The caller selects the shape (D-106), so a shape that does not match the session
/// it was handed is a bug in the call — not a property of the data — and must not be dressed up
/// as a <c>BedrockDiagnostic</c> a caller might try to handle.
/// </summary>
public sealed class ProberArgumentTests
{
    private static readonly SourceReadSettings Wide = ProbeFixtures.WideSettings();

    private static readonly SourceReadSettings Triple = SourceReadSettings.CreateTriple();

    // Every case here asserts a SYNCHRONOUS throw — the guards run before the async state
    // machine, so a caller that never awaits a misused call still sees the bug. Handing the
    // pending ValueTask to this sink is what lets the assertion stay synchronous without
    // consuming a task that, by construction, is never created.
    private static void Ignore<T>(ValueTask<T> pending) => _ = pending;

    [Fact]
    public void ProbeAsync_WhenSessionNull_ThenThrowsArgumentNullSynchronously() =>
        Assert.Throws<ArgumentNullException>("session", () => Ignore(Prober.ProbeAsync(null!, Wide)));

    [Fact]
    public void ProbeAsync_WhenReadSettingsNull_ThenThrowsArgumentNullSynchronously() =>
        Assert.Throws<ArgumentNullException>(
            "readSettings", () => Ignore(Prober.ProbeAsync(ProbeFixtures.Fake(new SourceSchema(1)), null!)));

    [Fact]
    public void ProbeAsync_WhenSettingsAreTripleShaped_ThenThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Ignore(Prober.ProbeAsync(ProbeFixtures.Fake(new SourceSchema(1)), Triple)));

        Assert.Equal("readSettings", ex.ParamName);
    }

    [Fact]
    public void ProbeAsync_WhenSessionReportsTripleShape_ThenThrowsArgumentException()
    {
        // A session may only claim one shape, and this entry point serves wide. Reachable
        // through a hand-written IWideSourceSession, so it is checked rather than assumed.
        var session = new ProbeFixtures.FakeWideSession(new SourceSchema(1), []) { Shape = SourceShape.Triple };

        var ex = Assert.Throws<ArgumentException>(() => Ignore(Prober.ProbeAsync(session, Wide)));
        Assert.Equal("session", ex.ParamName);
    }

    [Fact]
    public async Task ProbeAsync_WhenOptionsNull_ThenUsesTheDefaults()
    {
        // Null options means Default, not "no limits": the notes must record the default limit.
        var result = await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(1, ["a"]), ["x"]), Wide, options: null);

        Assert.Equal(
            ProbeDraftExpectations.NotesFor(ProbeOptions.Default.ValueRetentionLimit, 0),
            ProbeFixtures.Draft(result).Provenance!.Notes);
    }

    [Fact]
    public async Task ProbeAsync_WhenShapesAgree_ThenNoDiagnosticReportsAShapeProblem()
    {
        // The positive side of the guard: a matched pair simply works, so the checks above
        // cannot be passing for an unrelated reason.
        var result = await Prober.ProbeAsync(ProbeFixtures.Fake(new SourceSchema(1, ["a"]), ["x"]), Wide);

        Assert.True(result.IsOk, ProbeFixtures.Describe(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
    }
}
