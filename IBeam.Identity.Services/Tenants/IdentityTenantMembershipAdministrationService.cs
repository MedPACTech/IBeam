using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Services.Abstractions;
namespace IBeam.Identity.Services.Tenants;

/// <summary>Tenant-specific membership rules; no global user/password/default-tenant mutation.</summary>
public sealed class IdentityTenantMembershipAdministrationService(IIdentityTenantMembershipConditionalStore store, ITenantRoleStore roles,
    IIdentityTenantStore tenants, IServiceOperationExecutor operations) : IIdentityTenantMembershipAdministrationService
{
    public Task<IdentityTenantMembershipState> SetEnabledAsync(Guid tenant, Guid user, bool enabled, string version, Guid operation, IReadOnlyList<Guid> administrators, CancellationToken ct = default)
        => Run(tenant, user, "status", async token => {
            if (enabled) await Active(tenant, token);
            var approved = await AdministratorRoles(tenant, administrators, token);
            await store.ConfigureProtectionAsync(tenant, approved.Select(x => x.RoleId).ToArray(), token);
            var snapshot = await Snapshot(tenant, token);
            var current = Current(snapshot, user);
            if (current.LastOperationId == operation && current.IsEnabled == enabled) { await store.RepairProjectionAsync(tenant, user, token); return current; }
            return await Commit(snapshot, current, current with { IsEnabled = enabled }, version, operation, approved, token);
        }, ct);
    public Task<IdentityTenantMembershipState> SetRolesAsync(Guid tenant, Guid user, IReadOnlyList<TenantRole> desired, string version, Guid operation, IReadOnlyList<Guid> administrators, CancellationToken ct = default)
        => Run(tenant, user, "roles", async token => {
            await Active(tenant, token);
            if (desired is null || desired.Count > 32 || desired.Select(x => x.RoleId).Distinct().Count() != desired.Count ||
                desired.Any(x => x.TenantId != tenant || x.RoleId == Guid.Empty || !x.IsActive || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 100 || x.Name.Contains(',') || x.Name.Any(char.IsControl)))
                throw new IdentityValidationException("Membership roles are invalid.");
            foreach (var role in desired) {
                var currentRole = await roles.GetRoleAsync(tenant, role.RoleId, token);
                if (currentRole is null || currentRole.TenantId != tenant || currentRole.RoleId != role.RoleId || !currentRole.IsActive || currentRole.Name != role.Name) throw new IdentityValidationException("Membership role is unavailable.");
            }
            var approved = await AdministratorRoles(tenant, administrators, token);
            await store.ConfigureProtectionAsync(tenant, approved.Select(x => x.RoleId).ToArray(), token);
            var snapshot = await Snapshot(tenant, token); var current = Current(snapshot, user);
            var next = current with { RoleIds = desired.Select(x => x.RoleId).Order().ToArray(), RoleNames = desired.Select(x => x.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
            if (current.LastOperationId == operation && SameRoles(current, next)) { await store.RepairProjectionAsync(tenant, user, token); return current; }
            return await Commit(snapshot, current, next, version, operation, approved, token);
        }, ct);
    public Task<UserTenantRoleAssignment?> RevokeProtectedRolesAsync(Guid tenant, Guid user, IReadOnlyList<Guid> remove, CancellationToken ct = default)
        => Run<UserTenantRoleAssignment?>(tenant, user, "revoke", async token => {
            var snapshot = await Snapshot(tenant, token);
            if (snapshot.ProtectedAdministratorRoleIds.Count == 0) return null;
            await Active(tenant, token);
            var current = Current(snapshot, user); var administrators = await AdministratorRoles(tenant, snapshot.ProtectedAdministratorRoleIds, token);
            if (current.RoleIds.Count == 0 && current.RoleNames.Count != 0) throw new IdentityValidationException("Normalize legacy membership roles before administration.");
            var actual = new List<TenantRole>();
            foreach (var id in current.RoleIds) { var role = await roles.GetRoleAsync(tenant, id, token); if (role is { IsActive: true }) actual.Add(role); }
            var nextRoles = actual.Where(x => !remove.Contains(x.RoleId)).ToArray();
            var next = current with { RoleIds = nextRoles.Select(x => x.RoleId).Order().ToArray(), RoleNames = nextRoles.Select(x => x.Name).Order(StringComparer.Ordinal).ToArray() };
            await Commit(snapshot, current, next, current.Version, Guid.NewGuid(), administrators, token);
            return new(tenant, user, nextRoles);
        }, ct);
    private async Task<IdentityTenantMembershipState> Commit(IdentityTenantMembershipSnapshot snapshot, IdentityTenantMembershipState current,
        IdentityTenantMembershipState next, string version, Guid operation, IReadOnlyList<TenantRole> administrators, CancellationToken ct)
    {
        if (operation == Guid.Empty || current.Version != version || string.IsNullOrWhiteSpace(version) || version == "*") throw new IdentityConcurrencyException();
        if (!snapshot.ProtectedAdministratorRoleIds.Order().SequenceEqual(administrators.Select(x => x.RoleId).Order())) throw new IdentityConcurrencyException();
        bool Eligible(IdentityTenantMembershipState x) => x.IsEnabled && (x.RoleIds.Intersect(snapshot.ProtectedAdministratorRoleIds).Any() ||
            x.RoleIds.Count == 0 && x.RoleNames.Intersect(administrators.Select(r => r.Name), StringComparer.OrdinalIgnoreCase).Any());
        if (!snapshot.Members.Where(x => x.UserId != current.UserId).Append(next).Any(Eligible))
            throw new IdentityValidationException("The final eligible tenant administrator must remain enabled.");
        var result = await store.ReplaceStateAsync(next, version, snapshot.Version, operation, ct);
        await store.RepairProjectionAsync(current.TenantId, current.UserId, ct); return result;
    }
    private async Task<IReadOnlyList<TenantRole>> AdministratorRoles(Guid tenant, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids is not { Count: > 0 and <= 8 } || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Count) throw new IdentityValidationException("Administrator policy is invalid.");
        var result = new List<TenantRole>();
        foreach (var id in ids) {
            var role = await roles.GetRoleAsync(tenant, id, ct);
            if (role is null || role.TenantId != tenant || !role.IsActive || !role.IsSystem) throw new IdentityValidationException("An immutable active administrator role is required.");
            result.Add(role);
        }
        return result;
    }
    private async Task Active(Guid tenant, CancellationToken ct)
    { var row = await tenants.FindByIdAsync(tenant, ct); if (row is null || row.TenantId != tenant || !row.IsActive) throw new IdentityValidationException("Tenant is unavailable."); }
    private async Task<IdentityTenantMembershipSnapshot> Snapshot(Guid tenant, CancellationToken ct)
    {
        var value = await store.ReadSnapshotAsync(tenant, 5000, ct);
        if (value.TenantId != tenant || string.IsNullOrWhiteSpace(value.Version) || value.Version.Length > 256 || value.ProtectedAdministratorRoleIds.Count > 8 ||
            value.ProtectedAdministratorRoleIds.Any(x => x == Guid.Empty) || value.Members.Count > 5000 || value.Members.Select(x => x.UserId).Distinct().Count() != value.Members.Count ||
            value.Members.Any(x => x.TenantId != tenant || x.UserId == Guid.Empty || string.IsNullOrWhiteSpace(x.Version) || x.Version.Length > 256 ||
                x.RoleIds.Count > 32 || x.RoleNames.Count > 32 || x.RoleIds.Any(id => id == Guid.Empty))) throw new IdentityProviderException("Membership snapshot scope is invalid.");
        return value;
    }
    private static IdentityTenantMembershipState Current(IdentityTenantMembershipSnapshot snapshot, Guid user)
        => snapshot.Members.SingleOrDefault(x => x.UserId == user) ?? throw new IdentityNotFoundException("Membership was not found.");
    private static bool SameRoles(IdentityTenantMembershipState a, IdentityTenantMembershipState b) => a.RoleIds.Order().SequenceEqual(b.RoleIds.Order()) && a.RoleNames.Order(StringComparer.Ordinal).SequenceEqual(b.RoleNames.Order(StringComparer.Ordinal));
    private Task<T> Run<T>(Guid tenant, Guid user, string action, Func<CancellationToken, Task<T>> run, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            if (tenant == Guid.Empty || user == Guid.Empty) throw new IdentityValidationException("Membership scope is invalid.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20)); return await run(timeout.Token);
        }, new ServiceOperationExecutionOptions { OperationName = "identity.tenantmemberships." + action, TenantId = tenant, EntityId = user,
            PermissionEnabled = false, AuditEnabled = false }, ct); // Calling management/native role service owns authorization and metadata audit.
}
