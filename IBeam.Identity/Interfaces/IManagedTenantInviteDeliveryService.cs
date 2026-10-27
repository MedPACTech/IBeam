namespace IBeam.Identity.Interfaces;
/// <summary>Trusted delivery after a conditional native transition. Contains no customer impersonation or membership changes.</summary>
public interface IManagedTenantInviteDeliveryService
{
    Task DeliverAsync(Guid tenantId, Guid inviteId, Guid transitionOperationId, string token, CancellationToken ct = default);
}
