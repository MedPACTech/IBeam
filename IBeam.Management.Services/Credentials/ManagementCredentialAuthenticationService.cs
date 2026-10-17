using System.Net;
using System.Security.Claims;
using IBeam.Services.Abstractions;

namespace IBeam.Management.Services.Credentials;

public interface IManagementCredentialAuthenticationService
{
    Task<ClaimsPrincipal> AuthenticateAsync(string apiKey, IPAddress? remoteAddress, CancellationToken ct = default);
}

/// <summary>Business authentication boundary. No raw key is retained in the principal or operation context.</summary>
public sealed class ManagementCredentialAuthenticationService(
    IManagementCredentialValidator credentials,
    ManagementCredentialNetworkPolicy network,
    ManagementCredentialSecretCodec secrets,
    IServiceOperationExecutor operations) : IManagementCredentialAuthenticationService
{
    [IBeamOperation("management.credentials.authenticate", Permission = false)]
    public Task<ClaimsPrincipal> AuthenticateAsync(string apiKey, IPAddress? remoteAddress, CancellationToken ct = default)
    {
        var candidateId = secrets.TryReadId(apiKey, out var id) ? id : (Guid?)null;
        return operations.ExecuteAsync(this, async token =>
        {
            if (!network.Allows(remoteAddress)) throw new ManagementAccessDeniedException();
            var credential = await credentials.ValidateAsync(apiKey, token) ?? throw new ManagementAccessDeniedException();
            var claims = new List<Claim>
            {
                new("sub", "management-credential:" + credential.CredentialId.ToString("D")),
                new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Credential),
                new(ManagementClaimTypes.CredentialId, credential.CredentialId.ToString("D")),
                new(ManagementClaimTypes.CredentialVersion, credential.Version),
                new(ManagementClaimTypes.ApplicationKey, credential.ApplicationKey),
                new(ManagementClaimTypes.Environment, credential.Environment)
            };
            claims.AddRange(credential.Capabilities.Select(c => new Claim(ManagementClaimTypes.Permission, c)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "IBeam.Management.Credential", "sub", "role"));
        }, new ServiceOperationExecutionOptions
        {
            OperationName = "management.credentials.authenticate", AuditAction = "management.credentials.authenticate",
            EntityName = "ManagementApiCredential", EntityId = candidateId, PermissionEnabled = false, AuditEnabled = true
        }, ct);
    }
}
