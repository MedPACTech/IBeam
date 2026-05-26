using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableAuthSessionStore : IAuthSessionStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableAuthSessionStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task SaveAsync(AuthSessionRecord record, CancellationToken ct = default)
    {
        try
        {
            var table = GetTable();
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
            await table.UpsertEntityAsync(ToRefreshHashEntity(record), TableUpdateMode.Replace, ct).ConfigureAwait(false);
            await table.UpsertEntityAsync(ToUserSessionEntity(record), TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AuthSessionRecord?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
    {
        try
        {
            var table = GetTable();
            var response = await table.GetEntityIfExistsAsync<AuthSessionEntity>(
                PartitionForRefreshHash(refreshTokenHash),
                RowForRefreshHash(refreshTokenHash),
                cancellationToken: ct).ConfigureAwait(false);

            return response.HasValue ? ToModel(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task DeleteByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
    {
        try
        {
            var table = GetTable();
            var hashPk = PartitionForRefreshHash(refreshTokenHash);
            var hashRk = RowForRefreshHash(refreshTokenHash);

            var existing = await table.GetEntityIfExistsAsync<AuthSessionEntity>(
                hashPk,
                hashRk,
                cancellationToken: ct).ConfigureAwait(false);

            if (!existing.HasValue)
                return;

            await table.DeleteEntityAsync(hashPk, hashRk, cancellationToken: ct).ConfigureAwait(false);

            var e = existing.Value;
            if (!string.IsNullOrWhiteSpace(e.UserId) && !string.IsNullOrWhiteSpace(e.SessionId))
            {
                try
                {
                    await table.DeleteEntityAsync(
                        PartitionForUser(e.UserId),
                        RowForSessionId(e.SessionId),
                        cancellationToken: ct).ConfigureAwait(false);
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                {
                    // no-op (index may be absent for legacy rows)
                }
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // no-op
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<AuthSessionRecord>> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var table = GetTable();
            var results = new List<AuthSessionRecord>();
            var filter = $"PartitionKey eq '{PartitionForUser(userId)}'";

            await foreach (var e in table.QueryAsync<AuthSessionEntity>(filter: filter, cancellationToken: ct).ConfigureAwait(false))
                results.Add(ToModel(e));

            return results;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<bool> RevokeBySessionIdAsync(Guid userId, string sessionId, CancellationToken ct = default)
    {
        try
        {
            var table = GetTable();
            var userPk = PartitionForUser(userId);
            var userRk = RowForSessionId(sessionId);
            var now = DateTimeOffset.UtcNow;

            var indexed = await table.GetEntityIfExistsAsync<AuthSessionEntity>(
                userPk,
                userRk,
                cancellationToken: ct).ConfigureAwait(false);

            if (!indexed.HasValue)
                return false;

            var userEntity = indexed.Value;
            userEntity.RevokedAt = now;
            await table.UpdateEntityAsync(userEntity, userEntity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(userEntity.RefreshTokenHash))
            {
                try
                {
                    var hashPk = PartitionForRefreshHash(userEntity.RefreshTokenHash);
                    var hashRk = RowForRefreshHash(userEntity.RefreshTokenHash);
                    var hashEntityResponse = await table.GetEntityIfExistsAsync<AuthSessionEntity>(
                        hashPk,
                        hashRk,
                        cancellationToken: ct).ConfigureAwait(false);

                    if (hashEntityResponse.HasValue)
                    {
                        var hashEntity = hashEntityResponse.Value;
                        hashEntity.RevokedAt = now;
                        await table.UpdateEntityAsync(hashEntity, hashEntity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    }
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                {
                    // no-op
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TableClient GetTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AuthSessionsTableName));

    private static string PartitionForRefreshHash(string hash)
    {
        var h = (hash ?? string.Empty).Trim().ToLowerInvariant();
        var a = h.Length > 0 ? h[0] : '0';
        var b = h.Length > 1 ? h[1] : '0';
        return $"RTH|{a}{b}";
    }

    private static string RowForRefreshHash(string hash) => (hash ?? string.Empty).Trim().ToLowerInvariant();

    private static string PartitionForUser(Guid userId) => $"USR|{userId:D}";
    private static string PartitionForUser(string userId) => $"USR|{(userId ?? string.Empty).Trim().ToLowerInvariant()}";

    private static string RowForSessionId(string sessionId)
        => $"SID|{(sessionId ?? string.Empty).Trim().ToLowerInvariant()}";

    private static AuthSessionEntity ToRefreshHashEntity(AuthSessionRecord r)
        => new()
        {
            PartitionKey = PartitionForRefreshHash(r.RefreshTokenHash),
            RowKey = RowForRefreshHash(r.RefreshTokenHash),
            SessionId = r.SessionId,
            RefreshTokenHash = r.RefreshTokenHash,
            UserId = r.UserId.ToString("D"),
            TenantId = r.TenantId.ToString("D"),
            ClaimsJson = r.ClaimsJson,
            CreatedAt = r.CreatedAt,
            LastSeenAt = r.LastSeenAt,
            RefreshTokenExpiresAt = r.RefreshTokenExpiresAt,
            RevokedAt = r.RevokedAt,
            DeviceInfo = r.DeviceInfo
        };

    private static AuthSessionEntity ToUserSessionEntity(AuthSessionRecord r)
        => new()
        {
            PartitionKey = PartitionForUser(r.UserId),
            RowKey = RowForSessionId(r.SessionId),
            SessionId = r.SessionId,
            RefreshTokenHash = r.RefreshTokenHash,
            UserId = r.UserId.ToString("D"),
            TenantId = r.TenantId.ToString("D"),
            ClaimsJson = r.ClaimsJson,
            CreatedAt = r.CreatedAt,
            LastSeenAt = r.LastSeenAt,
            RefreshTokenExpiresAt = r.RefreshTokenExpiresAt,
            RevokedAt = r.RevokedAt,
            DeviceInfo = r.DeviceInfo
        };

    private static AuthSessionRecord ToModel(AuthSessionEntity e)
        => new(
            RefreshTokenHash: ResolveRefreshTokenHash(e),
            SessionId: e.SessionId,
            UserId: Guid.Parse(e.UserId),
            TenantId: Guid.Parse(e.TenantId),
            ClaimsJson: e.ClaimsJson,
            CreatedAt: e.CreatedAt,
            LastSeenAt: e.LastSeenAt,
            RefreshTokenExpiresAt: e.RefreshTokenExpiresAt,
            RevokedAt: e.RevokedAt,
            DeviceInfo: e.DeviceInfo);

    private static string ResolveRefreshTokenHash(AuthSessionEntity e)
        => string.IsNullOrWhiteSpace(e.RefreshTokenHash)
            ? e.RowKey
            : e.RefreshTokenHash;
}
