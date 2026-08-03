using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class JwtSigningKeyProviderTests
{
    [TestMethod]
    public void AsymmetricProvider_SignsTokenVerifiableFromJwks()
    {
        var pair = CreateKeyPair();
        var provider = CreateProvider(pair.PrivatePem, "active-key");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var token = handler.WriteToken(new JwtSecurityToken(
            "issuer", "audience", [new Claim("tid", Guid.NewGuid().ToString("D"))],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: provider.SigningCredentials));
        var jwk = provider.GetPublicJwks().Keys.Single();
        var validationKey = new RsaSecurityKey(new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(jwk.N),
            Exponent = Base64UrlEncoder.DecodeBytes(jwk.E)
        }) { KeyId = jwk.Kid };

        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = "issuer",
            ValidAudience = "audience",
            IssuerSigningKey = validationKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out var validated);

        Assert.IsNotNull(principal.FindFirst("tid"));
        Assert.AreEqual("active-key", ((JwtSecurityToken)validated).Header.Kid);
        Assert.AreEqual("RS256", jwk.Alg);
        Assert.IsNull(typeof(IBeam.Identity.Models.JsonWebKeyDto).GetProperty("D"));
    }

    [TestMethod]
    public void Jwks_PublishesPreviousKeyOnlyDuringOverlap()
    {
        var active = CreateKeyPair();
        var previous = CreateKeyPair();
        var now = DateTimeOffset.UtcNow;
        var options = WithPrevious(AsymmetricOptions(active.PrivatePem, "active"), previous.PublicPem, now.AddMinutes(10));
        var provider = new JwtSigningKeyProvider(Options.Create(options));

        CollectionAssert.AreEquivalent(new[] { "active", "previous" }, provider.GetPublicJwks(now).Keys.Select(x => x.Kid).ToArray());
        CollectionAssert.AreEqual(new[] { "active" }, provider.GetPublicJwks(now.AddMinutes(11)).Keys.Select(x => x.Kid).ToArray());
        CollectionAssert.AreEqual(new[] { "active" }, provider.GetValidationKeys(now.AddMinutes(11)).Select(x => x.KeyId).ToArray());
    }

    [TestMethod]
    public void Constructor_RejectsUndersizedRsaKey()
    {
        using var rsa = RSA.Create(1024);
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        Assert.ThrowsExactly<InvalidOperationException>(() => CreateProvider(pem, "small"));
    }

    [TestMethod]
    public void SymmetricMigrationMode_DoesNotPublishSecretAsJwks()
    {
        var provider = new JwtSigningKeyProvider(Options.Create(new JwtOptions
        {
            Issuer = "issuer",
            Audience = "audience",
            SigningKey = "symmetric-key-that-is-long-enough-for-tests",
            SigningMode = JwtSigningModes.Symmetric
        }));

        Assert.IsEmpty(provider.GetPublicJwks().Keys);
        Assert.AreEqual(SecurityAlgorithms.HmacSha256, provider.SigningCredentials.Algorithm);
    }

    private static JwtSigningKeyProvider CreateProvider(string privatePem, string keyId) =>
        new(Options.Create(AsymmetricOptions(privatePem, keyId)));

    private static JwtOptions AsymmetricOptions(string privatePem, string keyId) => new()
    {
        Issuer = "issuer",
        Audience = "audience",
        SigningMode = JwtSigningModes.Asymmetric,
        PrivateKeyPem = privatePem,
        KeyId = keyId
    };

    private static JwtOptions WithPrevious(JwtOptions options, string publicPem, DateTimeOffset until) => new()
    {
        Issuer = options.Issuer,
        Audience = options.Audience,
        SigningMode = options.SigningMode,
        PrivateKeyPem = options.PrivateKeyPem,
        KeyId = options.KeyId,
        PreviousSigningKeys = [new JwtPreviousSigningKeyOptions { KeyId = "previous", PublicKeyPem = publicPem, PublishUntilUtc = until }]
    };

    private static (string PrivatePem, string PublicPem) CreateKeyPair()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }
}
