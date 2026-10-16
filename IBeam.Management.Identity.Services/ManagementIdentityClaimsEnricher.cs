using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Management.Identity.Services;

// Only the trusted password-MFA completion path (or its persisted session on refresh) supplies amr=mfa.
public sealed class ManagementIdentityClaimsEnricher(ManagementIdentityMembershipService members) : IClaimsEnricher
{
    public async Task<IReadOnlyList<ClaimItem>> EnrichAsync(ClaimsEnrichmentContext context, CancellationToken ct = default)
    {
        if (!context.CurrentClaims.Any(c => c.Type == "amr" && c.Value == "mfa") ||
            context.CurrentClaims.Any(c => c.Type == "pt"))
            throw new ManagementAccessDeniedException();
        await members.RequireAsync(context.UserId, context.TenantId, ct);
        return
        [
            new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
            new(ManagementClaimTypes.OrganizationId, context.TenantId.ToString("D")),
            new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead)
        ];
    }
}
