using System.Security.Cryptography;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Services.Abstractions;

namespace IBeam.Identity.Services.Auth;

[IBeamOperation("identity.oauthclients")]
public sealed class OAuthClientAdministrationService : IOAuthClientAdministrationService
{
    private const string SecretHashAlgorithm = "pbkdf2-sha256";
    private readonly IOAuthClientStore _clients;
    private readonly IApiCredentialSecretHasher _secretHasher;
    private readonly IServiceOperationExecutor _operations;

    public OAuthClientAdministrationService(
        IOAuthClientStore clients,
        IApiCredentialSecretHasher secretHasher,
        IServiceOperationExecutor? operations = null)
    {
        _clients = clients;
        _secretHasher = secretHasher;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("identity.oauthclients.create")]
    public Task<OAuthClientCreatedResult> CreateAsync(
        Guid tenantId,
        CreateOAuthClientRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var audit = Audit(tenantId, ServiceAuditOperation.Create, "create", original: request);
        return _operations.ExecuteAsync(
            this,
            async token =>
            {
                ValidateTenantId(tenantId);
                var clientId = NormalizeOrGenerateClientId(request.ClientId);
                var confidential = string.Equals(request.ClientType?.Trim(), OAuthClientTypes.Confidential, StringComparison.OrdinalIgnoreCase);
                var rawSecret = confidential ? GenerateSecret() : null;
                var now = DateTimeOffset.UtcNow;
                var record = Validate(new OAuthClientRecord(
                    clientId,
                    tenantId,
                    request.DisplayName,
                    request.ClientType ?? string.Empty,
                    request.RedirectUris,
                    request.AllowedGrantTypes,
                    request.AllowedScopes,
                    request.AllowedResources,
                    request.RequirePkce,
                    OAuthClientStatuses.Active,
                    rawSecret is null ? null : _secretHasher.Hash(rawSecret),
                    rawSecret is null ? null : SecretHashAlgorithm,
                    now,
                    ClientSecretExpiresUtc: request.ClientSecretExpiresUtc));

                var created = await _clients.CreateAsync(record, token).ConfigureAwait(false);
                var result = new OAuthClientCreatedResult(OAuthClientInfo.FromRecord(created), rawSecret);
                audit.TransformedData = result.Client;
                return result;
            },
            audit,
            ct);
    }

    [IBeamOperation("identity.oauthclients.list")]
    public Task<IReadOnlyList<OAuthClientInfo>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        _operations.ExecuteAsync(
            this,
            async token =>
            {
                ValidateTenantId(tenantId);
                var records = await _clients.ListByTenantAsync(tenantId, token).ConfigureAwait(false);
                return (IReadOnlyList<OAuthClientInfo>)records.Select(OAuthClientInfo.FromRecord).ToList();
            },
            Audit(tenantId, ServiceAuditOperation.GetAll, "list"),
            ct);

    [IBeamOperation("identity.oauthclients.get")]
    public Task<OAuthClientInfo> GetAsync(Guid tenantId, string clientId, CancellationToken ct = default) =>
        _operations.ExecuteAsync(
            this,
            async token => OAuthClientInfo.FromRecord(await GetRecordAsync(tenantId, clientId, token).ConfigureAwait(false)),
            Audit(tenantId, ServiceAuditOperation.GetById, "get"),
            ct);

    [IBeamOperation("identity.oauthclients.update")]
    public Task<OAuthClientInfo> UpdateAsync(
        Guid tenantId,
        string clientId,
        UpdateOAuthClientRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var audit = Audit(tenantId, ServiceAuditOperation.Update, "update", original: request);
        return _operations.ExecuteAsync(
            this,
            async token =>
            {
                var existing = await GetRecordAsync(tenantId, clientId, token).ConfigureAwait(false);
                EnsureMutable(existing);
                var updated = Validate(existing with
                {
                    DisplayName = request.DisplayName,
                    RedirectUris = request.RedirectUris,
                    AllowedGrantTypes = request.AllowedGrantTypes,
                    AllowedScopes = request.AllowedScopes,
                    AllowedResources = request.AllowedResources,
                    RequirePkce = request.RequirePkce,
                    ClientSecretExpiresUtc = request.ClientSecretExpiresUtc,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });
                var info = OAuthClientInfo.FromRecord(await _clients.UpdateAsync(updated, token).ConfigureAwait(false));
                audit.TransformedData = info;
                return info;
            },
            audit,
            ct);
    }

    [IBeamOperation("identity.oauthclients.secret.rotate")]
    public Task<OAuthClientSecretRotatedResult> RotateSecretAsync(Guid tenantId, string clientId, CancellationToken ct = default)
    {
        var audit = Audit(tenantId, ServiceAuditOperation.Update, "rotate-secret");
        return _operations.ExecuteAsync(
            this,
            async token =>
            {
                var existing = await GetRecordAsync(tenantId, clientId, token).ConfigureAwait(false);
                EnsureMutable(existing);
                if (!string.Equals(existing.ClientType, OAuthClientTypes.Confidential, StringComparison.Ordinal))
                    throw new IdentityValidationException("Only confidential OAuth clients have secrets.");

                var rawSecret = GenerateSecret();
                var now = DateTimeOffset.UtcNow;
                var updated = existing with
                {
                    ClientSecretHash = _secretHasher.Hash(rawSecret),
                    ClientSecretHashAlgorithm = SecretHashAlgorithm,
                    SecretRotatedUtc = now,
                    UpdatedUtc = now
                };
                var info = OAuthClientInfo.FromRecord(await _clients.UpdateAsync(updated, token).ConfigureAwait(false));
                audit.TransformedData = info;
                return new OAuthClientSecretRotatedResult(info, rawSecret);
            },
            audit,
            ct);
    }

