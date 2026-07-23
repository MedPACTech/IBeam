using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Services.Abstractions;

namespace IBeam.Identity.Services.AgentUsers;

[IBeamOperation("identity.agentusers")]
public sealed class AgentUserService : IAgentUserService
{
    private readonly IAgentUserStore _store;
    private readonly IApiCredentialStore _credentials;
    private readonly IServiceOperationExecutor _operations;

    public AgentUserService(
        IAgentUserStore store,
        IApiCredentialStore credentials,
        IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _credentials = credentials;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("identity.agentusers.create")]
    public async Task<AgentUserInfo> CreateAsync(
        Guid tenantId,
        CreateAgentUserRequest request,
        Guid? createdByUserId,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CreateCoreAsync(tenantId, request, createdByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<AgentUserInfo> CreateCoreAsync(
        Guid tenantId,
        CreateAgentUserRequest request,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        ValidateTenantId(tenantId);
        var now = DateTimeOffset.UtcNow;
        var record = new AgentUserRecord(
            Guid.NewGuid(),
            tenantId,
            NormalizeRequired(request.DisplayName, "displayName"),
            NormalizeOptional(request.Description),
            NormalizeRequired(request.AgentType, "agentType"),
            NormalizeAgentKey(request.AgentKey, request.DisplayName),
            AgentUserStatuses.Active,
            now,
            createdByUserId == Guid.Empty ? null : createdByUserId,
            null,
            NormalizeOptional(request.MetadataJson));

        return AgentUserInfo.FromRecord(await _store.CreateAsync(record, ct).ConfigureAwait(false));
    }

    [IBeamOperation("identity.agentusers.list")]
    public async Task<IReadOnlyList<AgentUserInfo>> ListAsync(Guid tenantId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            async token => (await _store.ListByTenantAsync(tenantId, token).ConfigureAwait(false))
                .Select(AgentUserInfo.FromRecord)
                .ToList(),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    [IBeamOperation("identity.agentusers.get")]
    public async Task<AgentUserInfo> GetAsync(Guid tenantId, Guid agentUserId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GetCoreAsync(tenantId, agentUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = agentUserId },
            ct).ConfigureAwait(false);

    private async Task<AgentUserInfo> GetCoreAsync(Guid tenantId, Guid agentUserId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateAgentUserId(agentUserId);

        var record = await _store.GetAsync(tenantId, agentUserId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"Agent user '{agentUserId}' was not found.");

        return AgentUserInfo.FromRecord(record);
    }

    [IBeamOperation("identity.agentusers.update")]
    public async Task<AgentUserInfo> UpdateAsync(
        Guid tenantId,
        Guid agentUserId,
        UpdateAgentUserRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UpdateCoreAsync(tenantId, agentUserId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = agentUserId },
            ct).ConfigureAwait(false);

    private async Task<AgentUserInfo> UpdateCoreAsync(
        Guid tenantId,
        Guid agentUserId,
        UpdateAgentUserRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var existing = await _store.GetAsync(tenantId, agentUserId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"Agent user '{agentUserId}' was not found.");

        var updated = existing with
        {
            DisplayName = NormalizeRequired(request.DisplayName, "displayName"),
            Description = NormalizeOptional(request.Description),
            AgentType = NormalizeRequired(request.AgentType, "agentType"),
            AgentKey = NormalizeAgentKey(request.AgentKey, request.DisplayName),
            Status = NormalizeStatus(request.Status),
            UpdatedUtc = DateTimeOffset.UtcNow,
            MetadataJson = NormalizeOptional(request.MetadataJson)
        };

        return AgentUserInfo.FromRecord(await _store.UpdateAsync(updated, ct).ConfigureAwait(false));
    }

    [IBeamOperation("identity.agentusers.credentials.bind")]
    public async Task<AgentUserCredentialBindingInfo> BindCredentialAsync(
        Guid tenantId,
        Guid agentUserId,
        BindAgentUserCredentialRequest request,
        Guid? createdByUserId,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => BindCredentialCoreAsync(tenantId, agentUserId, request, createdByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = agentUserId },
            ct).ConfigureAwait(false);

    private async Task<AgentUserCredentialBindingInfo> BindCredentialCoreAsync(
        Guid tenantId,
        Guid agentUserId,
        BindAgentUserCredentialRequest request,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        _ = await GetCoreAsync(tenantId, agentUserId, ct).ConfigureAwait(false);

        if (request.CredentialId == Guid.Empty)
            throw new IdentityValidationException("credentialId is required.");

        var credential = await _credentials.GetAsync(tenantId, request.CredentialId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"API credential '{request.CredentialId}' was not found.");

        if (!credential.IsActive(DateTimeOffset.UtcNow))
            throw new IdentityValidationException("API credential must be active before it can be assigned to an agent user.");

        var binding = new AgentUserCredentialBindingRecord(
            Guid.NewGuid(),
            tenantId,
            agentUserId,
            request.CredentialId,
            NormalizeOptional(request.Purpose),
            NormalizeOptional(request.EnvironmentKey),
            AgentUserStatuses.Active,
            DateTimeOffset.UtcNow,
            createdByUserId == Guid.Empty ? null : createdByUserId,
            null,
            null,
            NormalizeOptional(request.MetadataJson));

        return AgentUserCredentialBindingInfo.FromRecord(await _store.BindCredentialAsync(binding, ct).ConfigureAwait(false));
    }

    [IBeamOperation("identity.agentusers.credentials.list")]
    public async Task<IReadOnlyList<AgentUserCredentialBindingInfo>> ListCredentialBindingsAsync(
        Guid tenantId,
        Guid agentUserId,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            async token => (await _store.ListCredentialBindingsAsync(tenantId, agentUserId, token).ConfigureAwait(false))
                .Select(AgentUserCredentialBindingInfo.FromRecord)
                .ToList(),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = agentUserId },
            ct).ConfigureAwait(false);

    [IBeamOperation("identity.agentusers.credentials.revoke")]
    public async Task RevokeCredentialBindingAsync(
        Guid tenantId,
        Guid agentUserId,
        Guid credentialId,
        Guid? revokedByUserId,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            async token =>
            {
                await _store.RevokeCredentialBindingAsync(
                    tenantId,
                    agentUserId,
                    credentialId,
                    revokedByUserId == Guid.Empty ? null : revokedByUserId,
                    token).ConfigureAwait(false);
                return true;
            },
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = agentUserId },
            ct).ConfigureAwait(false);

    public Task<AgentUserMeDto> GetCurrentAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var tenantId = ResolveGuid(principal, "tid", "tenant_id")
            ?? throw new IdentityUnauthorizedException("Authenticated tenant claim is missing.");
        var agentUserId = ResolveGuid(principal, AgentUserClaimTypes.AgentUserId)
            ?? throw new IdentityUnauthorizedException("Authenticated agent user claim is missing.");
        var displayName = ResolveString(principal, AgentUserClaimTypes.AgentUserName, ClaimTypes.Name, "name")
            ?? throw new IdentityUnauthorizedException("Authenticated agent user name claim is missing.");
        var agentType = ResolveString(principal, AgentUserClaimTypes.AgentType) ?? "custom";
        var agentKey = ResolveString(principal, AgentUserClaimTypes.AgentKey, "api_agent_key") ?? string.Empty;
        var roles = Values(principal, ClaimTypes.Role, "role", "roles");
        var scopes = Values(principal, "scope", "scopes", "scp");
        var tools = Values(principal, "tool", "tools");

        return Task.FromResult(new AgentUserMeDto(
            "agent",
            tenantId,
            agentUserId,
            displayName,
            agentType,
            agentKey,
            ResolveString(principal, "agent_user_description"),
            roles,
            scopes,
            tools));
    }

