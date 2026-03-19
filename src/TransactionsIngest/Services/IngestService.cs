using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransactionsIngest.Data;
using TransactionsIngest.Models;
using TransactionsIngest.Services.Interfaces;

namespace TransactionsIngest.Services;

/// <summary>
/// Core transaction ingestion service implementing idempotent upsert with field-level auditing.
/// </summary>
/// <remarks>
/// Orchestrates the complete ingestion workflow:
/// 1. Load transaction snapshot from feed
/// 2. For each transaction in snapshot: insert new records with initial audit trail or update existing
/// 3. Revoke transactions within 24-hour window that are absent from snapshot
/// 4. Optionally finalize records older than 24 hours (immutable thereafter)
/// 5. All operations wrapped in a single database transaction for ACID consistency
/// </remarks>
public class IngestService : IIngestService
{
    private readonly AppDbContext _db;
    private readonly IFeedLoader _feedLoader;
    private readonly ITimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly CardPrivacyService _cardPrivacy;
    private readonly ILogger<IngestService> _logger;

    /// <summary>Initializes the service with required dependencies.</summary>
    public IngestService(
        AppDbContext db,
        IFeedLoader feedLoader,
        ITimeProvider timeProvider,
        IConfiguration configuration,
        CardPrivacyService cardPrivacy,
        ILogger<IngestService> logger)
    {
        _db = db;
        _feedLoader = feedLoader;
        _timeProvider = timeProvider;
        _configuration = configuration;
        _cardPrivacy = cardPrivacy;
        _logger = logger;
    }

