namespace TransactionsIngest.Services.Interfaces;

/// <summary>
/// Abstracts the source of the current time (testability layer).
/// </summary>
/// <remarks>
/// Allows tests to inject a fixed time, making timestamp-dependent business logic deterministic and testable.
/// </remarks>
public interface ITimeProvider
{
    /// <summary>Gets the current UTC timestamp.</summary>
    DateTime UtcNow { get; }
}
