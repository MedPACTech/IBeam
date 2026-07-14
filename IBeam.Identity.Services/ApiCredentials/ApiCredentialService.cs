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
    private readonly IApiCredentialAccessService _access;

    public ApiCredentialService(
        IApiCredentialStore store,
        ITenantRoleStore tenantRoles,
        IApiCredentialRoleAssignmentValidator roleValidator,
        IApiCredentialKeyGenerator keyGenerator,
        IApiCredentialSecretHasher hasher,
        IApiCredentialAccessService access)
    {
        _store = store;
        _tenantRoles = tenantRoles;
        _roleValidator = roleValidator;
        _keyGenerator = keyGenerator;
        _hasher = hasher;
        _access = access;
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
        var description = NormalizeOptional(request.Description);
        var agentDisplayName = NormalizeOptional(request.AgentDisplayName);
        var allowedAgentKeys = NormalizeRoleNames(request.AllowedAgentKeys);
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
            RotatedUtc: null,
            RevokedUtc: null,
            RevokedByUserId: null,
            RevocationReason: null,
            IsDeleted: false,
            Description: description,
            AgentDisplayName: agentDisplayName,
            AllowedAgentKeys: allowedAgentKeys);

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

    public async Task<ApiCredentialInfo> GetAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateCredentialId(credentialId);

        var record = await _store.GetAsync(tenantId, credentialId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"API credential '{credentialId}' was not found.");

        return ApiCredentialInfo.FromRecord(record);
    }

    public async Task<ApiCredentialInfo> UpdateAsync(
        Guid tenantId,
        Guid credentialId,
        UpdateApiCredentialRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateTenantId(tenantId);
        ValidateCredentialId(credentialId);

        var existing = await _store.GetAsync(tenantId, credentialId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"API credential '{credentialId}' was not found.");

        var roleIds = NormalizeRoleIds(request.RoleIds);
        var roleNames = NormalizeRoleNames(request.RoleNames);
        await _roleValidator.ValidateAsync(tenantId, roleIds, roleNames, ct).ConfigureAwait(false);
        var resolved = await ResolveRolesAsync(tenantId, roleIds, roleNames, ct).ConfigureAwait(false);

        var updated = existing with
        {
            DisplayName = NormalizeDisplayName(request.DisplayName),
            Description = NormalizeOptional(request.Description),
            AgentKey = NormalizeOptional(request.AgentKey),
            AgentDisplayName = NormalizeOptional(request.AgentDisplayName),
            AllowedAgentKeys = NormalizeRoleNames(request.AllowedAgentKeys),
            RoleIds = resolved.RoleIds,
            RoleNames = resolved.RoleNames,
            ExpiresUtc = request.ExpiresUtc
        };

        if (updated.ExpiresUtc is { } expires && expires <= DateTimeOffset.UtcNow)
            throw new IdentityValidationException("expiresUtc must be in the future.");

        return ApiCredentialInfo.FromRecord(await _store.UpdateAsync(updated, ct).ConfigureAwait(false));
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

    public async Task<ApiCredentialAccessContextDto> GetAccessAsync(
        Guid tenantId,
        Guid credentialId,
        string? requestedAgentKey = null,
        CancellationToken ct = default)
    {
        var credential = await GetAsync(tenantId, credentialId, ct).ConfigureAwait(false);
        return await _access.BuildAccessContextAsync(credential, requestedAgentKey, ct).ConfigureAwait(false);
    }

    public async Task<ApiCredentialAccessContextDto> UpdateAccessAsync(
        Guid tenantId,
        Guid credentialId,
        UpdateApiCredentialAccessRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        ValidateTenantId(tenantId);
        ValidateCredentialId(credentialId);

        var existing = await _store.GetAsync(tenantId, credentialId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"API credential '{credentialId}' was not found.");
        var roleNames = NormalizeRoleNames(request.RoleNames)
            .Concat(request.ApiScopes.Select(x => $"api-scope:{x}"))
            .Concat(request.ToolScopes.Select(x => $"tool:{x}"))
            .Concat(request.AllowedAgentKeys.Select(x => $"api-agent:{x}"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roleIds = NormalizeRoleIds(request.RoleIds);
        await _roleValidator.ValidateAsync(tenantId, roleIds, roleNames, ct).ConfigureAwait(false);
        var resolved = await ResolveRolesAsync(tenantId, roleIds, roleNames, ct).ConfigureAwait(false);

        var updated = await _store.UpdateAsync(
            existing with
            {
                RoleNames = resolved.RoleNames,
                RoleIds = resolved.RoleIds,
                AllowedAgentKeys = NormalizeRoleNames(request.AllowedAgentKeys)
            },
            ct).ConfigureAwait(false);

        return await _access.BuildAccessContextAsync(ApiCredentialInfo.FromRecord(updated), null, ct).ConfigureAwait(false);
    }

    public async Task<CreateApiCredentialResult> RotateAsync(
        Guid tenantId,
        Guid credentialId,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateCredentialId(credentialId);

        var key = _keyGenerator.CreateKey(tenantId, credentialId);
        var rotated = await _store.RotateSecretAsync(
            tenantId,
            credentialId,
            _hasher.Hash(key.ParsedKey.Secret),
            DateTimeOffset.UtcNow,
            ct).ConfigureAwait(false);

        return new CreateApiCredentialResult
        {
            Credential = ApiCredentialInfo.FromRecord(rotated),
            ApiKey = key.RawKey
        };
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

    public async Task<ApiCredentialInfo> ActivateAsync(
        Guid tenantId,
        Guid credentialId,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateCredentialId(credentialId);

        return ApiCredentialInfo.FromRecord(await _store.ActivateAsync(tenantId, credentialId, ct).ConfigureAwait(false));
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
