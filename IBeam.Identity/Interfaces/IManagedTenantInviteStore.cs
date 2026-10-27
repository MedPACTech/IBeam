using IBeam.Identity.Models;
namespace IBeam.Identity.Interfaces;

/// <summary>Optional provider CAS. Native acceptance and invitation mutation writers participate.
/// An acceptance claim is durable and bound to a verified customer; it is not stolen on a timer.</summary>
public interface IManagedTenantInviteStore
{
    Task<TenantInviteRecord> BeginAcceptanceAsync(Guid tenantId, Guid inviteId, string tokenHash, Guid verifiedUserId, CancellationToken ct = default);
    Task<TenantInviteRecord> TransitionAsync(ManagedTenantInviteTransition request, CancellationToken ct = default);
}
public sealed class ManagedTenantInviteTransition
{
    public required Guid TenantId { get; init; }
    public required Guid InviteId { get; init; }
    public required Guid OperationId { get; init; }
    public required string Kind { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public string ExpectedVersion { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string? TokenHash { get; init; }
    public required DateTimeOffset NowUtc { get; init; }
    public DateTimeOffset? ExpiresUtc { get; init; }
    public DateTimeOffset? ResendWindowUtc { get; init; }
    public int ResendCount { get; init; }
    public Guid? CustomerActorId { get; init; }
    public ExternalTenantInviteActor? ExternalActor { get; init; }
    public override string ToString() => "Conditional invitation transition (version/token hash redacted)";
}
