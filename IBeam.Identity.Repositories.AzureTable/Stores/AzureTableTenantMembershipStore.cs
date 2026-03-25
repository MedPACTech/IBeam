using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableTenantMembershipStore : ITenantMembershipStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableTenantMembershipStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var table = GetUserTenantsTable();
            var userIdStr = userId.ToString("D");
            var pk = _opts.UserTenantsPk(userIdStr);

            var memberships = new List<UserTenantEntity>();

            await foreach (var e in table.QueryAsync<UserTenantEntity>(x => x.PartitionKey == pk, cancellationToken: ct)
                .ConfigureAwait(false))
            {
                // Skip malformed rows
                if (string.IsNullOrWhiteSpace(e.TenantId))
                {
                    // legacy fallback if you still have old rows
                    if (_opts.TryParseTenantIdFromUserTenantsRk(e.RowKey, out var parsed))
                        e.TenantId = parsed.ToString("D");
                    else
                        continue;
                }

                memberships.Add(e);
            }

            // Optional enrichment from Tenants table
            var nameMap = await GetTenantNameMapAsync(
                memberships.Select(m => m.TenantId).Distinct(),
                ct).ConfigureAwait(false);

            return memberships.Select(m =>
            {
                var tid = Guid.Parse(m.TenantId);

                var name =
                    nameMap.TryGetValue(tid, out var n) ? n :
                    (m.TenantDisplayName ?? string.Empty);

                return new TenantInfo(
                    TenantId: tid,
                    Name: name,
                    Roles: SplitRoles(m.RolesCsv),
                    IsActive: IsActiveStatus(m.Status),
                    RoleIds: SplitRoleIds(m.RoleIdsCsv)
                );
            }).ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }


    public async Task<TenantInfo?> GetTenantForUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var table = GetUserTenantsTable();
            var userIdStr = userId.ToString("D");
            var pk = _opts.UserTenantsPk(userIdStr);

            var rk = _opts.UserTenantsRk(tenantId);

            var resp = await table.GetEntityIfExistsAsync<UserTenantEntity>(pk, rk, cancellationToken: ct)
                .ConfigureAwait(false);

            if (!resp.HasValue) return null;

            var m = resp.Value;

            // Canonical tenant name
            var name = await TryGetTenantNameAsync(tenantId, ct).ConfigureAwait(false)
                ?? (m.TenantDisplayName ?? string.Empty);

            return new TenantInfo(
                TenantId: tenantId,
                Name: name,
                Roles: SplitRoles(m.RolesCsv),
                IsActive: IsActiveStatus(m.Status),
                RoleIds: SplitRoleIds(m.RoleIdsCsv)
            );
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }


    public async Task<Guid?> GetDefaultTenantIdAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var table = GetUserTenantsTable();
            var userIdStr = userId.ToString("D");
            var pk = _opts.UserTenantsPk(userIdStr);


            await foreach (var e in table.QueryAsync<UserTenantEntity>(
                x => x.PartitionKey == pk && x.IsDefault == true,
                cancellationToken: ct).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(e.TenantId) && Guid.TryParse(e.TenantId, out var tid))
                    return tid;

                if (_opts.TryParseTenantIdFromUserTenantsRk(e.RowKey, out var parsed))
                    return parsed;
            }

            return null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }


    public async Task SetDefaultTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var table = GetUserTenantsTable();
            var userIdStr = userId.ToString("D");
            var pk = _opts.UserTenantsPk(userIdStr);

            var targetRk = _opts.UserTenantsRk(tenantId);

            var entities = new List<UserTenantEntity>();
            await foreach (var e in table.QueryAsync<UserTenantEntity>(x => x.PartitionKey == pk, cancellationToken: ct)
                .ConfigureAwait(false))
                entities.Add(e);

            var target = entities.FirstOrDefault(x => x.RowKey == targetRk);
            if (target is null)
                throw new IdentityValidationException($"User '{userId}' is not a member of tenant '{tenantId}'.");

            var now = DateTimeOffset.UtcNow;

            var toUpdate = new List<UserTenantEntity>();

            foreach (var e in entities)
            {
                var shouldBeDefault = (e.RowKey == targetRk);

                if (e.IsDefault != shouldBeDefault)
                {
                    e.IsDefault = shouldBeDefault;
                    if (shouldBeDefault)
                        e.LastSelectedAt = now;

                    toUpdate.Add(e);
                }
            }

            if (toUpdate.Count == 0) return;

            foreach (var batch in Batch(toUpdate, 100))
            {
                var actions = batch
                    .Select(e => new TableTransactionAction(TableTransactionActionType.UpdateReplace, e))
                    .ToList();

                await table.SubmitTransactionAsync(actions, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }


    // -------- helpers --------

    private async Task<List<UserTenantEntity>> GetUserMembershipsAsync(string userId, CancellationToken ct)
    {
        var table = GetUserTenantsTable();
        var pk = _opts.UserTenantsPk(userId);


        var list = new List<UserTenantEntity>();
        await foreach (var e in table.QueryAsync<UserTenantEntity>(x => x.PartitionKey == pk, cancellationToken: ct).ConfigureAwait(false))
        {
            // If you support status filtering, apply here
            if (!string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase))
                continue;

            // Ensure TenantId is populated even if older rows only have RowKey format
            if (string.IsNullOrWhiteSpace(e.TenantId) && _opts.TryParseTenantIdFromUserTenantsRk(e.RowKey, out var parsed))
                e.TenantId = parsed.ToString("D");

            list.Add(e);
        }

        return list;
    }

    private async Task<Dictionary<Guid, string>> GetTenantNamesAsync(IEnumerable<string> tenantIds, CancellationToken ct)
    {
        var table = GetTenantsTable();

        var ids = tenantIds
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        // Point reads; can be parallelized later if needed
        var dict = new Dictionary<Guid, string>(ids.Count);
        foreach (var id in ids)
        {
            var resp = await table.GetEntityIfExistsAsync<TenantEntity>("TEN", id.ToString("D"), cancellationToken: ct).ConfigureAwait(false);
            if (resp.HasValue)
                dict[id] = resp.Value.Name ?? string.Empty;
        }

        return dict;
    }

    private TableClient GetUserTenantsTable()
        => _serviceClient.GetTableClient($"{_opts.TablePrefix}{_opts.UserTenantsTableName}");

    private TableClient GetTenantsTable()
    => _serviceClient.GetTableClient($"{_opts.TablePrefix}{_opts.TenantsTableName}");

    private async Task<Dictionary<Guid, string>> GetTenantNameMapAsync(IEnumerable<string> tenantIds, CancellationToken ct)
    {
        var table = GetTenantsTable();

        var ids = tenantIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();

        var dict = new Dictionary<Guid, string>();
        foreach (var id in ids)
        {
            var resp = await table.GetEntityIfExistsAsync<TenantEntity>("TEN", id.ToString("D"), cancellationToken: ct)
                .ConfigureAwait(false);

            if (resp.HasValue)
                dict[id] = resp.Value.Name ?? string.Empty;
        }

        return dict;
    }

    private async Task<string?> TryGetTenantNameAsync(Guid tenantId, CancellationToken ct)
    {
        var table = GetTenantsTable();
        var resp = await table.GetEntityIfExistsAsync<TenantEntity>("TEN", tenantId.ToString("D"), cancellationToken: ct)
            .ConfigureAwait(false);

        return resp.HasValue ? (resp.Value.Name ?? string.Empty) : null;
    }


    private static string[] SplitRoles(string? csv)
        => (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<Guid> SplitRoleIds(string? csv)
        => (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

    private static IEnumerable<List<T>> Batch<T>(IEnumerable<T> items, int size)
    {
        var batch = new List<T>(size);
        foreach (var item in items)
        {
            batch.Add(item);
            if (batch.Count == size)
            {
                yield return batch;
                batch = new List<T>(size);
            }
        }
        if (batch.Count > 0)
            yield return batch;
    }

    private static bool IsActiveStatus(string? status)
    => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

   

}
