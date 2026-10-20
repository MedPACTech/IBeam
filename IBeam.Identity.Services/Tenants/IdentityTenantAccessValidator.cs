using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Services.Abstractions;
namespace IBeam.Identity.Services.Tenants;

public sealed class IdentityTenantAccessValidator(IIdentityTenantMembershipConditionalStore members, IIdentityTenantStore tenants,
    IServiceOperationExecutor operations) : IIdentityTenantAccessValidator
{
    public Task<IdentityTenantAccessState> RequireAsync(Guid user, Guid tenant, string? version, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token => {
            try {
                if (user == Guid.Empty || tenant == Guid.Empty || tenants is not IIdentityTenantConditionalStore conditional) throw new IdentityUnauthorizedException("Tenant access is unavailable.");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var customer = await conditional.FindVersionedAsync(tenant, deadline.Token);
                var membership = await members.FindStateAsync(tenant, user, deadline.Token);
                if (customer is null || customer.Tenant.TenantId != tenant || !customer.Tenant.IsActive || string.IsNullOrWhiteSpace(customer.Version) ||
                    membership is null || membership.TenantId != tenant || membership.UserId != user || !membership.IsEnabled || string.IsNullOrWhiteSpace(membership.Version))
                    throw new IdentityUnauthorizedException("Tenant access is unavailable.");
                var current = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { TenantVersion = customer.Version, MembershipVersion = membership.Version, tenant, user })));
                if (version is not null && version != current) throw new IdentityUnauthorizedException("Tenant access has changed; sign in again.");
                return new IdentityTenantAccessState(user, tenant, current, membership.RoleNames, membership.RoleIds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (IdentityUnauthorizedException) { throw; }
            catch { throw new IdentityUnauthorizedException("Tenant access is unavailable."); }
        }, new ServiceOperationExecutionOptions { OperationName = "identity.tenant-access.require", TenantId = tenant, EntityId = user,
            PermissionEnabled = false, AuditEnabled = false }, ct);
}

/// <summary>Standard IBeam JWT issue/refresh integration. A stale refresh must reauthenticate, not reuse old role claims.</summary>
public sealed class IdentityTenantAccessClaimsEnricher(IIdentityTenantAccessValidator access) : IClaimsEnricher
{
    public async Task<IReadOnlyList<ClaimItem>> EnrichAsync(ClaimsEnrichmentContext context, CancellationToken ct = default)
    {
        var versions = context.CurrentClaims.Where(x => x.Type == IdentityTenantAccessClaims.Version).ToArray();
        if (versions.Length > 1) throw new IdentityUnauthorizedException("Tenant access is unavailable.");
        var state = await access.RequireAsync(context.UserId, context.TenantId, versions.SingleOrDefault()?.Value, ct);
        if (context.CurrentClaims.Where(x => x.Type == "role").Any(x => !state.RoleNames.Contains(x.Value, StringComparer.OrdinalIgnoreCase)) ||
            context.CurrentClaims.Where(x => x.Type == "rid").Any(x => !Guid.TryParse(x.Value, out var id) || !state.RoleIds.Contains(id)))
            throw new IdentityUnauthorizedException("Tenant access has changed; sign in again.");
        return [new(IdentityTenantAccessClaims.Version, state.Version)];
    }
}
