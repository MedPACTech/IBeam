using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Identity.Services.Tokens;

public interface IJwtSigningKeyProvider
{
    SigningCredentials SigningCredentials { get; }
    IReadOnlyList<SecurityKey> ValidationKeys { get; }
    IReadOnlyList<SecurityKey> GetValidationKeys(DateTimeOffset? now = null);
    JsonWebKeySetDto GetPublicJwks(DateTimeOffset? now = null);
}

public sealed class JwtSigningKeyProvider : IJwtSigningKeyProvider
{
    private readonly JsonWebKeyDto? _activeJwk;
    private readonly IReadOnlyList<(SecurityKey Key, JsonWebKeyDto Jwk, DateTimeOffset Until)> _previous;

    public JwtSigningKeyProvider(IOptions<JwtOptions> options)
    {
        var value = options.Value;
        value.Validate();
        if (value.SigningMode == JwtSigningModes.Symmetric)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value.SigningKey)) { KeyId = value.KeyId };
            SigningCredentials = new(key, SecurityAlgorithms.HmacSha256);
            ValidationKeys = [key];
            _previous = [];
            return;
        }

        var active = ReadRsa(value.PrivateKeyPem!, requirePrivate: true, value.KeyId!);
        SigningCredentials = new(active.Key, SecurityAlgorithms.RsaSha256);
        _activeJwk = active.Jwk;
        var previous = new List<(SecurityKey, JsonWebKeyDto, DateTimeOffset)>();
        foreach (var item in value.PreviousSigningKeys)
        {
            if (string.IsNullOrWhiteSpace(item.KeyId) || item.PublishUntilUtc == default)
                throw new InvalidOperationException("Previous JWT signing keys require KeyId and PublishUntilUtc.");
            var parsed = ReadRsa(item.PublicKeyPem, requirePrivate: false, item.KeyId);
            previous.Add((parsed.Key, parsed.Jwk, item.PublishUntilUtc));
        }
        if (previous.Select(x => x.Item2.Kid).Append(value.KeyId!).Distinct(StringComparer.Ordinal).Count() != previous.Count + 1)
            throw new InvalidOperationException("JWT signing key ids must be unique.");
        _previous = previous;
        ValidationKeys = [active.Key, .. previous.Select(x => x.Item1)];
    }

    public SigningCredentials SigningCredentials { get; }
    public IReadOnlyList<SecurityKey> ValidationKeys { get; }

    public IReadOnlyList<SecurityKey> GetValidationKeys(DateTimeOffset? now = null)
    {
        if (_activeJwk is null)
            return ValidationKeys;
        var current = now ?? DateTimeOffset.UtcNow;
        return [SigningCredentials.Key, .. _previous.Where(x => x.Until > current).Select(x => x.Key)];
    }

    public JsonWebKeySetDto GetPublicJwks(DateTimeOffset? now = null)
    {
        if (_activeJwk is null)
            return new([]);
        var current = now ?? DateTimeOffset.UtcNow;
        return new([_activeJwk, .. _previous.Where(x => x.Until > current).Select(x => x.Jwk)]);
    }

    private static (RsaSecurityKey Key, JsonWebKeyDto Jwk) ReadRsa(string pem, bool requirePrivate, string keyId)
    {
        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            var parameters = rsa.ExportParameters(requirePrivate);
            if (rsa.KeySize < 2048 || parameters.Modulus is null || parameters.Exponent is null)
                throw new InvalidOperationException("RSA JWT signing keys must be at least 2048 bits.");
            if (requirePrivate && parameters.D is null)
                throw new InvalidOperationException("The active JWT signing key must contain private key material.");
            var key = new RsaSecurityKey(rsa) { KeyId = keyId };
            var jwk = new JsonWebKeyDto("RSA", "sig", "RS256", keyId, Encode(parameters.Modulus), Encode(parameters.Exponent));
            return (key, jwk);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"JWT signing key '{keyId}' is invalid.", ex);
        }
    }

    private static string Encode(byte[] value) => Base64UrlEncoder.Encode(value);
}
