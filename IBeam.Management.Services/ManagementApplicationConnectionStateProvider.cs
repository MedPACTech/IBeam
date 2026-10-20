using IBeam.Management.Entities;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity;
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
        var capabilities = row.Capabilities.Where(x => x is ManagementOperations.ErrorsRead or ManagementOperations.LogsRead).ToList();
        if (row.CustomerIdentity is { Status: ManagementCustomerIdentityStatuses.Available, Info: { } info })
        {
            try { ManagementCustomerIdentityProtocol.ValidateProfile(new(null, row.CustomerIdentity), app.ApplicationKey, app.Environment); }
            catch (ManagementConnectionInvalidException) { return capabilities.Distinct(StringComparer.Ordinal).ToArray(); }
            capabilities.AddRange(info.Capabilities.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable).Where(operation =>
                ManagementCustomerIdentityPermissionPolicy.RequiredFor(operation).All(required =>
                    row.Capabilities.Contains(required, StringComparer.Ordinal) && info.Capabilities.Contains(required, StringComparer.Ordinal))));
        }
        return capabilities.Distinct(StringComparer.Ordinal).ToArray();
    }
}
