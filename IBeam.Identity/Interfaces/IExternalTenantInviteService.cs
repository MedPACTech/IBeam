using IBeam.Identity.Models;
namespace IBeam.Identity.Interfaces;

/// <summary>Trusted server adapter only. The caller owns authorization and durable delivery claims.
/// No customer inviter, membership, password or token is returned.</summary>
public interface IExternalTenantInviteService
{
    bool SupportsDurableCreation { get; }
    Task<TenantInviteInfo> EnsureAsync(ExternalTenantInviteRequest request, CancellationToken ct = default);
    Task DeliverAsync(Guid tenantId, Guid inviteId, Guid operationId, string token, CancellationToken ct = default);
}
/// <summary>Optional stable insert and token-index repair. Never replaces an existing invitation.</summary>
public interface IExternalTenantInviteStore
{
    Task<TenantInviteRecord> EnsureAsync(TenantInviteRecord invite, CancellationToken ct = default);
}
public sealed record ExternalTenantInviteActor(Guid OrganizationId, Guid UserId, Guid ApplicationId, Guid OperationId);
public sealed class ExternalTenantInviteRequest
{
    public required Guid TenantId { get; init; }
    public required Guid InviteId { get; init; }
    public required ExternalTenantInviteActor Actor { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public string Email { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Token { get; init; } = "";
    public required IReadOnlyList<Guid> RoleIds { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required DateTimeOffset ExpiresUtc { get; init; }
    public override string ToString() => "External invitation request (recipient and token redacted)";
}
