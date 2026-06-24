using System.Diagnostics.CodeAnalysis;

namespace FcaBedrock.Diagnostics;

/// <summary>
/// The project-standard carrier for operations that aggregate diagnostics — a
/// value plus every diagnostic produced, not just the first (principle P-13).
/// Used by validation and planning so callers can surface all problems at once.
/// </summary>
public readonly struct Diagnosed<T>
{
    /// <summary>Creates a result carrying a value and its accompanying diagnostics.</summary>
    public Diagnosed(T? value, IReadOnlyList<BedrockDiagnostic> diagnostics)
    {
        Value = value;
        Diagnostics = diagnostics;
    }

    /// <summary>The produced value. May be absent when <see cref="HasErrors"/>.</summary>
    public T? Value { get; }

    /// <summary>Every diagnostic produced, in production order.</summary>
    public IReadOnlyList<BedrockDiagnostic> Diagnostics { get; }

    /// <summary>True if any diagnostic is <see cref="DiagnosticSeverity.Error"/> or worse.</summary>
    public bool HasErrors
    {
        get
        {
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>True when no error-or-worse diagnostic is present.</summary>
    public bool IsOk => !HasErrors;

    /// <summary>A successful result with no diagnostics.</summary>
    public static Diagnosed<T> Ok(T value) => new(value, Array.Empty<BedrockDiagnostic>());

    /// <summary>A result carrying a value alongside (typically non-error) diagnostics.</summary>
    public static Diagnosed<T> Ok(T value, IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        new(value, diagnostics);

    /// <summary>A failed result: no value, carrying the diagnostics that explain why.</summary>
    public static Diagnosed<T> Failed(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        new(default, diagnostics);

    /// <summary>Exposes the value only when present and error-free.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        if (IsOk && Value is not null)
        {
            value = Value;
            return true;
        }

        value = default;
        return false;
    }
}
