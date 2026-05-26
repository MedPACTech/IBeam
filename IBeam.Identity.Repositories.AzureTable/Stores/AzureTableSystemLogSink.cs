using Azure.Data.Tables;
using IBeam.Api.Abstractions;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableSystemLogSink : ISystemLogSink
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableSystemLogSink(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task SaveAsync(SystemLogRecord log, CancellationToken cancellationToken = default)
    {
        var table = _serviceClient.GetTableClient(_opts.FullTableName(_opts.SystemLogsTableName));
        await table.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);

        var now = log.Timestamp == default ? DateTimeOffset.UtcNow : log.Timestamp;
        var entity = new SystemLogEntity
        {
            PartitionKey = $"LOG|{now.UtcDateTime:yyyyMMdd}",
            RowKey = $"{now:HHmmssfff}|{Guid.NewGuid():N}",
            Source = log.Source,
            Level = string.IsNullOrWhiteSpace(log.Level) ? "Information" : log.Level,
            Message = log.Message,
            Detail = log.Detail,
            TraceId = log.TraceId,
            OccurredAtUtc = now
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
    }
}