    /// <summary>Executes the complete idempotent transaction ingestion workflow.</summary>
    /// <remarks>Runs upsert, revocation, and optional finalization in a single atomic transaction.</remarks>
    public async Task RunAsync()
    {
        var now = _timeProvider.UtcNow;
        var snapshot = (await _feedLoader.LoadAsync()).ToList();
        _logger.LogInformation("Loaded {count} items from feed", snapshot.Count);

        var snapshotIds = snapshot.Select(x => x.TransactionId).ToHashSet();
        var last24 = now.AddHours(-24);

        // Wrap the entire ingestion run in a database transaction
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Load existing transactions that we might touch
            var existing = await _db.Transactions
                .Where(t => snapshotIds.Contains(t.TransactionId) || t.TransactionTime >= last24)
                .ToListAsync();

            var existingMap = existing.ToDictionary(t => t.TransactionId);

            foreach (var dto in snapshot)
            {
                // Normalize incoming time to UTC
                var txnTime = NormalizeToUtc(dto.TransactionTime);

                var (last4, hash) = _cardPrivacy.ProcessCard(dto.CardNumber);

                if (existingMap.TryGetValue(dto.TransactionId, out var existingTxn))
                {
                    if (existingTxn.Status == TransactionStatus.Finalized)
                    {
                        _logger.LogDebug("Skipping finalized transaction {id}", dto.TransactionId);
                        continue;
                    }

                    var changed = false;

                    // Diff each tracked field against the incoming value; CompareAndUpdate writes an
                    // audit entry and applies the update only if the stringified values differ
                    changed |= CompareAndUpdate(existingTxn, nameof(Transaction.CardLast4), existingTxn.CardLast4, last4, v => existingTxn.CardLast4 = v);
                    changed |= CompareAndUpdate(existingTxn, nameof(Transaction.CardHash), existingTxn.CardHash, hash, v => existingTxn.CardHash = v);
                    changed |= CompareAndUpdate(existingTxn, nameof(Transaction.LocationCode), existingTxn.LocationCode, dto.LocationCode, v => existingTxn.LocationCode = v);
                    changed |= CompareAndUpdate(existingTxn, nameof(Transaction.ProductName), existingTxn.ProductName, dto.ProductName, v => existingTxn.ProductName = v);
                    changed |= CompareAndUpdate(existingTxn, nameof(Transaction.Amount), existingTxn.Amount.ToString("F2", CultureInfo.InvariantCulture), dto.Amount.ToString("F2", CultureInfo.InvariantCulture), v => existingTxn.Amount = decimal.Parse(v, CultureInfo.InvariantCulture));

                    // SQLite returns DateTime with Unspecified kind; normalise before comparing to avoid
                    // spurious mismatch between an Unspecified and an explicit UTC value
                    var storedTimeUtc = NormalizeStoredUtc(existingTxn.TransactionTime);
                    if (storedTimeUtc != txnTime)
                    {
                        AddAudit(existingTxn.TransactionId, nameof(Transaction.TransactionTime), storedTimeUtc.ToString("o", CultureInfo.InvariantCulture), txnTime.ToString("o", CultureInfo.InvariantCulture), now);
                        existingTxn.TransactionTime = txnTime;
                        changed = true;
                    }

                    if (changed)
                    {
                        existingTxn.UpdatedAt = now;
                        _logger.LogInformation("Updated transaction {id}", existingTxn.TransactionId);
                    }
                }
                else
                {
                    var newTxn = new Transaction
                    {
                        TransactionId = dto.TransactionId,
                        CardLast4 = last4,
                        CardHash = hash,
                        LocationCode = dto.LocationCode,
                        ProductName = dto.ProductName,
                        Amount = dto.Amount,
                        TransactionTime = txnTime,
                        Status = dto.IsFinalized ? TransactionStatus.Finalized : TransactionStatus.Active,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    _db.Transactions.Add(newTxn);
                    _logger.LogInformation("Inserted transaction {id}", newTxn.TransactionId);

                    // Capture initial field values in the audit log so the full change history is
                    // traceable from the moment of first insertion, not just subsequent updates
                    AddAudit(newTxn.TransactionId, nameof(Transaction.CardLast4), null, newTxn.CardLast4, now);
                    if (newTxn.CardHash is not null)
                        AddAudit(newTxn.TransactionId, nameof(Transaction.CardHash), null, newTxn.CardHash, now);
                    AddAudit(newTxn.TransactionId, nameof(Transaction.LocationCode), null, newTxn.LocationCode, now);
                    AddAudit(newTxn.TransactionId, nameof(Transaction.ProductName), null, newTxn.ProductName, now);
                    AddAudit(newTxn.TransactionId, nameof(Transaction.Amount), null, newTxn.Amount.ToString("F2", CultureInfo.InvariantCulture), now);
                    AddAudit(newTxn.TransactionId, nameof(Transaction.TransactionTime), null, newTxn.TransactionTime.ToString("o", CultureInfo.InvariantCulture), now);
                }
            }

            // Mark as revoked: stored transactions whose TransactionTime is within last 24 hours, absent from snapshot, and not finalized
            var toRevoke = await _db.Transactions
                .Where(t => t.TransactionTime >= last24 && !snapshotIds.Contains(t.TransactionId) && t.Status != TransactionStatus.Finalized && t.Status != TransactionStatus.Revoked)
                .ToListAsync();

            foreach (var r in toRevoke)
            {
                var old = r.Status.ToString();
                r.Status = TransactionStatus.Revoked;
                r.UpdatedAt = now;
                AddAudit(r.TransactionId, nameof(Transaction.Status), old, r.Status.ToString(), now);
                _logger.LogInformation("Revoked transaction {id}", r.TransactionId);
            }

            // Optionally finalize records older than 24 hours
            if (_configuration.GetValue<bool>("Ingest:FinalizeOlderThan24Hours", false))
            {
                var toFinalize = await _db.Transactions
                    .Where(t => t.TransactionTime < last24 && t.Status != TransactionStatus.Finalized)
                    .ToListAsync();

                foreach (var f in toFinalize)
                {
                    var old = f.Status.ToString();
                    f.Status = TransactionStatus.Finalized;
                    f.UpdatedAt = now;
                    AddAudit(f.TransactionId, nameof(Transaction.Status), old, f.Status.ToString(), now);
                    _logger.LogInformation("Finalized transaction {id}", f.TransactionId);
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Compares field values, audits changes, and applies updates.</summary>
    private bool CompareAndUpdate<T>(Transaction entity, string fieldName, T? oldValue, T? newValue, Action<string> apply)
    {
        var oldStr = oldValue?.ToString();
        var newStr = newValue?.ToString();
        if (string.Equals(oldStr, newStr, StringComparison.Ordinal))
            return false;

        AddAudit(entity.TransactionId, fieldName, oldStr, newStr, _timeProvider.UtcNow);
        apply(newStr ?? string.Empty);
        return true;
    }

    /// <summary>Creates an audit record only if old and new values differ.</summary>
    private void AddAudit(int transactionId, string field, string? oldValue, string? newValue, DateTime when)
    {
        // Avoid writing audits where there is no effective change
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            return;

        var audit = new TransactionAudit
        {
            TransactionId = transactionId,
            FieldName = field,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = when,
            ChangedBy = "IngestJob"
        };

        _db.TransactionAudits.Add(audit);
    }

    /// <summary>Normalizes incoming timestamp to UTC for consistent storage and comparison.</summary>
    private static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static DateTime NormalizeStoredUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
