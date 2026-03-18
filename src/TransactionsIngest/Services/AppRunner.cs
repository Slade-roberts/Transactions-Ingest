using Microsoft.Extensions.Logging;
using TransactionsIngest.Data;
using TransactionsIngest.Services.Interfaces;

namespace TransactionsIngest.Services;

public class AppRunner
{
    private readonly AppDbContext _db;
    private readonly IIngestService _ingest;
    private readonly ILogger<AppRunner> _logger;

    public AppRunner(
        AppDbContext db,
        IIngestService ingest,
        ILogger<AppRunner> logger)
    {
        _db = db;
        _ingest = ingest;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        await _db.Database.EnsureCreatedAsync();

        _logger.LogInformation("Starting ingestion run...");
        await _ingest.RunAsync();
        _logger.LogInformation("Ingestion completed successfully.");
    }
}
