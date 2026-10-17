using System.Security.Cryptography;
using System.Text;

namespace IBeam.Management.Services.Credentials;

/// <summary>32 random bytes; SHA-256 is appropriate for these high-entropy generated secrets, not user passwords.</summary>
public sealed class ManagementCredentialSecretCodec
{
    private const string Prefix = "ibmgmt_v1";
    public string Create(Guid credentialId)
        => $"{Prefix}.{credentialId:N}.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    public bool TryReadId(string? apiKey, out Guid id)
    {
        id = Guid.Empty;
        if (apiKey is null || apiKey.Length != Prefix.Length + 1 + 32 + 1 + 43) return false;
        var parts = apiKey.Split('.');
        return parts.Length == 3 && parts[0] == Prefix &&
            Guid.TryParseExact(parts[1], "N", out id) && id != Guid.Empty &&
            parts[1] == id.ToString("N") && parts[2].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    public string Hash(string apiKey) => "sha256-v1:" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes("IBeam.Management.Credential.v1\0" + apiKey)));

    public bool Verify(string apiKey, string storedHash)
    {
        if (!TryReadId(apiKey, out _) || storedHash is null || storedHash.Length != 74 ||
            !storedHash.StartsWith("sha256-v1:", StringComparison.Ordinal)) return false;
        try
        {
            var expected = Convert.FromHexString(storedHash[10..]);
            var actual = Convert.FromHexString(Hash(apiKey)[10..]);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
