namespace IBeam.AccessControl;

public interface IResourceAccessStore
{
    Task<IReadOnlyList<ResourceAccessGrantRecord>> ListGrantsAsync(Guid tenantId, CancellationToken ct = default);
    Task<ResourceAccessGrantRecord?> GetGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);
    Task<ResourceAccessGrantRecord> UpsertGrantAsync(ResourceAccessGrantRecord grant, CancellationToken ct = default);
    Task DeleteGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);
}

public interface IResourceAccessService
{
    Task<IReadOnlyList<ResourceAccessGrantInfo>> ListGrantsAsync(
        Guid tenantId,
        string? resourceType = null,
        string? resourceId = null,
        AccessSubject? subject = null,
        CancellationToken ct = default);

    Task<ResourceAccessGrantInfo> GrantAccessAsync(
        Guid tenantId,
        GrantResourceAccessRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default);

    Task<ResourceAccessGrantInfo> UpdateGrantAsync(
        Guid tenantId,
        Guid grantId,
        UpdateResourceAccessGrantRequest request,
        CancellationToken ct = default);

    Task RevokeGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);
}

public interface IResourceAccessAuthorizer
{
    Task<ResourceAccessAuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        string resourceType,
        string resourceId,
        AccessSubject subject,
        string requiredAccessLevel,
        CancellationToken ct = default);
}

