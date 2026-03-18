using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace TransactionsIngest.Services;

public class CardPrivacyService
{
    private readonly bool _hashEnabled;

    public CardPrivacyService(IConfiguration configuration)
    {
        _hashEnabled = configuration.GetValue<bool>("CardHash:Enabled", true);
    }

    public (string last4, string? hash) ProcessCard(string? cardNumber)
    {
        var normalized = cardNumber ?? string.Empty;
        var last4 = normalized.Length >= 4 ? normalized[^4..] : normalized;
        string? hash = null;

        if (_hashEnabled && !string.IsNullOrEmpty(normalized))
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            hash = Convert.ToHexString(bytes);
        }

        return (last4, hash);
    }
}
