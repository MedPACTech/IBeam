using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IApiCredentialService
{
    Task<CreateApiCredentialResult> CreateAsync(
        Guid tenantId,
        CreateApiCredentialRequest request,
        Guid? createdByUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ApiCredentialInfo>> ListAsync(Guid tenantId, CancellationToken ct = default);

    Task<ApiCredentialInfo> UpdateRolesAsync(
        Guid tenantId,
        Guid credentialId,
        UpdateApiCredentialRolesRequest request,
        CancellationToken ct = default);

    Task<ApiCredentialInfo> RevokeAsync(
        Guid tenantId,
        Guid credentialId,
        Guid? revokedByUserId,
        string? reason,
        CancellationToken ct = default);
}
