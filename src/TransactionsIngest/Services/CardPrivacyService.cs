using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace TransactionsIngest.Services;

/// <summary>
/// Extracts and securely processes card number data for privacy compliance.
/// </summary>
/// <remarks>
/// Outputs only the last 4 digits (PII masking) and optionally a SHA-256 hash of the full card number.
/// Hashing can be toggled via configuration for flexibility in different deployment environments.
/// </remarks>
public class CardPrivacyService
{
    private readonly bool _hashEnabled;

    /// <summary>
    /// Initializes the service with configuration settings for card hash generation.
    /// </summary>
    /// <param name="configuration">Configuration object containing CardHash:Enabled flag.</param>
    public CardPrivacyService(IConfiguration configuration)
    {
        _hashEnabled = configuration.GetValue<bool>("CardHash:Enabled", true);
    }

    /// <summary>
    /// Processes a card number and returns masked last-4 digits and optional hash.
    /// </summary>
    /// <param name="cardNumber">Full card number from feed (can be null or empty).</param>
    /// <returns>Tuple of (last4 digits, SHA-256 hex hash or null). Safe to store in database.</returns>
    public (string last4, string? hash) ProcessCard(string? cardNumber)
    {
        var normalized = cardNumber ?? string.Empty;
        // Extract last 4 digits safely
        var last4 = normalized.Length >= 4 ? normalized[^4..] : normalized;
        string? hash = null;

        // Optionally generate SHA-256 hash of the full card number for compliance/audit purposes
        if (_hashEnabled && !string.IsNullOrEmpty(normalized))
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            hash = Convert.ToHexString(bytes);
        }

        return (last4, hash);
    }
}
