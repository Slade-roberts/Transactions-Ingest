using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TransactionsIngest.Data;
using TransactionsIngest.Services;
using TransactionsIngest.Services.Interfaces;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Default") ?? "Data Source=transactions.db"));

        services.AddSingleton<ITimeProvider, SystemTimeProvider>();
        services.AddSingleton<CardPrivacyService>();
        services.AddScoped<IFeedLoader, FileFeedLoader>();
        services.AddScoped<IIngestService, IngestService>();
        services.AddScoped<AppRunner>();
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
        logging.AddSimpleConsole(options => { options.IncludeScopes = false; });
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    })
    .Build();

using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;
var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TransactionsIngest");
var runner = services.GetRequiredService<AppRunner>();
try
{
    await runner.RunAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "Application run failed.");
    throw;
}
