using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Repositories.EntityFramework.OAuth;

// The device_code grant ships with an Azure Table backend only (v1) - see
// AzureTableOAuthDeviceAuthorizationStore. This placeholder keeps every other OAuth grant
// (authorization_code, refresh_token, client_credentials) working unchanged for EF-backed
// deployments, since OAuthTokenService now takes IOAuthDeviceAuthorizationStore as a required
// dependency; it only throws if a device_code request actually reaches an EF-backed deployment.
// Register a real implementation to support device_code there.
public sealed class NotImplementedOAuthDeviceAuthorizationStore : IOAuthDeviceAuthorizationStore
{
    private static NotSupportedException NotSupported() => new(
        "The OAuth device_code grant has no Entity Framework store implementation yet. " +
        "Register a real IOAuthDeviceAuthorizationStore, or use the Azure Table backend.");

    public Task<OAuthDeviceAuthorizationRecord> CreateAsync(OAuthDeviceAuthorizationRecord authorization, CancellationToken ct = default) => throw NotSupported();
    public Task<OAuthDeviceAuthorizationRecord?> GetByDeviceCodeHashAsync(string deviceCodeHash, CancellationToken ct = default) => throw NotSupported();
    public Task<OAuthDeviceAuthorizationRecord?> GetByUserCodeAsync(string userCode, CancellationToken ct = default) => throw NotSupported();
    public Task<OAuthDeviceAuthorizationRecord?> TryApproveAsync(string userCode, Guid userId, Guid tenantId, IReadOnlyList<string> grantedScopes, DateTimeOffset approvedUtc, CancellationToken ct = default) => throw NotSupported();
    public Task<OAuthDeviceAuthorizationRecord?> TryDenyAsync(string userCode, DateTimeOffset deniedUtc, CancellationToken ct = default) => throw NotSupported();
    public Task<OAuthDeviceAuthorizationRecord?> TryConsumeAsync(string deviceCodeHash, DateTimeOffset consumedUtc, CancellationToken ct = default) => throw NotSupported();
    public Task<bool> RecordPollAsync(string deviceCodeHash, DateTimeOffset polledUtc, int minIntervalSeconds, CancellationToken ct = default) => throw NotSupported();
}
