using System.Security.Cryptography;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialSecretHasher : IApiCredentialSecretHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const string Version = "v1";
    private readonly ApiCredentialOptions _options;

    public ApiCredentialSecretHasher(IOptions<ApiCredentialOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public string Hash(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret is required.", nameof(secret));

        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            _options.HashIterations,
            HashAlgorithmName.SHA256,
            32);

        return string.Join(':',
            Algorithm,
            Version,
            _options.HashIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string secret, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split(':');
        if (parts.Length != 5 ||
            !string.Equals(parts[0], Algorithm, StringComparison.Ordinal) ||
            !string.Equals(parts[1], Version, StringComparison.Ordinal) ||
            !int.TryParse(parts[2], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}
