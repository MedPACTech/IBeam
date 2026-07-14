using IBeam.Identity.Models;
using System.Security.Claims;

namespace IBeam.Identity.Interfaces;

public interface IApiCredentialService
{
    Task<CreateApiCredentialResult> CreateAsync(
        Guid tenantId,
        CreateApiCredentialRequest request,
        Guid? createdByUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ApiCredentialInfo>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task<ApiCredentialInfo> GetAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default);

    Task<ApiCredentialInfo> UpdateAsync(
        Guid tenantId,
        Guid credentialId,
        UpdateApiCredentialRequest request,
        CancellationToken ct = default);

    Task<ApiCredentialInfo> UpdateRolesAsync(
        Guid tenantId,
        Guid credentialId,
        UpdateApiCredentialRolesRequest request,
        CancellationToken ct = default);

    Task<ApiCredentialAccessContextDto> GetAccessAsync(
        Guid tenantId,
        Guid credentialId,
        string? requestedAgentKey = null,
        CancellationToken ct = default);

    Task<ApiCredentialAccessContextDto> UpdateAccessAsync(
        Guid tenantId,
        Guid credentialId,
        UpdateApiCredentialAccessRequest request,
        CancellationToken ct = default);

    Task<CreateApiCredentialResult> RotateAsync(
        Guid tenantId,
        Guid credentialId,
        CancellationToken ct = default);

    Task<ApiCredentialInfo> RevokeAsync(
        Guid tenantId,
        Guid credentialId,
        Guid? revokedByUserId,
        string? reason,
        CancellationToken ct = default);

    Task<ApiCredentialInfo> ActivateAsync(
        Guid tenantId,
        Guid credentialId,
        CancellationToken ct = default);
}

public interface IApiCredentialAccessService
{
    Task<ApiCredentialAccessContextDto> BuildAccessContextAsync(
        ApiCredentialInfo credential,
        string? requestedAgentKey = null,
        CancellationToken ct = default);

    Task<ApiCredentialContext?> GetCurrentApiCredentialAsync(
        ClaimsPrincipal principal,
        CancellationToken ct = default);

    Task<ApiCredentialAccessContextDto> GetCurrentAccessContextAsync(
        ClaimsPrincipal principal,
        string? requestedAgentKey = null,
        CancellationToken ct = default);

    Task<bool> HasApiScopeAsync(ClaimsPrincipal principal, string moduleKey, CancellationToken ct = default);
    Task<bool> HasToolAccessAsync(ClaimsPrincipal principal, string toolKey, CancellationToken ct = default);
    Task<bool> CanActAsAgentAsync(ClaimsPrincipal principal, string agentKey, CancellationToken ct = default);
    Task<bool> CanCredentialActAsAgentAsync(Guid tenantId, Guid credentialId, string agentKey, CancellationToken ct = default);
    Task<bool> HasResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default);

    Task RequireApiScopeAsync(ClaimsPrincipal principal, string moduleKey, CancellationToken ct = default);
    Task RequireToolAccessAsync(ClaimsPrincipal principal, string toolKey, CancellationToken ct = default);
    Task RequireAgentAccessAsync(ClaimsPrincipal principal, string agentKey, CancellationToken ct = default);
    Task RequireResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default);
}

public interface IApiCredentialScopeCatalogProvider
{
    Task<IReadOnlyList<ApiScopeCatalogItem>> GetScopesAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IIBeamApiScopeCatalogProvider : IApiCredentialScopeCatalogProvider
{
}

public interface IAgentCatalogProvider
{
    Task<IReadOnlyList<AgentCatalogItem>> GetAgentsAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IIBeamAgentCatalogProvider : IAgentCatalogProvider
{
}

public interface IApiCredentialAccessRuleProvider
{
    Task<IReadOnlyList<AccessDecision>> EvaluateAsync(ApiCredentialAccessEvaluationContext context, CancellationToken ct = default);
}

public interface IIBeamApiCredentialAccessRuleProvider : IApiCredentialAccessRuleProvider
{
}
