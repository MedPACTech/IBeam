using Azure;
using Azure.Data.Tables;
using IBeam.Repositories.Abstractions;
using IBeam.Repositories.AzureTables.Internal;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IBeam.Repositories.AzureTables;

public sealed class AzureTablesRepositoryStore<T> : IRepositoryStore<T>
    where T : class, IEntity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TableServiceClient _serviceClient;
    private readonly AzureTablesOptions _options;
    private TableClient? _table;

    private bool IsTenantSpecific => typeof(ITenantEntity).IsAssignableFrom(typeof(T));

    public AzureTablesRepositoryStore(IOptions<AzureTablesOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("AzureTablesOptions.ConnectionString must be configured.");

        _serviceClient = new TableServiceClient(_options.ConnectionString);
    }

    private async Task<TableClient> GetTableAsync(CancellationToken ct)
    {
        if (_table != null) return _table;

        var tableName = BuildTableName(typeof(T).Name, _options.TableNamePrefix);
        var table = _serviceClient.GetTableClient(tableName);

        if (_options.CreateTablesIfNotExists)
            await table.CreateIfNotExistsAsync(ct);

        _table = table;
        return table;
    }

    private static string BuildTableName(string typeName, string? prefix)
    {
        var raw = (prefix ?? string.Empty) + typeName;
        var alnum = new string(raw.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(alnum)) alnum = "IBeam";
        if (!char.IsLetter(alnum[0])) alnum = "T" + alnum;
        if (alnum.Length < 3) alnum = alnum.PadRight(3, '0');
        if (alnum.Length > 63) alnum = alnum[..63];
        return alnum;
    }

    private string PartitionKeyForTenant(Guid? tenantId)
    {
        if (!IsTenantSpecific) return "global";

        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            throw new InvalidOperationException($"TenantId is required for tenant-specific table '{typeof(T).Name}'.");

        return tenantId.Value.ToString("N");
    }


    private static string ToRowKey(Guid id) => id.ToString("N");

    private static AzureTableEnvelope Wrap(T entity, string pk) => new()
    {
        PartitionKey = pk,
        RowKey = ToRowKey(entity.Id),
        Type = typeof(T).FullName,
        Data = JsonSerializer.Serialize(entity, JsonOptions)
        //SchemaVersion = _options.SchemaVersion TODO: implement versioning
    };

    private static T? Unwrap(AzureTableEnvelope? env)
        => env == null || string.IsNullOrWhiteSpace(env.Data)
            ? null
            : JsonSerializer.Deserialize<T>(env.Data, JsonOptions);

    private static string PartitionKeyForWrite(Guid? tenantId, T entity, bool isTenantSpecific)
    {
        if (!isTenantSpecific) return "global";

        if (entity is ITenantEntity te && te.TenantId != Guid.Empty)
            return te.TenantId.ToString("N");

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            return tenantId.Value.ToString("N");

        throw new InvalidOperationException($"TenantId is required for tenant-specific table '{typeof(T).Name}'.");
    }

    public Task<T?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
    => Execute("GetByIdAsync", async () =>
    {
        if (id == Guid.Empty) return null;

        var table = await GetTableAsync(ct);
        var pk = PartitionKeyForTenant(tenantId);
        var rk = ToRowKey(id);

        var response = await table.GetEntityIfExistsAsync<AzureTableEnvelope>(pk, rk, cancellationToken: ct);
        return response.HasValue ? Unwrap(response.Value) : null;
    });


    public async Task<IReadOnlyList<T>> GetByIdsAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new();
        if (idList.Count == 0) return Array.Empty<T>();

        var throttler = new SemaphoreSlim(8);

        var tasks = idList.Select(async id =>
        {
            await throttler.WaitAsync(ct);
            try
            {
                return await GetByIdAsync(tenantId, id, ct); // already wrapped
            }
            finally
            {
                throttler.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(x => x != null).Cast<T>().ToList();
    }

    public Task<IReadOnlyList<T>> GetAllAsync(Guid? tenantId, CancellationToken ct = default)
    => Execute("GetAllAsync", async () =>
    {
        var table = await GetTableAsync(ct);
        var pk = PartitionKeyForTenant(tenantId);

        var filter = TableClient.CreateQueryFilter<AzureTableEnvelope>(x => x.PartitionKey == pk);

        var list = new List<T>();
        await foreach (var env in table.QueryAsync<AzureTableEnvelope>(filter: filter, cancellationToken: ct))
        {
            var entity = Unwrap(env);
            if (entity != null) list.Add(entity);
        }

        return (IReadOnlyList<T>)list;
    });

    public Task<T> UpsertAsync(Guid? tenantId, T entity, CancellationToken ct = default)
        => Execute("UpsertAsync", async () =>
        {
            ArgumentNullException.ThrowIfNull(entity);
            var table = await GetTableAsync(ct);
            var pk = PartitionKeyForWrite(tenantId, entity, IsTenantSpecific);
            await table.UpsertEntityAsync(Wrap(entity, pk), TableUpdateMode.Replace, ct);
            return entity;
        });

    public Task<IReadOnlyList<T>> UpsertAllAsync(Guid? tenantId, IReadOnlyList<T> entities, CancellationToken ct = default)
    => Execute("UpsertAllAsync", async () =>
    {
        var table = await GetTableAsync(ct);
        var list = entities?.Where(x => x != null).ToList() ?? new();
        if (list.Count == 0) return (IReadOnlyList<T>)Array.Empty<T>();

        var groups = list.GroupBy(e => PartitionKeyForWrite(tenantId, e, IsTenantSpecific));

        foreach (var g in groups)
        {
            var batch = new List<TableTransactionAction>(100);

            foreach (var e in g)
            {
                batch.Add(new TableTransactionAction(
                    TableTransactionActionType.UpsertReplace,
                    Wrap(e, g.Key)));

                if (batch.Count == 100)
                {
                    await table.SubmitTransactionAsync(batch, ct);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                await table.SubmitTransactionAsync(batch, ct);
        }

        return (IReadOnlyList<T>)list;
    });


    public async Task HardDeleteAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
        => await Execute("HardDeleteAsync", async () =>
    {
        if (id == Guid.Empty) return;

        var table = await GetTableAsync(ct);
        var pk = PartitionKeyForTenant(tenantId);
        var rk = ToRowKey(id);

        try { await table.DeleteEntityAsync(pk, rk, ETag.All, ct); }
        catch (RequestFailedException ex) when (ex.Status == 404) { /* ignore */ }
    });

    public Task HardDeleteAllAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    => Execute("HardDeleteAllAsync", async () =>
    {
        var table = await GetTableAsync(ct);

        var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new();
        if (list.Count == 0) return;

        var pk = PartitionKeyForTenant(tenantId);

        var batch = new List<TableTransactionAction>(100);
        foreach (var id in list)
        {
            var rk = ToRowKey(id);
            var env = new AzureTableEnvelope { PartitionKey = pk, RowKey = rk, ETag = ETag.All };
            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, env));

            if (batch.Count == 100)
            {
                await SubmitDeleteBatchSafely(table, batch, pk, ct);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            await SubmitDeleteBatchSafely(table, batch, pk, ct);
    });


    private static async Task SubmitDeleteBatchSafely(
        TableClient table,
        List<TableTransactionAction> batch,
        string pk,
        CancellationToken ct)
    {
        try
        {
            await table.SubmitTransactionAsync(batch, ct);
        }
        catch (RequestFailedException)
        {
            // Fall back to individual deletes and ignore 404s
            foreach (var action in batch)
            {
                var env = (AzureTableEnvelope)action.Entity;
                try { await table.DeleteEntityAsync(pk, env.RowKey, ETag.All, ct); }
                catch (RequestFailedException ex) when (ex.Status == 404) { }
            }
        }
    }

    private async Task<TResult> Execute<TResult>(string op, Func<Task<TResult>> work)
    {
        try { return await work(); }
        catch (RequestFailedException ex)
        {
            throw new RepositoryStoreException(typeof(T).Name, op, ex);
        }
    }

    private async Task Execute(string op, Func<Task> work)
    {
        try { await work(); }
        catch (RequestFailedException ex)
        {
            throw new RepositoryStoreException(typeof(T).Name, op, ex);
        }
    }

}
