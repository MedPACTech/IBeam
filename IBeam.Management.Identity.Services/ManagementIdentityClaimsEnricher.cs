using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Identity.Services;

// Only the trusted password-MFA completion path (or its persisted session on refresh) supplies amr=mfa.
public sealed class ManagementIdentityClaimsEnricher(ManagementIdentityMembershipService members) : IClaimsEnricher
{
    private readonly IOptionsMonitor<ManagementIdentityOptions>? _options;
    public ManagementIdentityClaimsEnricher(ManagementIdentityMembershipService members, IOptionsMonitor<ManagementIdentityOptions> options)
        : this(members) => _options = options;
    public async Task<IReadOnlyList<ClaimItem>> EnrichAsync(ClaimsEnrichmentContext context, CancellationToken ct = default)
    {
        if (!context.CurrentClaims.Any(c => c.Type == "amr" && c.Value == "mfa") ||
            context.CurrentClaims.Any(c => c.Type == "pt"))
            throw new ManagementAccessDeniedException();
        var member = await members.RequireAsync(context.UserId, context.TenantId, ct);
        return
        [
            new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
            new(ManagementClaimTypes.OrganizationId, context.TenantId.ToString("D")),
            .. ManagementIdentityPermissions.ForRoles(member.Membership.Roles).Select(x => new ClaimItem(ManagementClaimTypes.Permission, x)),
            .. (_options is null ? [] : ManagementIdentityPermissions.CustomerDelegationsFor(context.UserId, member.Membership.Roles,
                _options.CurrentValue)).Select(x => new ClaimItem(ManagementClaimTypes.Permission, x))
        ];
    }
}
