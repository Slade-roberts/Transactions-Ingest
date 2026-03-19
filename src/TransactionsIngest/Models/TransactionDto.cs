using System.Text.Json.Serialization;

namespace TransactionsIngest.Models;

/// <summary>
/// Data Transfer Object (DTO) for deserializing transaction data from the feed JSON file.
/// </summary>
/// <remarks>
/// Maps incoming JSON to Transaction entity properties. Supports both "TransactionTime" and legacy "timestamp" field names
/// for backward compatibility with different feed formats.
/// </remarks>
public class TransactionDto
{
    /// <summary>Unique identifier for the transaction (matches Transaction.TransactionId).</summary>
    public int TransactionId { get; set; }

    /// <summary>Full card number from the feed (will be masked and hashed by CardPrivacyService).</summary>
    public string? CardNumber { get; set; }

    /// <summary>Location code where the transaction occurred.</summary>
    public string LocationCode { get; set; } = string.Empty;

    /// <summary>Product name purchased in this transaction.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Transaction amount in base currency units.</summary>
    public decimal Amount { get; set; }

    /// <summary>Timestamp of the transaction (can be local or UTC, will be normalized).</summary>
    public DateTime TransactionTime { get; set; }

    // Compatibility with the exercise sample payload that uses "timestamp" field name
    /// <summary>
    /// Provides compatibility with JSON feeds that use "timestamp" instead of "TransactionTime".
    /// This property maps the JSON field name for deserialization.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp
    {
        get => TransactionTime;
        set => TransactionTime = value;
    }

    /// <summary>Flag indicating whether this transaction should be immediately finalized (immutable).</summary>
    public bool IsFinalized { get; set; } = false;
}
