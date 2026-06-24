using System.Diagnostics.CodeAnalysis;

namespace FcaBedrock.Diagnostics;

/// <summary>
/// The project-standard result type for single-error operations (principle P-5):
/// a value of <typeparamref name="T"/> or an error of <typeparamref name="TError"/>,
/// never both. A <c>readonly struct</c>, so <see cref="Ok"/>/<see cref="Err"/>
/// allocate nothing.
/// </summary>
/// <remarks>
/// For the domain, <typeparamref name="TError"/> is <see cref="BedrockDiagnostic"/>;
/// call sites use <c>Result&lt;T, BedrockDiagnostic&gt;</c> directly. There is no
/// separate <c>BedrockResult&lt;T&gt;</c> alias — C# cannot alias a partly-closed
/// generic, and the wrapper would carry no runtime benefit (decisions.md D-006).
/// Operations that aggregate several diagnostics use <see cref="Diagnosed{T}"/> instead.
/// </remarks>
public readonly struct Result<T, TError>
{
    private readonly T _value;
    private readonly TError _error;

    private Result(bool isOk, T value, TError error)
    {
        IsOk = isOk;
        _value = value;
        _error = error;
    }

    /// <summary>True when this holds a value.</summary>
    public bool IsOk { get; }

    /// <summary>True when this holds an error.</summary>
    public bool IsError => !IsOk;

    /// <summary>The value. Throws if this is an error (a programmer error — P-13).</summary>
    public T Value =>
        IsOk ? _value : throw new InvalidOperationException("Cannot read Value of an error Result.");

    /// <summary>The error. Throws if this is ok (a programmer error — P-13).</summary>
    public TError Error =>
        IsOk ? throw new InvalidOperationException("Cannot read Error of an ok Result.") : _error;

    /// <summary>Creates a successful result.</summary>
    public static Result<T, TError> Ok(T value) => new(true, value, default!);

    /// <summary>Creates a failed result.</summary>
    public static Result<T, TError> Err(TError error) => new(false, default!, error);

    /// <summary>Exposes the value without throwing when this is an error.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = IsOk ? _value : default;
        return IsOk;
    }

    /// <summary>Transforms the value, preserving an error unchanged.</summary>
    public Result<TNext, TError> Map<TNext>(Func<T, TNext> map) =>
        IsOk ? Result<TNext, TError>.Ok(map(_value)) : Result<TNext, TError>.Err(_error);

    /// <summary>Chains a result-producing step, short-circuiting on an error.</summary>
    public Result<TNext, TError> Bind<TNext>(Func<T, Result<TNext, TError>> bind) =>
        IsOk ? bind(_value) : Result<TNext, TError>.Err(_error);

    /// <summary>Collapses both cases to a single value.</summary>
    public TResult Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> error) =>
        IsOk ? ok(_value) : error(_error);
}
