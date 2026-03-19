using TransactionsIngest.Models;

namespace TransactionsIngest.Services.Interfaces;

/// <summary>
/// Abstracts the loading of transaction data from external feed sources.
/// </summary>
/// <remarks>
/// Enables testability and supports multiple feed formats (JSON files, APIs, etc) through different implementations.
/// </remarks>
public interface IFeedLoader
{
    /// <summary>
    /// Loads and returns a list of transaction DTOs from the configured feed source.
    /// </summary>
    /// <returns>List of transaction DTOs; empty list if feed is unavailable or empty.</returns>
    Task<List<TransactionDto>> LoadAsync();
}
