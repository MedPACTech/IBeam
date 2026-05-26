using Azure.Data.Tables;
using IBeam.Api.Abstractions;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableApiErrorSink : IApiErrorSink
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableApiErrorSink(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task SaveAsync(ApiErrorRecord error, CancellationToken cancellationToken = default)
    {
        var table = _serviceClient.GetTableClient(_opts.FullTableName(_opts.SystemErrorsTableName));
        await table.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);

        var now = error.Timestamp == default ? DateTimeOffset.UtcNow : error.Timestamp;
        var entity = new SystemErrorEntity
        {
            PartitionKey = $"ERR|{now.UtcDateTime:yyyyMMdd}",
            RowKey = $"{now:HHmmssfff}|{Guid.NewGuid():N}",
            Source = error.Source,
            Path = error.Path,
            Method = error.Method,
            Message = error.Message,
            Exception = error.Exception,
            TraceId = error.TraceId,
            OccurredAtUtc = now
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
    }
}
