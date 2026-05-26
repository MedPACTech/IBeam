using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTablePermissionAccessStore : IPermissionAccessStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTablePermissionAccessStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<PermissionGrantSet> ResolveGrantsAsync(
        Guid tenantId,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (tenantId == Guid.Empty)
                return PermissionGrantSet.Empty;

            var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roleIds = new HashSet<Guid>();

            var normalizedNames = permissionNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizePermissionName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var uniqueIds = permissionIds
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            foreach (var permissionName in normalizedNames)
            {
                var entity = await TryGetByPermissionNameAsync(tenantId, permissionName, ct).ConfigureAwait(false);
                if (entity is null || !IsActive(entity.Status))
                    continue;

                foreach (var roleName in ParseCsv(entity.RoleNamesCsv))
                    roleNames.Add(roleName);
                foreach (var roleId in ParseGuidCsv(entity.RoleIdsCsv))
                    roleIds.Add(roleId);
            }

            foreach (var permissionId in uniqueIds)
            {
                var entity = await TryGetByPermissionIdAsync(tenantId, permissionId, ct).ConfigureAwait(false);
                if (entity is null || !IsActive(entity.Status))
                    continue;

                foreach (var roleName in ParseCsv(entity.RoleNamesCsv))
                    roleNames.Add(roleName);
                foreach (var roleId in ParseGuidCsv(entity.RoleIdsCsv))
                    roleIds.Add(roleId);
            }

            return new PermissionGrantSet(roleNames.ToList(), roleIds.ToList());
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<PermissionRoleMap>> GetMappingsAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (tenantId == Guid.Empty)
                throw new IdentityValidationException("tenantId is required.");

            var list = new List<PermissionRoleMap>();
            var pk = _opts.PermissionRoleMapsPk(tenantId);
            await foreach (var entity in Table().QueryAsync<PermissionRoleMapEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
            {
                list.Add(Map(entity));
            }

            return list;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<PermissionRoleMap> UpsertByPermissionNameAsync(
        Guid tenantId,
        string permissionName,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (tenantId == Guid.Empty)
                throw new IdentityValidationException("tenantId is required.");
            var normalized = NormalizePermissionName(permissionName);
            var now = DateTimeOffset.UtcNow;

            var entity = new PermissionRoleMapEntity
            {
                PartitionKey = _opts.PermissionRoleMapsPk(tenantId),
                RowKey = PermissionNameRowKey(normalized),
                TenantId = tenantId.ToString("D"),
                PermissionName = normalized,
                PermissionId = null,
                RoleNamesCsv = string.Join(",", NormalizeRoleNames(roleNames)),
                RoleIdsCsv = string.Join(",", NormalizeRoleIds(roleIds).Select(x => x.ToString("D"))),
                Status = "Active",
                UpdatedAt = now
            };

            await Table().UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<PermissionRoleMap> UpsertByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (tenantId == Guid.Empty)
                throw new IdentityValidationException("tenantId is required.");
            if (permissionId == Guid.Empty)
                throw new IdentityValidationException("permissionId is required.");
            var now = DateTimeOffset.UtcNow;

            var entity = new PermissionRoleMapEntity
            {
                PartitionKey = _opts.PermissionRoleMapsPk(tenantId),
                RowKey = PermissionIdRowKey(permissionId),
                TenantId = tenantId.ToString("D"),
                PermissionName = null,
                PermissionId = permissionId.ToString("D"),
                RoleNamesCsv = string.Join(",", NormalizeRoleNames(roleNames)),
                RoleIdsCsv = string.Join(",", NormalizeRoleIds(roleIds).Select(x => x.ToString("D"))),
                Status = "Active",
                UpdatedAt = now
            };

            await Table().UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (tenantId == Guid.Empty)
                throw new IdentityValidationException("tenantId is required.");
            var normalized = NormalizePermissionName(permissionName);
            await Table().DeleteEntityAsync(_opts.PermissionRoleMapsPk(tenantId), PermissionNameRowKey(normalized), ETag.All, ct)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // idempotent delete
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (tenantId == Guid.Empty)
                throw new IdentityValidationException("tenantId is required.");
            if (permissionId == Guid.Empty)
                throw new IdentityValidationException("permissionId is required.");
            await Table().DeleteEntityAsync(_opts.PermissionRoleMapsPk(tenantId), PermissionIdRowKey(permissionId), ETag.All, ct)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // idempotent delete
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private async Task<PermissionRoleMapEntity?> TryGetByPermissionNameAsync(Guid tenantId, string normalizedPermissionName, CancellationToken ct)
    {
        var response = await Table().GetEntityIfExistsAsync<PermissionRoleMapEntity>(
                _opts.PermissionRoleMapsPk(tenantId),
                PermissionNameRowKey(normalizedPermissionName),
                cancellationToken: ct)
            .ConfigureAwait(false);
        return response.HasValue ? response.Value : null;
    }

    private async Task<PermissionRoleMapEntity?> TryGetByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct)
    {
        var response = await Table().GetEntityIfExistsAsync<PermissionRoleMapEntity>(
                _opts.PermissionRoleMapsPk(tenantId),
                PermissionIdRowKey(permissionId),
                cancellationToken: ct)
            .ConfigureAwait(false);
        return response.HasValue ? response.Value : null;
    }

    private TableClient Table()
        => _serviceClient.GetTableClient($"{_opts.TablePrefix}{_opts.PermissionRoleMapsTableName}");

    private static string PermissionNameRowKey(string normalizedPermissionName)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPermissionName));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"NAM|{hash}";
    }

    private static string PermissionIdRowKey(Guid permissionId)
        => $"ID|{permissionId:D}";

    private static string NormalizePermissionName(string permissionName)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
            throw new IdentityValidationException("permissionName is required.");

        return permissionName.Trim();
    }

    private static List<string> NormalizeRoleNames(IReadOnlyList<string> roleNames)
        => roleNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<Guid> NormalizeRoleIds(IReadOnlyList<Guid> roleIds)
        => roleIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

    private static List<string> ParseCsv(string? csv)
        => (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<Guid> ParseGuidCsv(string? csv)
        => (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

    private static bool IsActive(string? status)
        => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private static PermissionRoleMap Map(PermissionRoleMapEntity entity)
        => new(
            TenantId: Guid.TryParse(entity.TenantId, out var tid) ? tid : Guid.Empty,
            PermissionName: entity.PermissionName,
            PermissionId: Guid.TryParse(entity.PermissionId, out var pid) ? pid : null,
            RoleNames: ParseCsv(entity.RoleNamesCsv),
            RoleIds: ParseGuidCsv(entity.RoleIdsCsv),
            IsActive: IsActive(entity.Status),
            UpdatedAt: entity.UpdatedAt);
}
