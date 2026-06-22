using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialService : IApiCredentialService
{
    private readonly IApiCredentialStore _store;
    private readonly ITenantRoleStore _tenantRoles;
    private readonly IApiCredentialRoleAssignmentValidator _roleValidator;
    private readonly IApiCredentialKeyGenerator _keyGenerator;
    private readonly IApiCredentialSecretHasher _hasher;

    public ApiCredentialService(
        IApiCredentialStore store,
        ITenantRoleStore tenantRoles,
        IApiCredentialRoleAssignmentValidator roleValidator,
        IApiCredentialKeyGenerator keyGenerator,
        IApiCredentialSecretHasher hasher)
    {
        _store = store;
        _tenantRoles = tenantRoles;
        _roleValidator = roleValidator;
        _keyGenerator = keyGenerator;
        _hasher = hasher;
    }

    public async Task<CreateApiCredentialResult> CreateAsync(
        Guid tenantId,
        CreateApiCredentialRequest request,
        Guid? createdByUserId,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateTenantId(tenantId);

        var displayName = NormalizeDisplayName(request.DisplayName);
        var agentKey = NormalizeOptional(request.AgentKey);
        var roleIds = NormalizeRoleIds(request.RoleIds);
        var roleNames = NormalizeRoleNames(request.RoleNames);

        if (request.ExpiresUtc is { } expires && expires <= DateTimeOffset.UtcNow)
            throw new IdentityValidationException("expiresUtc must be in the future.");

        await _roleValidator.ValidateAsync(tenantId, roleIds, roleNames, ct).ConfigureAwait(false);
        var resolved = await ResolveRolesAsync(tenantId, roleIds, roleNames, ct).ConfigureAwait(false);

        var credentialId = Guid.NewGuid();
        var key = _keyGenerator.CreateKey(tenantId, credentialId);
        var now = DateTimeOffset.UtcNow;

        var record = new ApiCredentialRecord(
            CredentialId: credentialId,
            TenantId: tenantId,
            DisplayName: displayName,
            AgentKey: agentKey,
            KeyPrefix: key.KeyPrefix,
            SecretHash: _hasher.Hash(key.ParsedKey.Secret),
            RoleNames: resolved.RoleNames,
            RoleIds: resolved.RoleIds,
            CreatedUtc: now,
            CreatedByUserId: createdByUserId == Guid.Empty ? null : createdByUserId,
            ExpiresUtc: request.ExpiresUtc,
            LastUsedUtc: null,
            LastUsedIp: null,
            RevokedUtc: null,
            RevokedByUserId: null,
            RevocationReason: null,
            IsDeleted: false);

        var created = await _store.CreateAsync(record, ct).ConfigureAwait(false);
        return new CreateApiCredentialResult
        {
            Credential = ApiCredentialInfo.FromRecord(created),
            ApiKey = key.RawKey
        };
    }

    public async Task<IReadOnlyList<ApiCredentialInfo>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var records = await _store.ListByTenantAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(ApiCredentialInfo.FromRecord).ToList();
    }

    public async Task<ApiCredentialInfo> UpdateRolesAsync(
        Guid tenantId,
        Guid credentialId,
        UpdateApiCredentialRolesRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateTenantId(tenantId);
        ValidateCredentialId(credentialId);

        var roleIds = NormalizeRoleIds(request.RoleIds);
        var roleNames = NormalizeRoleNames(request.RoleNames);
        await _roleValidator.ValidateAsync(tenantId, roleIds, roleNames, ct).ConfigureAwait(false);
        var resolved = await ResolveRolesAsync(tenantId, roleIds, roleNames, ct).ConfigureAwait(false);

        var updated = await _store.UpdateRolesAsync(tenantId, credentialId, resolved.RoleIds, resolved.RoleNames, ct)
            .ConfigureAwait(false);
        return ApiCredentialInfo.FromRecord(updated);
    }

    public async Task<ApiCredentialInfo> RevokeAsync(
        Guid tenantId,
        Guid credentialId,
        Guid? revokedByUserId,
        string? reason,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateCredentialId(credentialId);

        var revoked = await _store.RevokeAsync(
            tenantId,
            credentialId,
            revokedByUserId == Guid.Empty ? null : revokedByUserId,
            NormalizeOptional(reason),
            ct).ConfigureAwait(false);

        return ApiCredentialInfo.FromRecord(revoked);
    }

    private async Task<(IReadOnlyList<Guid> RoleIds, IReadOnlyList<string> RoleNames)> ResolveRolesAsync(
        Guid tenantId,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> requestedRoleNames,
        CancellationToken ct)
    {
        var resolvedNames = requestedRoleNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ids = roleIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count > 0)
        {
            var activeRoles = await _tenantRoles.GetRolesAsync(tenantId, ct).ConfigureAwait(false);
            var roleMap = activeRoles.ToDictionary(x => x.RoleId, x => x);
            foreach (var id in ids)
            {
                if (roleMap.TryGetValue(id, out var role))
                    resolvedNames.Add(role.Name);
            }
        }

        return (ids, resolvedNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
    }

    private static void ValidateCredentialId(Guid credentialId)
    {
        if (credentialId == Guid.Empty)
            throw new IdentityValidationException("credentialId is required.");
    }

    private static string NormalizeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new IdentityValidationException("displayName is required.");

        var value = displayName.Trim();
        if (value.Length > 128)
            throw new IdentityValidationException("displayName must be 128 characters or fewer.");
        return value;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<Guid> NormalizeRoleIds(IEnumerable<Guid>? roleIds)
        => (roleIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

    private static IReadOnlyList<string> NormalizeRoleNames(IEnumerable<string>? roleNames)
        => (roleNames ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
