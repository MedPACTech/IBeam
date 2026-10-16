using IBeam.Management.Entities;
namespace IBeam.Management.Services;

/// <summary>Entity service lookup port for already-authorized management operations; no HTTP surface.</summary>
public sealed class ManagementApplicationStateProvider(IManagementApplicationRepository applications) : IManagementApplicationStateProvider
{
    public async Task<ManagementApplicationEntity?> GetAsync(Guid organizationId, Guid applicationId, CancellationToken ct = default)
    {
        var row = await applications.GetAsync(organizationId, applicationId, ct);
        if (row is not null && (row.OrganizationId != organizationId || row.ApplicationId != applicationId))
            throw new ManagementApplicationUnavailableException();
        return row;
    }
}
