using Microsoft.Extensions.Logging;
using TransactionsIngest.Data;
using TransactionsIngest.Services.Interfaces;

namespace TransactionsIngest.Services;

/// <summary>
/// Orchestrates the application lifecycle: database initialization and ingestion run.
/// </summary>
/// <remarks>
/// Single entry point for the console application. Ensures the database schema is created before
/// attempting ingestion (idempotent, does nothing if schema already exists).
/// </remarks>
public class AppRunner
{
    private readonly AppDbContext _db;
    private readonly IIngestService _ingest;
    private readonly ILogger<AppRunner> _logger;

    /// <summary>
    /// Initializes the runner with required services.
    /// </summary>
    public AppRunner(
        AppDbContext db,
        IIngestService ingest,
        ILogger<AppRunner> logger)
    {
        _db = db;
        _ingest = ingest;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full application lifecycle: ensure DB created, then run ingestion.
    /// </summary>
    public async Task RunAsync()
    {
        // Create SQLite database and schema if they don't exist (idempotent)
        await _db.Database.EnsureCreatedAsync();

        _logger.LogInformation("Starting ingestion run...");
        await _ingest.RunAsync();
        _logger.LogInformation("Ingestion completed successfully.");
    }
}
