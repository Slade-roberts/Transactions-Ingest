using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TransactionsIngest.Models;

public class Transaction
{
    [Key]
    public int TransactionId { get; set; }

    public string CardLast4 { get; set; } = string.Empty;

    public string? CardHash { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public DateTime TransactionTime { get; set; }

    public TransactionStatus Status { get; set; } = TransactionStatus.Active;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<TransactionAudit> Audits { get; set; } = new();
}
