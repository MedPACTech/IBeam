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
            new("api_credential_name", credential.DisplayName),
            new("principal_type", AccessSubjectTypes.ApiCredential),
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

        foreach (var allowedAgentKey in credential.AllowedAgentKeys ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(allowedAgentKey))
                claims.Add(new Claim("allowed_agent_key", allowedAgentKey.Trim()));
        }

        foreach (var roleName in credential.RoleNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized = roleName.Trim();
            claims.Add(new Claim("role", normalized));
            claims.Add(new Claim(ClaimTypes.Role, normalized));

            if (TryConsumePrefix(normalized, "api-scope:", out var scope))
                claims.Add(new Claim("scope", scope));
            else if (TryConsumePrefix(normalized, "tool:", out var tool))
                claims.Add(new Claim("tool", tool));
            else if (TryConsumePrefix(normalized, "api-agent:", out var apiAgent))
                claims.Add(new Claim("allowed_agent_key", apiAgent));
            else if (TryConsumePrefix(normalized, "agent:", out var agent))
                claims.Add(new Claim("allowed_agent_key", agent));
        }

        foreach (var roleId in credential.RoleIds.Where(x => x != Guid.Empty).Distinct())
        {
            var value = roleId.ToString("D");
            claims.Add(new Claim("rid", value));
            claims.Add(new Claim("role_id", value));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, _options.AuthorizationSchemeName, "name", "role"));
    }

    private static bool TryConsumePrefix(string value, string prefix, out string remainder)
    {
        remainder = string.Empty;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        remainder = value[prefix.Length..].Trim();
        return remainder.Length > 0;
    }
}
