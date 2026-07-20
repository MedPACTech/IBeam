using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using IBeam.AccessControl;
using Microsoft.Extensions.Options;

namespace IBeam.AccessControl.Repositories.AzureTable;

public sealed class AzureTablePermissionRoleMapStore : IPermissionRoleMapStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableAccessControlOptions _options;

    public AzureTablePermissionRoleMapStore(
        TableServiceClient serviceClient,
        IOptions<AzureTableAccessControlOptions> options)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<PermissionGrantSet> ResolveGrantsAsync(
        Guid tenantId,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleIds = new HashSet<Guid>();

        foreach (var permissionName in NormalizePermissionNames(permissionNames))
        {
            var entity = await TryGetByPermissionNameAsync(tenantId, permissionName, ct).ConfigureAwait(false);
            AddActiveGrants(entity, roleNames, roleIds);
        }

        foreach (var permissionId in NormalizePermissionIds(permissionIds))
        {
            var entity = await TryGetByPermissionIdAsync(tenantId, permissionId, ct).ConfigureAwait(false);
            AddActiveGrants(entity, roleNames, roleIds);
        }

        return new PermissionGrantSet(
            roleNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            roleIds.OrderBy(x => x).ToList());
    }

    public async Task<IReadOnlyList<PermissionRoleMapRecord>> ListMappingsAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        var partitionKey = _options.PermissionRoleMapsPk(tenantId);
        var results = new List<PermissionRoleMapRecord>();

        await foreach (var entity in table.QueryAsync<PermissionRoleMapEntity>(
                           x => x.PartitionKey == partitionKey,
                           cancellationToken: ct).ConfigureAwait(false))
        {
            results.Add(ToRecord(entity));
        }

        return results
            .OrderBy(x => x.PermissionName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PermissionId)
            .ToList();
    }

    public async Task<PermissionRoleMapRecord> UpsertByPermissionNameAsync(
        Guid tenantId,
        string permissionName,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var normalizedPermissionName = NormalizePermissionName(permissionName);
        var record = new PermissionRoleMapRecord(
            tenantId,
            normalizedPermissionName,
            null,
            NormalizeRoleNames(roleNames),
            NormalizeRoleIds(roleIds),
            PermissionRoleMapStatuses.Active,
            DateTimeOffset.UtcNow);

        var table = await GetTableAsync(ct).ConfigureAwait(false);
        await table.UpsertEntityAsync(ToEntity(record), TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return record;
    }

    public async Task<PermissionRoleMapRecord> UpsertByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidatePermissionId(permissionId);
        var record = new PermissionRoleMapRecord(
            tenantId,
            null,
            permissionId,
            NormalizeRoleNames(roleNames),
            NormalizeRoleIds(roleIds),
            PermissionRoleMapStatuses.Active,
            DateTimeOffset.UtcNow);

        var table = await GetTableAsync(ct).ConfigureAwait(false);
        await table.UpsertEntityAsync(ToEntity(record), TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return record;
    }

    public Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        return DeleteAsync(
            tenantId,
            _options.PermissionRoleMapByNameRk(PermissionNameHash(NormalizePermissionName(permissionName))),
            ct);
    }

    public Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidatePermissionId(permissionId);
        return DeleteAsync(tenantId, _options.PermissionRoleMapByIdRk(permissionId), ct);
    }

    private async Task DeleteAsync(Guid tenantId, string rowKey, CancellationToken ct)
    {
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        try
        {
            await table.DeleteEntityAsync(
                _options.PermissionRoleMapsPk(tenantId),
                rowKey,
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private async Task<PermissionRoleMapEntity?> TryGetByPermissionNameAsync(
        Guid tenantId,
        string normalizedPermissionName,
        CancellationToken ct)
    {
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        var response = await table.GetEntityIfExistsAsync<PermissionRoleMapEntity>(
            _options.PermissionRoleMapsPk(tenantId),
            _options.PermissionRoleMapByNameRk(PermissionNameHash(normalizedPermissionName)),
            cancellationToken: ct).ConfigureAwait(false);

        return response.HasValue ? response.Value : null;
    }

    private async Task<PermissionRoleMapEntity?> TryGetByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        CancellationToken ct)
    {
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        var response = await table.GetEntityIfExistsAsync<PermissionRoleMapEntity>(
            _options.PermissionRoleMapsPk(tenantId),
            _options.PermissionRoleMapByIdRk(permissionId),
            cancellationToken: ct).ConfigureAwait(false);

        return response.HasValue ? response.Value : null;
    }

    private async Task<TableClient> GetTableAsync(CancellationToken ct)
    {
        var table = _serviceClient.GetTableClient(_options.FullTableName(_options.PermissionRoleMapsTableName));
        if (_options.CreateTablesIfNotExists)
        {
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        }

        return table;
    }

    private PermissionRoleMapEntity ToEntity(PermissionRoleMapRecord record)
        => new()
        {
            PartitionKey = _options.PermissionRoleMapsPk(record.TenantId),
            RowKey = record.PermissionId.HasValue
                ? _options.PermissionRoleMapByIdRk(record.PermissionId.Value)
                : _options.PermissionRoleMapByNameRk(PermissionNameHash(record.PermissionName ?? string.Empty)),
            TenantId = record.TenantId,
            PermissionName = record.PermissionName,
            PermissionId = record.PermissionId,
            RoleNamesCsv = string.Join(",", record.RoleNames),
            RoleIdsCsv = string.Join(",", record.RoleIds.Select(x => x.ToString("D"))),
            Status = record.Status,
            UpdatedUtc = record.UpdatedUtc
        };

    private static PermissionRoleMapRecord ToRecord(PermissionRoleMapEntity entity)
        => new(
            TenantId: entity.TenantId,
            PermissionName: entity.PermissionName,
            PermissionId: entity.PermissionId,
            RoleNames: SplitCsv(entity.RoleNamesCsv),
            RoleIds: SplitGuidCsv(entity.RoleIdsCsv),
            Status: string.IsNullOrWhiteSpace(entity.Status) ? PermissionRoleMapStatuses.Active : entity.Status,
            UpdatedUtc: entity.UpdatedUtc);

    private static void AddActiveGrants(
        PermissionRoleMapEntity? entity,
        HashSet<string> roleNames,
        HashSet<Guid> roleIds)
    {
        if (entity is null ||
            !string.Equals(entity.Status, PermissionRoleMapStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var roleName in SplitCsv(entity.RoleNamesCsv))
            roleNames.Add(roleName);
        foreach (var roleId in SplitGuidCsv(entity.RoleIdsCsv))
            roleIds.Add(roleId);
    }

    private static string PermissionNameHash(string normalizedPermissionName)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPermissionName));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizePermissionName(string permissionName)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
            throw new AccessControlException("permissionName is required.");

        return permissionName.Trim();
    }

    private static IReadOnlyList<string> NormalizePermissionNames(IReadOnlyList<string>? permissionNames)
        => (permissionNames ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<Guid> NormalizePermissionIds(IReadOnlyList<Guid>? permissionIds)
        => (permissionIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

    private static IReadOnlyList<string> NormalizeRoleNames(IReadOnlyList<string>? roleNames)
        => (roleNames ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<Guid> NormalizeRoleIds(IReadOnlyList<Guid>? roleIds)
        => (roleIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

    private static IReadOnlyList<string> SplitCsv(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<Guid> SplitGuidCsv(string? value)
        => SplitCsv(value)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new AccessControlException("tenantId is required.");
    }

    private static void ValidatePermissionId(Guid permissionId)
    {
        if (permissionId == Guid.Empty)
            throw new AccessControlException("permissionId is required.");
    }
}
