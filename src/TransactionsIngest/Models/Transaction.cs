using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TransactionsIngest.Models;

/// <summary>
/// Represents a financial transaction record with change tracking and status management.
/// </summary>
/// <remarks>
/// Transactions can be in one of three states: Active, Revoked, or Finalized.
/// Active transactions can be updated during ingestion runs.
/// Revoked transactions are marked when missing from a feed snapshot within the 24-hour window.
/// Finalized transactions are locked and cannot be modified (optional feature based on configuration).
/// </remarks>
public class Transaction
{
    /// <summary>Primary key: unique transaction identifier from the feed source.</summary>
    [Key]
    public int TransactionId { get; set; }

    /// <summary>Last 4 digits of the card number (PII masked).</summary>
    public string CardLast4 { get; set; } = string.Empty;

    /// <summary>Optional SHA-256 hash of the full card number for additional privacy checks.</summary>
    public string? CardHash { get; set; }

    /// <summary>Location or store code where the transaction occurred.</summary>
    public string LocationCode { get; set; } = string.Empty;

    /// <summary>Product or item name that was purchased.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Transaction amount (decimal with 2 precision places for currency).</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>Timestamp of the transaction (normalized to UTC).</summary>
    public DateTime TransactionTime { get; set; }

    /// <summary>Current status of the transaction (Active, Revoked, Finalized).</summary>
    public TransactionStatus Status { get; set; } = TransactionStatus.Active;

    /// <summary>Timestamp when this record was first created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Timestamp when this record was last updated (includes new inserts and modifications).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Collection of audit records tracking field-level changes to this transaction.</summary>
    public List<TransactionAudit> Audits { get; set; } = new();
}
