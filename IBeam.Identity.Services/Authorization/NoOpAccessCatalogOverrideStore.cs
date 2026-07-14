using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Authorization;

public sealed class NoOpAccessCatalogOverrideStore : IIBeamAccessCatalogOverrideStore
{
    public Task<IReadOnlyList<AccessCatalogOverride>> GetOverridesAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AccessCatalogOverride>>(Array.Empty<AccessCatalogOverride>());

    public Task<AccessCatalogOverride?> GetOverrideAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default)
        => Task.FromResult<AccessCatalogOverride?>(null);

    public Task<AccessCatalogOverride> UpsertOverrideAsync(
        Guid tenantId,
        Guid? catalogItemId,
        UpsertAccessCatalogOverrideRequest request,
        CancellationToken ct = default)
        => Task.FromResult(new AccessCatalogOverride(
            catalogItemId.GetValueOrDefault(Guid.NewGuid()),
            tenantId,
            request.Key,
            request.Label,
            request.Description,
            request.Category,
            request.IsAssignable,
            request.IsMutable,
            request.IsEnabled,
            request.SubjectTypes,
            request.ResourceType,
            request.ResourceId,
            request.ParentResourceType,
            request.ParentResourceId,
            request.SupportedAccessLevels,
            request.Rank,
            request.ModuleKey,
            request.RequiredAccessLevel,
            request.IsDangerous,
            request.IdParameter,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

    public Task DeleteOverrideAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default)
        => Task.CompletedTask;
}
