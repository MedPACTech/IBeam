using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Services.CustomerIdentity;

/// <summary>Returns connection metadata only after a live assigned-app authorization check. No customer storage dependency.</summary>
public sealed class ManagementCustomerIdentityAvailabilityService(IManagementPrincipalProvider principals,
    IManagementApplicationStateProvider applications, IManagementApplicationCredentialRepository credentials,
    IManagementApplicationAccessService access, IServiceOperationExecutor operations) : IManagementCustomerIdentityAvailabilityService
{
    public Task<ManagementCustomerIdentityDiscovery> GetAsync(Guid applicationId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var principal = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
            var organization = ManagementPrincipalGuard.RequireGuid(principal, ManagementClaimTypes.OrganizationId);
            ManagementPrincipalGuard.RequireHumanUserId(principal);
            var app = await applications.GetAsync(organization, applicationId, token);
            if (app is null || app.OrganizationId != organization || app.ApplicationId != applicationId ||
                !app.IsEnabled || app.ActiveCredentialId is not Guid credentialId) throw new ManagementAccessDeniedException();
            var credential = await credentials.GetAsync(organization, applicationId, credentialId, token);
            if (credential is null || credential.OrganizationId != organization || credential.ApplicationId != applicationId ||
                credential.CredentialId != credentialId || credential.BaseUrl != app.BaseUrl) throw new ManagementAccessDeniedException();
            // Discovery itself confers no read/write grant. An existing operation assignment is required, including for administrators.
            string? permitted = null;
            foreach (var permission in credential.Capabilities.Where(IsAssignableOperation).Distinct(StringComparer.Ordinal))
            {
                try { await access.RequireAsync(applicationId, permission, token); permitted = permission; break; }
                catch (ManagementAccessDeniedException) { }
            }
            if (permitted is null) throw new ManagementAccessDeniedException();
            var current = await applications.GetAsync(organization, applicationId, token);
            if (current is null || !current.IsEnabled || current.Version != app.Version || current.ActiveCredentialId != credentialId ||
                current.BaseUrl != app.BaseUrl) throw new ManagementAccessDeniedException();
            await access.RequireAsync(applicationId, permitted, token);
            var result = credential.CustomerIdentity ?? new(ManagementCustomerIdentityStatuses.NotInstalled);
            if (result.Status == ManagementCustomerIdentityStatuses.Available)
            {
                ManagementCustomerIdentityProtocol.ValidateProfile(new(null, result), app.ApplicationKey, app.Environment);
                var effective = new List<string> { ManagementCustomerIdentityOperations.Info };
                foreach (var operation in result.Info!.Capabilities.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable))
                {
                    if (!ManagementCustomerIdentityPermissionPolicy.RequiredFor(operation).All(required =>
                        credential.Capabilities.Contains(required, StringComparer.Ordinal) &&
                        result.Info.Capabilities.Contains(required, StringComparer.Ordinal))) continue;
                    try { await access.RequireAsync(applicationId, operation, token); effective.Add(operation); }
                    catch (ManagementAccessDeniedException) { }
                }
                await access.RequireAsync(applicationId, permitted, token);
                return result with { Info = result.Info with { Capabilities = Array.AsReadOnly(effective.ToArray()) } };
            }
            else if (result.Info is not null || result.Status is not (ManagementCustomerIdentityStatuses.NotInstalled or
                ManagementCustomerIdentityStatuses.Incompatible or ManagementCustomerIdentityStatuses.Unavailable))
                throw new ManagementConnectionInvalidException();
            return result;
        }, new ServiceOperationExecutionOptions { OperationName = "management.portal.customer-identity.info",
            AuditAction = "management.portal.customer-identity.info", EntityId = applicationId, EntityName = "ManagementCustomerIdentityAvailability",
            PermissionEnabled = false, AuditEnabled = true }, ct);

    internal static bool IsAssignableOperation(string operation) => operation is ManagementOperations.ErrorsRead or ManagementOperations.LogsRead ||
        ManagementCustomerIdentityPermissionPolicy.IsAssignable(operation);
}
public static class ManagementCustomerIdentityPortalServiceExtensions
{
    public static IServiceCollection AddIBeamManagementCustomerIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementConnections(configuration);
        services.AddIBeamManagementApplicationAccess();
        services.TryAddScoped<IManagementTargetProfileProbe, ManagementTargetConnectionProbe>();
        services.TryAddScoped<IManagementCustomerIdentityAvailabilityService, ManagementCustomerIdentityAvailabilityService>();
        services.TryAddScoped<IManagementCustomerIdentityAuthorizationService, ManagementCustomerIdentityAuthorizationService>();
        return services;
    }
}
