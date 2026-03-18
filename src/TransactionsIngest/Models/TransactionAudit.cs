using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TransactionsIngest.Models;

public class TransactionAudit
{
    [Key]
    public long Id { get; set; }

    public int TransactionId { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime ChangedAt { get; set; }

    public string ChangedBy { get; set; } = "IngestJob";

    public Transaction? Transaction { get; set; }
}
