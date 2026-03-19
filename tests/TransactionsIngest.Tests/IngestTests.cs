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

/// <summary>
/// Integration tests for <see cref="IngestService"/> using isolated in-memory SQLite databases.
/// </summary>
/// <remarks>
/// Each test opens its own <see cref="SqliteConnection"/> so tests cannot share state.
/// <see cref="TestFeedLoader"/> and <see cref="TestTimeProvider"/> stub out all external dependencies.
/// </remarks>
public class IngestTests
{
    /// <summary>Creates an <see cref="AppDbContext"/> over the given in-memory connection and ensures the schema exists.</summary>
    private AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    /// <summary>Builds an in-memory <see cref="IConfiguration"/> with optional card-hash and finalization feature flags.</summary>
    private IConfiguration CreateConfig(bool hashCards = false, bool finalize = false)
    {
        var dict = new Dictionary<string, string?>
        {
            ["CardHash:Enabled"] = hashCards.ToString(),
            ["Ingest:FinalizeOlderThan24Hours"] = finalize.ToString()
        };

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    /// <summary>Stub <see cref="IFeedLoader"/> that returns a fixed list of DTOs provided at construction time.</summary>
    class TestFeedLoader : IFeedLoader
    {
        private readonly List<TransactionDto> _items;
        public TestFeedLoader(IEnumerable<TransactionDto> items) => _items = items.ToList();
        public Task<List<TransactionDto>> LoadAsync() => Task.FromResult(_items);
    }

    /// <summary>Stub <see cref="ITimeProvider"/> with a directly settable clock for deterministic time-based assertions.</summary>
    class TestTimeProvider : ITimeProvider
    {
        public TestTimeProvider(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; set; }
    }

    /// <summary>
    /// A brand-new transaction in the feed snapshot should be inserted with card masking applied
    /// and at least one initial field-level audit record written.
    /// </summary>
    [Fact]
    public async Task InsertNewTransaction_CreatesRowAndAudits()
    {
        // Arrange
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

        // Act
        await svc.RunAsync();

        // Assert
        var stored = ctx.Transactions.Single();
        Assert.Equal(dto.TransactionId, stored.TransactionId);
        Assert.Equal("4242", stored.CardLast4); // full card number must be masked to last 4 digits only

        var audits = ctx.TransactionAudits.ToList();
        Assert.True(audits.Count >= 1); // at least one audit record must exist for the initial insert
    }

    /// <summary>
    /// Running ingestion twice with identical feed data must not create duplicate rows or additional
    /// audit entries — the operation must be fully idempotent.
    /// </summary>
    [Fact]
    public async Task Idempotent_MultipleRuns_NoDuplicates()
    {
        // Arrange
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

        // Act: first run inserts the transaction
        await svc.RunAsync();
        var countsAfterFirst = (await ctx.Transactions.ToListAsync()).Count;
        var auditsAfterFirst = (await ctx.TransactionAudits.ToListAsync()).Count;

        // Act: second run with identical feed — values are unchanged so nothing should differ
        await svc.RunAsync();
        var countsAfterSecond = (await ctx.Transactions.ToListAsync()).Count;
        var auditsAfterSecond = (await ctx.TransactionAudits.ToListAsync()).Count;

        // Assert: row and audit counts are stable across runs
        Assert.Equal(countsAfterFirst, countsAfterSecond);
        Assert.Equal(auditsAfterFirst, auditsAfterSecond);
    }

    /// <summary>
    /// When a tracked field (Amount) changes between runs, exactly one audit record should be
    /// created on first detection. A subsequent identical run must not add a duplicate.
    /// </summary>
    [Fact]
    public async Task Update_RecordsFieldChangeAndIsIdempotent()
    {
        // Arrange: pre-seed transaction with Amount = 1.00; feed will supply Amount = 2.50
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

        // Act: first run detects the Amount change and writes an audit entry
        await svc.RunAsync();

        // Assert: exactly one Amount audit record after the first run
        var auditsAfterFirst = await ctx.TransactionAudits.Where(a => a.TransactionId == 9003 && a.FieldName == nameof(Transaction.Amount)).ToListAsync();
        Assert.Single(auditsAfterFirst);

        // Act: second run — stored value now matches feed so no change should be detected
        await svc.RunAsync();

        // Assert: still exactly one audit record — no duplicate written on the re-run
        var auditsAfterSecond = await ctx.TransactionAudits.Where(a => a.TransactionId == 9003 && a.FieldName == nameof(Transaction.Amount)).ToListAsync();
        Assert.Single(auditsAfterSecond);
    }

    /// <summary>
    /// A finalized transaction is completely locked: even when the feed supplies different field
    /// values, no updates should be applied and no audit records should be created.
    /// </summary>
    [Fact]
    public async Task Finalized_IsNotUpdated()
    {
        // Arrange: seed a finalized transaction; the feed will attempt to change ProductName and Amount
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

        // Act
        await svc.RunAsync();

        // Assert: finalized record must be unchanged and produce no audit entries
        var stored = await ctx.Transactions.FindAsync(9004);
        Assert.Equal("Locked", stored!.ProductName); // ProductName must not have been overwritten
        var audits = await ctx.TransactionAudits.Where(a => a.TransactionId == 9004).ToListAsync();
        Assert.Empty(audits); // no audit rows must exist for a skipped finalized transaction
    }

    /// <summary>
    /// An active transaction that is absent from the current snapshot but whose TransactionTime
    /// is within the last 24 hours must be set to Revoked with a Status audit record created.
    /// </summary>
    [Fact]
    public async Task Revoked_WhenMissingFromSnapshotAndWithin24Hours()
    {
        // Arrange: seed an active transaction within the 24h window; run with an empty snapshot
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

        // Empty snapshot triggers revocation of any in-window active transaction not present in the feed
        var feed = new TestFeedLoader(new TransactionDto[] { });
        var config = CreateConfig(hashCards: false);
        var card = new CardPrivacyService(config);
        var time = new TestTimeProvider(now);

        var svc = new IngestService(ctx, feed, time, config, card, NullLogger<IngestService>.Instance);

        // Act
        await svc.RunAsync();

        // Assert: transaction status changed to Revoked and one Status audit entry was written
        var stored = await ctx.Transactions.FindAsync(9005);
        Assert.Equal(TransactionStatus.Revoked, stored!.Status);
        var audits = await ctx.TransactionAudits.Where(a => a.TransactionId == 9005 && a.FieldName == nameof(Transaction.Status)).ToListAsync();
        Assert.Single(audits); // exactly one Status audit: "Active" → "Revoked"
    }
}
