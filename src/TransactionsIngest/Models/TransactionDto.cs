using System.Text.Json.Serialization;

namespace TransactionsIngest.Models;

public class TransactionDto
{
    public int TransactionId { get; set; }

    public string? CardNumber { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime TransactionTime { get; set; }

    // Compatibility with the exercise sample payload that uses "timestamp"
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp
    {
        get => TransactionTime;
        set => TransactionTime = value;
    }

    public bool IsFinalized { get; set; } = false;
}
