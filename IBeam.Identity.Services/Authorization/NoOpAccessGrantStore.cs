using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Authorization;

public sealed class NoOpAccessGrantStore : IIBeamAccessGrantStore
{
    public Task<IReadOnlyList<AccessGrant>> GetGrantsAsync(
        Guid tenantId,
        string? subjectType = null,
        string? subjectId = null,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AccessGrant>>(Array.Empty<AccessGrant>());

    public Task<AccessGrant?> GetGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
        => Task.FromResult<AccessGrant?>(null);

    public Task<AccessGrant> UpsertGrantAsync(
        Guid tenantId,
        Guid? grantId,
        string subjectType,
        string subjectId,
        string resourceType,
        string resourceId,
        string accessLevel,
        CancellationToken ct = default)
        => Task.FromResult(new AccessGrant(
            grantId.GetValueOrDefault(Guid.NewGuid()),
            tenantId,
            subjectType,
            subjectId,
            resourceType,
            resourceId,
            accessLevel,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

    public Task DeleteGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
        => Task.CompletedTask;
}

