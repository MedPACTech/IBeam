using System.Security.Claims;
using IBeam.Management;

namespace IBeam.Management.Services;

internal static class ManagementPrincipalGuard
{
    public static ClaimsIdentity RequireIdentity(ClaimsPrincipal principal, string subjectType, string operation)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var identities = principal.Identities.Where(x => x.IsAuthenticated).ToArray();
        if (identities.Length != 1)
            throw new ManagementAccessDeniedException();

        var identity = identities[0];
        if (!string.Equals(RequireSingle(identity, ManagementClaimTypes.SubjectType), subjectType, StringComparison.Ordinal) ||
            !identity.HasClaim(ManagementClaimTypes.Permission, operation))
            throw new ManagementAccessDeniedException();

        // Do not accept two conflicting subject identities through the common JWT claim aliases.
        var subjects = identity.Claims
            .Where(c => c.Type is "uid" or "sub" || c.Type == ClaimTypes.NameIdentifier)
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (subjects.Length > 1 || subjects.Any(string.IsNullOrWhiteSpace))
            throw new ManagementAccessDeniedException();
        return identity;
    }

    public static string RequireSingle(ClaimsIdentity identity, string type)
    {
        var values = identity.FindAll(type).Select(c => c.Value).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new ManagementAccessDeniedException();
        return values[0];
    }

    public static string RequireHumanUserId(ClaimsIdentity identity)
        => identity.FindFirst("uid")?.Value ??
           identity.FindFirst("sub")?.Value ??
           identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
           throw new ManagementAccessDeniedException();

    public static Guid RequireGuid(ClaimsIdentity identity, string type)
    {
        if (!Guid.TryParse(RequireSingle(identity, type), out var value) || value == Guid.Empty)
            throw new ManagementAccessDeniedException();
        return value;
    }
}
