using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TransactionsIngest.Data;
using TransactionsIngest.Models;
using TransactionsIngest.Services;
using TransactionsIngest.Services.Interfaces;
using Xunit;

namespace TransactionsIngest.Tests;

public class IngestTests
{
    private AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private IConfiguration CreateConfig(bool hashCards = false, bool finalize = false)
    {
        var dict = new Dictionary<string, string?>
        {
            ["CardHash:Enabled"] = hashCards.ToString(),
            ["Ingest:FinalizeOlderThan24Hours"] = finalize.ToString()
        };

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    class TestFeedLoader : IFeedLoader
    {
        private readonly List<TransactionDto> _items;
        public TestFeedLoader(IEnumerable<TransactionDto> items) => _items = items.ToList();
        public Task<List<TransactionDto>> LoadAsync() => Task.FromResult(_items);
    }

    class TestTimeProvider : ITimeProvider
    {
        public TestTimeProvider(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; set; }
    }

    [Fact]
    public async Task InsertNewTransaction_CreatesRowAndAudits()
    {
        var now = DateTime.UtcNow;
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var ctx = CreateContext(connection);
        var dto = new TransactionDto
        {
            TransactionId = 9001,
            CardNumber = "4242424242424242",
            LocationCode = "TST1",
            ProductName = "TestItem",
            Amount = 12.34m,
            TransactionTime = now
        };

        var config = CreateConfig(hashCards: false);
        var card = new CardPrivacyService(config);
        var feed = new TestFeedLoader(new[] { dto });
        var time = new TestTimeProvider(now);

        var svc = new IngestService(ctx, feed, time, config, card, NullLogger<IngestService>.Instance);
        await svc.RunAsync();

        var stored = ctx.Transactions.Single();
        Assert.Equal(dto.TransactionId, stored.TransactionId);
        Assert.Equal("4242", stored.CardLast4);

        var audits = ctx.TransactionAudits.ToList();
        Assert.True(audits.Count >= 1);
    }

    [Fact]
    public async Task Idempotent_MultipleRuns_NoDuplicates()
    {
        var now = DateTime.UtcNow;
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var ctx = CreateContext(connection);
        var dto = new TransactionDto
        {
            TransactionId = 9002,
            CardNumber = "4000000000000002",
            LocationCode = "TST2",
            ProductName = "Item2",
            Amount = 5.00m,
            TransactionTime = now
        };

        var config = CreateConfig(hashCards: false);
        var card = new CardPrivacyService(config);
        var feed = new TestFeedLoader(new[] { dto });
        var time = new TestTimeProvider(now);

        var svc = new IngestService(ctx, feed, time, config, card, NullLogger<IngestService>.Instance);
        await svc.RunAsync();
        var countsAfterFirst = (await ctx.Transactions.ToListAsync()).Count;
        var auditsAfterFirst = (await ctx.TransactionAudits.ToListAsync()).Count;

        // Run again with identical input
        await svc.RunAsync();
        var countsAfterSecond = (await ctx.Transactions.ToListAsync()).Count;
        var auditsAfterSecond = (await ctx.TransactionAudits.ToListAsync()).Count;

        Assert.Equal(countsAfterFirst, countsAfterSecond);
        Assert.Equal(auditsAfterFirst, auditsAfterSecond);
    }

    [Fact]
    public async Task Update_RecordsFieldChangeAndIsIdempotent()
    {
        var now = DateTime.UtcNow;
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var ctx = CreateContext(connection);
        // seed existing
        var seed = new Transaction
        {
            TransactionId = 9003,
            CardLast4 = "1111",
            LocationCode = "SEED",
            ProductName = "Old",
            Amount = 1.00m,
            TransactionTime = now.AddHours(-1),
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1),
            Status = TransactionStatus.Active
        };
        ctx.Transactions.Add(seed);
        await ctx.SaveChangesAsync();

        var dto = new TransactionDto
        {
            TransactionId = 9003,
            CardNumber = "0000000000001111",
            LocationCode = "SEED",
            ProductName = "Old",
            Amount = 2.50m,
            TransactionTime = now.AddHours(-1)
        };

        var config = CreateConfig(hashCards: false);
        var card = new CardPrivacyService(config);
        var feed = new TestFeedLoader(new[] { dto });
        var time = new TestTimeProvider(now);

        var svc = new IngestService(ctx, feed, time, config, card, NullLogger<IngestService>.Instance);
        await svc.RunAsync();

        var auditsAfterFirst = await ctx.TransactionAudits.Where(a => a.TransactionId == 9003 && a.FieldName == nameof(Transaction.Amount)).ToListAsync();
        Assert.Single(auditsAfterFirst);

        // Run again - should be idempotent
        await svc.RunAsync();
        var auditsAfterSecond = await ctx.TransactionAudits.Where(a => a.TransactionId == 9003 && a.FieldName == nameof(Transaction.Amount)).ToListAsync();
        Assert.Single(auditsAfterSecond);
    }

    [Fact]
    public async Task Finalized_IsNotUpdated()
    {
        var now = DateTime.UtcNow;
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var ctx = CreateContext(connection);
        var seed = new Transaction
        {
            TransactionId = 9004,
            CardLast4 = "2222",
            LocationCode = "FINAL",
            ProductName = "Locked",
            Amount = 10.00m,
            TransactionTime = now.AddDays(-2),
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddDays(-2),
            Status = TransactionStatus.Finalized
        };
        ctx.Transactions.Add(seed);
        await ctx.SaveChangesAsync();

        var dto = new TransactionDto
        {
            TransactionId = 9004,
            CardNumber = "3333333333332222",
            LocationCode = "FINAL",
            ProductName = "Changed",
            Amount = 11.00m,
            TransactionTime = now.AddDays(-2)
        };

        var config = CreateConfig(hashCards: false);
        var card = new CardPrivacyService(config);
        var feed = new TestFeedLoader(new[] { dto });
        var time = new TestTimeProvider(now);

        var svc = new IngestService(ctx, feed, time, config, card, NullLogger<IngestService>.Instance);
        await svc.RunAsync();

        var stored = await ctx.Transactions.FindAsync(9004);
        Assert.Equal("Locked", stored!.ProductName);
        var audits = await ctx.TransactionAudits.Where(a => a.TransactionId == 9004).ToListAsync();
        Assert.Empty(audits);
    }

    [Fact]
    public async Task Revoked_WhenMissingFromSnapshotAndWithin24Hours()
    {
        var now = DateTime.UtcNow;
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var ctx = CreateContext(connection);
        var seed = new Transaction
        {
            TransactionId = 9005,
            CardLast4 = "4444",
            LocationCode = "REV",
            ProductName = "ToBeRevoked",
            Amount = 4.00m,
            TransactionTime = now.AddHours(-1),
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1),
            Status = TransactionStatus.Active
        };
        ctx.Transactions.Add(seed);
        await ctx.SaveChangesAsync();

        // Empty snapshot - will cause revocation of the seeded transaction
        var feed = new TestFeedLoader(new TransactionDto[] { });
        var config = CreateConfig(hashCards: false);
        var card = new CardPrivacyService(config);
        var time = new TestTimeProvider(now);

        var svc = new IngestService(ctx, feed, time, config, card, NullLogger<IngestService>.Instance);
        await svc.RunAsync();

        var stored = await ctx.Transactions.FindAsync(9005);
        Assert.Equal(TransactionStatus.Revoked, stored!.Status);
        var audits = await ctx.TransactionAudits.Where(a => a.TransactionId == 9005 && a.FieldName == nameof(Transaction.Status)).ToListAsync();
        Assert.Single(audits);
    }
}
