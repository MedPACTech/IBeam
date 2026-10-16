using IBeam.Management.Entities;
namespace IBeam.Management.Services;
/// <summary>Credential entity service; never resolves a secret or contacts a target.</summary>
public sealed class ManagementApplicationConnectionStateProvider(IManagementApplicationCredentialRepository credentials) : IManagementApplicationConnectionStateProvider
{
    public async Task<IReadOnlyList<string>> CapabilitiesAsync(ManagementApplicationEntity app, CancellationToken ct = default)
    {
        if (!app.IsEnabled || !app.ActiveCredentialId.HasValue) return [];
        var row = await credentials.GetAsync(app.OrganizationId, app.ApplicationId, app.ActiveCredentialId.Value, ct);
        if (row is null || row.OrganizationId != app.OrganizationId || row.ApplicationId != app.ApplicationId ||
            row.CredentialId != app.ActiveCredentialId || row.BaseUrl != app.BaseUrl) return [];
        return row.Capabilities.Where(x => x is ManagementOperations.ErrorsRead or ManagementOperations.LogsRead).Distinct().ToArray();
    }
}
