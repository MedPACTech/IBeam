using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IBeam.Identity.Repositories.EntityFramework.OAuth;

internal static class OAuthEntitySerialization
{
    public static string Write(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values ?? Array.Empty<string>());

    public static IReadOnlyList<string> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    public static string ConsentLookupKey(Guid userId, Guid tenantId, string clientId, string resource)
    {
        var source = $"{tenantId:D}\n{userId:D}\n{clientId.Trim()}\n{resource.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
