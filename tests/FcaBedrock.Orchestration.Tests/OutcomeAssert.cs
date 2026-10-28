using FcaBedrock.Diagnostics;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The one place the tests apply the completion rule of the coordinator's results (D-140 part 4):
/// an error result is the host failure, a produced value is completion whatever severities its list
/// holds, and no value is the diagnostic failure. Completion is decided by value presence, not by
/// <c>IsOk</c>, <c>HasErrors</c> or <c>TryGetValue</c> of the inner carrier.
/// </summary>
internal static class OutcomeAssert
{
    /// <summary>A mixed operation completed: its value and its complete list.</summary>
    public static (T Value, IReadOnlyList<BedrockDiagnostic> Diagnostics) Completed<T>(Result<Diagnosed<T>, HostFailure> outcome)
        where T : class
    {
        Assert.False(outcome.IsError, outcome.IsError ? $"a host failure: {Describe(outcome.Error.Error)}" : null);
        return Completed(outcome.Value);
    }

    /// <summary>A mixed operation's diagnostic failure: no value, inside an ok result; its complete list.</summary>
    public static IReadOnlyList<BedrockDiagnostic> Failed<T>(Result<Diagnosed<T>, HostFailure> outcome)
        where T : class
    {
        Assert.False(outcome.IsError, outcome.IsError ? $"a host failure: {Describe(outcome.Error.Error)}" : null);
        Assert.Null(outcome.Value.Value);
        return outcome.Value.Diagnostics;
    }

    /// <summary>A mixed operation failed on the host: the failure and the list collected so far.</summary>
    public static HostFailure HostFailed<T>(Result<Diagnosed<T>, HostFailure> outcome)
        where T : class
    {
        Assert.True(outcome.IsError, "the operation did not fail on the host.");
        return outcome.Error;
    }

    /// <summary>A pure operation completed: its value and its complete list.</summary>
    public static (T Value, IReadOnlyList<BedrockDiagnostic> Diagnostics) Completed<T>(Diagnosed<T> outcome)
        where T : class
    {
        var value = outcome.Value;
        Assert.True(value is not null, $"no value: {string.Join("; ", outcome.Diagnostics.Select(Describe))}");
        return (value, outcome.Diagnostics);
    }

    /// <summary>A host-only operation completed: its value.</summary>
    public static T Completed<T>(Result<T, HostError> outcome)
    {
        Assert.False(outcome.IsError, outcome.IsError ? $"a host failure: {Describe(outcome.Error)}" : null);
        return outcome.Value;
    }

    /// <summary>A host-only operation failed on the host: its error.</summary>
    public static HostError HostFailed<T>(Result<T, HostError> outcome)
    {
        Assert.True(outcome.IsError, "the operation did not fail on the host.");
        return outcome.Error;
    }

    /// <summary>
    /// A publish of either family, boxed by a helper that serves both, completed: the committed record.
    /// </summary>
    public static Published Committed(object outcome) => outcome switch
    {
        Result<Diagnosed<Published>, HostFailure> convert => Completed(convert).Value,
        Result<Published, HostError> spec => Completed(spec),
        _ => throw new InvalidOperationException($"not a publish result: {outcome.GetType().Name}"),
    };

    /// <summary>A publish of either family, boxed, failed on the host: its error.</summary>
    public static HostError PublishHostFailed(object outcome) => outcome switch
    {
        Result<Diagnosed<Published>, HostFailure> convert => HostFailed(convert).Error,
        Result<Published, HostError> spec => HostFailed(spec),
        _ => throw new InvalidOperationException($"not a publish result: {outcome.GetType().Name}"),
    };

    private static string Describe(HostError error) => $"{error.Kind} {error.Message}";

    private static string Describe(BedrockDiagnostic diagnostic) =>
        $"{diagnostic.Severity} {diagnostic.Code} {diagnostic.Message}";
}
