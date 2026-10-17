using IBeam.Management.Entities;
namespace IBeam.Management.Services;

/// <summary>Entity service lookup port for already-authorized management operations; no HTTP surface.</summary>
public sealed class ManagementApplicationStateProvider(IManagementApplicationRepository applications) : IManagementApplicationStateProvider, IManagementApplicationCatalogProvider
{
    public async Task<ManagementApplicationPage> ListAsync(Guid organizationId, Guid? after, int pageSize, CancellationToken ct = default)
    {
        var page = await applications.ListAsync(organizationId, after, pageSize, ct);
        if (page.Items.Count > pageSize || page.Items.Any(x => x.OrganizationId != organizationId || x.ApplicationId == Guid.Empty) ||
            page.Items.Select(x => x.ApplicationId).Distinct().Count() != page.Items.Count ||
            page.Items.Any(x => after.HasValue && string.CompareOrdinal(x.ApplicationId.ToString("N"), after.Value.ToString("N")) <= 0) ||
            !page.Items.Select(x => x.ApplicationId.ToString("N")).SequenceEqual(page.Items.Select(x => x.ApplicationId.ToString("N")).Order(StringComparer.Ordinal)) ||
            page.Next.HasValue && (page.Items.Count == 0 || page.Next.Value != page.Items[^1].ApplicationId))
            throw new ManagementApplicationUnavailableException();
        return page;
    }
    public async Task<ManagementApplicationEntity?> GetAsync(Guid organizationId, Guid applicationId, CancellationToken ct = default)
    {
        var row = await applications.GetAsync(organizationId, applicationId, ct);
        if (row is not null && (row.OrganizationId != organizationId || row.ApplicationId != applicationId))
            throw new ManagementApplicationUnavailableException();
        return row;
    }
}