    [IBeamOperation("identity.oauthclients.disable")]
    public Task<OAuthClientInfo> DisableAsync(Guid tenantId, string clientId, CancellationToken ct = default) =>
        ChangeStatusAsync(tenantId, clientId, OAuthClientStatuses.Disabled, ct);

    [IBeamOperation("identity.oauthclients.revoke")]
    public Task<OAuthClientInfo> RevokeAsync(Guid tenantId, string clientId, CancellationToken ct = default) =>
        ChangeStatusAsync(tenantId, clientId, OAuthClientStatuses.Revoked, ct);

    private Task<OAuthClientInfo> ChangeStatusAsync(Guid tenantId, string clientId, string status, CancellationToken ct)
    {
        var audit = Audit(tenantId, ServiceAuditOperation.Update, status);
        return _operations.ExecuteAsync(
            this,
            async token =>
            {
                var existing = await GetRecordAsync(tenantId, clientId, token).ConfigureAwait(false);
                if (string.Equals(existing.Status, OAuthClientStatuses.Revoked, StringComparison.Ordinal))
                    throw new IdentityValidationException("A revoked OAuth client cannot be changed.");

                var now = DateTimeOffset.UtcNow;
                var updated = existing with
                {
                    Status = status,
                    UpdatedUtc = now,
                    DisabledUtc = status == OAuthClientStatuses.Disabled ? now : existing.DisabledUtc,
                    RevokedUtc = status == OAuthClientStatuses.Revoked ? now : existing.RevokedUtc
                };
                var info = OAuthClientInfo.FromRecord(await _clients.UpdateAsync(updated, token).ConfigureAwait(false));
                audit.TransformedData = info;
                return info;
            },
            audit,
            ct);
    }

    private async Task<OAuthClientRecord> GetRecordAsync(Guid tenantId, string clientId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        var normalized = NormalizeClientId(clientId);
        var record = await _clients.GetAsync(normalized, ct).ConfigureAwait(false);
        if (record is null || record.TenantId != tenantId)
            throw new IdentityNotFoundException($"OAuth client '{normalized}' was not found.");
        return record;
    }

    private static OAuthClientRecord Validate(OAuthClientRecord record)
    {
        var options = new OAuthClientRegistrationOptions
        {
            ClientId = record.ClientId,
            TenantId = record.TenantId,
            DisplayName = record.DisplayName,
            ClientType = record.ClientType,
            RedirectUris = record.RedirectUris.ToList(),
            AllowedGrantTypes = record.AllowedGrantTypes.ToList(),
            AllowedScopes = record.AllowedScopes.ToList(),
            AllowedResources = record.AllowedResources.ToList(),
            RequirePkce = record.RequirePkce,
            Status = record.Status,
            ClientSecretHash = record.ClientSecretHash,
            ClientSecretHashAlgorithm = record.ClientSecretHashAlgorithm,
            ClientSecretExpiresUtc = record.ClientSecretExpiresUtc
        };
        try
        {
            options.NormalizeAndValidate();
        }
        catch (InvalidOperationException ex)
        {
            throw new IdentityValidationException(ex.Message);
        }

        if (options.ClientSecretExpiresUtc is { } expires && expires <= DateTimeOffset.UtcNow)
            throw new IdentityValidationException("clientSecretExpiresUtc must be in the future.");

        return record with
        {
            ClientId = options.ClientId,
            DisplayName = options.DisplayName,
            ClientType = options.ClientType,
            RedirectUris = options.RedirectUris,
            AllowedGrantTypes = options.AllowedGrantTypes,
            AllowedScopes = options.AllowedScopes,
            AllowedResources = options.AllowedResources,
            Status = options.Status,
            ClientSecretExpiresUtc = options.ClientSecretExpiresUtc
        };
    }

    private static ServiceOperationExecutionOptions Audit(
        Guid tenantId,
        ServiceAuditOperation operation,
        string action,
        object? original = null) => new()
    {
        TenantId = tenantId,
        EntityName = "OAuthClient",
        AuditOperation = operation,
        AuditAction = action,
        OriginalData = original
    };

    private static void EnsureMutable(OAuthClientRecord client)
    {
        if (string.Equals(client.Status, OAuthClientStatuses.Revoked, StringComparison.Ordinal))
            throw new IdentityValidationException("A revoked OAuth client cannot be changed.");
    }

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
    }

    private static string NormalizeOrGenerateClientId(string? clientId) =>
        string.IsNullOrWhiteSpace(clientId) ? $"ibc_{Base64Url(RandomNumberGenerator.GetBytes(18))}" : NormalizeClientId(clientId);

    private static string NormalizeClientId(string? clientId)
    {
        var normalized = clientId?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 200 || normalized.Any(char.IsWhiteSpace))
            throw new IdentityValidationException("clientId must be between 1 and 200 characters and cannot contain whitespace.");
        return normalized;
    }

    private static string GenerateSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
