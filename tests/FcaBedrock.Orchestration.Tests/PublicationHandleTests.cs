using System.Reflection;
using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The handle rules (D-140): construction only by preflight, the family as a type, association
/// with the run by reference, argument failures that leave a handle usable, single use, release on
/// every exit of a publish, disposal that releases and never rolls back, and a preflight that holds
/// nothing once it refuses or throws.
/// <para>
/// <b>The release observation is Windows'.</b> The seam's own removal marks an object with the
/// classic disposition, which keeps its name occupied while any handle on it is open, so a free name
/// shows no reference is held; <see cref="Preflight_WhenAReplacedTargetIsHeldAndTheHandleIsDisposed_ThenItsReferenceIsReleased"/>
/// is the positive control that shows the observation sees a held one. A leaked reference is an
/// unreachable handle the runtime may finalize at any moment, so a free name is evidence at that
/// instant, not proof against every schedule; the guard's own control flow is the structural
/// evidence. Off Windows the observation is unavailable, and those cases skip with that reason
/// after asserting everything else.
/// </para>
/// </summary>
public sealed class PublicationHandleTests
{
    private const string Convert = PublicationRun.Convert;
    private const string SpecFile = PublicationRun.SpecFile;

    // ---- association ------------------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_WhenTheConversionIsForAnotherRun_ThenItThrowsBeforeAnyMutation()
    {
        using var mine = await PublicationRun.CreateAsync(Convert);
        using var other = await PublicationRun.CreateAsync(Convert);
        var handle = (PreflightedConvertOutput)mine.Preflight();

        await Assert.ThrowsAsync<ArgumentException>(() => ConvertPublication.PublishAsync(
            other.Conversion, handle, mine.Facts, mine.Diagnostics, mine.Signals.Token));

        Assert.Empty(mine.Mutations());
        Assert.Empty(other.Mutations());
        Assert.Equal(PublicationAttemptState.NotBegun, handle.Attempt.State);
        handle.Dispose();
    }

    [Fact]
    public async Task PublishAsync_WhenTheHandleIsForAnotherRun_ThenItThrowsBeforeAnyMutation()
    {
        using var mine = await PublicationRun.CreateAsync(Convert);
        using var other = await PublicationRun.CreateAsync(Convert);
        var foreign = (PreflightedConvertOutput)other.Preflight();

        await Assert.ThrowsAsync<ArgumentException>(() => ConvertPublication.PublishAsync(
            mine.Conversion, foreign, mine.Facts, mine.Diagnostics, mine.Signals.Token));

        Assert.Empty(mine.Mutations());
        Assert.Empty(other.Mutations());
        Assert.Equal(PublicationAttemptState.NotBegun, foreign.Attempt.State);
        foreign.Dispose();
    }

    [Fact]
    public async Task PublishAsync_WhenRefusedForAnArgument_ThenTheHandleStillPublishesCorrectlyAfterwards()
    {
        using var mine = await PublicationRun.CreateAsync(Convert);
        using var other = await PublicationRun.CreateAsync(Convert);
        var handle = (PreflightedConvertOutput)mine.Preflight();

        await Assert.ThrowsAsync<ArgumentException>(() => ConvertPublication.PublishAsync(
            other.Conversion, handle, mine.Facts, mine.Diagnostics, mine.Signals.Token));

        // Not spent: the correct conversion publishes through the same handle.
        var published = OutcomeAssert.Committed(await mine.PublishAsync(handle));
        Assert.Equal(PublicationAttemptState.Committed, handle.Attempt.State);
        Assert.Same(published, handle.Attempt.Committed);
        Assert.True(File.Exists(mine.Target));
    }

