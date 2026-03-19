using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransactionsIngest.Models;
using TransactionsIngest.Services.Interfaces;

namespace TransactionsIngest.Services;

/// <summary>
/// Loads transaction data from a JSON file feed.
/// </summary>
/// <remarks>
/// Implements resilient path resolution to support multiple deployment scenarios:
/// - Relative paths (for console app output directories)
/// - Absolute paths (for full file system references)
/// - Fallback resolution from AppContext.BaseDirectory
/// 
/// Gracefully handles missing files by returning an empty list (idempotent behavior).
/// </remarks>
public class FileFeedLoader : IFeedLoader
{
    private readonly string _configuredPath;
    private readonly ILogger<FileFeedLoader> _logger;

    /// <summary>
    /// Initializes the loader with the configured feed file path.
    /// </summary>
    /// <param name="configuration">Configuration object containing Feed:Path setting.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public FileFeedLoader(IConfiguration configuration, ILogger<FileFeedLoader> logger)
    {
        _configuredPath = configuration.GetValue<string>("Feed:Path") ?? "mock-data.json";
        _logger = logger;
    }

    /// <summary>
    /// Loads transaction data from the configured JSON file.
    /// </summary>
    /// <returns>Deserialized list of TransactionDtos; empty list if file not found or empty.</returns>
    public async Task<List<TransactionDto>> LoadAsync()
    {
        var path = ResolvePath(_configuredPath);
        _logger.LogInformation("Loading feed from {path}", path);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Feed file not found at {path}", path);
            return new List<TransactionDto>();
        }

        var content = await File.ReadAllTextAsync(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var items = JsonSerializer.Deserialize<List<TransactionDto>>(content, options) ?? new List<TransactionDto>();
        return items;
    }

    /// <summary>
    /// Resolves the feed file path with multiple fallback strategies.
    /// </summary>
    /// <remarks>
    /// 1. Returns absolute paths as-is
    /// 2. Tries relative path from AppContext.BaseDirectory (output directory after publish)
    /// 3. Falls back to relative path from current directory
    /// </remarks>
    private static string ResolvePath(string configuredPath)
    {
        // Already absolute - use directly
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        // Try output directory first (published app)
        var outputDirCandidate = Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (File.Exists(outputDirCandidate))
            return outputDirCandidate;

        // Fall back to current directory
        return Path.GetFullPath(configuredPath);
    }
}
