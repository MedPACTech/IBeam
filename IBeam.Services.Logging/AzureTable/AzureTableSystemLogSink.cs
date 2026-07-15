using Azure.Data.Tables;
using IBeam.Api.Abstractions;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace IBeam.Services.Logging.AzureTable;

public sealed class AzureTableSystemLogSink : ISystemLogSink, IAuditTrailSink
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableSystemLogOptions _options;

    public AzureTableSystemLogSink(
        TableServiceClient serviceClient,
        IOptions<AzureTableSystemLogOptions> options)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.NormalizeAndValidate(requireConnectionString: false);
    }

    public async Task SaveAsync(SystemLogRecord log, CancellationToken cancellationToken = default)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        var now = log.Timestamp == default ? DateTimeOffset.UtcNow : log.Timestamp;
        var entity = new AzureTableSystemLogEntity
        {
            PartitionKey = BuildPartitionKey(null, now),
            RowKey = BuildEventRowKey(now),
            OccurredAtUtc = now,
            DateUtc = now.UtcDateTime.ToString("yyyy-MM-dd"),
            Category = "System",
            Source = log.Source,
            Level = string.IsNullOrWhiteSpace(log.Level) ? "Information" : log.Level,
            Message = log.Message,
            Detail = log.Detail,
            TraceId = log.TraceId,
            CorrelationId = log.TraceId,
            FirstSeenUtc = now,
            LastSeenUtc = now,
            Count = 1
        };

        await UpsertAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
    {
        if (transaction is null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }

        var now = transaction.OccurredUtc == default ? DateTimeOffset.UtcNow : transaction.OccurredUtc;
        var action = string.IsNullOrWhiteSpace(transaction.Action)
            ? transaction.Operation.ToString()
            : transaction.Action;

        var entity = new AzureTableSystemLogEntity
        {
            PartitionKey = BuildPartitionKey(transaction.TenantId, now),
            RowKey = BuildEventRowKey(now),
            OccurredAtUtc = now,
            DateUtc = now.UtcDateTime.ToString("yyyy-MM-dd"),
            Category = "EntityChange",
            Source = transaction.ServiceName,
            Level = "Information",
            Message = action,
            Detail = null,
            ServiceName = transaction.ServiceName,
            EntityName = transaction.EntityName,
            Operation = transaction.Operation.ToString(),
            Action = action,
            EntityId = transaction.EntityId,
            TenantId = transaction.TenantId,
            ActorId = transaction.ActorId,
            TraceId = transaction.CorrelationId,
            CorrelationId = transaction.CorrelationId,
            IpAddress = transaction.IpAddress,
            UserAgent = transaction.UserAgent,
            DeviceId = transaction.DeviceId,
            BeforeJson = transaction.BeforeJson,
            AfterJson = transaction.AfterJson,
            OriginalJson = transaction.OriginalJson,
            TransformedJson = transaction.TransformedJson,
            FirstSeenUtc = now,
            LastSeenUtc = now,
            Count = 1
        };

        await UpsertAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default)
    {
        if (rollup is null)
        {
            throw new ArgumentNullException(nameof(rollup));
        }

        var table = await GetTableAsync(ct).ConfigureAwait(false);
        var partitionKey = BuildPartitionKey(rollup.TenantId, rollup.LastSeenUtc == default ? DateTimeOffset.UtcNow : rollup.LastSeenUtc);
        var rowKey = BuildRollupRowKey(rollup);
        var response = await table.GetEntityIfExistsAsync<AzureTableSystemLogEntity>(partitionKey, rowKey, cancellationToken: ct).ConfigureAwait(false);

        if (response.HasValue)
        {
            var existing = response.Value;
            existing.LastSeenUtc = rollup.LastSeenUtc;
            existing.Count += Math.Max(rollup.Count, 1);
            await table.UpsertEntityAsync(existing, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var action = string.IsNullOrWhiteSpace(rollup.Action)
            ? rollup.Operation.ToString()
            : rollup.Action;

        var entity = new AzureTableSystemLogEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            OccurredAtUtc = now,
            DateUtc = rollup.DateUtc.ToString("yyyy-MM-dd"),
            Category = "SelectRollup",
            Source = rollup.ServiceName,
            Level = "Information",
            Message = action,
            ServiceName = rollup.ServiceName,
            EntityName = rollup.EntityName,
            Operation = rollup.Operation.ToString(),
            Action = action,
            TenantId = rollup.TenantId,
            ActorId = rollup.ActorId,
            IsSelectRollup = true,
            QuerySignature = rollup.QuerySignature,
            FirstSeenUtc = rollup.FirstSeenUtc,
            LastSeenUtc = rollup.LastSeenUtc,
            Count = Math.Max(rollup.Count, 1)
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
    }

    private async Task UpsertAsync(AzureTableSystemLogEntity entity, CancellationToken ct)
    {
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
    }

    private async Task<TableClient> GetTableAsync(CancellationToken ct)
    {
        var table = _serviceClient.GetTableClient(_options.FullTableName());
        if (_options.CreateTableIfNotExists)
        {
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        }

        return table;
    }

    private static string BuildPartitionKey(Guid? tenantId, DateTimeOffset occurredUtc)
    {
        var date = occurredUtc.UtcDateTime.ToString("yyyyMMdd");
        return tenantId.HasValue
            ? $"TENANT|{tenantId.Value:D}|DAY|{date}"
            : $"SYSTEM|DAY|{date}";
    }

    private static string BuildEventRowKey(DateTimeOffset occurredUtc)
        => $"{occurredUtc.UtcDateTime:HHmmssfffffff}|{Guid.NewGuid():N}";

    private static string BuildRollupRowKey(ServiceSelectAuditRollup rollup)
    {
        var key = string.Join("|",
            rollup.DateUtc,
            rollup.ServiceName,
            rollup.EntityName,
            rollup.Operation,
            rollup.Action,
            rollup.TenantId,
            rollup.ActorId,
            rollup.QuerySignature);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return $"ROLLUP|{Convert.ToHexString(bytes)}";
    }
}