    private static IReadOnlyList<string> Values(ClaimsPrincipal principal, params string[] claimTypes)
        => claimTypes
            .SelectMany(type => principal.FindAll(type))
            .SelectMany(claim => claim.Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Guid? ResolveGuid(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
                return parsed;
        }

        return null;
    }

    private static string? ResolveString(ClaimsPrincipal principal, params string[] claimTypes)
        => claimTypes
            .Select(claimType => principal.FindFirst(claimType)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
    }

    private static void ValidateAgentUserId(Guid agentUserId)
    {
        if (agentUserId == Guid.Empty)
            throw new IdentityValidationException("agentUserId is required.");
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new IdentityValidationException($"{fieldName} is required.");
        return normalized;
    }

    private static string NormalizeAgentKey(string? value, string fallback)
        => NormalizeOptional(value) ?? Slugify(fallback);

    private static string NormalizeStatus(string? value)
    {
        var normalized = NormalizeOptional(value) ?? AgentUserStatuses.Active;
        if (string.Equals(normalized, AgentUserStatuses.Active, StringComparison.OrdinalIgnoreCase))
            return AgentUserStatuses.Active;
        if (string.Equals(normalized, AgentUserStatuses.Disabled, StringComparison.OrdinalIgnoreCase))
            return AgentUserStatuses.Disabled;
        if (string.Equals(normalized, AgentUserStatuses.Archived, StringComparison.OrdinalIgnoreCase))
            return AgentUserStatuses.Archived;

        throw new IdentityValidationException("status must be active, disabled, or archived.");
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Slugify(string value)
    {
        var chars = NormalizeRequired(value, "displayName")
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
