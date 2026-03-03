using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public sealed class AzureEntityKey
{
    public required string PartitionKey { get; init; }
    public required string RowKey { get; init; }
}

public sealed class AzureEntityMappingOptions<T>
    where T : class, IEntity
{
    public required string TableName { get; set; }

    public required Func<Guid?, T, AzureEntityKey> WriteKey { get; set; }

    public Func<Guid?, Guid, IReadOnlyList<string>?>? CandidatePartitionsForId { get; set; }
        = null;

    public bool EnableIdLocator { get; set; }

    public string SoftDeleteProperty { get; set; } = "IsDeleted";
}