    [Fact]
    public async Task PublishAsync_WhenRefusedForAnArgument_ThenDisposingReleasesEveryReference()
    {
        using var mine = await PublicationRun.CreateAsync(Convert);
        using var other = await PublicationRun.CreateAsync(Convert);
        PublicationRun.Occupy(mine.Target);
        var handle = (PreflightedConvertOutput)mine.Preflight(replace: true);

        await Assert.ThrowsAsync<ArgumentException>(() => ConvertPublication.PublishAsync(
            other.Conversion, handle, mine.Facts, mine.Diagnostics, mine.Signals.Token));
        handle.Dispose();

        Assert.Empty(mine.Mutations());
        Assert.Equal(PublicationRun.Old, File.ReadAllText(mine.Target));
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
        PublicationRun.AssertNoReferenceHeld(mine.Target);
    }

    [Fact]
    public void ConvertPublication_ShouldTakeOnlyAFileBackedPreparedRun()
    {
        // An export of an in-memory root cannot be expressed: every run-typed parameter of the
        // public convert operations, and the conversion's own run, is the file-backed kind.
        var runParameters = typeof(ConvertPublication)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(method => method.GetParameters())
            .Where(parameter => typeof(PreparedRun).IsAssignableFrom(parameter.ParameterType))
            .ToList();

        Assert.Equal(2, runParameters.Count);
        Assert.All(runParameters, parameter => Assert.Equal(typeof(PreparedFileRun), parameter.ParameterType));
        Assert.Equal(typeof(PreparedFileRun), typeof(EffectiveConversion).GetProperty(nameof(EffectiveConversion.Run))!.PropertyType);
    }

    [Fact]
    public void Publish_ShouldTakeOnlyTheHandleOfItsOwnFamily()
    {
        Assert.Contains(
            typeof(SpecPublication).GetMethod(nameof(SpecPublication.PublishAsync))!.GetParameters(),
            parameter => parameter.ParameterType == typeof(PreflightedSpecFile));
        Assert.Contains(
            typeof(ConvertPublication).GetMethod(nameof(ConvertPublication.PublishAsync))!.GetParameters(),
            parameter => parameter.ParameterType == typeof(PreflightedConvertOutput));
        Assert.DoesNotContain(
            typeof(SpecPublication).GetMethods().Concat(typeof(ConvertPublication).GetMethods()).SelectMany(method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(PreflightedPublication));
    }

    // ---- single use and release -------------------------------------------------------------------

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenCalledASecondTime_ThenItThrowsInvalidOperation(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        var handle = run.Preflight();
        OutcomeAssert.Committed(await run.PublishAsync(handle));
        var first = handle.Attempt;

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.PublishAsync(handle));

        Assert.Same(first, handle.Attempt);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenTheHandleWasDisposed_ThenItThrowsObjectDisposed(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        var handle = run.Preflight();
        handle.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => run.PublishAsync(handle));

        Assert.Equal(PublicationAttemptState.NotBegun, handle.Attempt.State);
        Assert.Empty(run.Mutations());
    }

    [Theory]
    [InlineData(Convert, "returns")]
    [InlineData(Convert, "throws")]
    [InlineData(SpecFile, "returns")]
    [InlineData(SpecFile, "throws")]
    public async Task PublishAsync_WhenItReturnsOrThrows_ThenEveryReferenceIsReleased(string family, string exit)
    {
        // Observed WITHOUT disposing the handle: the publish itself released what it held, the
        // replaced target's reference included.
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);
        if (exit == "throws")
        {
            CancelOnceBegun(run);
        }

        var handle = run.Preflight(replace: true);

