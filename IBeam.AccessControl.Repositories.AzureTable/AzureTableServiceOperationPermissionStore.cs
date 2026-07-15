using Azure.Data.Tables;
using IBeam.AccessControl;
using Microsoft.Extensions.Options;

namespace IBeam.AccessControl.Repositories.AzureTable;

public sealed class AzureTableServiceOperationPermissionStore : IServiceOperationPermissionStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableAccessControlOptions _options;

    public AzureTableServiceOperationPermissionStore(
        TableServiceClient serviceClient,
        IOptions<AzureTableAccessControlOptions> options)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<IReadOnlyList<ServiceOperationPermissionRule>> ListRulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        var partitionKey = _options.ServiceOperationPermissionsPk(tenantId);
        var results = new List<ServiceOperationPermissionRule>();

        await foreach (var entity in table.QueryAsync<ServiceOperationPermissionEntity>(
                           x => x.PartitionKey == partitionKey,
                           cancellationToken: ct).ConfigureAwait(false))
        {
            results.Add(ToRecord(entity));
        }

        return results
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Pattern, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ServiceOperationPermissionRule?> GetRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateRuleId(ruleId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        var response = await table.GetEntityIfExistsAsync<ServiceOperationPermissionEntity>(
            _options.ServiceOperationPermissionsPk(tenantId),
            _options.ServiceOperationPermissionsRk(ruleId),
            cancellationToken: ct).ConfigureAwait(false);

        return response.HasValue ? ToRecord(response.Value) : null;
    }

    public async Task<ServiceOperationPermissionRule> UpsertRuleAsync(ServiceOperationPermissionRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.TenantId is not Guid tenantId)
        {
            throw new AccessControlException("tenantId is required.");
        }

        ValidateTenantId(tenantId);
        ValidateRuleId(rule.RuleId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        await table.UpsertEntityAsync(ToEntity(rule, tenantId), TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return rule;
    }

    public async Task DisableRuleAsync(Guid tenantId, Guid ruleId, Guid? updatedByUserId = null, CancellationToken ct = default)
    {
        var existing = await GetRuleAsync(tenantId, ruleId, ct).ConfigureAwait(false)
            ?? throw new AccessControlException($"Service operation permission rule '{ruleId}' was not found.");

        await UpsertRuleAsync(existing with
        {
            Status = ServiceOperationPermissionStatuses.Disabled,
            UpdatedUtc = DateTimeOffset.UtcNow,
            UpdatedByUserId = updatedByUserId
        }, ct).ConfigureAwait(false);
    }

    public async Task DeleteRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateRuleId(ruleId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        await table.DeleteEntityAsync(
            _options.ServiceOperationPermissionsPk(tenantId),
            _options.ServiceOperationPermissionsRk(ruleId),
            cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task<TableClient> GetTableAsync(CancellationToken ct)
    {
        var table = _serviceClient.GetTableClient(_options.FullTableName(_options.ServiceOperationPermissionsTableName));
        await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        return table;
    }

    private ServiceOperationPermissionEntity ToEntity(ServiceOperationPermissionRule rule, Guid tenantId)
        => new()
        {
            PartitionKey = _options.ServiceOperationPermissionsPk(tenantId),
            RowKey = _options.ServiceOperationPermissionsRk(rule.RuleId),
            RuleId = rule.RuleId,
            TenantId = tenantId,
            Pattern = rule.Pattern,
            Effect = rule.Effect,
            SubjectTypesCsv = string.Join(",", rule.SubjectTypes),
            RoleNamesCsv = string.Join(",", rule.RoleNames),
            RoleIdsCsv = string.Join(",", rule.RoleIds.Select(x => x.ToString("D"))),
            Priority = rule.Priority,
            Source = rule.Source,
            Status = rule.Status,
            CreatedUtc = rule.CreatedUtc,
            UpdatedUtc = rule.UpdatedUtc,
            UpdatedByUserId = rule.UpdatedByUserId
        };

    private static ServiceOperationPermissionRule ToRecord(ServiceOperationPermissionEntity entity)
        => new(
            RuleId: entity.RuleId,
            TenantId: entity.TenantId,
            Pattern: entity.Pattern,
            Effect: entity.Effect,
            SubjectTypes: SplitCsv(entity.SubjectTypesCsv),
            RoleNames: SplitCsv(entity.RoleNamesCsv),
            RoleIds: SplitGuidCsv(entity.RoleIdsCsv),
            Priority: entity.Priority,
            Source: string.IsNullOrWhiteSpace(entity.Source) ? ServiceOperationPermissionSources.Store : entity.Source,
            Status: string.IsNullOrWhiteSpace(entity.Status) ? ServiceOperationPermissionStatuses.Active : entity.Status,
            CreatedUtc: entity.CreatedUtc,
            UpdatedUtc: entity.UpdatedUtc,
            UpdatedByUserId: entity.UpdatedByUserId);

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

    private static void ValidateRuleId(Guid ruleId)
    {
        if (ruleId == Guid.Empty)
            throw new AccessControlException("ruleId is required.");
    }
}
