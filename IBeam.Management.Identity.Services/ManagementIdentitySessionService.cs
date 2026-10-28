using IBeam.Management.Operations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Identity.Services;

/// <summary>No membership or session caching: revocation is enforced on the next request.</summary>
public sealed class ManagementIdentitySessionService(
    ManagementIdentityMembershipService members,
    IAuthSessionStore sessions,
    IOptions<ManagementIdentityOptions> identityOptions,
    IOptions<JwtOptions> jwtOptions)
{
    public async Task<ManagementLiveMember> ValidateAccessAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var identities = principal.Identities.Where(x => x.IsAuthenticated).ToArray();
        if (identities.Length != 1) throw new ManagementAccessDeniedException();
        var identity = identities[0];
        var userId = ReadGuid(identity, "uid");
        if (ReadGuid(identity, "sub") != userId ||
            ReadGuid(identity, "tid") != identityOptions.Value.OrganizationId ||
            ReadGuid(identity, ManagementClaimTypes.OrganizationId) != identityOptions.Value.OrganizationId ||
            Read(identity, ManagementClaimTypes.SubjectType) != ManagementSubjectTypes.Human ||
            !identity.HasClaim("amr", "mfa") || identity.HasClaim(c => c.Type == "pt"))
            throw new ManagementAccessDeniedException();
        var sid = Read(identity, "sid");
        var matching = (await sessions.GetByUserAsync(userId, ct)).Where(x => x.SessionId == sid).ToArray();
        if (matching.Length != 1 || matching[0].UserId != userId) throw new ManagementAccessDeniedException();
        return await ValidateRecordAsync(matching[0], ct);
    }

    public async Task<AuthSessionRecord> ValidateRefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 4096) throw new ManagementAccessDeniedException();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken.Trim()))).ToLowerInvariant();
        var record = await sessions.GetByRefreshTokenHashAsync(hash, ct) ?? throw new ManagementAccessDeniedException();
        var indexed = (await sessions.GetByUserAsync(record.UserId, ct))
            .SingleOrDefault(x => x.SessionId == record.SessionId);
        if (indexed is null || indexed.RefreshTokenHash != hash || indexed.RevokedAt.HasValue)
            throw new ManagementAccessDeniedException();
        await ValidateRecordAsync(record, ct);
        return record;
    }

    private async Task<ManagementLiveMember> ValidateRecordAsync(AuthSessionRecord record, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var jwt = jwtOptions.Value;
        if (record.TenantId != identityOptions.Value.OrganizationId || record.RevokedAt.HasValue ||
            record.RefreshTokenExpiresAt <= now ||
            jwt.SessionInactivityMinutes is int minutes && record.LastSeenAt.AddMinutes(minutes) <= now ||
            jwt.SessionAbsoluteLifetimeDays is int days && record.CreatedAt.AddDays(days) <= now)
            throw new ManagementAccessDeniedException();
        IReadOnlyList<ClaimItem> claims;
        try { claims = JsonSerializer.Deserialize<List<ClaimItem>>(record.ClaimsJson) ?? []; }
        catch (JsonException) { throw new ManagementAccessDeniedException(); }
        if (!claims.Any(x => x.Type == "amr" && x.Value == "mfa") ||
            !claims.Any(x => x.Type == ManagementClaimTypes.SubjectType && x.Value == ManagementSubjectTypes.Human) ||
            !claims.Any(x => x.Type == ManagementClaimTypes.OrganizationId && x.Value == record.TenantId.ToString("D")) ||
            !claims.Any(x => x.Type == "sid" && x.Value == record.SessionId) ||
            claims.Any(x => x.Type == "pt"))
            throw new ManagementAccessDeniedException();
        return await members.RequireAsync(record.UserId, record.TenantId, ct);
    }

    public static void RefreshPrincipal(ClaimsPrincipal principal, ManagementLiveMember member)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        var stale = identity.Claims.Where(c => c.Type is "role" or "roles" or "rid" or "role_id" or
            "permission" or "permissions" or "scope" or "scp" ||
            c.Type == ClaimTypes.Role || c.Type == ManagementClaimTypes.Permission).ToArray();
        foreach (var claim in stale) identity.RemoveClaim(claim);
        foreach (var role in member.Membership.Roles) identity.AddClaim(new Claim("role", role));
        foreach (var permission in ManagementIdentityPermissions.ForRoles(member.Membership.Roles))
            identity.AddClaim(new Claim(ManagementClaimTypes.Permission, permission));
    }
    public static void RefreshPrincipal(ClaimsPrincipal principal, ManagementLiveMember member, ManagementIdentityOptions options)
        => RefreshPrincipal(principal, member, options, ManagementOperationRegistry.BuiltIn);
    public static void RefreshPrincipal(ClaimsPrincipal principal, ManagementLiveMember member, ManagementIdentityOptions options, ManagementOperationRegistry registry)
    {
        RefreshPrincipal(principal, member);
        var identity = (ClaimsIdentity)principal.Identity!;
        var userId = ReadGuid(identity, "uid");
        if (userId != member.User.UserId || member.Membership.TenantId != options.OrganizationId ||
            ReadGuid(identity, ManagementClaimTypes.OrganizationId) != options.OrganizationId ||
            !ManagementIdentityPermissions.ValidCustomerDelegations(options, registry)) throw new ManagementAccessDeniedException();
        foreach (var permission in ManagementIdentityPermissions.CustomerDelegationsFor(userId, member.Membership.Roles, options, registry))
            identity.AddClaim(new Claim(ManagementClaimTypes.Permission, permission));
    }

    private static string Read(ClaimsIdentity identity, string type)
    {
        var claims = identity.FindAll(type).ToArray();
        if (claims.Length != 1 || string.IsNullOrWhiteSpace(claims[0].Value)) throw new ManagementAccessDeniedException();
        return claims[0].Value;
    }
    private static Guid ReadGuid(ClaimsIdentity identity, string type)
        => Guid.TryParse(Read(identity, type), out var id) && id != Guid.Empty ? id : throw new ManagementAccessDeniedException();
}
