using System.Security.Cryptography;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialKeyGenerator : IApiCredentialKeyGenerator
{
    private readonly ApiCredentialOptions _options;

    public ApiCredentialKeyGenerator(IOptions<ApiCredentialOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public (string RawKey, ParsedApiCredentialKey ParsedKey, string KeyPrefix) CreateKey(Guid tenantId, Guid credentialId)
    {
        var secretBytes = RandomNumberGenerator.GetBytes(_options.SecretByteLength);
        var secret = Base64UrlEncode(secretBytes);
        var prefix = _options.KeyPrefix.ToLowerInvariant();
        var raw = $"{prefix}_{tenantId:N}_{credentialId:N}_{secret}";
        var keyPrefix = $"{prefix}_{credentialId:N}".Substring(0, prefix.Length + 1 + 8);
        return (raw, new ParsedApiCredentialKey(prefix, tenantId, credentialId, secret), keyPrefix);
    }

    public bool TryParse(string rawKey, out ParsedApiCredentialKey parsedKey)
    {
        parsedKey = default!;
        if (string.IsNullOrWhiteSpace(rawKey))
            return false;

        var parts = rawKey.Trim().Split('_', 4);
        if (parts.Length != 4)
            return false;

        var prefix = parts[0].Trim();
        var tenant = parts[1].Trim();
        var credential = parts[2].Trim();
        var secret = parts[3];

        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrEmpty(secret))
            return false;
        if (!Guid.TryParseExact(tenant, "N", out var tenantId))
            return false;
        if (!Guid.TryParseExact(credential, "N", out var credentialId))
            return false;

        parsedKey = new ParsedApiCredentialKey(prefix, tenantId, credentialId, secret);
        return true;
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
