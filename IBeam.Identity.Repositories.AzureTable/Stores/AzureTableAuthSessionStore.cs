using Azure.Data.Tables;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
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
            await table.UpsertEntityAsync(ToEntity(record), TableUpdateMode.Replace, ct).ConfigureAwait(false);
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
                PartitionForHash(refreshTokenHash),
                RowForHash(refreshTokenHash),
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
            await table.DeleteEntityAsync(
                PartitionForHash(refreshTokenHash),
                RowForHash(refreshTokenHash),
                cancellationToken: ct).ConfigureAwait(false);
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
            var userIdString = userId.ToString("D");
            var results = new List<AuthSessionRecord>();

            await foreach (var e in table.QueryAsync<AuthSessionEntity>(x => x.UserId == userIdString, cancellationToken: ct).ConfigureAwait(false))
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
            var userIdString = userId.ToString("D");

            await foreach (var e in table.QueryAsync<AuthSessionEntity>(x => x.UserId == userIdString && x.SessionId == sessionId, cancellationToken: ct).ConfigureAwait(false))
            {
                e.RevokedAt = DateTimeOffset.UtcNow;
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TableClient GetTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AuthSessionsTableName));

    private static string PartitionForHash(string hash)
    {
        var h = (hash ?? string.Empty).Trim().ToLowerInvariant();
        var a = h.Length > 0 ? h[0] : '0';
        var b = h.Length > 1 ? h[1] : '0';
        return $"RTH|{a}{b}";
    }

    private static string RowForHash(string hash) => (hash ?? string.Empty).Trim().ToLowerInvariant();

    private static AuthSessionEntity ToEntity(AuthSessionRecord r)
        => new()
        {
            PartitionKey = PartitionForHash(r.RefreshTokenHash),
            RowKey = RowForHash(r.RefreshTokenHash),
            SessionId = r.SessionId,
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
            RefreshTokenHash: e.RowKey,
            SessionId: e.SessionId,
            UserId: Guid.Parse(e.UserId),
            TenantId: Guid.Parse(e.TenantId),
            ClaimsJson: e.ClaimsJson,
            CreatedAt: e.CreatedAt,
            LastSeenAt: e.LastSeenAt,
            RefreshTokenExpiresAt: e.RefreshTokenExpiresAt,
            RevokedAt: e.RevokedAt,
            DeviceInfo: e.DeviceInfo);
}
