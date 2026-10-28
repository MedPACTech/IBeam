using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("IBeam.Identity.Services")]

namespace IBeam.Identity.Interfaces;

/// <summary>Trusted native scope only. The optional management bridge verifies the original confirmed journal job and live worker lease.</summary>
public sealed record IdentityTenantPurgeScope(string ApplicationKey, string Environment, Guid OrganizationId, Guid ApplicationId,
    Guid RealmId, Guid TenantId, Guid ActorUserId, Guid JobId, string ExpectedTenantVersion);
/// <summary>Public operational binding, not a credential or authority grant.</summary>
public static class IdentityTenantPurgeScopeBinding
{
    public static string Fingerprint(IdentityTenantPurgeScope scope) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(scope)));
}
public interface IIdentityTenantPurgeLease
{
    Task RequireAsync(CancellationToken ct = default);
}
/// <summary>Required trusted server adapter. No default implementation and no customer/support HTTP equivalent.</summary>
public interface IIdentityTenantPurgeAuthority
{
    Task<IIdentityTenantPurgeLease> RequireConfirmedAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default);
}
/// <summary>Opaque native authorization created only after the native service's trusted verifier succeeds. It is not an HTTP DTO or bypass flag.</summary>
public sealed class IdentityTenantPurgeAuthorization
{
    public IdentityTenantPurgeScope Scope { get; }
    public string ScopeHash { get; }
    [JsonIgnore] private readonly IIdentityTenantPurgeLease lease;
    internal IdentityTenantPurgeAuthorization(IdentityTenantPurgeScope scope, IIdentityTenantPurgeLease lease)
    {
        Scope = scope; this.lease = lease;
        ScopeHash = IdentityTenantPurgeScopeBinding.Fingerprint(scope);
    }
    public Task RequireLeaseAsync(CancellationToken ct = default) => lease.RequireAsync(ct);
    public override string ToString() => "Verified tenant purge authorization (server-only)";
}
/// <summary>Entity-family persistence boundary. Native service owns authorization and orchestration; a store never calls another store.</summary>
public interface IIdentityTenantPurgeResourceStore
{
    int ProtocolVersion { get; }
    string Key { get; }
    bool ProtectsAllWriters { get; }
    Task<bool> HasPurgeMarkerAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default);
    Task PreviewAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> FencePageAsync(IdentityTenantPurgeAuthorization authorization, int pageSize, CancellationToken ct = default);
    Task<bool> IsFencedAsync(IdentityTenantPurgeAuthorization authorization, CancellationToken ct = default);
    Task RequireFencedAsync(IdentityTenantPurgeAuthorization authorization, CancellationToken ct = default);
    Task<bool> CleanupPageAsync(IdentityTenantPurgeAuthorization authorization, int pageSize, CancellationToken ct = default);
    Task<bool> VerifyEmptyAsync(IdentityTenantPurgeAuthorization authorization, CancellationToken ct = default);
}
public static class IdentityTenantPurgeInventory
{
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { "tenant-metadata", "tenant-memberships", "user-membership-indexes", "tenant-roles", "tenant-invitations",
        "tenant-api-credentials", "tenant-agent-users", "tenant-agent-credentials", "tenant-oauth-consents", "tenant-access-catalog-overrides" });
}
public interface IIdentityTenantPurgeService
{
    bool SupportsTenantPurge { get; }
    Task<bool> HasPurgeMarkerAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default);
    Task PreviewAsync(Guid tenantId, CancellationToken ct = default);
    Task FenceAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default);
    Task RequireFencedAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default);
    Task CleanupAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default);
    Task VerifyAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default);
}
public sealed class IdentityTenantPurgePendingException() : Exception("Tenant Identity cleanup is still running.");
