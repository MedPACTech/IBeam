using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public static class AzureTablePartitionKeyStrategies
{
    public static IAzureTablePartitionKeyStrategy<T> Default<T>()
        where T : class, IEntity
        => new DefaultAzureTablePartitionKeyStrategy<T>();

    public static IAzureTablePartitionKeyStrategy<T> Global<T>(string partitionKey = "global")
        where T : class, IEntity
        => new GlobalAzureTablePartitionKeyStrategy<T>(partitionKey);

    public static IAzureTablePartitionKeyStrategy<T> Tenant<T>()
        where T : class, IEntity
        => new TenantAzureTablePartitionKeyStrategy<T>();

    public static IAzureTablePartitionKeyStrategy<T> TenantHashBucket<T>(int bucketCount = 16)
        where T : class, IEntity
        => new TenantHashBucketAzureTablePartitionKeyStrategy<T>(bucketCount);

    public static IAzureTablePartitionKeyStrategy<T> Create<T>(
        Func<Guid?, T, string> writePartition,
        Func<Guid?, Guid, IReadOnlyList<string>?>? idCandidates = null,
        Func<Guid?, IReadOnlyList<string>?>? getAllPartitions = null)
        where T : class, IEntity
        => new DelegateAzureTablePartitionKeyStrategy<T>(writePartition, idCandidates, getAllPartitions);
}

internal sealed class DefaultAzureTablePartitionKeyStrategy<T> : IAzureTablePartitionKeyStrategy<T>
    where T : class, IEntity
{
    private static readonly bool IsTenantSpecific = typeof(ITenantEntity).IsAssignableFrom(typeof(T));

    public string GetPartitionKeyForWrite(Guid? tenantId, T entity)
    {
        if (!IsTenantSpecific) return "global";

        if (entity is ITenantEntity te && te.TenantId != Guid.Empty)
            return te.TenantId.ToString("N");

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            return tenantId.Value.ToString("N");

        throw new InvalidOperationException($"TenantId is required for tenant-specific table '{typeof(T).Name}'.");
    }

    public IReadOnlyList<string>? GetCandidatePartitionsForId(Guid? tenantId, Guid id)
    {
        if (!IsTenantSpecific) return new[] { "global" };
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            throw new InvalidOperationException($"TenantId is required for tenant-specific table '{typeof(T).Name}'.");

        return new[] { tenantId.Value.ToString("N") };
    }

    public IReadOnlyList<string>? GetPartitionsForGetAll(Guid? tenantId)
        => GetCandidatePartitionsForId(tenantId, Guid.Empty);
}

internal sealed class GlobalAzureTablePartitionKeyStrategy<T> : IAzureTablePartitionKeyStrategy<T>
    where T : class, IEntity
{
    private readonly string _partitionKey;

    public GlobalAzureTablePartitionKeyStrategy(string partitionKey)
    {
        if (string.IsNullOrWhiteSpace(partitionKey))
            throw new ArgumentException("Partition key is required.", nameof(partitionKey));

        _partitionKey = partitionKey.Trim();
    }

    public string GetPartitionKeyForWrite(Guid? tenantId, T entity) => _partitionKey;
    public IReadOnlyList<string>? GetCandidatePartitionsForId(Guid? tenantId, Guid id) => new[] { _partitionKey };
    public IReadOnlyList<string>? GetPartitionsForGetAll(Guid? tenantId) => new[] { _partitionKey };
}

internal sealed class TenantAzureTablePartitionKeyStrategy<T> : IAzureTablePartitionKeyStrategy<T>
    where T : class, IEntity
{
    public string GetPartitionKeyForWrite(Guid? tenantId, T entity)
    {
        if (entity is ITenantEntity te && te.TenantId != Guid.Empty)
            return te.TenantId.ToString("N");

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            return tenantId.Value.ToString("N");

        throw new InvalidOperationException($"TenantId is required for table '{typeof(T).Name}'.");
    }

    public IReadOnlyList<string>? GetCandidatePartitionsForId(Guid? tenantId, Guid id)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            throw new InvalidOperationException($"TenantId is required for table '{typeof(T).Name}'.");

        return new[] { tenantId.Value.ToString("N") };
    }

    public IReadOnlyList<string>? GetPartitionsForGetAll(Guid? tenantId)
        => GetCandidatePartitionsForId(tenantId, Guid.Empty);
}

internal sealed class TenantHashBucketAzureTablePartitionKeyStrategy<T> : IAzureTablePartitionKeyStrategy<T>
    where T : class, IEntity
{
    private readonly int _bucketCount;

    public TenantHashBucketAzureTablePartitionKeyStrategy(int bucketCount)
    {
        if (bucketCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bucketCount), "Bucket count must be greater than 0.");

        _bucketCount = bucketCount;
    }

    public string GetPartitionKeyForWrite(Guid? tenantId, T entity)
    {
        var tenant = GetTenantId(tenantId, entity);
        var bucket = ComputeBucket(entity.Id, _bucketCount);
        return $"{tenant:N}|b{bucket:D2}";
    }

    public IReadOnlyList<string>? GetCandidatePartitionsForId(Guid? tenantId, Guid id)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            throw new InvalidOperationException($"TenantId is required for table '{typeof(T).Name}'.");

        var bucket = ComputeBucket(id, _bucketCount);
        return new[] { $"{tenantId.Value:N}|b{bucket:D2}" };
    }

    public IReadOnlyList<string>? GetPartitionsForGetAll(Guid? tenantId)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            throw new InvalidOperationException($"TenantId is required for table '{typeof(T).Name}'.");

        var list = new List<string>(_bucketCount);
        for (var i = 0; i < _bucketCount; i++)
            list.Add($"{tenantId.Value:N}|b{i:D2}");

        return list;
    }

    private static Guid GetTenantId(Guid? tenantId, T entity)
    {
        if (entity is ITenantEntity te && te.TenantId != Guid.Empty)
            return te.TenantId;

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            return tenantId.Value;

        throw new InvalidOperationException($"TenantId is required for table '{typeof(T).Name}'.");
    }

    private static int ComputeBucket(Guid id, int bucketCount)
    {
        if (id == Guid.Empty)
            return 0;

        return Math.Abs(id.GetHashCode()) % bucketCount;
    }
}

internal sealed class DelegateAzureTablePartitionKeyStrategy<T> : IAzureTablePartitionKeyStrategy<T>
    where T : class, IEntity
{
    private readonly Func<Guid?, T, string> _writePartition;
    private readonly Func<Guid?, Guid, IReadOnlyList<string>?>? _idCandidates;
    private readonly Func<Guid?, IReadOnlyList<string>?>? _getAllPartitions;

    public DelegateAzureTablePartitionKeyStrategy(
        Func<Guid?, T, string> writePartition,
        Func<Guid?, Guid, IReadOnlyList<string>?>? idCandidates = null,
        Func<Guid?, IReadOnlyList<string>?>? getAllPartitions = null)
    {
        _writePartition = writePartition ?? throw new ArgumentNullException(nameof(writePartition));
        _idCandidates = idCandidates;
        _getAllPartitions = getAllPartitions;
    }

    public string GetPartitionKeyForWrite(Guid? tenantId, T entity)
        => _writePartition(tenantId, entity);

    public IReadOnlyList<string>? GetCandidatePartitionsForId(Guid? tenantId, Guid id)
        => _idCandidates?.Invoke(tenantId, id);

    public IReadOnlyList<string>? GetPartitionsForGetAll(Guid? tenantId)
        => _getAllPartitions?.Invoke(tenantId);
}
