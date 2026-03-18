using Microsoft.EntityFrameworkCore;
using TransactionsIngest.Models;

namespace TransactionsIngest.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions { get; set; } = null!;
    public DbSet<TransactionAudit> TransactionAudits { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(b =>
        {
            b.HasKey(t => t.TransactionId);
            b.Property(t => t.CardLast4).HasMaxLength(4).IsRequired();
            b.Property(t => t.CardHash).HasMaxLength(128);
            b.Property(t => t.LocationCode).HasMaxLength(20).IsRequired();
            b.Property(t => t.ProductName).HasMaxLength(20).IsRequired();
            b.Property(t => t.Amount).HasPrecision(18, 2);
            b.Property(t => t.TransactionTime).IsRequired();
            b.HasIndex(t => t.TransactionTime);
            b.Property(t => t.Status).HasConversion<int>();
        });

        modelBuilder.Entity<TransactionAudit>(b =>
        {
            b.HasKey(a => a.Id);
            b.HasOne(a => a.Transaction).WithMany(t => t.Audits).HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Cascade);
            b.Property(a => a.ChangedAt).IsRequired();
        });
    }
}
