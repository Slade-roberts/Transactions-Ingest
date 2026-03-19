using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TransactionsIngest.Models;

/// <summary>
/// Audit record tracking field-level changes to a transaction.
/// </summary>
/// <remarks>
/// Each time a field on a Transaction changes during an ingestion run, an audit record is created
/// to preserve the old and new values. This enables compliance auditing and change history analysis.
/// </remarks>
public class TransactionAudit
{
    /// <summary>Primary key: auto-incremented audit record ID.</summary>
    [Key]
    public long Id { get; set; }

    /// <summary>Foreign key reference to the Transaction being audited.</summary>
    public int TransactionId { get; set; }

    /// <summary>Name of the field that changed (e.g., "Amount", "Status").</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Previous value of the field before the change.</summary>
    public string? OldValue { get; set; }

    /// <summary>New value of the field after the change.</summary>
    public string? NewValue { get; set; }

    /// <summary>Timestamp when the change occurred (UTC).</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>System or user identifier that made the change (defaults to "IngestJob").</summary>
    public string ChangedBy { get; set; } = "IngestJob";

    /// <summary>Navigation property to the associated Transaction (set by EF Core).</summary>
    public Transaction? Transaction { get; set; }
}
