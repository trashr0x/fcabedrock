using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

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

    // --- The triple entry point, under the same rules -------------------------------

    [Fact]
    public void ProbeTripleAsync_WhenSessionNull_ThenThrowsArgumentNullSynchronously() =>
        Assert.Throws<ArgumentNullException>("session", () => Ignore(Prober.ProbeTripleAsync(null!, Triple)));

    [Fact]
    public void ProbeTripleAsync_WhenReadSettingsNull_ThenThrowsArgumentNullSynchronously() =>
        Assert.Throws<ArgumentNullException>(
            "readSettings", () => Ignore(Prober.ProbeTripleAsync(TripleProbeFixtures.Fake(), null!)));

    [Fact]
    public void ProbeTripleAsync_WhenSettingsAreWideShaped_ThenThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Ignore(Prober.ProbeTripleAsync(TripleProbeFixtures.Fake(), Wide)));

        Assert.Equal("readSettings", ex.ParamName);
    }

    [Fact]
    public void ProbeTripleAsync_WhenSessionReportsWideShape_ThenThrowsArgumentException()
    {
        var session = new TripleProbeFixtures.FakeTripleSession(new SourceSchema(3), [])
        {
            Shape = SourceShape.Wide,
        };

        var ex = Assert.Throws<ArgumentException>(() => Ignore(Prober.ProbeTripleAsync(session, Triple)));
        Assert.Equal("session", ex.ParamName);
    }

    [Fact]
    public async Task ProbeTripleAsync_WhenOptionsNull_ThenUsesTheDefaults()
    {
        var result = await Prober.ProbeTripleAsync(
            TripleProbeFixtures.Fake(("s", "p", "v")), Triple, columns: null, options: null);

        Assert.Equal(
            ProbeDraftExpectations.NotesFor(ProbeOptions.Default.ValueRetentionLimit, 0),
            ProbeFixtures.Draft(result).Provenance!.Notes);
    }

    [Fact]
    public async Task ProbeTripleAsync_WhenColumnsNull_ThenReadsAndAuthorsTheDefaultRoles()
    {
        // Omission is a default, not an absence: the read uses 0/1/2 and the draft SAYS 0/1/2,
        // so a reread does not depend on the reader knowing §5.3's default (D-107).
        var session = TripleProbeFixtures.Fake(("s", "p", "v"));

        var draft = ProbeFixtures.Draft(await Prober.ProbeTripleAsync(session, Triple, columns: null));

        Assert.Equal(new TripleColumns(0, 1, 2), Assert.Single(session.RolesRead));
        Assert.Equal(
            new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(2)),
            draft.Binding!.Columns);
    }

    [Fact]
    public async Task ProbeTripleAsync_WhenShapesAgree_ThenNoDiagnosticReportsAShapeProblem()
    {
        var result = await Prober.ProbeTripleAsync(TripleProbeFixtures.Fake(("s", "p", "v")), Triple);

        Assert.True(result.IsOk, ProbeFixtures.Describe(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
    }
}
