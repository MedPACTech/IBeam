using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class ManagementCustomerIdentityConnectionResolver(IManagementCustomerIdentityAuthorizationService authorization,
    IManagementPrincipalProvider principals, IManagementApplicationStateProvider applications, IManagementApplicationCredentialRepository credentials,
    IManagementSecretStore secrets, ManagementConnectionNetworkPolicy network, IServiceOperationExecutor operations) : IManagementCustomerIdentityConnectionResolver
{
    public Task<ManagementCustomerIdentityConnection> ResolveAsync(Guid applicationId, string operation, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            try
            {
                await authorization.RequireAsync(applicationId, operation, token);
                var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
                var org = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId);
                if (!Guid.TryParse(ManagementPrincipalGuard.RequireHumanUserId(identity), out var user) || user == Guid.Empty) throw new ManagementAccessDeniedException();
                var app = await applications.GetAsync(org, applicationId, token);
                if (app is null || !app.IsEnabled || app.OrganizationId != org || app.ApplicationId != applicationId ||
                    app.ActiveCredentialId is not Guid id) throw new ManagementAccessDeniedException();
                network.RequireOrigin(app.BaseUrl);
                var credential = await credentials.GetAsync(org, applicationId, id, token);
                if (credential is null || credential.OrganizationId != org || credential.ApplicationId != applicationId || credential.CredentialId != id ||
                    credential.BaseUrl != app.BaseUrl || credential.CustomerIdentity is not { Status: ManagementCustomerIdentityStatuses.Available, Info: { } info })
                    throw new ManagementAccessDeniedException();
                ManagementCustomerIdentityProtocol.ValidateProfile(new(null, credential.CustomerIdentity), app.ApplicationKey, app.Environment);
                var key = await secrets.ReadAsync(new(org, applicationId), credential.SecretReference, token);
                if (key.Value is not { Length: >= 16 and <= 4096 } || key.Value.Any(c => c is < '!' or > '~')) throw new ManagementConnectionUnavailableException();
                var result = new ManagementCustomerIdentityConnection { OrganizationId = org, UserId = user, ApplicationId = applicationId,
                    CredentialId = id, IdentityRealmId = info.IdentityRealmId, ApplicationKey = app.ApplicationKey, Environment = app.Environment,
                    BaseUrl = app.BaseUrl, Version = app.Version, Key = key };
                await RecheckAsync(result, operation, token);
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (ManagementAccessDeniedException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.portal.customer-identity.connection",
            AuditAction = "management.portal.customer-identity.connection", EntityId = applicationId,
            EntityName = "ManagementCustomerIdentityConnection", PermissionEnabled = false, AuditEnabled = true }, ct);

    public Task RecheckAsync(ManagementCustomerIdentityConnection c, string operation, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
    {
        try
        {
        await authorization.RequireAsync(c.ApplicationId, operation, token);
        var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
        if (ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId) != c.OrganizationId ||
            !Guid.TryParse(ManagementPrincipalGuard.RequireHumanUserId(identity), out var user) || user != c.UserId) throw new ManagementAccessDeniedException();
        var app = await applications.GetAsync(c.OrganizationId, c.ApplicationId, token);
        if (app is null || !app.IsEnabled || app.OrganizationId != c.OrganizationId || app.ApplicationId != c.ApplicationId ||
            app.ActiveCredentialId != c.CredentialId || app.Version != c.Version || app.BaseUrl != c.BaseUrl) throw new ManagementAccessDeniedException();
        }
        catch (OperationCanceledException) { throw; }
        catch (ManagementAccessDeniedException) { throw; }
        catch { throw new ManagementCustomerIdentityUnavailableException(); }
    }, new ServiceOperationExecutionOptions { OperationName = "management.portal.customer-identity.connection-check",
        AuditAction = "management.portal.customer-identity.connection-check", EntityId = c.ApplicationId,
        EntityName = "ManagementCustomerIdentityConnection", PermissionEnabled = false, AuditEnabled = true }, ct);
}
