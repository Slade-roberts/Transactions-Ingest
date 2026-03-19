using TransactionsIngest.Services.Interfaces;

namespace TransactionsIngest.Services;

/// <summary>
/// Production implementation of ITimeProvider using system clock.
/// </summary>
/// <remarks>
/// Returns the current UTC time from the system. In tests, a mock implementation with a fixed time is used for determinism.
/// </remarks>
public class SystemTimeProvider : ITimeProvider
{
    /// <summary>Gets the current UTC timestamp from the system clock.</summary>
    public DateTime UtcNow => DateTime.UtcNow;
}
