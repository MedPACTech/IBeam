using System.Security.Cryptography;
using System.Text;

namespace IBeam.Communications.Abstractions;

/// <summary>
/// Shared-secret-in-query-string guard for webhook endpoints whose senders cannot
/// attach a bearer token (Event Grid and similar). Compare the ?code= value against
/// the configured secret before processing the payload.
/// </summary>
public static class WebhookSharedSecret
{
    public static bool Matches(string? provided, string? configuredSecret)
    {
        // An unconfigured secret fails closed: nobody gets in until one is set.
        if (string.IsNullOrWhiteSpace(configuredSecret) || string.IsNullOrWhiteSpace(provided))
            return false;

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(configuredSecret);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
