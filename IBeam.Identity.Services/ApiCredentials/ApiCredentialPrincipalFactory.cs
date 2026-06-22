using System.Security.Claims;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialPrincipalFactory : IApiCredentialPrincipalFactory
{
    private readonly ApiCredentialOptions _options;

    public ApiCredentialPrincipalFactory(IOptions<ApiCredentialOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public ClaimsPrincipal CreatePrincipal(ApiCredentialRecord credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var credentialId = credential.CredentialId.ToString("D");
        var tenantId = credential.TenantId.ToString("D");
        var claims = new List<Claim>
        {
            new("tenant_id", tenantId),
            new("tid", tenantId),
            new("sub", credentialId),
            new(ClaimTypes.NameIdentifier, credentialId),
            new("uid", credentialId),
            new("api_credential_id", credentialId),
            new("api_subject_type", "credential")
        };

        if (!string.IsNullOrWhiteSpace(credential.DisplayName))
        {
            claims.Add(new Claim("name", credential.DisplayName));
            claims.Add(new Claim(ClaimTypes.Name, credential.DisplayName));
        }

        if (!string.IsNullOrWhiteSpace(credential.AgentKey))
        {
            claims.Add(new Claim("api_agent_key", credential.AgentKey));
            claims.Add(new Claim("agent_key", credential.AgentKey));
        }

        foreach (var roleName in credential.RoleNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("role", roleName));
            claims.Add(new Claim(ClaimTypes.Role, roleName));
        }

        foreach (var roleId in credential.RoleIds.Where(x => x != Guid.Empty).Distinct())
        {
            var value = roleId.ToString("D");
            claims.Add(new Claim("rid", value));
            claims.Add(new Claim("role_id", value));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, _options.AuthorizationSchemeName, "name", "role"));
    }
}