        if (exit == "throws")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(handle));
            Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        }
        else
        {
            OutcomeAssert.Committed(await run.PublishAsync(handle));
        }

        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
        PublicationRun.AssertNoReferenceHeld(run.Target);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenCancelledBeforeTheCommitPoint_ThenItRollsBackAndThrowsForThePassedToken(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);
        CancelOnceBegun(run);
        var handle = run.Preflight(replace: true);

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(handle));

        Assert.Equal(run.Signals.Token, cancelled.CancellationToken);
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        Assert.Empty(run.Residue());
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.Completed, handle.Attempt.Rollback);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenCancelledBetweenPreflightAndPublish_ThenNothingBeginsAndThePassedTokenPropagates(string family)
    {
        // The host cancels after a completed preflight. The publish validates, spends the handle and
        // then observes the token before Begin, so this attempt creates nothing and owes no rollback.
        // Recovery inside the preflight is a separate step, so only the publish's own calls count.
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);
        var handle = run.Preflight(replace: true);
        var mutations = run.Mutations();
        var names = run.Names();
        run.Signals.Cancel();

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(handle));

        Assert.Equal(run.Signals.Token, cancelled.CancellationToken);
        Assert.Equal(mutations, run.Mutations());
        Assert.Equal(names, run.Names());
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        var attempt = handle.Attempt;
        Assert.Equal(PublicationAttemptState.NotBegun, attempt.State);
        Assert.Equal(RollbackOutcome.NotOwed, attempt.Rollback);
        Assert.Null(attempt.RollbackBlocker);
        Assert.Null(attempt.Committed);

        // Observed before Dispose: the publish itself released the replaced target's reference.
        if (OperatingSystem.IsWindows())
        {
            PublicationRun.AssertNoReferenceHeld(run.Target);
        }

        // Spent before the check: a second publish is refused as a second use, not cancelled again.
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.PublishAsync(handle));
        Assert.Same(attempt, handle.Attempt);
        Assert.Equal(mutations, run.Mutations());

        handle.Dispose();
        Assert.Same(attempt, handle.Attempt);
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task Preflight_WhenTheHandleIsDisposedWithoutPublishing_ThenNoRecordStageOrMarkerExists(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);
        var names = run.Names();
        var handle = run.Preflight(replace: true);

        handle.Dispose();

        // Disposal releases; it never stands in for a rollback, and there was nothing to roll back.
        Assert.Empty(run.Mutations());
        Assert.Equal(names, run.Names());
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        Assert.Equal(PublicationAttemptState.NotBegun, handle.Attempt.State);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task Preflight_WhenAReplacedTargetIsHeldAndTheHandleIsDisposed_ThenItsReferenceIsReleased(string family)
    {
        // The positive control: a successful preflight keeps the replaced target's reference until
        // the handle is published or disposed, and the observation sees it held.
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);
        var handle = run.Preflight(replace: true);
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);

        PublicationRun.AssertReferenceHeld(run.Target);

        handle.Dispose();

        // The pending removal completed when the last handle closed, and the name is free.
        Assert.False(File.Exists(run.Target));
        File.WriteAllText(run.Target, "the next occupant");
        Assert.Equal("the next occupant", File.ReadAllText(run.Target));
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task Preflight_WhenRefused_ThenNoHandleExistsAndNoReferenceIsHeld(string family)
    {
        // The refusals that acquire nothing: an occupant without replacement is refused before any
        // reference is taken.
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);

        var error = run.Refused();

        Assert.Equal(HostErrorKind.TargetOccupied, error.Kind);
        Assert.Empty(run.Mutations());
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
        PublicationRun.AssertNoReferenceHeld(run.Target);
    }

    [Theory]
    [InlineData("operands")]
    [InlineData("chain")]
    [InlineData("command line")]
    public async Task Preflight_WhenTheRequestListsChangeAfterwards_ThenTheHeldInputsAreUnchanged(string list)
    {
        // Every collection a request carries is copied when the operation starts, so a caller that
        // mutates or reuses its request cannot retarget a held handle: the single-file preflight's
        // operands and chain, and the convert publish's manifest command line.
        if (list == "command line")
        {
            await AssertCommandLineCopiedAsync();
            return;
        }

        using var run = await PublicationRun.CreateAsync(SpecFile);
        List<string> operands = [run.SpecPath, run.DataPath];
        List<SpecChainFile> chain = [.. run.Run.SpecChain];
        var handle = OutcomeAssert.Completed(SpecPublication.Preflight(
            new SpecDelivery { Target = run.Base, ReplaceExisting = false, Operands = operands, Chain = chain },
            run.Host,
            run.Signals.Token));
        var held = HeldInputs(handle).Select(input => input.Spelling).ToList();

        if (list == "operands")
        {
            operands.Clear();
            operands.Add(run.Target);
        }
        else
        {
            chain.Clear();
        }

        Assert.Equal(held, HeldInputs(handle).Select(input => input.Spelling));
        Assert.Equal([run.SpecPath, run.DataPath, .. run.Run.SpecChain.Select(file => file.Spelling)], held);
        OutcomeAssert.Committed(await run.PublishAsync(handle));
    }

    [Fact]
    public async Task Dispose_WhenCalledTwice_ThenNothingChanges()
    {
        using var run = await PublicationRun.CreateAsync(Convert);
        PublicationRun.Occupy(run.Target);
        var handle = run.Preflight(replace: true);
        var attempt = handle.Attempt;

        handle.Dispose();
        handle.Dispose();

        Assert.Same(attempt, handle.Attempt);
        Assert.Empty(run.Mutations());
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
        PublicationRun.AssertNoReferenceHeld(run.Target);
    }

    // ---- a preflight that throws or refuses after acquiring ---------------------------------------

    [Fact]
    public async Task Preflight_WhenALaterAcquisitionThrowsAfterAnEarlierSucceeded_ThenTheRefusalIsUnchangedAndTheEarlierReferenceIsReleased()
    {
        // The .cxt's reference is taken, then the .dat's acquisition throws. The refusal is today's:
        // the location text with no established cause, because the acquisition is not a tagged
        // boundary; and the .cxt's reference is released before the refusal returns.
        using var run = await PublicationRun.CreateAsync(Convert);
        PublicationRun.Occupy(run.Base + ".cxt");
        PublicationRun.Occupy(run.Base + ".dat");
        run.Files.FailReadKind = "Acquire";
        run.Files.FailReadNamePrefix = "out.dat";
        run.Files.FailReadWith = static () => new IOException("injected acquisition failure");

        var error = run.Refused(replace: true, writesDat: true);

        Assert.Equal(HostErrorKind.PublicationControlFailed, error.Kind);
        Assert.Equal($"cannot start publication for the output base '{run.Base}'.", error.Message);
        Assert.Equal(PublicationCause.Unknown, error.Cause);
        AssertAcquiredInOrder(run, "Acquire:out.cxt", "Acquire:out.dat");
        AssertNothingMutated(run, run.Base + ".cxt", run.Base + ".dat");
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
        PublicationRun.AssertNoReferenceHeld(run.Base + ".cxt");
    }

    [Fact]
    public async Task Preflight_WhenALaterAcquisitionAnswersNothingAfterAnEarlierSucceeded_ThenTheUnprovableRefusalReleasesTheEarlierReference()
    {
        using var run = await PublicationRun.CreateAsync(Convert);
        PublicationRun.Occupy(run.Base + ".cxt");
        PublicationRun.Occupy(run.Base + ".dat");
        run.Files.SuppressReferenceNamePrefix = "out.dat";

        var error = run.Refused(replace: true, writesDat: true);

        Assert.Equal(HostErrorKind.CommitFailed, error.Kind);
        Assert.Equal($"cannot publish the output '{run.Base}.dat'.", error.Message);
        Assert.Equal(1, run.Files.ReferencesSuppressed);
        AssertAcquiredInOrder(run, "Acquire:out.cxt", "Acquire:out.dat");
        AssertNothingMutated(run, run.Base + ".cxt", run.Base + ".dat");
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
        PublicationRun.AssertNoReferenceHeld(run.Base + ".cxt");
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task Preflight_WhenTheReadOnlyQueryRaisesAContractFaultAfterAnAcquisition_ThenTheTaggedFaultPropagatesAndTheReferenceIsReleased(
        string family)
    {
        // The read-only question is a tagged boundary: the contract fault it raises propagates as
        // itself, past the location refusal, exactly as before; the reference taken just before it is
        // released on the way out.
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);
        run.Files.MutateBefore = "ReadOnly:" + Path.GetFileName(run.Target);
        run.Files.Mutate = static () => throw new ObjectDisposedException("publication");

        var fault = Assert.Throws<PublicationFaultException>(() => run.PreflightOutcome(replace: true));

        Assert.IsType<ObjectDisposedException>(fault.InnerException);
        Assert.Equal(1, run.Files.MutationsFired);
        AssertNothingMutated(run, run.Target);
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
        PublicationRun.AssertNoReferenceHeld(run.Target);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task Preflight_WhenCancelledAfterAnAcquisition_ThenTheHostTokenPropagatesAndTheReferenceIsReleased(string family)
    {
        // The same boundary cancels the host's source instead, so the preflight's cancellation check
        // throws for the exact token after the acquisition.
        using var run = await PublicationRun.CreateAsync(family);
        PublicationRun.Occupy(run.Target);
        run.Files.MutateBefore = "ReadOnly:" + Path.GetFileName(run.Target);
        run.Files.Mutate = run.Signals.Cancel;

        var cancelled = Assert.ThrowsAny<OperationCanceledException>(() => run.PreflightOutcome(replace: true));

        Assert.Equal(run.Signals.Token, cancelled.CancellationToken);
        Assert.Equal(1, run.Files.MutationsFired);
        AssertNothingMutated(run, run.Target);
        Assert.SkipUnless(OperatingSystem.IsWindows(), PublicationRun.WindowsOnly);
        PublicationRun.AssertNoReferenceHeld(run.Target);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static void CancelOnceBegun(PublicationRun run)
    {
        run.Files.CancelAfterMoveToPrefix = run.BaseName + ".fcabedrock-transaction-";
        run.Files.CancelAfterMove = run.Signals.Cancel;
    }

    private static void AssertAcquiredInOrder(PublicationRun run, string first, string second)
    {
        var acquisitions = run.Files.Operations
            .Where(operation => operation.StartsWith("Acquire:", StringComparison.Ordinal))
            .ToList();
        Assert.Equal([first, second], acquisitions);
    }

    // No create, rename or removal was recorded, and every occupant still holds its old bytes.
    private static void AssertNothingMutated(PublicationRun run, params string[] occupants)
    {
        Assert.Empty(run.Mutations());
        Assert.All(occupants, path => Assert.Equal(PublicationRun.Old, File.ReadAllText(path)));
        Assert.Empty(run.Residue());
    }

    // The inputs the handle's transaction holds, read through its private fields: the one place a
    // test needs them, and no production member exists to expose them.
    private static IReadOnlyList<PublicationInput> HeldInputs(PreflightedPublication handle)
    {
        var transaction = typeof(PreflightedPublication)
            .GetField("_transaction", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handle)!;
        return (IReadOnlyList<PublicationInput>)typeof(PublicationTransaction)
            .GetField("_inputs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(transaction)!;
    }

    // The publish copies the manifest's command line when it starts: a caller that changes its
    // list while the data is being converted does not change what the manifest records.
    private static async Task AssertCommandLineCopiedAsync()
    {
        List<string> commandLine = ["fcabedrock", "convert"];
        var armed = false;
        using var run = await PublicationRun.CreateAsync(Convert, path =>
        {
            if (armed && path.EndsWith("data.csv", StringComparison.Ordinal))
            {
                commandLine.Add("mutated");
            }

            return File.OpenRead(path);
        });
        armed = true;
        var handle = (PreflightedConvertOutput)run.Preflight();
        var facts = new ManifestHostFacts { ToolVersion = "t", CommandLine = commandLine, UtcNow = run.Facts.UtcNow };

        OutcomeAssert.Completed(await ConvertPublication.PublishAsync(
            run.Conversion, handle, facts, run.Diagnostics, run.Signals.Token));

        Assert.Contains("mutated", commandLine);
        var manifest = File.ReadAllText(run.Base + ".manifest.toml");
        Assert.Contains("\"convert\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("mutated", manifest, StringComparison.Ordinal);
    }
}
