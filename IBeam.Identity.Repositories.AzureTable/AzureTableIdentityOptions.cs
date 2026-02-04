
namespace IBeam.Identity.Repositories.AzureTable;

public sealed class AzureTableIdentityOptions
{
    public string StorageConnectionString { get; init; } = string.Empty;
    public string? TablePrefix { get; init; }
    public string? UserTableName { get; init; }
    public string? RoleTableName { get; init; }
    public string? IndexTableName { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(StorageConnectionString))
            throw new InvalidOperationException("IdentityAzureTable:IdentityConfiguration:StorageConnectionString is required.");
    }
}

