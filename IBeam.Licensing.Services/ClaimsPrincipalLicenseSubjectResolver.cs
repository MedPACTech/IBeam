using System.Security.Claims;

namespace IBeam.Licensing.Services;

public sealed class ClaimsPrincipalLicenseSubjectResolver : ILicenseSubjectResolver
{
    public LicenseSubject? ResolveSubject(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var explicitType = ReadClaim(principal, LicenseSubjectClaimTypes.SubjectType);
        var explicitId = ReadClaim(principal, LicenseSubjectClaimTypes.SubjectId);
        if (!string.IsNullOrWhiteSpace(explicitType) && !string.IsNullOrWhiteSpace(explicitId))
            return new LicenseSubject(explicitType, explicitId, ReadDisplayName(principal));

        var agentUserId = ReadClaim(principal, LicenseSubjectClaimTypes.AgentUserId);
        if (!string.IsNullOrWhiteSpace(agentUserId))
            return new LicenseSubject(LicenseSubjectTypes.Agent, agentUserId, ReadDisplayName(principal));

        var apiCredentialId = ReadClaim(principal, LicenseSubjectClaimTypes.ApiCredentialId)
                              ?? ReadClaim(principal, "client_id");
        if (!string.IsNullOrWhiteSpace(apiCredentialId))
            return new LicenseSubject(LicenseSubjectTypes.ApiCredential, apiCredentialId, ReadDisplayName(principal));

        var userId = ReadClaim(principal, ClaimTypes.NameIdentifier)
                     ?? ReadClaim(principal, "sub");
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : new LicenseSubject(LicenseSubjectTypes.User, userId, ReadDisplayName(principal));
    }

    private static string? ReadDisplayName(ClaimsPrincipal principal)
        => ReadClaim(principal, ClaimTypes.Name)
           ?? ReadClaim(principal, "name");

    private static string? ReadClaim(ClaimsPrincipal principal, string type)
        => principal.Claims.FirstOrDefault(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase))?.Value;
}
