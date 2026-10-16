using IBeam.Management.Models;
namespace IBeam.Management.Services.Diagnostics;

/// <summary>Credential entity service; delegates application/access state to their services and never accepts browser connection material.</summary>
public sealed class ManagementDiagnosticConnectionResolver(IManagementApplicationAccessService access,
    IManagementApplicationStateProvider applications, IManagementApplicationCredentialRepository credentials,
    IManagementSecretStore secrets, IManagementPrincipalProvider principals, ManagementConnectionNetworkPolicy network)
    : IManagementDiagnosticConnectionResolver
{
    public async Task<ManagementDiagnosticConnection> ResolveAsync(Guid applicationId, string permission, CancellationToken ct = default)
    {
        await access.RequireAsync(applicationId, permission, ct);
        var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
        var org = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId);
        if (!Guid.TryParse(ManagementPrincipalGuard.RequireHumanUserId(identity), out var user)) throw new ManagementAccessDeniedException();
        var app = await applications.GetAsync(org, applicationId, ct);
        if (app is null || !app.IsEnabled || app.OrganizationId != org || app.ApplicationId != applicationId ||
            app.ActiveCredentialId is not Guid credentialId) throw new ManagementAccessDeniedException();
        network.RequireOrigin(app.BaseUrl);
        var credential = await credentials.GetAsync(org, applicationId, credentialId, ct);
        if (credential is null || credential.OrganizationId != org || credential.ApplicationId != applicationId ||
            credential.CredentialId != credentialId || credential.BaseUrl != app.BaseUrl ||
            !credential.Capabilities.Contains(permission, StringComparer.Ordinal)) throw new ManagementAccessDeniedException();
        var key = await secrets.ReadAsync(new(org, applicationId), credential.SecretReference, ct);
        if (key.Value is not { Length: >= 16 and <= 4096 } || key.Value.Any(c => c is < '!' or > '~'))
            throw new ManagementConnectionUnavailableException();
        return new() { OrganizationId = org, ApplicationId = applicationId, UserId = user, CredentialId = credentialId,
            ApplicationKey = app.ApplicationKey, Environment = app.Environment, BaseUrl = app.BaseUrl, Version = app.Version,
            Capabilities = credential.Capabilities.ToArray(), Key = key };
    }
}
