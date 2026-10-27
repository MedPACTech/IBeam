using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Identity.Services.Tenants;

public sealed class IdentityTenantOwnershipProtection : IIdentityTenantOwnershipProtection
{
    private readonly IIdentityTenantOwnershipConditionalStore store;
    private readonly IIdentityTenantOwnershipRoleStore roles;
    private readonly IdentityTenantOwnershipOptions policy;
    private readonly IServiceOperationExecutor operations;
    public int ProtocolVersion => 1;
    public IdentityTenantOwnershipProtection(IIdentityTenantMembershipConditionalStore memberships, ITenantRoleStore roleStore,
        IOptions<IdentityTenantOwnershipOptions> options, IServiceOperationExecutor operations)
    {
        policy = options.Value;
        if (!policy.Enabled || !policy.IsValid() || memberships is not IIdentityTenantOwnershipConditionalStore conditional ||
            conditional.OwnershipProtocolVersion != 1 || !conditional.RequiresOwnershipProtection ||
            roleStore is not IIdentityTenantOwnershipRoleStore definitions || definitions.OwnershipProtocolVersion != 1 || !definitions.RequiresOwnershipProtection)
            throw new InvalidOperationException("Explicit ownership policy and compatible fenced native membership/role writers are required.");
        store = conditional; roles = definitions; this.operations = operations;
        policy = new() { Enabled = policy.Enabled, OwnerRoleNames = policy.OwnerRoleNames.ToArray() };
    }
    public Task EnsureAsync(Guid tenant, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token => {
            try {
            if (tenant == Guid.Empty || !policy.Enabled || !policy.IsValid()) throw new IdentityValidationException("Ownership policy is invalid.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var resolved = await roles.ReadOwnershipRolesAsync(tenant, policy.OwnerRoleNames, timeout.Token);
            if (resolved.Count != policy.OwnerRoleNames.Length || resolved.Any(x => x.TenantId != tenant || x.RoleId == Guid.Empty || !x.IsActive || !x.IsSystem ||
                    !policy.OwnerRoleNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase)) ||
                resolved.Select(x => x.RoleId).Distinct().Count() != resolved.Count || resolved.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != resolved.Count)
                throw new IdentityValidationException("Active immutable unambiguous tenant owner roles are required.");
            await store.ConfigureOwnershipProtectionAsync(tenant, resolved.Select(x => x.RoleId).Order().ToArray(), timeout.Token);
            } catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (IdentityConcurrencyException) { throw; }
            catch (IdentityValidationException) { throw new IdentityValidationException("Active immutable unambiguous tenant owner roles are required."); }
            catch { throw new IdentityProviderException("Tenant ownership protection is unavailable."); }
        }, new ServiceOperationExecutionOptions { OperationName = "identity.tenantownership.configure", TenantId = tenant,
            PermissionEnabled = false, AuditEnabled = false }, ct);
    public Task RequireGlobalAccountMutationAsync(Guid userId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, token => Task.FromException(new IdentityValidationException("Global account lifecycle mutations are not supported by tenant ownership protection.")),
            new ServiceOperationExecutionOptions { OperationName = "identity.tenantownership.global-account", EntityId = userId,
                PermissionEnabled = false, AuditEnabled = false }, ct);
}
