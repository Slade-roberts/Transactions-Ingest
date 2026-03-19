namespace TransactionsIngest.Models;

/// <summary>
/// Enumeration of possible transaction states during the ingestion lifecycle.
/// </summary>
public enum TransactionStatus
{
    /// <summary>Transaction is active and can be updated during ingestion runs.</summary>
    Active = 0,

    /// <summary>Transaction was marked as revoked because it was missing from a feed snapshot within 24 hours.</summary>
    Revoked = 1,

    /// <summary>Transaction is finalized and cannot be modified (locked state for records older than 24 hours).</summary>
    Finalized = 2
}
