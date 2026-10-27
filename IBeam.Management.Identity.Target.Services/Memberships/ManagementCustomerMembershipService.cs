using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Memberships;

public sealed class ManagementCustomerMembershipService(IIdentityTenantMembershipAdministrationService administration,
    IIdentityTenantMembershipConditionalStore members, ITenantRoleStore roles, IManagementAssignableRoleReadRepository roleReads,
    IManagementAssignableRoleCatalogService catalog, IIdentityTenantStore tenants, IManagementIdentityOperationService receipts,
    IManagementCustomerIdentityTargetAccessService access, IIdentityTenantAccessEnforcement enforcement,
    IOptions<ManagementCustomerMembershipOptions> policy, IOptions<ManagementIdentityOperationOptions> options,
    IServiceOperationExecutor operations) : IManagementCustomerMembershipService
{
    public Task<ManagementCustomerMembershipResult> SetEnabledAsync(Guid tenant, Guid user, ChangeManagementCustomerMembership request, string version, string key, CancellationToken ct = default)
        => Change(tenant, user, request, null, version, key, ct);
    public Task<ManagementCustomerMembershipResult> SetRolesAsync(Guid tenant, Guid user, ChangeManagementCustomerRoles request, string version, string key, CancellationToken ct = default)
        => Change(tenant, user, null, request, version, key, ct);
    private Task<ManagementCustomerMembershipResult> Change(Guid tenant, Guid user, ChangeManagementCustomerMembership? status,
        ChangeManagementCustomerRoles? assignment, string version, string key, CancellationToken ct)
    {
        var authority = assignment is null ? ManagementCustomerIdentityOperations.MembershipsManage : ManagementCustomerIdentityOperations.RolesAssign;
        return operations.ExecuteAsync(this, async token => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            var cancel = timeout.Token;
            try {
                await access.RequireAsync(authority, cancel);
                if (enforcement.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(enforcement.Mode) || !policy.Value.IsValid()) throw new ManagementAccessDeniedException();
                if (tenant == Guid.Empty || user == Guid.Empty || status is null && assignment is null || string.IsNullOrWhiteSpace(version) || version == "*" ||
                    version.Length > 256 || version.Any(char.IsControl)) throw new ManagementCustomerIdentityInvalidRequestException();
                var customer = await tenants.FindByIdAsync(tenant, cancel) ?? throw new ManagementCustomerIdentityNotFoundException();
                if (customer.TenantId != tenant) throw new ManagementCustomerIdentityNotFoundException();
                if ((assignment is not null || status!.IsEnabled) && !customer.IsActive) throw new ManagementIdentityOperationConflictException();
                var selected = new List<TenantRole>();
                if (assignment is not null) {
                    var approved = await catalog.RequireAsync(tenant, assignment.PolicyKey, assignment.RoleIds, authority, cancel);
                    foreach (var item in approved) {
                        var role = await roles.GetRoleAsync(tenant, item.RoleId, cancel);
                        if (role is null || role.TenantId != tenant || !role.IsActive || role.Name != item.Name || role.Name.Contains(',')) throw new ManagementAccessDeniedException();
                        selected.Add(role);
                    }
                    await RequireEditableReplacement(tenant, user, assignment, selected, cancel);
                }
                var administrators = await Administrators(tenant, cancel);
                var payload = assignment is null ? JsonSerializer.SerializeToUtf8Bytes(new { IsEnabled = status!.IsEnabled, ExpectedVersion = version }) :
                    JsonSerializer.SerializeToUtf8Bytes(new { assignment.PolicyKey, RoleIds = assignment.RoleIds.Order().ToArray(), ExpectedVersion = version });
                var receipt = await receipts.ExecuteAsync(new() { Operation = authority, ResourceScope = tenant, ExistingResourceId = user, IdempotencyKey = key, CanonicalPayload = payload },
                    new Handler(async (context, inner) => {
                        var current = await members.FindStateAsync(tenant, user, inner) ?? throw new ManagementCustomerIdentityNotFoundException();
                        if (current.LastOperationId == context.OperationId) {
                            var applied = assignment is null ? current.IsEnabled == status!.IsEnabled : current.RoleIds.Order().SequenceEqual(assignment.RoleIds.Order());
                            if (!applied) return ManagementIdentityReconciliation.Unknown;
                            await members.RepairProjectionAsync(tenant, user, inner); return ManagementIdentityReconciliation.Applied;
                        }
                        if (current.Version != version) throw new ManagementCustomerIdentityPreconditionException();
                        return ManagementIdentityReconciliation.Absent;
                    }, async (context, inner) => {
                        await access.RequireAsync(authority, inner);
                        try {
                            if (assignment is null) await administration.SetEnabledAsync(tenant, user, status!.IsEnabled, version, context.OperationId, administrators, inner);
                            else await administration.SetRolesAsync(tenant, user, selected, version, context.OperationId, administrators, inner);
                        } catch (IdentityConcurrencyException) { throw new ManagementCustomerIdentityPreconditionException(); }
                        catch (IdentityNotFoundException) { throw new ManagementCustomerIdentityNotFoundException(); }
                        catch (IdentityValidationException) { throw new ManagementIdentityOperationConflictException(); }
                    }), cancel);
                var state = await members.FindStateAsync(tenant, user, cancel) ?? throw new ManagementCustomerIdentityNotFoundException();
                if (state.TenantId != tenant || state.UserId != user || state.RoleIds.Count > 32 || state.RoleNames.Count > 32 ||
                    state.Version is not { Length: > 0 and <= 256 } || state.RoleNames.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || x.Any(char.IsControl)))
                    throw new ManagementCustomerIdentityUnavailableException();
                var dto = new ManagementCustomerUserDto(tenant, user, null, null, null, state.IsEnabled ? "active" : "disabled", state.RoleIds, state.RoleNames) { Version = state.Version };
                await access.RequireAsync(authority, cancel); return new ManagementCustomerMembershipResult(receipt, dto);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or ManagementCustomerIdentityNotFoundException or
                ManagementIdentityOperationConflictException or ManagementCustomerIdentityPreconditionException or ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = authority, AuditAction = authority, EntityId = user, EntityName = "ManagementCustomerMembership",
            PermissionEnabled = false, AuditEnabled = true }, ct);
    }
    private async Task RequireEditableReplacement(Guid tenant, Guid user, ChangeManagementCustomerRoles request,
        IReadOnlyList<TenantRole> selected, CancellationToken ct)
    {
        var current = await members.FindStateAsync(tenant, user, ct) ?? throw new ManagementCustomerIdentityNotFoundException();
        if (current.TenantId != tenant || current.UserId != user || current.RoleIds.Count > 32 || current.RoleNames.Count > 32 ||
            current.RoleIds.Any(x => x == Guid.Empty) || current.RoleIds.Distinct().Count() != current.RoleIds.Count ||
            current.RoleNames.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || x.Any(char.IsControl)))
            throw new ManagementCustomerIdentityUnavailableException();
        // Replacement must not turn a narrow assignment policy into authority to revoke other roles.
        // Named legacy roles must first be normalized through a trusted native migration.
        if (current.RoleIds.Count == 0 && current.RoleNames.Count != 0) throw new ManagementIdentityOperationConflictException();
        var currentNames = selected.Where(x => current.RoleIds.Contains(x.RoleId)).Select(x => x.Name).ToList();
        foreach (var removed in current.RoleIds.Except(request.RoleIds).Chunk(8))
        {
            var approved = await catalog.RequireAsync(tenant, request.PolicyKey, removed, ManagementCustomerIdentityOperations.RolesAssign, ct);
            if (approved.Count != removed.Length || approved.Any(x => x.TenantId != tenant || !removed.Contains(x.RoleId)) ||
                approved.Select(x => x.RoleId).Distinct().Count() != removed.Length)
                throw new ManagementCustomerIdentityUnavailableException();
            currentNames.AddRange(approved.Select(x => x.Name));
        }
        if (!current.RoleNames.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(currentNames.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            throw new ManagementIdentityOperationConflictException();
    }
    private async Task<IReadOnlyList<Guid>> Administrators(Guid tenant, CancellationToken ct)
    {
        var rows = await roleReads.ReadAsync(tenant, policy.Value.AdministratorRoleNames, ct);
        if (rows.Count is < 1 or > 8 || rows.Any(x => x.TenantId != tenant || !x.IsActive || !policy.Value.AdministratorRoleNames.Contains(x.Name, StringComparer.Ordinal))) throw new ManagementAccessDeniedException();
        foreach (var row in rows) {
            var role = await roles.GetRoleAsync(tenant, row.RoleId, ct);
            if (role is null || !role.IsActive || !role.IsSystem || role.TenantId != tenant || role.Name != row.Name) throw new ManagementAccessDeniedException();
        }
        return rows.Select(x => x.RoleId).Distinct().Order().ToArray();
    }
    private sealed class Handler(Func<ManagementIdentityMutationContext, CancellationToken, Task<ManagementIdentityReconciliation>> reconcile,
        Func<ManagementIdentityMutationContext, CancellationToken, Task> apply) : IManagementIdentityMutationHandler
    { public Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct) => reconcile(context, ct);
      public Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct) => apply(context, ct); }
}
