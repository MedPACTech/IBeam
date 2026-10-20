using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Management.Identity.Target.Services.Invitations;

/// <summary>Authoritative, bounded point reads only. No workflow resume, decryption or mail delivery.</summary>
public sealed class ManagementCustomerInvitationOperationQuery(IManagementIdentityOperationRepository operations,
    ITenantInviteStore invites, IManagementCustomerIdentityTargetAccessService access, IServiceProvider services,
    TimeProvider clock, IServiceOperationExecutor executor) : IManagementCustomerInvitationOperationQuery
{
    public Task<ManagementCustomerInvitationOperationDto?> GetAsync(ManagementIdentityOperationDto receipt, string authority, CancellationToken ct = default)
        => executor.ExecuteAsync(this, async token => {
            try {
                var actor = await access.RequireAsync(authority, token);
                if (receipt.Operation != authority || authority is not (ManagementCustomerIdentityOperations.UsersProvision or ManagementCustomerIdentityOperations.InvitesManage))
                    throw new ManagementAccessDeniedException();
                var operation = await operations.GetAsync(new(actor.IdentityRealmId, actor.OrganizationId, actor.ApplicationId), receipt.OperationId, token)
                    ?? throw new ManagementCustomerIdentityNotFoundException();
                if (operation.Operation != authority || operation.ApplicationKey != actor.ApplicationKey || operation.Environment != actor.Environment ||
                    operation.OperationId != receipt.OperationId || operation.ResourceId != receipt.ResourceId || operation.ResourceScope == Guid.Empty || operation.ExpiresUtc <= clock.GetUtcNow())
                    throw new ManagementCustomerIdentityUnavailableException();
                var scope = new ManagementTenantAdministrationScope(actor.IdentityRealmId, actor.ApplicationKey, actor.Environment);
                var tenant = operation.ResourceScope; Guid invite; var delivery = "unknown";
                if (authority == ManagementCustomerIdentityOperations.UsersProvision) {
                    if (receipt.ResourceId != tenant) throw new ManagementCustomerIdentityUnavailableException();
                    var repository = services.GetService<IManagementCustomerProvisioningRepository>();
                    var row = repository is null ? null : await repository.GetForOperationAsync(scope, tenant, receipt.OperationId, token);
                    if (row is null) { await access.RequireAsync(authority, token); return null; }
                    if (row.Scope != scope || row.TenantId != tenant || row.InviteId == Guid.Empty) throw new ManagementCustomerIdentityUnavailableException();
                    invite = row.InviteId; delivery = Delivery(row.DeliveryStatus, row.DeliveryLeaseUtc);
                } else invite = receipt.ResourceId;
                var native = await invites.GetAsync(tenant, invite, token);
                if (native is null) { await access.RequireAsync(authority, token); return null; }
                if (native.TenantId != tenant || native.InviteId != invite || native.Version is not { Length: > 0 and <= 256 }) throw new ManagementCustomerIdentityUnavailableException();
                if (native.LastTransitionOperationId is Guid latest && services.GetService<IManagementCustomerInvitationTransitionRepository>() is { } transitions) {
                    var row = await transitions.GetAsync(scope, tenant, latest, token);
                    if (row is not null) {
                        if (row.Scope != scope || row.TenantId != tenant || row.InviteId != invite || row.OperationId != latest) throw new ManagementCustomerIdentityUnavailableException();
                        delivery = Delivery(row.DeliveryStatus, row.DeliveryLeaseUtc);
                    }
                }
                var status = native.Status.ToLowerInvariant();
                if (status is not ("pending" or "sent" or "redeemed" or "expired" or "revoked")) throw new ManagementCustomerIdentityUnavailableException();
                if (status is "pending" or "sent" && native.ExpiresUtc <= clock.GetUtcNow()) status = "expired";
                if (status == "redeemed") status = "accepted";
                await access.RequireAsync(authority, token);
                return new ManagementCustomerInvitationOperationDto(tenant, invite, status, delivery, native.Version);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityNotFoundException or ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.identity.invitations.operation.read", PermissionEnabled = false, AuditEnabled = true }, ct);
    private string Delivery(string value, DateTimeOffset? lease)
    { if (value is not ("pending" or "sending" or "sent" or "failed" or "unknown" or "not-required" or "superseded")) throw new ManagementCustomerIdentityUnavailableException();
      return value == "sending" && lease <= clock.GetUtcNow() ? "unknown" : value; }
}
