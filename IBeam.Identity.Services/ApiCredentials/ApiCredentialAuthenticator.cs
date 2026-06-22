using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialAuthenticator : IApiCredentialAuthenticator
{
    private readonly IApiCredentialStore _store;
    private readonly IApiCredentialKeyGenerator _keyGenerator;
    private readonly IApiCredentialSecretHasher _hasher;
    private readonly IApiCredentialPrincipalFactory _principalFactory;

    public ApiCredentialAuthenticator(
        IApiCredentialStore store,
        IApiCredentialKeyGenerator keyGenerator,
        IApiCredentialSecretHasher hasher,
        IApiCredentialPrincipalFactory principalFactory)
    {
        _store = store;
        _keyGenerator = keyGenerator;
        _hasher = hasher;
        _principalFactory = principalFactory;
    }

    public async Task<ApiCredentialAuthenticationResult> AuthenticateAsync(
        string apiKey,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (!_keyGenerator.TryParse(apiKey, out var parsed))
            return ApiCredentialAuthenticationResult.Fail("malformed");

        var record = await _store.GetAsync(parsed.TenantId, parsed.CredentialId, ct).ConfigureAwait(false);
        if (record is null)
            return ApiCredentialAuthenticationResult.Fail("not_found");

        var now = DateTimeOffset.UtcNow;
        if (record.IsDeleted)
            return ApiCredentialAuthenticationResult.Fail("deleted");
        if (record.RevokedUtc is not null)
            return ApiCredentialAuthenticationResult.Fail("revoked");
        if (record.ExpiresUtc is not null && record.ExpiresUtc <= now)
            return ApiCredentialAuthenticationResult.Fail("expired");
        if (!_hasher.Verify(parsed.Secret, record.SecretHash))
            return ApiCredentialAuthenticationResult.Fail("invalid_hash");

        await _store.TouchLastUsedAsync(record.TenantId, record.CredentialId, now, ipAddress, ct).ConfigureAwait(false);
        var principal = _principalFactory.CreatePrincipal(record);
        return ApiCredentialAuthenticationResult.Success(record, principal);
    }
}
