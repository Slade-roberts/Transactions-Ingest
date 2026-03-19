namespace TransactionsIngest.Services.Interfaces;

/// <summary>
/// Orchestrates the transaction ingestion process from feed to database.
/// </summary>
/// <remarks>
/// Responsible for:
/// - Loading transaction snapshots from the feed
/// - Performing idempotent upserts with field-level change tracking
/// - Revoking transactions missing from the snapshot (within 24 hours)
/// - Optionally finalizing records older than 24 hours (making them immutable)
/// - Wrapping all operations in a single database transaction for atomicity
/// </remarks>
public interface IIngestService
{
    /// <summary>
    /// Executes the complete ingestion run: load, upsert, revoke, and finalize logic.
    /// </summary>
    Task RunAsync();
}
