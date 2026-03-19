// Transaction Ingestion Console Application
// 
// This application reads transaction feed data from a JSON file and performs idempotent upsert operations
// into a SQLite database. It tracks field-level changes via an audit trail and supports transaction revocation
// and finalization based on time windows and snapshot presence.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TransactionsIngest.Data;
using TransactionsIngest.Services;
using TransactionsIngest.Services.Interfaces;

// Build the dependency injection host and configure services
var host = Host.CreateDefaultBuilder(args)
    // Load application configuration from appsettings.json
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
    })
    // Register services with dependency injection
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        
        // Database context using SQLite
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Default") ?? "Data Source=transactions.db"));

        // Register core services: time provider is singleton (stateless), others are scoped for transaction safety
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();
        services.AddSingleton<CardPrivacyService>();
        services.AddScoped<IFeedLoader, FileFeedLoader>();
        services.AddScoped<IIngestService, IngestService>();
        services.AddScoped<AppRunner>();
    })
    // Configure logging to output application messages only (suppress EF Core SQL command logs)
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
        logging.AddSimpleConsole(options => { options.IncludeScopes = false; });
        
        // Suppress verbose Microsoft framework logs
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    })
    .Build();

// Run the application in a service scope
using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;
var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TransactionsIngest");
var runner = services.GetRequiredService<AppRunner>();

// Execute the single-run ingestion job with error handling
try
{
    await runner.RunAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "Application run failed.");
    throw;
}
