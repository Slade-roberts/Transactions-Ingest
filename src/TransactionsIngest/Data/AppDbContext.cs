using Microsoft.EntityFrameworkCore;
using TransactionsIngest.Models;

namespace TransactionsIngest.Data;

/// <summary>
/// Entity Framework Core DbContext for the transaction ingestion database.
/// </summary>
/// <remarks>
/// Configures the SQLite database schema with proper constraints, relationships, and indexes.
/// Supports code-first migrations and ensures data integrity through foreign key cascading and field validation.
/// </remarks>
public class AppDbContext : DbContext
{
    /// <summary>Initializes a new instance of the AppDbContext with provided EF Core options.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>DbSet for accessing Transaction entities.</summary>
    public DbSet<Transaction> Transactions { get; set; } = null!;
    
    /// <summary>DbSet for accessing TransactionAudit entities (change history).</summary>
    public DbSet<TransactionAudit> TransactionAudits { get; set; } = null!;

    /// <summary>
    /// Configures database schema, relationships, constraints, and indexes.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Transaction entity: primary key, field constraints, indexes
        modelBuilder.Entity<Transaction>(b =>
        {
            b.HasKey(t => t.TransactionId);
            b.Property(t => t.CardLast4).HasMaxLength(4).IsRequired();          // Last 4 digits (PII masked)
            b.Property(t => t.CardHash).HasMaxLength(128);                       // Optional SHA-256 hash (hex string)
            b.Property(t => t.LocationCode).HasMaxLength(20).IsRequired();       // Location code
            b.Property(t => t.ProductName).HasMaxLength(20).IsRequired();        // Product name
            b.Property(t => t.Amount).HasPrecision(18, 2);                       // Currency: 18 digits, 2 decimals
            b.Property(t => t.TransactionTime).IsRequired();                     // Transaction timestamp
            b.HasIndex(t => t.TransactionTime);                                  // Index for time-based queries (24h window)
            b.Property(t => t.Status).HasConversion<int>();                     // Store enum as integer
        });

        // Configure TransactionAudit entity: cascade delete on parent transaction removal
        modelBuilder.Entity<TransactionAudit>(b =>
        {
            b.HasKey(a => a.Id);
            // When a Transaction is deleted, all related audits are automatically deleted
            b.HasOne(a => a.Transaction).WithMany(t => t.Audits).HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Cascade);
            b.Property(a => a.ChangedAt).IsRequired();                           // Audit timestamp
        });
    }
}
