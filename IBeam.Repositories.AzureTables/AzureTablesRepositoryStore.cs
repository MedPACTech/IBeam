using Azure;
using Azure.Data.Tables;
using IBeam.Repositories.Abstractions;
using IBeam.Repositories.AzureTables.Internal;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace IBeam.Repositories.AzureTables;

public sealed class AzureTablesRepositoryStore<T> : IAzureTablesRepositoryStore<T>
    where T : class, IEntity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ReservedColumns =
    {
        "PartitionKey", "RowKey", "Timestamp", "ETag", "odata.etag"
    };

    private readonly TableServiceClient _serviceClient;
    private readonly AzureTablesOptions _options;
    private readonly IAzureTablePartitionKeyStrategy<T> _partitionKeyStrategy;
    private readonly AzureEntityMappingOptions<T>? _mapping;
    private readonly IEntityLocator _locator;
    private TableClient? _table;

    private bool UseEnvelopeModel => _options.StorageModel == AzureTableStorageModel.Envelope;

    public AzureTablesRepositoryStore(
        IOptions<AzureTablesOptions> options,
        IAzureTablePartitionKeyStrategy<T>? partitionKeyStrategy = null,
        AzureEntityMappingOptions<T>? mapping = null,
        IEntityLocator? locator = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _partitionKeyStrategy = partitionKeyStrategy ?? AzureTablePartitionKeyStrategies.Default<T>();
        _mapping = mapping;
        _locator = locator ?? new NullEntityLocator();

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("AzureTablesOptions.ConnectionString must be configured.");
        if (_mapping?.EnableIdLocator == true && locator is null)
            throw new InvalidOperationException(
                $"{typeof(T).Name} mapping enabled id locator but no {nameof(IEntityLocator)} is registered.");

        _serviceClient = new TableServiceClient(_options.ConnectionString);
    }

    private async Task<TableClient> GetTableAsync(CancellationToken ct)
    {
        if (_table != null) return _table;

        var mappedName = _mapping?.TableName;
        var baseName = string.IsNullOrWhiteSpace(mappedName) ? typeof(T).Name : mappedName.Trim();
        var tableName = BuildTableName(baseName, _options.TableNamePrefix);
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

    private static string ToRowKey(Guid id) => id.ToString("N");

    private AzureEntityKey ResolveWriteKey(Guid? tenantId, T entity)
    {
        if (_mapping?.WriteKey is not null)
            return _mapping.WriteKey(tenantId, entity);

        return new AzureEntityKey
        {
            PartitionKey = _partitionKeyStrategy.GetPartitionKeyForWrite(tenantId, entity),
            RowKey = ToRowKey(entity.Id)
        };
    }

    private string EntityTypeName => typeof(T).FullName ?? typeof(T).Name;

    private static string ScopeFromTenant(Guid? tenantId)
        => tenantId.HasValue && tenantId.Value != Guid.Empty
            ? $"TENANT|{tenantId.Value:D}"
            : "GLOBAL";

    private string EffectiveSoftDeleteProperty(string requested)
        => string.Equals(requested, "IsDeleted", StringComparison.Ordinal)
            ? (_mapping?.SoftDeleteProperty ?? requested)
            : requested;

    private IReadOnlyList<string>? ResolveCandidatePartitionsForId(Guid? tenantId, Guid id)
    {
        if (_mapping?.CandidatePartitionsForId is not null)
            return _mapping.CandidatePartitionsForId(tenantId, id);

        return _partitionKeyStrategy.GetCandidatePartitionsForId(tenantId, id);
    }

    private static AzureTableEnvelope WrapEnvelope(T entity, string partitionKey) => new()
    {
        PartitionKey = partitionKey,
        RowKey = ToRowKey(entity.Id),
        Type = typeof(T).FullName,
        Data = JsonSerializer.Serialize(entity, JsonOptions)
    };

    private static T? UnwrapEnvelope(AzureTableEnvelope? envelope)
        => envelope == null || string.IsNullOrWhiteSpace(envelope.Data)
            ? null
            : JsonSerializer.Deserialize<T>(envelope.Data, JsonOptions);

    private static TableEntity WrapColumns(T entity, string partitionKey)
    {
        var tableEntity = new TableEntity(partitionKey, ToRowKey(entity.Id))
        {
            ["Type"] = typeof(T).FullName ?? typeof(T).Name
        };

        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        foreach (var prop in props)
        {
            if (ReservedColumns.Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            var value = prop.GetValue(entity);
            if (value is null)
                continue;

            tableEntity[prop.Name] = ConvertToTableValue(value);
        }

        return tableEntity;
    }

    private static object ConvertToTableValue(object value)
    {
        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();

        if (type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long) ||
            type == typeof(double) || type == typeof(Guid) || type == typeof(DateTimeOffset) ||
            type == typeof(byte[]))
            return value;

        if (type == typeof(DateTime))
            return new DateTimeOffset(((DateTime)value).ToUniversalTime());

        if (type.IsEnum)
            return Convert.ToInt32(value);

        if (type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(ushort) || type == typeof(uint))
            return Convert.ToInt32(value);

        if (type == typeof(ulong))
            return Convert.ToInt64(value);

        if (type == typeof(float) || type == typeof(decimal))
            return Convert.ToDouble(value);

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static T? UnwrapColumns(TableEntity? tableEntity)
    {
        if (tableEntity == null)
            return null;

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in tableEntity)
        {
            if (ReservedColumns.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                continue;

            if (string.Equals(kvp.Key, "Type", StringComparison.OrdinalIgnoreCase))
                continue;

            data[kvp.Key] = kvp.Value;
        }

        if (!data.ContainsKey(nameof(IEntity.Id)) && Guid.TryParse(tableEntity.RowKey, out var id))
            data[nameof(IEntity.Id)] = id;

        var json = JsonSerializer.Serialize(data, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public Task<T?> GetByKeysAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        => Execute("GetByKeysAsync", async () =>
        {
            if (string.IsNullOrWhiteSpace(partitionKey) || string.IsNullOrWhiteSpace(rowKey))
                return null;

            var table = await GetTableAsync(ct);

            if (UseEnvelopeModel)
            {
                var response = await table.GetEntityIfExistsAsync<AzureTableEnvelope>(partitionKey, rowKey, cancellationToken: ct);
                return response.HasValue ? UnwrapEnvelope(response.Value) : null;
            }

            var colResponse = await table.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
            return colResponse.HasValue ? UnwrapColumns(colResponse.Value) : null;
        });

    public async Task DeleteByKeysAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        => await Execute("DeleteByKeysAsync", async () =>
        {
            if (string.IsNullOrWhiteSpace(partitionKey) || string.IsNullOrWhiteSpace(rowKey))
                return;

            var table = await GetTableAsync(ct);
            try
            {
                await table.DeleteEntityAsync(partitionKey, rowKey, ETag.All, ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // no-op
            }
        });

    public Task<T?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
        => Execute("GetByIdAsync", async () =>
        {
            if (id == Guid.Empty) return null;

            var table = await GetTableAsync(ct);
            var rowKey = ToRowKey(id);
            var partitionKey = await ResolvePartitionKeyForIdAsync(table, tenantId, id, rowKey, ct);
            if (string.IsNullOrWhiteSpace(partitionKey))
                return null;

            if (UseEnvelopeModel)
            {
                var response = await table.GetEntityIfExistsAsync<AzureTableEnvelope>(partitionKey, rowKey, cancellationToken: ct);
                return response.HasValue ? UnwrapEnvelope(response.Value) : null;
            }

            var colResponse = await table.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
            return colResponse.HasValue ? UnwrapColumns(colResponse.Value) : null;
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
                return await GetByIdAsync(tenantId, id, ct);
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
            var partitions = _partitionKeyStrategy.GetPartitionsForGetAll(tenantId);
            var list = new List<T>();

            async Task QueryPartitionAsync(string? partitionKey)
            {
                if (string.IsNullOrWhiteSpace(partitionKey))
                    return;

                if (UseEnvelopeModel)
                {
                    var filter = TableClient.CreateQueryFilter<AzureTableEnvelope>(x => x.PartitionKey == partitionKey);
                    await foreach (var entity in table.QueryAsync<AzureTableEnvelope>(filter: filter, cancellationToken: ct))
                    {
                        var model = UnwrapEnvelope(entity);
                        if (model != null) list.Add(model);
                    }
                    return;
                }

                var colFilter = TableClient.CreateQueryFilter<TableEntity>(x => x.PartitionKey == partitionKey);
                await foreach (var entity in table.QueryAsync<TableEntity>(filter: colFilter, cancellationToken: ct))
                {
                    var model = UnwrapColumns(entity);
                    if (model != null) list.Add(model);
                }
            }

            if (partitions is null)
            {
                if (UseEnvelopeModel)
                {
                    await foreach (var entity in table.QueryAsync<AzureTableEnvelope>(cancellationToken: ct))
                    {
                        var model = UnwrapEnvelope(entity);
                        if (model != null) list.Add(model);
                    }
                }
                else
                {
                    await foreach (var entity in table.QueryAsync<TableEntity>(cancellationToken: ct))
                    {
                        var model = UnwrapColumns(entity);
                        if (model != null) list.Add(model);
                    }
                }

                return (IReadOnlyList<T>)list;
            }

            foreach (var partition in partitions.Distinct())
                await QueryPartitionAsync(partition);

            return (IReadOnlyList<T>)list;
        });

    public Task<T> AddAsync(Guid? tenantId, T entity, CancellationToken ct = default)
        => Execute("AddAsync", async () =>
        {
            ArgumentNullException.ThrowIfNull(entity);

            var table = await GetTableAsync(ct);
            var key = ResolveWriteKey(tenantId, entity);

            if (UseEnvelopeModel)
            {
                var envelope = WrapEnvelope(entity, key.PartitionKey);
                envelope.RowKey = key.RowKey;
                await table.AddEntityAsync(envelope, ct);
                if (_mapping?.EnableIdLocator == true)
                    await _locator.UpsertAsync(ScopeFromTenant(tenantId), EntityTypeName, entity.Id.ToString("D"), key.PartitionKey, key.RowKey, ct);
                return entity;
            }

            var columns = WrapColumns(entity, key.PartitionKey);
            columns.RowKey = key.RowKey;
            await table.AddEntityAsync(columns, ct);
            if (_mapping?.EnableIdLocator == true)
                await _locator.UpsertAsync(ScopeFromTenant(tenantId), EntityTypeName, entity.Id.ToString("D"), key.PartitionKey, key.RowKey, ct);
            return entity;
        });

    public Task<T> UpdateAsync(
        Guid? tenantId,
        T entity,
        TableUpdateMode mode = TableUpdateMode.Replace,
        ETag? eTag = null,
        CancellationToken ct = default)
        => Execute("UpdateAsync", async () =>
        {
            ArgumentNullException.ThrowIfNull(entity);

            var table = await GetTableAsync(ct);
            var key = ResolveWriteKey(tenantId, entity);
            var effectiveEtag = eTag ?? ETag.All;

            if (UseEnvelopeModel)
            {
                var envelope = WrapEnvelope(entity, key.PartitionKey);
                envelope.RowKey = key.RowKey;
                envelope.ETag = effectiveEtag;
                await table.UpdateEntityAsync(envelope, envelope.ETag, mode, ct);
                if (_mapping?.EnableIdLocator == true)
                    await _locator.UpsertAsync(ScopeFromTenant(tenantId), EntityTypeName, entity.Id.ToString("D"), key.PartitionKey, key.RowKey, ct);
                return entity;
            }

            var columns = WrapColumns(entity, key.PartitionKey);
            columns.RowKey = key.RowKey;
            columns.ETag = effectiveEtag;
            await table.UpdateEntityAsync(columns, columns.ETag, mode, ct);
            if (_mapping?.EnableIdLocator == true)
                await _locator.UpsertAsync(ScopeFromTenant(tenantId), EntityTypeName, entity.Id.ToString("D"), key.PartitionKey, key.RowKey, ct);
            return entity;
        });

    public Task<T> UpsertAsync(Guid? tenantId, T entity, CancellationToken ct = default)
        => Execute("UpsertAsync", async () =>
        {
            ArgumentNullException.ThrowIfNull(entity);

            var table = await GetTableAsync(ct);
            var key = ResolveWriteKey(tenantId, entity);

            if (UseEnvelopeModel)
            {
                var envelope = WrapEnvelope(entity, key.PartitionKey);
                envelope.RowKey = key.RowKey;
                await table.UpsertEntityAsync(envelope, TableUpdateMode.Replace, ct);
                if (_mapping?.EnableIdLocator == true)
                    await _locator.UpsertAsync(ScopeFromTenant(tenantId), EntityTypeName, entity.Id.ToString("D"), key.PartitionKey, key.RowKey, ct);
                return entity;
            }

            var columns = WrapColumns(entity, key.PartitionKey);
            columns.RowKey = key.RowKey;
            await table.UpsertEntityAsync(columns, TableUpdateMode.Replace, ct);
            if (_mapping?.EnableIdLocator == true)
                await _locator.UpsertAsync(ScopeFromTenant(tenantId), EntityTypeName, entity.Id.ToString("D"), key.PartitionKey, key.RowKey, ct);
            return entity;
        });

    public Task<IReadOnlyList<T>> UpsertAllAsync(Guid? tenantId, IReadOnlyList<T> entities, CancellationToken ct = default)
        => Execute("UpsertAllAsync", async () =>
        {
            var table = await GetTableAsync(ct);
            var list = entities?.Where(x => x != null).ToList() ?? new();
            if (list.Count == 0) return (IReadOnlyList<T>)Array.Empty<T>();

            var groups = list.GroupBy(e => ResolveWriteKey(tenantId, e).PartitionKey);
            foreach (var group in groups)
            {
                var batch = new List<TableTransactionAction>(100);
                foreach (var entity in group)
                {
                    var key = ResolveWriteKey(tenantId, entity);
                    ITableEntity payload;
                    if (UseEnvelopeModel)
                    {
                        var envelope = WrapEnvelope(entity, key.PartitionKey);
                        envelope.RowKey = key.RowKey;
                        payload = envelope;
                    }
                    else
                    {
                        var columns = WrapColumns(entity, key.PartitionKey);
                        columns.RowKey = key.RowKey;
                        payload = columns;
                    }

                    batch.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, payload));

                    if (batch.Count == 100)
                    {
                        await table.SubmitTransactionAsync(batch, ct);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                    await table.SubmitTransactionAsync(batch, ct);
            }

            if (_mapping?.EnableIdLocator == true)
            {
                foreach (var entity in list)
                {
                    var key = ResolveWriteKey(tenantId, entity);
                    await _locator.UpsertAsync(
                        ScopeFromTenant(tenantId),
                        EntityTypeName,
                        entity.Id.ToString("D"),
                        key.PartitionKey,
                        key.RowKey,
                        ct);
                }
            }

            return (IReadOnlyList<T>)list;
        });

    public async Task HardDeleteAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
        => await Execute("HardDeleteAsync", async () =>
        {
            if (id == Guid.Empty) return;

            var table = await GetTableAsync(ct);
            var rowKey = ToRowKey(id);
            var partitionKey = await ResolvePartitionKeyForIdAsync(table, tenantId, id, rowKey, ct);
            if (string.IsNullOrWhiteSpace(partitionKey))
                return;

            try { await table.DeleteEntityAsync(partitionKey, rowKey, ETag.All, ct); }
            catch (RequestFailedException ex) when (ex.Status == 404) { }

            if (_mapping?.EnableIdLocator == true)
                await _locator.DeleteAsync(ScopeFromTenant(tenantId), EntityTypeName, id.ToString("D"), ct);
        });

    public Task HardDeleteAllAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
        => Execute("HardDeleteAllAsync", async () =>
        {
            var table = await GetTableAsync(ct);
            var idList = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new();
            if (idList.Count == 0) return;

            var grouped = idList
                .Select(id => new
                {
                    Id = id,
                    Candidates = ResolveCandidatePartitionsForId(tenantId, id)
                })
                .Where(x => x.Candidates is { Count: > 0 })
                .SelectMany(x => x.Candidates!
                    .Where(pk => !string.IsNullOrWhiteSpace(pk))
                    .Select(pk => new { Partition = pk, x.Id }))
                .GroupBy(x => x.Partition, x => x.Id);

            foreach (var group in grouped)
            {
                var batch = new List<TableTransactionAction>(100);
                foreach (var id in group.Distinct())
                {
                    batch.Add(new TableTransactionAction(
                        TableTransactionActionType.Delete,
                        new TableEntity(group.Key, ToRowKey(id)) { ETag = ETag.All }));

                    if (batch.Count == 100)
                    {
                        await SubmitDeleteBatchSafely(table, batch, group.Key, ct);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                    await SubmitDeleteBatchSafely(table, batch, group.Key, ct);
            }

            var unknownIds = idList.Where(id =>
            {
                var candidates = ResolveCandidatePartitionsForId(tenantId, id);
                return candidates is null || candidates.Count == 0;
            });

            foreach (var id in unknownIds)
                await HardDeleteAsync(tenantId, id, ct);

            if (_mapping?.EnableIdLocator == true)
            {
                foreach (var id in idList)
                    await _locator.DeleteAsync(ScopeFromTenant(tenantId), EntityTypeName, id.ToString("D"), ct);
            }
        });

    public Task<(IEnumerable<T> Results, string? ContinuationToken)> GetByPartitionPagedAsync(
        string partitionKey,
        int pageSize,
        string? continuationToken = null,
        CancellationToken ct = default,
        string softDeleteProperty = "IsDeleted")
        => Execute<(IEnumerable<T> Results, string? ContinuationToken)>("GetByPartitionPagedAsync", async () =>
        {
            if (string.IsNullOrWhiteSpace(partitionKey))
                throw new ArgumentException("Partition key is required.", nameof(partitionKey));
            if (pageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be greater than 0.");

            var table = await GetTableAsync(ct);
            var results = new List<T>();
            string? nextToken = null;
            var softDeleteProp = EffectiveSoftDeleteProperty(softDeleteProperty);

            if (UseEnvelopeModel)
            {
                var filter = TableClient.CreateQueryFilter<AzureTableEnvelope>(x => x.PartitionKey == partitionKey);
                var pages = table.QueryAsync<AzureTableEnvelope>(filter: filter, maxPerPage: pageSize, cancellationToken: ct)
                    .AsPages(continuationToken, pageSize);
                await using var enumerator = pages.GetAsyncEnumerator(ct);
                if (!await enumerator.MoveNextAsync())
                    return ((IEnumerable<T>)Array.Empty<T>(), null);

                var page = enumerator.Current;
                nextToken = page.ContinuationToken;
                foreach (var env in page.Values)
                {
                    var entity = UnwrapEnvelope(env);
                    if (entity is not null && !IsSoftDeleted(entity, softDeleteProp))
                        results.Add(entity);
                }

                return ((IEnumerable<T>)results, nextToken);
            }

            var colFilter = TableClient.CreateQueryFilter<TableEntity>(x => x.PartitionKey == partitionKey);
            var colPages = table.QueryAsync<TableEntity>(filter: colFilter, maxPerPage: pageSize, cancellationToken: ct)
                .AsPages(continuationToken, pageSize);
            await using (var enumerator = colPages.GetAsyncEnumerator(ct))
            {
                if (!await enumerator.MoveNextAsync())
                    return ((IEnumerable<T>)Array.Empty<T>(), null);

                var page = enumerator.Current;
                nextToken = page.ContinuationToken;
                foreach (var row in page.Values)
                {
                    var entity = UnwrapColumns(row);
                    if (entity is not null && !IsSoftDeleted(entity, softDeleteProp))
                        results.Add(entity);
                }
            }

            return ((IEnumerable<T>)results, nextToken);
        });

    public async IAsyncEnumerable<T> QueryAsync(
        Expression<Func<T, bool>>? predicate = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default,
        string softDeleteProperty = "IsDeleted")
    {
        var table = await GetTableAsync(ct);
        var compiled = predicate?.Compile();
        var softDeleteProp = EffectiveSoftDeleteProperty(softDeleteProperty);

        if (UseEnvelopeModel)
        {
            await foreach (var env in table.QueryAsync<AzureTableEnvelope>(cancellationToken: ct))
            {
                var entity = UnwrapEnvelope(env);
                if (entity is null || IsSoftDeleted(entity, softDeleteProp))
                    continue;

                if (compiled is null || compiled(entity))
                    yield return entity;
            }

            yield break;
        }

        await foreach (var row in table.QueryAsync<TableEntity>(cancellationToken: ct))
        {
            var entity = UnwrapColumns(row);
            if (entity is null || IsSoftDeleted(entity, softDeleteProp))
                continue;

            if (compiled is null || compiled(entity))
                yield return entity;
        }
    }

    private async Task<string?> ResolvePartitionKeyForIdAsync(
        TableClient table,
        Guid? tenantId,
        Guid id,
        string rowKey,
        CancellationToken ct)
    {
        if (_mapping?.EnableIdLocator == true)
        {
            var located = await _locator.FindAsync(ScopeFromTenant(tenantId), EntityTypeName, id.ToString("D"), ct);
            if (located.HasValue)
            {
                if (!string.Equals(located.Value.RowKey, rowKey, StringComparison.OrdinalIgnoreCase))
                    return null;
                return located.Value.PartitionKey;
            }
        }

        var candidates = ResolveCandidatePartitionsForId(tenantId, id);

        if (candidates is { Count: > 0 })
        {
            foreach (var partition in candidates.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                if (UseEnvelopeModel)
                {
                    var check = await table.GetEntityIfExistsAsync<AzureTableEnvelope>(partition, rowKey, cancellationToken: ct);
                    if (check.HasValue)
                        return partition;
                }
                else
                {
                    var check = await table.GetEntityIfExistsAsync<TableEntity>(partition, rowKey, cancellationToken: ct);
                    if (check.HasValue)
                        return partition;
                }
            }

            return null;
        }

        if (UseEnvelopeModel)
        {
            var envFilter = TableClient.CreateQueryFilter<AzureTableEnvelope>(x => x.RowKey == rowKey);
            await foreach (var entity in table.QueryAsync<AzureTableEnvelope>(filter: envFilter, maxPerPage: 1, cancellationToken: ct))
                return entity.PartitionKey;
        }
        else
        {
            var colFilter = TableClient.CreateQueryFilter<TableEntity>(x => x.RowKey == rowKey);
            await foreach (var entity in table.QueryAsync<TableEntity>(filter: colFilter, maxPerPage: 1, cancellationToken: ct))
                return entity.PartitionKey;
        }

        return null;
    }

    private static bool IsSoftDeleted(T entity, string softDeleteProperty)
    {
        if (string.IsNullOrWhiteSpace(softDeleteProperty))
            return entity.IsDeleted;

        var prop = typeof(T).GetProperty(softDeleteProperty, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop is null)
            return entity.IsDeleted;

        var value = prop.GetValue(entity);
        if (value is bool b)
            return b;

        return entity.IsDeleted;
    }

    private static async Task SubmitDeleteBatchSafely(
        TableClient table,
        List<TableTransactionAction> batch,
        string partitionKey,
        CancellationToken ct)
    {
        try
        {
            await table.SubmitTransactionAsync(batch, ct);
        }
        catch (RequestFailedException)
        {
            foreach (var action in batch)
            {
                if (action.Entity is not ITableEntity entity)
                    continue;

                try { await table.DeleteEntityAsync(partitionKey, entity.RowKey, ETag.All, ct); }
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
