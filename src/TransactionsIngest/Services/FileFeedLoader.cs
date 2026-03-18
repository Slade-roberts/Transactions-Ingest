using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransactionsIngest.Models;
using TransactionsIngest.Services.Interfaces;

namespace TransactionsIngest.Services;

public class FileFeedLoader : IFeedLoader
{
    private readonly string _configuredPath;
    private readonly ILogger<FileFeedLoader> _logger;

    public FileFeedLoader(IConfiguration configuration, ILogger<FileFeedLoader> logger)
    {
        _configuredPath = configuration.GetValue<string>("Feed:Path") ?? "mock-data.json";
        _logger = logger;
    }

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

    private static string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        var outputDirCandidate = Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (File.Exists(outputDirCandidate))
            return outputDirCandidate;

        return Path.GetFullPath(configuredPath);
    }
}
