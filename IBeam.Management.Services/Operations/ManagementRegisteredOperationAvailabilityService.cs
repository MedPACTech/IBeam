using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using IBeam.Management.Services.CustomerIdentity;
using IBeam.Services.Abstractions;

namespace IBeam.Management.Services;

/// <summary>Live selected-app authorization before connection metadata; never resolves machine secrets.</summary>
public sealed class ManagementRegisteredOperationAvailabilityService(ManagementOperationRegistry registry,
    IManagementPrincipalProvider principals, IManagementApplicationStateProvider applications,
    IManagementApplicationCredentialRepository credentials, IManagementApplicationAccessService access,
    IServiceOperationExecutor operations) : IManagementRegisteredOperationAvailabilityService
{
    public Task<ManagementRegisteredOperationDiscovery> GetAsync(Guid applicationId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
            var org = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId); ManagementPrincipalGuard.RequireHumanUserId(identity);
            var app = await applications.GetAsync(org, applicationId, token);
            if (app is null || app.OrganizationId != org || app.ApplicationId != applicationId || !app.IsEnabled || app.ActiveCredentialId is not Guid id)
                throw new ManagementAccessDeniedException();
            var row = await credentials.GetAsync(org, applicationId, id, token);
            if (row is null || row.OrganizationId != org || row.ApplicationId != applicationId || row.CredentialId != id || row.BaseUrl != app.BaseUrl)
                throw new ManagementAccessDeniedException();
            string? assigned = null;
            foreach (var permission in row.Capabilities.Where(registry.IsAssignable).Distinct(StringComparer.Ordinal))
            {
                try { await access.RequireAsync(applicationId, permission, token); assigned = permission; break; }
                catch (ManagementAccessDeniedException) { }
            }
            if (assigned is null) throw new ManagementAccessDeniedException();
            var current = await applications.GetAsync(org, applicationId, token);
            if (current is null || !current.IsEnabled || current.Version != app.Version || current.ActiveCredentialId != id) throw new ManagementAccessDeniedException();
            await access.RequireAsync(applicationId, assigned, token);
            var discovery = row.Operations ?? new(ManagementCustomerIdentityStatuses.NotInstalled);
            if (discovery.Status != ManagementCustomerIdentityStatuses.Available)
            {
                if (discovery.Info is not null || discovery.Status is not (ManagementCustomerIdentityStatuses.NotInstalled or ManagementCustomerIdentityStatuses.Incompatible or ManagementCustomerIdentityStatuses.Unavailable))
                    throw new ManagementConnectionInvalidException();
                return discovery;
            }
            var profile = new ManagementTargetProfile(null, row.CustomerIdentity ?? new(ManagementCustomerIdentityStatuses.NotInstalled)) { Operations = discovery };
            ManagementCustomerIdentityProtocol.ValidateProfile(profile, app.ApplicationKey, app.Environment, registry);
            var effective = new List<ManagementOperationCapability>();
            foreach (var capability in discovery.Info!.Capabilities)
            {
                if (registry.RequiredFor(capability.Operation).Any(x => !row.Capabilities.Contains(x, StringComparer.Ordinal))) continue;
                try
                {
                    foreach (var required in registry.RequiredFor(capability.Operation)) await access.RequireAsync(applicationId, required, token);
                    effective.Add(capability);
                }
                catch (ManagementAccessDeniedException) { }
            }
            await access.RequireAsync(applicationId, assigned, token);
            return discovery with { Info = discovery.Info with { Capabilities = effective.ToArray() } };
        }, new ServiceOperationExecutionOptions { OperationName = "management.operations.availability", AuditAction = "management.operations.availability",
            EntityId = applicationId, EntityName = "ManagementRegisteredOperationAvailability", PermissionEnabled = false, AuditEnabled = true }, ct);
}
